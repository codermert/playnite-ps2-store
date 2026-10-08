using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;

namespace BDSoftPS2Store
{
    public static class CatalogService
    {
        public static List<StoreGame> Load(string pluginDir)
        {
            string path = Path.Combine(pluginDir, "catalog.json");
            var entries = Serialization.FromJsonFile<List<CatalogEntry>>(path) ?? new List<CatalogEntry>();
            return entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Title))
                .Select(e => new StoreGame(e))
                .ToList();
        }
    }

    public static class Http
    {
        static Http()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        // WebClient with a timeout (the default is 100 s).
        private class TimeoutWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                var request = base.GetWebRequest(address);
                if (request != null)
                {
                    request.Timeout = 8000;
                }
                return request;
            }
        }

        public static WebClient CreateClient()
        {
            var client = new TimeoutWebClient();
            client.Encoding = Encoding.UTF8;
            client.Headers[HttpRequestHeader.UserAgent] = "BDSoftPS2Store/1.0 (Playnite plugin)";
            return client;
        }

        public static string Hash(string value)
        {
            using (var sha = SHA1.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }

    public class WikiThumbnail
    {
        public string Source { get; set; }
    }

    public class WikiSummary
    {
        public string Title { get; set; }
        public string Extract { get; set; }
        public string Type { get; set; }
        public WikiThumbnail Thumbnail { get; set; }
    }

    // Wikipedia page summaries (text is CC BY-SA). Cached on disk.
    public class WikipediaService
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly string cacheDir;

        public WikipediaService(string cacheRoot)
        {
            cacheDir = Path.Combine(cacheRoot, "wiki");
            Directory.CreateDirectory(cacheDir);
        }

        public Task<WikiSummary> GetSummaryAsync(StoreGame game)
        {
            return Task.Run(() => GetSummaryCoreAsync(game));
        }

        private async Task<WikiSummary> GetSummaryCoreAsync(StoreGame game)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(game.Entry.Wiki))
            {
                candidates.Add(game.Entry.Wiki);
            }
            else
            {
                candidates.Add(game.Title + " (video game)");
                candidates.Add(game.Title);
            }

            foreach (string title in candidates)
            {
                var summary = await GetPageAsync(title);
                if (summary != null && summary.Type != "disambiguation" && !string.IsNullOrEmpty(summary.Extract) &&
                    (!string.IsNullOrEmpty(game.Entry.Wiki) || LooksLikeGame(summary.Extract)))
                {
                    return summary;
                }
            }
            return null;
        }

        private static bool LooksLikeGame(string extract)
        {
            string e = extract.ToLowerInvariant();
            return e.Contains("video game") || e.Contains("playstation 2");
        }

        private async Task<WikiSummary> GetPageAsync(string title)
        {
            string cacheFile = Path.Combine(cacheDir, Http.Hash(title) + ".json");
            string json = null;
            if (File.Exists(cacheFile))
            {
                json = File.ReadAllText(cacheFile, Encoding.UTF8);
            }
            else
            {
                try
                {
                    using (var client = Http.CreateClient())
                    {
                        string url = "https://en.wikipedia.org/api/rest_v1/page/summary/" + Uri.EscapeDataString(title.Replace(' ', '_'));
                        json = await client.DownloadStringTaskAsync(url);
                    }
                }
                catch (WebException e)
                {
                    var response = e.Response as HttpWebResponse;
                    if (response != null && response.StatusCode == HttpStatusCode.NotFound)
                    {
                        json = "{}";
                    }
                    else
                    {
                        logger.Warn(e, "Wikipedia request failed for " + title);
                        return null;
                    }
                }
                File.WriteAllText(cacheFile, json, Encoding.UTF8);
            }

            WikiSummary summary;
            return Serialization.TryFromJson(json, out summary) ? summary : null;
        }
    }

    // Covers: PCSX2 community covers by serial (xlenore/ps2-covers), Wikipedia thumbnail as fallback.
    public class CoverService
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const string SerialCoverUrl = "https://raw.githubusercontent.com/xlenore/ps2-covers/main/covers/default/{0}.jpg";

        private readonly string cacheDir;
        private readonly WikipediaService wikipedia;
        private readonly Dispatcher dispatcher;
        private readonly SemaphoreSlim slots = new SemaphoreSlim(4);
        private readonly ConcurrentDictionary<string, bool> missing = new ConcurrentDictionary<string, bool>();

        public CoverService(string cacheRoot, WikipediaService wikipedia, Dispatcher dispatcher)
        {
            cacheDir = Path.Combine(cacheRoot, "covers");
            Directory.CreateDirectory(cacheDir);
            this.wikipedia = wikipedia;
            this.dispatcher = dispatcher;
        }

        // Used when no catalog cover exists (e.g. the cover of the matching Playnite library game).
        public Func<StoreGame, string> FallbackPath { get; set; }

        // Cover set for the game in games.json ("cover": http(s) URL or local file), used first.
        public Func<StoreGame, string> CoverOverride { get; set; }

        public string GetCachedPath(StoreGame game)
        {
            string path = Path.Combine(cacheDir, CacheKey(game) + ".jpg");
            return File.Exists(path) ? path : null;
        }

        public void Request(StoreGame game)
        {
            Task.Run(() => LoadAsync(game));
        }

        private static string CacheKey(StoreGame game)
        {
            return string.IsNullOrEmpty(game.Entry.Serial) ? "wd_" + game.Entry.Id : game.Entry.Serial;
        }

        private async Task LoadAsync(StoreGame game)
        {
            string key = CacheKey(game);
            string path = Path.Combine(cacheDir, key + ".jpg");
            string missingMarker = path + ".none";
            try
            {
                string custom = CoverOverride != null ? CoverOverride(game) : null;
                if (!string.IsNullOrEmpty(custom))
                {
                    string customPath = custom;
                    if (custom.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || custom.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        customPath = Path.Combine(cacheDir, "custom_" + Http.Hash(custom) + ".img");
                        if (!File.Exists(customPath))
                        {
                            var bytes = await TryDownload(custom);
                            if (bytes != null)
                            {
                                File.WriteAllBytes(customPath, bytes);
                            }
                        }
                    }
                    if (File.Exists(customPath))
                    {
                        var customImage = LoadImage(customPath, 360);
                        if (customImage != null)
                        {
                            var shown = dispatcher.BeginInvoke(new Action(() => game.Cover = customImage));
                            return;
                        }
                    }
                }
                if (!File.Exists(path) && !File.Exists(missingMarker))
                {
                    await slots.WaitAsync();
                    try
                    {
                        byte[] data = null;
                        if (!string.IsNullOrEmpty(game.Entry.Serial))
                        {
                            data = await TryDownload(string.Format(SerialCoverUrl, game.Entry.Serial));
                        }
                        if (data == null)
                        {
                            var summary = await wikipedia.GetSummaryAsync(game);
                            if (summary != null && summary.Thumbnail != null && !string.IsNullOrEmpty(summary.Thumbnail.Source))
                            {
                                data = await TryDownload(summary.Thumbnail.Source);
                            }
                        }
                        if (data != null)
                        {
                            File.WriteAllBytes(path, data);
                        }
                        else
                        {
                            File.WriteAllText(missingMarker, string.Empty);
                        }
                    }
                    finally
                    {
                        slots.Release();
                    }
                }

                string source = File.Exists(path) ? path : (FallbackPath != null ? FallbackPath(game) : null);
                if (!string.IsNullOrEmpty(source) && File.Exists(source))
                {
                    var image = LoadImage(source, 360);
                    if (image != null)
                    {
                        var operation = dispatcher.BeginInvoke(new Action(() => game.Cover = image));
                        return;
                    }
                }
                var missing = dispatcher.BeginInvoke(new Action(() => game.CoverMissing = true));
            }
            catch (Exception e)
            {
                logger.Warn(e, "Cover load failed for " + game.Title);
            }
        }

        private static async Task<byte[]> TryDownload(string url)
        {
            try
            {
                using (var client = Http.CreateClient())
                {
                    return await client.DownloadDataTaskAsync(url);
                }
            }
            catch (WebException)
            {
                return null;
            }
        }

        public static ImageSource LoadImage(string path, int decodeWidth)
        {
            try
            {
                var image = new BitmapImage();
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = decodeWidth;
                    image.StreamSource = stream;
                    image.EndInit();
                }
                image.Freeze();
                return image;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    [DataContract]
    public class ExternalLinkEntry
    {
        [DataMember(Name = "id", Order = 1)] public string Id { get; set; }
        [DataMember(Name = "name", Order = 2)] public string Name { get; set; }
        [DataMember(Name = "platform", Order = 3)] public string Platform { get; set; }
        [DataMember(Name = "serial", Order = 4, EmitDefaultValue = false)] public string Serial { get; set; }
        [DataMember(Name = "cover", Order = 5)] public string Cover { get; set; }
        [DataMember(Name = "externalUrl", Order = 6)] public string ExternalUrl { get; set; }
    }

    [DataContract]
    public class ExternalLinksFile
    {
        [DataMember(Name = "games")] public List<ExternalLinkEntry> Games { get; set; }
    }

    // Per-game data for the store kept in one editable file (games.json in the plugin data folder):
    //   id, name, platform, cover, externalUrl
    // Every catalog game has an entry. "Kur" opens the entry's externalUrl exactly as written
    // (no URL is ever generated from the game name). The file is re-read when it changes.
    public class ExternalLinksService
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly string file;
        private DateTime loadedStamp = DateTime.MinValue;
        private Dictionary<string, ExternalLinkEntry> byId = new Dictionary<string, ExternalLinkEntry>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, ExternalLinkEntry> bySerial = new Dictionary<string, ExternalLinkEntry>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, ExternalLinkEntry> byName = new Dictionary<string, ExternalLinkEntry>();

        public ExternalLinksService(string dataDir)
        {
            file = Path.Combine(dataDir, "games.json");
        }

        public string FilePath { get { return file; } }

        // Makes sure games.json lists every catalog game. Existing entries (and their values) are
        // kept as they are; only games that are missing are added, with an empty externalUrl.
        public void EnsureCatalog(IEnumerable<StoreGame> games)
        {
            var data = Read(file) ?? new ExternalLinksFile();
            if (data.Games == null)
            {
                data.Games = new List<ExternalLinkEntry>();
            }

            var ids = new HashSet<string>(data.Games.Where(e => e != null && !string.IsNullOrEmpty(e.Id)).Select(e => e.Id), StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(data.Games.Where(e => e != null && string.IsNullOrEmpty(e.Id)).Select(e => TitleNormalizer.Normalize(e.Name)));
            int added = 0;
            foreach (var game in games)
            {
                var entry = game.Entry;
                if (ids.Contains(entry.Id))
                {
                    continue;
                }
                // an older entry without id (matched by name) gets the catalog id
                var byNameEntry = data.Games.FirstOrDefault(e => e != null && string.IsNullOrEmpty(e.Id) && names.Contains(game.NormalizedTitle) && TitleNormalizer.Normalize(e.Name) == game.NormalizedTitle);
                if (byNameEntry != null)
                {
                    byNameEntry.Id = entry.Id;
                    ids.Add(entry.Id);
                    added++;
                    continue;
                }
                data.Games.Add(new ExternalLinkEntry
                {
                    Id = entry.Id,
                    Name = entry.Title,
                    Platform = "PlayStation 2",
                    Serial = string.IsNullOrEmpty(entry.Serial) ? null : entry.Serial,
                    Cover = string.Empty,
                    ExternalUrl = entry.ExternalUrl ?? string.Empty
                });
                ids.Add(entry.Id);
                added++;
            }

            if (added > 0 || !File.Exists(file))
            {
                Write(file, data);
                loadedStamp = DateTime.MinValue;
            }
        }

        private void Reload()
        {
            DateTime stamp = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
            if (stamp == loadedStamp)
            {
                return;
            }
            loadedStamp = stamp;
            var ids = new Dictionary<string, ExternalLinkEntry>(StringComparer.OrdinalIgnoreCase);
            var serials = new Dictionary<string, ExternalLinkEntry>(StringComparer.OrdinalIgnoreCase);
            var names = new Dictionary<string, ExternalLinkEntry>();
            var data = Read(file);
            if (data != null && data.Games != null)
            {
                foreach (var entry in data.Games)
                {
                    if (entry == null)
                    {
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(entry.Platform) && !entry.Platform.ToLowerInvariant().Contains("playstation 2") && !entry.Platform.ToLowerInvariant().Contains("ps2"))
                    {
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(entry.Id))
                    {
                        ids[entry.Id.Trim()] = entry;
                    }
                    if (!string.IsNullOrWhiteSpace(entry.Serial) && !serials.ContainsKey(entry.Serial.Trim()))
                    {
                        serials[entry.Serial.Trim()] = entry;
                    }
                    string key = TitleNormalizer.Normalize(entry.Name);
                    if (key.Length > 0 && !names.ContainsKey(key))
                    {
                        names[key] = entry;
                    }
                }
            }
            byId = ids;
            bySerial = serials;
            byName = names;
        }

        // Entry of a game: by id, then serial, then name.
        public ExternalLinkEntry Find(StoreGame game)
        {
            Reload();
            ExternalLinkEntry entry;
            if (!string.IsNullOrEmpty(game.Entry.Id) && byId.TryGetValue(game.Entry.Id, out entry))
            {
                return entry;
            }
            if (!string.IsNullOrEmpty(game.Entry.Serial) && bySerial.TryGetValue(game.Entry.Serial, out entry))
            {
                return entry;
            }
            return byName.TryGetValue(game.NormalizedTitle, out entry) ? entry : null;
        }

        // The game's externalUrl: games.json first, then the catalog's own externalUrl field.
        public string GetUrl(StoreGame game)
        {
            var entry = Find(game);
            if (entry != null && !string.IsNullOrWhiteSpace(entry.ExternalUrl))
            {
                return entry.ExternalUrl.Trim();
            }
            return string.IsNullOrWhiteSpace(game.Entry.ExternalUrl) ? null : game.Entry.ExternalUrl.Trim();
        }

        // Optional cover override from games.json (http(s) URL or local file path).
        public string GetCover(StoreGame game)
        {
            var entry = Find(game);
            return entry == null || string.IsNullOrWhiteSpace(entry.Cover) ? null : entry.Cover.Trim();
        }

        private static ExternalLinksFile Read(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(ExternalLinksFile));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(path, Encoding.UTF8))))
                {
                    return serializer.ReadObject(stream) as ExternalLinksFile;
                }
            }
            catch (Exception e)
            {
                logger.Warn(e, "games.json could not be read");
                return null;
            }
        }

        // Readable, one-field-per-line JSON so the file is easy to edit by hand.
        private static void Write(string path, ExternalLinksFile data)
        {
            var sb = new StringBuilder();
            sb.Append("{\r\n  \"games\": [");
            for (int i = 0; i < data.Games.Count; i++)
            {
                var e = data.Games[i];
                if (e == null)
                {
                    continue;
                }
                sb.Append(i == 0 ? "\r\n" : ",\r\n");
                sb.Append("    {\r\n");
                sb.Append("      \"id\": ").Append(Quote(e.Id)).Append(",\r\n");
                sb.Append("      \"name\": ").Append(Quote(e.Name)).Append(",\r\n");
                sb.Append("      \"platform\": ").Append(Quote(e.Platform)).Append(",\r\n");
                if (!string.IsNullOrEmpty(e.Serial))
                {
                    sb.Append("      \"serial\": ").Append(Quote(e.Serial)).Append(",\r\n");
                }
                sb.Append("      \"cover\": ").Append(Quote(e.Cover)).Append(",\r\n");
                sb.Append("      \"externalUrl\": ").Append(Quote(e.ExternalUrl)).Append("\r\n");
                sb.Append("    }");
            }
            sb.Append("\r\n  ]\r\n}\r\n");
            string temp = path + ".tmp";
            File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Copy(path, path + ".bak", true);
                File.Delete(path);
            }
            File.Move(temp, path);
        }

        private static string Quote(string value)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in value ?? string.Empty)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }

    public class FavoritesService
    {
        private readonly string file;
        private readonly HashSet<string> ids;

        public FavoritesService(string dataDir)
        {
            file = Path.Combine(dataDir, "favorites.json");
            List<string> saved;
            ids = File.Exists(file) && Serialization.TryFromJsonFile(file, out saved) && saved != null
                ? new HashSet<string>(saved)
                : new HashSet<string>();
        }

        public bool Contains(string id)
        {
            return ids.Contains(id);
        }

        public bool Toggle(string id)
        {
            bool nowFavorite;
            if (ids.Contains(id))
            {
                ids.Remove(id);
                nowFavorite = false;
            }
            else
            {
                ids.Add(id);
                nowFavorite = true;
            }
            File.WriteAllText(file, Serialization.ToJson(ids.ToList(), true), Encoding.UTF8);
            return nowFavorite;
        }
    }

    public class EmulatorTarget
    {
        public Emulator Emulator { get; set; }
        public string ProfileId { get; set; }
        public string ProfileName { get; set; }
    }

    // Playnite library + emulator integration.
    public class LibraryService
    {
        public const string PlatformSpecId = "sony_playstation2";
        public const string RomFilter = "PS2 oyun dosyası|*.iso;*.chd;*.cso;*.bin;*.img;*.mdf;*.nrg;*.gz;*.m3u|Tüm dosyalar|*.*";

        private readonly IPlayniteAPI api;
        private Dictionary<string, Game> byTitle = new Dictionary<string, Game>();

        public LibraryService(IPlayniteAPI api)
        {
            this.api = api;
        }

        public void Refresh()
        {
            var map = new Dictionary<string, Game>();
            foreach (var game in api.Database.Games)
            {
                string key = TitleNormalizer.Normalize(game.Name);
                if (key.Length == 0)
                {
                    continue;
                }
                Game existing;
                if (!map.TryGetValue(key, out existing) || (!IsPs2(existing) && IsPs2(game)))
                {
                    map[key] = game;
                }
            }
            byTitle = map;
        }

        public Game Find(StoreGame storeGame)
        {
            Game game;
            if (byTitle.TryGetValue(storeGame.NormalizedTitle, out game))
            {
                return api.Database.Games.Get(game.Id) ?? game;
            }
            return null;
        }

        public bool IsPlayable(Game game)
        {
            return game != null && game.IsInstalled && game.GameActions != null && game.GameActions.Any(a => a.IsPlayAction);
        }

        public Platform GetPs2Platform()
        {
            var platform = api.Database.Platforms.FirstOrDefault(p => p.SpecificationId == PlatformSpecId);
            if (platform == null)
            {
                platform = new Platform("Sony PlayStation 2") { SpecificationId = PlatformSpecId };
                api.Database.Platforms.Add(platform);
            }
            return platform;
        }

        private bool IsPs2(Game game)
        {
            if (game.PlatformIds == null)
            {
                return false;
            }
            var platform = api.Database.Platforms.FirstOrDefault(p => p.SpecificationId == PlatformSpecId);
            return platform != null && game.PlatformIds.Contains(platform.Id);
        }

        public Game AddToLibrary(StoreGame storeGame, string coverPath)
        {
            var entry = storeGame.Entry;
            var game = new Game(entry.Title);
            game.PlatformIds = new List<Guid> { GetPs2Platform().Id };
            if (entry.Year > 0)
            {
                game.ReleaseDate = new ReleaseDate(entry.Year);
            }
            game.DeveloperIds = AddNamed(entry.Developers, api.Database.Companies);
            game.PublisherIds = AddNamed(entry.Publishers, api.Database.Companies);
            game.GenreIds = AddNamed(entry.Genres, api.Database.Genres);
            if (!string.IsNullOrEmpty(storeGame.Description))
            {
                game.Description = storeGame.Description;
            }
            if (storeGame.WikipediaUrl != null)
            {
                game.Links = new ObservableCollection<Link> { new Link("Wikipedia", storeGame.WikipediaUrl) };
            }
            game.Favorite = storeGame.IsFavorite;
            game.IsInstalled = false;

            api.Database.Games.Add(game);
            if (!string.IsNullOrEmpty(coverPath) && File.Exists(coverPath))
            {
                game.CoverImage = api.Database.AddFile(coverPath, game.Id);
                api.Database.Games.Update(game);
            }
            Refresh();
            return game;
        }

        private static List<Guid> AddNamed<T>(List<string> names, IItemCollection<T> collection) where T : DatabaseObject
        {
            var ids = new List<Guid>();
            if (names == null)
            {
                return ids;
            }
            foreach (string name in names.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                var item = collection.Add(name);
                if (item != null && !ids.Contains(item.Id))
                {
                    ids.Add(item.Id);
                }
            }
            return ids;
        }

        public EmulatorTarget FindPs2Emulator()
        {
            var platform = GetPs2Platform();
            foreach (var emulator in api.Database.Emulators)
            {
                if (emulator.CustomProfiles != null)
                {
                    foreach (var profile in emulator.CustomProfiles)
                    {
                        if (profile.Platforms != null && profile.Platforms.Contains(platform.Id))
                        {
                            return new EmulatorTarget { Emulator = emulator, ProfileId = profile.Id, ProfileName = profile.Name };
                        }
                    }
                }

                if (emulator.BuiltinProfiles != null && !string.IsNullOrEmpty(emulator.BuiltInConfigId))
                {
                    var definition = api.Emulation.GetEmulator(emulator.BuiltInConfigId);
                    if (definition == null || definition.Profiles == null)
                    {
                        continue;
                    }
                    foreach (var profile in emulator.BuiltinProfiles)
                    {
                        var defProfile = definition.Profiles.FirstOrDefault(p => p.Name == profile.BuiltInProfileName);
                        if (defProfile != null && defProfile.Platforms != null && defProfile.Platforms.Contains(PlatformSpecId))
                        {
                            return new EmulatorTarget { Emulator = emulator, ProfileId = profile.Id, ProfileName = profile.Name };
                        }
                    }
                }
            }
            return null;
        }

        // Links a user-selected game file to the library game and sets up an emulator play action.
        public EmulatorTarget LinkRom(Game game, string romPath)
        {
            game.Roms = new ObservableCollection<GameRom> { new GameRom(game.Name, romPath) };
            game.InstallDirectory = Path.GetDirectoryName(romPath);
            game.IsInstalled = true;

            var target = FindPs2Emulator();
            if (target != null)
            {
                var actions = game.GameActions ?? new ObservableCollection<GameAction>();
                foreach (var old in actions.Where(a => a.IsPlayAction && a.Type == GameActionType.Emulator).ToList())
                {
                    actions.Remove(old);
                }
                actions.Insert(0, new GameAction
                {
                    Name = "Play",
                    Type = GameActionType.Emulator,
                    EmulatorId = target.Emulator.Id,
                    EmulatorProfileId = target.ProfileId,
                    IsPlayAction = true
                });
                game.GameActions = actions;
            }
            api.Database.Games.Update(game);
            return target;
        }
    }
}
