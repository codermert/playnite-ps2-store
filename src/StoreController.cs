using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace BDSoftPS2Store
{
    // View model + window logic for the store.
    public class StoreController : ObservableObject
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly IPlayniteAPI api;
        private readonly string pluginDir;
        private readonly string dataDir;

        private List<StoreGame> allGames;
        private FavoritesService favorites;
        private WikipediaService wikipedia;
        private CoverService covers;
        private LibraryService library;
        private ExternalLinksService links;

        private Window window;
        private FrameworkElement root;
        private ListBox gameList;
        private ListBox tabList;
        private TextBox searchBox;
        private Gamepad gamepad;
        private DispatcherTimer searchTimer;
        private readonly Dictionary<Key, DateTime> lastKeyDown = new Dictionary<Key, DateTime>();

        private List<StoreGame> visibleGames = new List<StoreGame>();
        private StoreGame selectedGame;
        private StoreGame detailGame;
        private StoreTab selectedTab;
        private string searchText = string.Empty;
        private bool isDetailOpen;
        private string statusText;

        public StoreController(IPlayniteAPI api, string pluginDir, string dataDir)
        {
            this.api = api;
            this.pluginDir = pluginDir;
            this.dataDir = dataDir;

            Tabs = new List<StoreTab>
            {
                new StoreTab { Key = "all", Label = "All" },
                new StoreTab { Key = "fav", Label = "Favorites" },
                new StoreTab { Key = "lib", Label = "Library" },
                new StoreTab { Key = "pc", Label = "PC'de var" },
                new StoreTab { Key = "Action", Label = "Action" },
                new StoreTab { Key = "Adventure", Label = "Adventure" },
                new StoreTab { Key = "RPG", Label = "RPG" },
                new StoreTab { Key = "Racing", Label = "Racing" },
                new StoreTab { Key = "Fighting", Label = "Fighting" },
                new StoreTab { Key = "Sports", Label = "Sports" },
                new StoreTab { Key = "Horror", Label = "Horror" }
            };
            selectedTab = Tabs[0];

            OpenDetailCommand = new RelayCommand(p => OpenDetail(p as StoreGame ?? SelectedGame));
            CloseDetailCommand = new RelayCommand(p => CloseDetail());
            ToggleFavoriteCommand = new RelayCommand(p => ToggleFavorite(p as StoreGame ?? DetailGame ?? SelectedGame));
            AddToLibraryCommand = new RelayCommand(p => AddToLibrary(DetailGame), p => DetailGame != null && !DetailGame.InLibrary);
            FindFileCommand = new RelayCommand(p => FindFile(DetailGame), p => DetailGame != null);
            InstallCommand = new RelayCommand(p => StatusText = OpenExternalUrl(DetailGame), p => DetailGame != null);
            PlayCommand = new RelayCommand(p => Play(DetailGame), p => DetailGame != null && DetailGame.IsPlayable);
            CloseCommand = new RelayCommand(p => CloseWindow());
            ClearSearchCommand = new RelayCommand(p => SearchText = string.Empty);
        }

        public List<StoreTab> Tabs { get; private set; }
        public ICommand OpenDetailCommand { get; private set; }
        public ICommand CloseDetailCommand { get; private set; }
        public ICommand ToggleFavoriteCommand { get; private set; }
        public ICommand AddToLibraryCommand { get; private set; }
        public ICommand FindFileCommand { get; private set; }
        public ICommand InstallCommand { get; private set; }
        public ICommand PlayCommand { get; private set; }
        public ICommand CloseCommand { get; private set; }
        public ICommand ClearSearchCommand { get; private set; }

        public List<StoreGame> VisibleGames
        {
            get { return visibleGames; }
            private set
            {
                visibleGames = value;
                OnPropertyChanged("VisibleGames");
                OnPropertyChanged("ResultText");
                OnPropertyChanged("IsEmpty");
            }
        }

        public bool IsEmpty { get { return visibleGames.Count == 0; } }

        public string ResultText
        {
            get
            {
                string scope = selectedTab == null || selectedTab.Key == "all" ? "PlayStation 2 oyunları" : selectedTab.Label;
                int onPc = visibleGames.Count(g => g.IsOnPc);
                string text = searchText.Trim().Length > 0
                    ? "\"" + searchText.Trim() + "\" için " + visibleGames.Count + " sonuç"
                    : scope + "  ·  " + visibleGames.Count + " oyun";
                return text + "  ·  " + onPc + " tanesi PC'de var, " + (visibleGames.Count - onPc) + " tanesi PC'de yok";
            }
        }

        public StoreTab SelectedTab
        {
            get { return selectedTab; }
            set
            {
                if (value == null || value == selectedTab)
                {
                    return;
                }
                selectedTab = value;
                OnPropertyChanged("SelectedTab");
                ApplyFilter();
            }
        }

        public string SearchText
        {
            get { return searchText; }
            set
            {
                searchText = value ?? string.Empty;
                OnPropertyChanged("SearchText");
                OnPropertyChanged("HasSearchText");
                if (searchTimer != null)
                {
                    searchTimer.Stop();
                    searchTimer.Start();
                }
            }
        }

        public bool HasSearchText { get { return searchText.Length > 0; } }

        public StoreGame SelectedGame
        {
            get { return selectedGame; }
            set
            {
                if (value == selectedGame)
                {
                    return;
                }
                selectedGame = value;
                OnPropertyChanged("SelectedGame");
                OnPropertyChanged("HasSelection");
            }
        }

        public bool HasSelection { get { return selectedGame != null; } }

        public StoreGame DetailGame
        {
            get { return detailGame; }
            private set
            {
                detailGame = value;
                OnPropertyChanged("DetailGame");
            }
        }

        public bool IsDetailOpen
        {
            get { return isDetailOpen; }
            private set
            {
                isDetailOpen = value;
                OnPropertyChanged("IsDetailOpen");
            }
        }

        public string StatusText
        {
            get { return statusText; }
            private set
            {
                statusText = value;
                OnPropertyChanged("StatusText");
                OnPropertyChanged("HasStatus");
            }
        }

        public bool HasStatus { get { return !string.IsNullOrEmpty(statusText); } }

        public void Show()
        {
            if (window != null)
            {
                window.Activate();
                return;
            }

            EnsureLoaded();
            SyncLibrary();
            var dispatcher = Application.Current.Dispatcher;

            searchTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(220) };
            searchTimer.Tick += (s, e) =>
            {
                searchTimer.Stop();
                ApplyFilter();
            };

            CreateWindow();
            ApplyFilter();
            window.Show();
            window.Activate();
            FocusGameListLater();
        }

        public void EnsureLoaded()
        {
            if (allGames != null)
            {
                return;
            }
            var dispatcher = Application.Current.Dispatcher;
            {
                string cacheRoot = Path.Combine(dataDir, "cache");
                Directory.CreateDirectory(cacheRoot);
                favorites = new FavoritesService(dataDir);
                wikipedia = new WikipediaService(cacheRoot);
                covers = new CoverService(cacheRoot, wikipedia, dispatcher);
                covers.FallbackPath = GetLibraryCover;
                library = new LibraryService(api);
                links = new ExternalLinksService(dataDir);
                covers.CoverOverride = links.GetCover;
                allGames = CatalogService.Load(pluginDir);
                foreach (var game in allGames)
                {
                    game.CoverLoader = covers.Request;
                    game.IsFavorite = favorites.Contains(game.Entry.Id);
                }
                // list every catalog game in games.json (keeps existing values), off the UI thread
                var catalog = allGames;
                var linkService = links;
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        linkService.EnsureCatalog(catalog);
                    }
                    catch (Exception e)
                    {
                        logger.Error(e, "games.json could not be updated");
                    }
                });
            }
        }

        // ---------- inline (theme page) API: no window ----------

        public List<StoreGame> Search(string text, int max)
        {
            EnsureLoaded();
            SyncLibrary();
            string[] terms = (text ?? string.Empty).ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0)
            {
                return new List<StoreGame>();
            }
            string first = terms[0];
            return allGames.Where(g => terms.All(t => g.Matches(t)))
                .OrderBy(g => g.IsOnPc ? 0 : g.InLibrary ? 1 : 2)
                .ThenBy(g => string.IsNullOrEmpty(g.Entry.Serial) ? 1 : 0)
                .ThenBy(g => g.IsRich ? 0 : 1)
                .ThenBy(g => g.SearchText.StartsWith(first) || g.AcronymStartsWith(first) ? 0 : 1)
                .ThenBy(g => g.SortKey)
                .Take(max)
                .ToList();
        }

        public string AskSearchText(string current)
        {
            var result = api.Dialogs.SelectString("Oyun adı yazın (ör. dirge)", "PS2 Store'da ara", current ?? string.Empty);
            return result != null && result.Result ? result.SelectedString : null;
        }

        public string AddGameToLibrary(StoreGame game)
        {
            EnsureLoaded();
            StatusText = null;
            if (game.InLibrary)
            {
                return "Bu oyun zaten kütüphanede.";
            }
            AddToLibrary(game);
            return StatusText;
        }

        public string LinkGameFile(StoreGame game)
        {
            EnsureLoaded();
            StatusText = null;
            FindFile(game);
            return StatusText;
        }

        public const string NoExternalUrlMessage = "Bu oyun için harici bağlantı tanımlanmamış.";

        // "Kur": opens the game's external URL from games.json in the default browser.
        // Nothing is downloaded or run; Playnite stays on the store page.
        public string OpenExternalUrl(StoreGame game)
        {
            EnsureLoaded();
            if (game == null)
            {
                return NoExternalUrlMessage;
            }
            game.ExternalUrl = links.GetUrl(game);
            Uri uri;
            if (string.IsNullOrWhiteSpace(game.ExternalUrl) ||
                !Uri.TryCreate(game.ExternalUrl, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return NoExternalUrlMessage;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                return "Bağlantı tarayıcıda açıldı.";
            }
            catch (Exception e)
            {
                logger.Error(e, "External URL could not be opened");
                return "Bağlantı açılamadı: " + e.Message;
            }
        }

        public void PlayGame(StoreGame game)
        {
            if (game != null && game.InLibrary && game.IsPlayable)
            {
                api.StartGame(game.LibraryGameId);
            }
        }

        private readonly Random random = new Random();

        // A fresh random pick of store games that are not in the library (titles with full data first).
        public List<StoreGame> GetRandomStoreGames(int count)
        {
            return GetRandomStoreGames(count, null);
        }

        public List<StoreGame> GetRandomStoreGames(int count, ICollection<StoreGame> exclude)
        {
            EnsureLoaded();
            SyncLibrary();
            Func<StoreGame, bool> allowed = g => !g.InLibrary && (exclude == null || !exclude.Contains(g));
            var candidates = allGames.Where(g => allowed(g) && g.IsRich && !string.IsNullOrEmpty(g.Entry.Wiki)).ToList();
            if (candidates.Count < count)
            {
                candidates = allGames.Where(g => allowed(g) && !string.IsNullOrEmpty(g.Entry.Serial)).ToList();
            }
            return candidates.OrderBy(g => random.Next()).Take(count).ToList();
        }

        // Opens the store with the search active (theme search button).
        public void ShowSearch()
        {
            Show();
            if (window != null)
            {
                window.Dispatcher.BeginInvoke(new Action(Search), DispatcherPriority.ApplicationIdle);
            }
        }

        // Opens the store window directly on a game's detail page.
        public void ShowGame(StoreGame game)
        {
            Show();
            if (game != null)
            {
                SelectedGame = game;
                if (gameList != null)
                {
                    gameList.ScrollIntoView(game);
                }
                OpenDetail(game);
            }
        }

        private string GetLibraryCover(StoreGame game)
        {
            if (!game.InLibrary)
            {
                return null;
            }
            var libGame = api.Database.Games.Get(game.LibraryGameId);
            if (libGame == null || string.IsNullOrEmpty(libGame.CoverImage))
            {
                return null;
            }
            return api.Database.GetFullFilePath(libGame.CoverImage);
        }

        public string GetCachedCoverPath(StoreGame game)
        {
            EnsureLoaded();
            return covers.GetCachedPath(game);
        }

        // Full path of the Playnite background image of the matching library game, if any.
        public string GetLibraryBackground(StoreGame game)
        {
            if (!game.InLibrary)
            {
                return null;
            }
            var libGame = api.Database.Games.Get(game.LibraryGameId);
            if (libGame == null || string.IsNullOrEmpty(libGame.BackgroundImage))
            {
                return null;
            }
            string path = api.Database.GetFullFilePath(libGame.BackgroundImage);
            return !string.IsNullOrEmpty(path) && File.Exists(path) ? path : null;
        }

        public async System.Threading.Tasks.Task<string> GetShortDescriptionAsync(StoreGame game)
        {
            EnsureLoaded();
            try
            {
                var summary = await wikipedia.GetSummaryAsync(game);
                return summary == null ? null : summary.Extract;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void SyncLibrary()
        {
            library.Refresh();
            foreach (var game in allGames)
            {
                var libGame = library.Find(game);
                game.LibraryGameId = libGame == null ? Guid.Empty : libGame.Id;
                game.IsPlayable = library.IsPlayable(libGame);
            }
        }

        private void CreateWindow()
        {
            using (var stream = GetType().Assembly.GetManifestResourceStream("BDSoftPS2Store.StoreView.xaml"))
            {
                root = (FrameworkElement)XamlReader.Load(stream);
            }
            root.DataContext = this;
            gameList = (ListBox)root.FindName("GameList");
            tabList = (ListBox)root.FindName("TabList");
            searchBox = (TextBox)root.FindName("SearchBox");

            gameList.PreviewMouseLeftButtonUp += GameList_PreviewMouseLeftButtonUp;
            gameList.PreviewMouseMove += GameList_PreviewMouseMove;
            gameList.PreviewMouseWheel += GameList_PreviewMouseWheel;

            window = api.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowCloseButton = true,
                ShowMaximizeButton = true,
                ShowMinimizeButton = false
            });
            window.Title = "BD Soft PS2 Store";
            window.Background = new SolidColorBrush(Color.FromRgb(8, 10, 16));
            window.Content = root;

            var owner = api.Dialogs.GetCurrentAppWindow();
            if (owner != null && owner != window)
            {
                window.Owner = owner;
            }

            if (api.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
            {
                window.WindowStyle = WindowStyle.None;
                window.ResizeMode = ResizeMode.NoResize;
                window.WindowState = WindowState.Maximized;
            }
            else
            {
                window.Width = 1600;
                window.Height = 900;
                window.WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            }

            window.PreviewKeyDown += Window_PreviewKeyDown;
            window.Closed += Window_Closed;

            gamepad = new Gamepad(window.Dispatcher, () => window != null && window.IsActive, OnPad);
            gamepad.Start();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (gamepad != null)
            {
                gamepad.Dispose();
                gamepad = null;
            }
            if (searchTimer != null)
            {
                searchTimer.Stop();
            }
            var owner = window.Owner;
            window = null;
            root = null;
            IsDetailOpen = false;
            DetailGame = null;
            if (owner != null)
            {
                owner.Activate();
            }
        }

        private void CloseWindow()
        {
            if (window != null)
            {
                window.Close();
            }
        }

        private void ApplyFilter()
        {
            if (allGames == null)
            {
                return;
            }

            IEnumerable<StoreGame> query = allGames;
            string key = selectedTab == null ? "all" : selectedTab.Key;
            if (key == "fav")
            {
                query = query.Where(g => g.IsFavorite);
            }
            else if (key == "lib")
            {
                query = query.Where(g => g.InLibrary);
            }
            else if (key == "pc")
            {
                query = query.Where(g => g.IsOnPc);
            }
            else if (key != "all")
            {
                query = query.Where(g => g.HasCategory(key));
            }

            string[] terms = searchText.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length > 0)
            {
                query = query.Where(g => terms.All(t => g.Matches(t)));
            }

            List<StoreGame> list;
            if (terms.Length > 0)
            {
                string first = terms[0];
                list = query.OrderBy(g => g.IsOnPc ? 0 : g.InLibrary ? 1 : 2).ThenBy(g => string.IsNullOrEmpty(g.Entry.Serial) ? 1 : 0).ThenBy(g => g.IsRich ? 0 : 1).ThenBy(g => g.SearchText.StartsWith(first) || g.AcronymStartsWith(first) ? 0 : 1).ThenBy(g => g.SortKey).ToList();
            }
            else
            {
                list = query.OrderBy(g => g.InLibrary ? 0 : 1).ThenBy(g => g.IsRich ? 0 : 1).ThenBy(g => g.SortKey).ToList();
            }

            VisibleGames = list;
            if (selectedGame == null || !list.Contains(selectedGame))
            {
                SelectedGame = list.FirstOrDefault();
            }
            if (gameList != null && selectedGame != null)
            {
                gameList.ScrollIntoView(selectedGame);
            }
        }

        private async void OpenDetail(StoreGame game)
        {
            if (game == null)
            {
                return;
            }
            SelectedGame = game;
            DetailGame = game;
            StatusText = null;
            IsDetailOpen = true;
            FocusDetailLater();

            if (game.Description == null)
            {
                game.Description = "Açıklama yükleniyor...";
                try
                {
                    var summary = await wikipedia.GetSummaryAsync(game);
                    game.Description = summary != null
                        ? summary.Extract + "\n\nKaynak: Wikipedia (CC BY-SA)"
                        : "Bu oyun için açıklama bulunamadı.";
                }
                catch (Exception e)
                {
                    logger.Warn(e, "Description load failed");
                    game.Description = "Açıklama yüklenemedi (internet bağlantısını kontrol edin).";
                }
            }
        }

        private void CloseDetail()
        {
            IsDetailOpen = false;
            DetailGame = null;
            FocusGameListLater();
        }

        public void ToggleFavorite(StoreGame game)
        {
            if (game == null)
            {
                return;
            }
            game.IsFavorite = favorites.Toggle(game.Entry.Id);
            if (game.InLibrary)
            {
                var libGame = api.Database.Games.Get(game.LibraryGameId);
                if (libGame != null && libGame.Favorite != game.IsFavorite)
                {
                    libGame.Favorite = game.IsFavorite;
                    api.Database.Games.Update(libGame);
                }
            }
            if (selectedTab != null && selectedTab.Key == "fav" && !IsDetailOpen)
            {
                ApplyFilter();
            }
        }

        private Game AddToLibrary(StoreGame game)
        {
            if (game == null)
            {
                return null;
            }
            if (game.InLibrary)
            {
                return api.Database.Games.Get(game.LibraryGameId);
            }
            try
            {
                var libGame = library.AddToLibrary(game, covers.GetCachedPath(game));
                game.LibraryGameId = libGame.Id;
                game.IsPlayable = false;
                StatusText = "Kütüphaneye eklendi. Oynamak için \"Oyunu Bul\" ile kendi oyun dosyanı seç.";
                CommandManager.InvalidateRequerySuggested();
                return libGame;
            }
            catch (Exception e)
            {
                logger.Error(e, "Add to library failed");
                StatusText = "Kütüphaneye eklenemedi: " + e.Message;
                return null;
            }
        }

        private void FindFile(StoreGame game)
        {
            if (game == null)
            {
                return;
            }
            string path = api.Dialogs.SelectFile(LibraryService.RomFilter);
            if (window != null)
            {
                window.Activate();
            }
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var libGame = game.InLibrary ? api.Database.Games.Get(game.LibraryGameId) : AddToLibrary(game);
            if (libGame == null)
            {
                return;
            }

            var target = library.LinkRom(libGame, path);
            game.IsPlayable = library.IsPlayable(api.Database.Games.Get(libGame.Id));
            StatusText = target != null
                ? "Oyun dosyası bağlandı: " + Path.GetFileName(path) + "  ·  Emülatör: " + target.Emulator.Name + " (" + target.ProfileName + ")"
                : "Oyun dosyası bağlandı, ancak PS2 emülatörü bulunamadı. Playnite'ta Kitaplık > Emülatörler bölümünden PCSX2 ekleyin.";
            CommandManager.InvalidateRequerySuggested();
            FocusDetailLater();
        }

        private void Play(StoreGame game)
        {
            if (game == null || !game.InLibrary)
            {
                return;
            }
            Guid id = game.LibraryGameId;
            CloseWindow();
            api.StartGame(id);
        }

        // ---------- input ----------

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            lastKeyDown[e.Key] = DateTime.UtcNow;
            bool inSearch = searchBox != null && searchBox.IsKeyboardFocusWithin;
            bool inList = gameList != null && gameList.IsKeyboardFocusWithin;
            bool inTabs = tabList != null && tabList.IsKeyboardFocusWithin;

            if (e.Key == Key.Escape)
            {
                if (IsDetailOpen)
                {
                    CloseDetail();
                }
                else if (inSearch && HasSearchText)
                {
                    SearchText = string.Empty;
                }
                else if (inSearch)
                {
                    FocusGameList();
                }
                else
                {
                    CloseWindow();
                }
                e.Handled = true;
            }
            else if (IsDetailOpen)
            {
                if (e.Key == Key.F && !inSearch)
                {
                    ToggleFavorite(DetailGame);
                    e.Handled = true;
                }
            }
            else if ((e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) || e.Key == Key.F3)
            {
                FocusSearch();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && inList)
            {
                OpenDetail(SelectedGame);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && inSearch)
            {
                FocusGameList();
                e.Handled = true;
            }
            else if (e.Key == Key.Down && (inTabs || inSearch))
            {
                FocusGameList();
                e.Handled = true;
            }
            else if (e.Key == Key.Up && inList)
            {
                FocusTabs();
                e.Handled = true;
            }
            else if (e.Key == Key.F && inList)
            {
                ToggleFavorite(SelectedGame);
                e.Handled = true;
            }
        }

        // Some setups already turn the controller into key presses (Playnite or the DKG helper).
        // Skip a pad press when the matching key arrived just before it.
        private bool AlreadyHandledByKeyboard(PadButton button)
        {
            Key key;
            switch (button)
            {
                case PadButton.Up: key = Key.Up; break;
                case PadButton.Down: key = Key.Down; break;
                case PadButton.Left: key = Key.Left; break;
                case PadButton.Right: key = Key.Right; break;
                case PadButton.A: key = Key.Enter; break;
                case PadButton.B: key = Key.Escape; break;
                default: return false;
            }
            DateTime time;
            return lastKeyDown.TryGetValue(key, out time) && (DateTime.UtcNow - time).TotalMilliseconds < 200;
        }

        private void OnPad(PadButton button)
        {
            if (window == null || AlreadyHandledByKeyboard(button))
            {
                return;
            }

            if (IsDetailOpen)
            {
                switch (button)
                {
                    case PadButton.Up: MoveFocus(FocusNavigationDirection.Up); break;
                    case PadButton.Down: MoveFocus(FocusNavigationDirection.Down); break;
                    case PadButton.Left: MoveFocus(FocusNavigationDirection.Left); break;
                    case PadButton.Right: MoveFocus(FocusNavigationDirection.Right); break;
                    case PadButton.A: InvokeFocusedButton(); break;
                    case PadButton.B: CloseDetail(); break;
                    case PadButton.Y: ToggleFavorite(DetailGame); break;
                }
                return;
            }

            bool inTabs = tabList.IsKeyboardFocusWithin;
            bool inSearch = searchBox.IsKeyboardFocusWithin;
            switch (button)
            {
                case PadButton.Left:
                    if (inTabs) { ShiftTab(-1); } else if (!inSearch) { MoveSelection(-1); }
                    break;
                case PadButton.Right:
                    if (inTabs) { ShiftTab(1); } else if (!inSearch) { MoveSelection(1); }
                    break;
                case PadButton.Up:
                    if (!inTabs && !inSearch) { FocusTabs(); }
                    break;
                case PadButton.Down:
                    if (inTabs || inSearch) { FocusGameList(); }
                    break;
                case PadButton.A:
                    if (inTabs || inSearch) { FocusGameList(); } else { OpenDetail(SelectedGame); }
                    break;
                case PadButton.B:
                    if (inTabs || inSearch) { FocusGameList(); } else { CloseWindow(); }
                    break;
                case PadButton.X:
                    Search();
                    break;
                case PadButton.Y:
                    ToggleFavorite(SelectedGame);
                    break;
                case PadButton.LB:
                    ShiftTab(-1);
                    break;
                case PadButton.RB:
                    ShiftTab(1);
                    break;
            }
        }

        private void Search()
        {
            if (api.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
            {
                var result = api.Dialogs.SelectString("Oyun adı yazın (ör. dirge)", "PS2 Store'da ara", SearchText);
                if (window != null)
                {
                    window.Activate();
                }
                if (result != null && result.Result)
                {
                    SearchText = result.SelectedString;
                    searchTimer.Stop();
                    ApplyFilter();
                    FocusGameListLater();
                }
            }
            else
            {
                FocusSearch();
            }
        }

        private void ShiftTab(int delta)
        {
            int index = Tabs.IndexOf(selectedTab) + delta;
            if (index < 0 || index >= Tabs.Count)
            {
                return;
            }
            SelectedTab = Tabs[index];
            if (tabList.IsKeyboardFocusWithin)
            {
                FocusTabs();
            }
        }

        private void MoveSelection(int delta)
        {
            if (visibleGames.Count == 0)
            {
                return;
            }
            int index = selectedGame == null ? 0 : visibleGames.IndexOf(selectedGame) + delta;
            index = Math.Max(0, Math.Min(visibleGames.Count - 1, index));
            SelectedGame = visibleGames[index];
            FocusGameList();
        }

        private void MoveFocus(FocusNavigationDirection direction)
        {
            var element = Keyboard.FocusedElement as UIElement;
            if (element != null)
            {
                element.MoveFocus(new TraversalRequest(direction));
            }
        }

        private void InvokeFocusedButton()
        {
            var button = Keyboard.FocusedElement as Button;
            if (button == null || !button.IsEnabled)
            {
                return;
            }
            if (button.Command != null)
            {
                if (button.Command.CanExecute(button.CommandParameter))
                {
                    button.Command.Execute(button.CommandParameter);
                }
            }
            else
            {
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
        }

        private void FocusSearch()
        {
            if (searchBox != null)
            {
                searchBox.Focus();
                searchBox.SelectAll();
            }
        }

        private void FocusTabs()
        {
            if (tabList == null)
            {
                return;
            }
            tabList.UpdateLayout();
            var item = tabList.ItemContainerGenerator.ContainerFromItem(selectedTab) as ListBoxItem;
            if (item != null)
            {
                item.Focus();
            }
        }

        private void FocusGameList()
        {
            if (gameList == null || selectedGame == null)
            {
                return;
            }
            gameList.ScrollIntoView(selectedGame);
            gameList.UpdateLayout();
            var item = gameList.ItemContainerGenerator.ContainerFromItem(selectedGame) as ListBoxItem;
            if (item != null)
            {
                item.Focus();
            }
            else
            {
                gameList.Focus();
            }
        }

        private void FocusGameListLater()
        {
            if (window != null)
            {
                window.Dispatcher.BeginInvoke(new Action(FocusGameList), DispatcherPriority.Input);
            }
        }

        private void FocusDetailLater()
        {
            if (window == null)
            {
                return;
            }
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                string[] order = { "BtnPlay", "BtnLibrary", "BtnFind", "BtnFavorite" };
                foreach (string name in order)
                {
                    var button = root == null ? null : root.FindName(name) as Button;
                    if (button != null && button.IsVisible && button.IsEnabled)
                    {
                        button.Focus();
                        return;
                    }
                }
            }), DispatcherPriority.Input);
        }

        // ---------- mouse ----------

        private static ListBoxItem ItemFromSource(object source)
        {
            var element = source as DependencyObject;
            while (element != null && !(element is ListBoxItem))
            {
                element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
            }
            return element as ListBoxItem;
        }

        private void GameList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var item = ItemFromSource(e.OriginalSource);
            if (item != null)
            {
                OpenDetail(item.DataContext as StoreGame);
                e.Handled = true;
            }
        }

        private void GameList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            var item = ItemFromSource(e.OriginalSource);
            var game = item == null ? null : item.DataContext as StoreGame;
            if (game != null && game != selectedGame)
            {
                SelectedGame = game;
            }
        }

        private void GameList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scroller = gameList.Template == null ? null : gameList.Template.FindName("ListScroller", gameList) as ScrollViewer;
            if (scroller == null)
            {
                scroller = FindChild<ScrollViewer>(gameList);
            }
            if (scroller == null)
            {
                return;
            }
            for (int i = 0; i < 3; i++)
            {
                if (e.Delta < 0)
                {
                    scroller.LineRight();
                }
                else
                {
                    scroller.LineLeft();
                }
            }
            e.Handled = true;
        }

        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var typed = child as T;
                if (typed != null)
                {
                    return typed;
                }
                var nested = FindChild<T>(child);
                if (nested != null)
                {
                    return nested;
                }
            }
            return null;
        }
    }
}
