using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace BDSoftPS2Store
{
    // Gives PS2 library games a home-screen background (the PS5ish home shows the selected game's
    // background image). Fan art + clear logo from TheGamesDB when available, otherwise a PS5-style
    // background made from the cover. Runs on worker threads; database updates go through the UI thread.
    public class BackgroundService
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const int Width = 1920;
        private const int Height = 1080;

        private readonly IPlayniteAPI api;
        private readonly string cacheDir;
        private readonly Dispatcher dispatcher;
        private readonly SemaphoreSlim slots = new SemaphoreSlim(2);
        private readonly HashSet<Guid> running = new HashSet<Guid>();

        public BackgroundService(IPlayniteAPI api, string dataDir, Dispatcher dispatcher)
        {
            this.api = api;
            this.dispatcher = dispatcher;
            cacheDir = Path.Combine(dataDir, "cache", "backgrounds");
            Directory.CreateDirectory(cacheDir);
        }

        // Bump when the matching rules change: backgrounds this plugin made earlier are rebuilt once.
        private const string RulesVersion = "2";

        public void UpgradeIfNeeded()
        {
            string marker = Path.Combine(cacheDir, "rules.version");
            string current = File.Exists(marker) ? File.ReadAllText(marker).Trim() : "1";
            if (current != RulesVersion)
            {
                RegenerateAll();
                File.WriteAllText(marker, RulesVersion);
            }
        }

        // Rebuilds every background this plugin created (cache files are named by game id).
        public int RegenerateAll()
        {
            int count = 0;
            foreach (string file in Directory.GetFiles(cacheDir, "*.jpg"))
            {
                Guid id;
                if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out id))
                {
                    continue;
                }
                File.Delete(file);
                var game = api.Database.Games.Get(id);
                if (game == null)
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(game.BackgroundImage))
                {
                    api.Database.RemoveFile(game.BackgroundImage);
                    game.BackgroundImage = null;
                    api.Database.Games.Update(game);
                }
                Request(id);
                count++;
            }
            return count;
        }

        // All PS2 games in the library without a background (e.g. games added from the store).
        public void FillMissing()
        {
            var platform = api.Database.Platforms.FirstOrDefault(p => p.SpecificationId == LibraryService.PlatformSpecId);
            if (platform == null)
            {
                return;
            }
            var games = api.Database.Games
                .Where(g => g.PlatformIds != null && g.PlatformIds.Contains(platform.Id) && string.IsNullOrEmpty(g.BackgroundImage))
                .Select(g => g.Id)
                .ToList();
            foreach (var id in games)
            {
                Request(id);
            }
        }

        public void Request(Guid gameId)
        {
            lock (running)
            {
                if (!running.Add(gameId))
                {
                    return;
                }
            }
            Task.Run(async () =>
            {
                await slots.WaitAsync();
                try
                {
                    var game = api.Database.Games.Get(gameId);
                    if (game == null || !string.IsNullOrEmpty(game.BackgroundImage))
                    {
                        return;
                    }
                    string coverPath = string.IsNullOrEmpty(game.CoverImage) ? null : api.Database.GetFullFilePath(game.CoverImage);
                    string file = Path.Combine(cacheDir, gameId.ToString("N") + ".jpg");
                    if (!File.Exists(file))
                    {
                        Create(game.Name, coverPath, file);
                    }
                    if (File.Exists(file))
                    {
                        dispatcher.Invoke(new Action(() =>
                        {
                            var current = api.Database.Games.Get(gameId);
                            if (current != null && string.IsNullOrEmpty(current.BackgroundImage))
                            {
                                current.BackgroundImage = api.Database.AddFile(file, gameId);
                                api.Database.Games.Update(current);
                            }
                        }));
                    }
                }
                catch (Exception e)
                {
                    logger.Warn(e, "Background could not be created for " + gameId);
                }
                finally
                {
                    slots.Release();
                    lock (running)
                    {
                        running.Remove(gameId);
                    }
                }
            });
        }

        // ---------- image building ----------

        private static void Create(string title, string coverPath, string target)
        {
            string fanartUrl, logoUrl;
            FindArt(title, out fanartUrl, out logoUrl);
            using (var fanart = Download(fanartUrl))
            using (var logo = Download(logoUrl))
            using (var cover = coverPath != null && File.Exists(coverPath) ? LoadBitmap(coverPath) : null)
            {
                if (fanart == null && cover == null)
                {
                    return;
                }
                using (var canvas = new Bitmap(Width, Height))
                using (var g = Graphics.FromImage(canvas))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    if (fanart != null)
                    {
                        DrawFill(g, fanart);
                    }
                    else
                    {
                        DrawFromCover(g, cover);
                    }
                    using (var shade = new LinearGradientBrush(new Rectangle(0, 0, 1100, Height), Color.FromArgb(150, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 0f))
                    {
                        g.FillRectangle(shade, 0, 0, 1100, Height);
                    }
                    if (logo != null)
                    {
                        DrawLogo(g, logo);
                    }
                    SaveJpeg(canvas, target);
                }
            }
        }

        private static void DrawFill(Graphics g, Image image)
        {
            double scale = Math.Max((double)Width / image.Width, (double)Height / image.Height);
            float w = (float)(image.Width * scale), h = (float)(image.Height * scale);
            g.DrawImage(image, (Width - w) / 2, (Height - h) / 2, w, h);
        }

        // No fan art: blurred, darkened cover filling the screen with the sharp cover on the right.
        private static void DrawFromCover(Graphics g, Image cover)
        {
            using (var small = new Bitmap(64, 36))
            {
                using (var sg = Graphics.FromImage(small))
                {
                    sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    float srcH = cover.Width * 36f / 64f;
                    sg.DrawImage(cover, new Rectangle(0, 0, 64, 36), 0, Math.Max(0, (cover.Height - srcH) / 2), cover.Width, Math.Min(cover.Height, srcH), GraphicsUnit.Pixel);
                }
                g.DrawImage(small, -30, -17, Width + 60, Height + 34);
            }
            using (var dark = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            {
                g.FillRectangle(dark, 0, 0, Width, Height);
            }
            int coverH = 860;
            int coverW = (int)(cover.Width * (double)coverH / cover.Height);
            g.DrawImage(cover, Width - coverW - 160, (Height - coverH) / 2, coverW, coverH);
        }

        private static void DrawLogo(Graphics g, Bitmap logo)
        {
            var bounds = OpaqueBounds(logo);
            if (bounds.Width < 4 || bounds.Height < 4)
            {
                return;
            }
            double scale = Math.Min(560.0 / bounds.Width, 230.0 / bounds.Height);
            float w = (float)(bounds.Width * scale), h = (float)(bounds.Height * scale);
            g.DrawImage(logo, new RectangleF(180 + (560 - w) / 2, 560 + (230 - h) / 2, w, h), bounds, GraphicsUnit.Pixel);
        }

        private static Rectangle OpaqueBounds(Bitmap bitmap)
        {
            int minX = bitmap.Width, minY = bitmap.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < bitmap.Height; y += 2)
            {
                for (int x = 0; x < bitmap.Width; x += 2)
                {
                    if (bitmap.GetPixel(x, y).A > 25)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            return maxX < 0 ? Rectangle.Empty : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }

        private static void SaveJpeg(Bitmap bitmap, string path)
        {
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
            using (var parameters = new EncoderParameters(1))
            {
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
                bitmap.Save(path, codec, parameters);
            }
        }

        private static Bitmap LoadBitmap(string path)
        {
            try
            {
                using (var stream = new MemoryStream(File.ReadAllBytes(path)))
                using (var image = Image.FromStream(stream))
                {
                    return new Bitmap(image);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Bitmap Download(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }
            try
            {
                using (var client = Http.CreateClient())
                using (var stream = new MemoryStream(client.DownloadData(url)))
                using (var image = Image.FromStream(stream))
                {
                    return new Bitmap(image);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---------- TheGamesDB lookup (fan art + clear logo) ----------

        private static readonly Regex Candidate = new Regex("game\\.php\\?id=(\\d+)\">[\\s\\S]*?<p>([^<]+)</p>[\\s\\S]*?<p class=\"text-muted\">([^<]+)</p>", RegexOptions.Compiled);
        private static readonly Regex FanartRx = new Regex("https://cdn\\.thegamesdb\\.net/images/original/fanart/[^\"' ]+\\.(?:jpg|png)", RegexOptions.Compiled);
        private static readonly Regex LogoRx = new Regex("https://cdn\\.thegamesdb\\.net/images/original/clearlogo/[^\"' ]+\\.png", RegexOptions.Compiled);

        private static void FindArt(string title, out string fanart, out string logo)
        {
            fanart = null;
            logo = null;
            try
            {
                var queries = new List<string> { title };
                var shortTitle = Regex.Split(title, "\\s*(?::|\\s-\\s)\\s*")[0];
                if (shortTitle.Length > 2 && shortTitle != title)
                {
                    queries.Add(shortTitle);
                }
                using (var client = Http.CreateClient())
                {
                    foreach (string query in queries)
                    {
                        string html = client.DownloadString("https://thegamesdb.net/search.php?name=" + Uri.EscapeDataString(query));
                        string wanted = TitleNormalizer.Normalize(query);
                        // the short query ("EyeToy" for "EyeToy: Monkey Mania") must still match the rest of the
                        // title, otherwise it finds the series / hardware instead of the game
                        string rest = query == title ? string.Empty : TitleNormalizer.Normalize(title.Substring(Math.Min(title.Length, query.Length)));
                        var candidates = Candidate.Matches(html).Cast<Match>()
                            .Select(m => new { Id = m.Groups[1].Value, Title = WebUtility.HtmlDecode(m.Groups[2].Value.Trim()), Platform = m.Groups[3].Value })
                            .Where(c => TitleNormalizer.Normalize(c.Title).Contains(wanted) && (rest.Length == 0 || TitleNormalizer.Normalize(c.Title).Contains(rest)))
                            .OrderBy(c => c.Platform.Contains("Playstation 2") ? 0 : 1)
                            .ThenBy(c => Math.Abs(TitleNormalizer.Normalize(c.Title).Length - wanted.Length))
                            .Take(6)
                            .ToList();
                        foreach (var c in candidates)
                        {
                            string page = client.DownloadString("https://thegamesdb.net/game.php?id=" + c.Id);
                            if (fanart == null)
                            {
                                var f = FanartRx.Match(page);
                                if (f.Success)
                                {
                                    fanart = f.Value;
                                }
                            }
                            if (logo == null)
                            {
                                var l = LogoRx.Match(page);
                                if (l.Success)
                                {
                                    logo = l.Value;
                                }
                            }
                            if (fanart != null && logo != null)
                            {
                                return;
                            }
                        }
                        if (fanart != null)
                        {
                            return;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.Warn(e, "TheGamesDB lookup failed for " + title);
            }
        }
    }
}
