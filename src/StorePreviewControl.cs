using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Controls;

namespace BDSoftPS2Store
{
    // The PS2 store, rendered inside the PS5ish theme page that is shown while the theme's
    // PlayStation Store tile is focused (theme element: BDSoftPS2Store_StorePreview).
    // Everything happens on this page (no extra window). It is mouse driven and never takes
    // keyboard focus, because the theme hides the page as soon as the Store tile loses focus.
    public class StorePreviewControl : PluginUserControl
    {
        public static StorePreviewControl Current { get; private set; }

        private const double DesignWidth = 2580;
        private const double DesignHeight = 1080;
        private const double ContentLeft = 500;
        private const double VisibleRight = 2250; // the theme centers 2580px; 1920px of it is on screen
        private const double HeaderTop = 320;     // below the theme's top bar and game row
        private const double CardWidth = 180;
        private const double CardSpacing = 24;
        private const double RowPadding = 22;
        private const double HeroWidth = 230;
        private const double HeroHeight = 315;
        private const double HeroLeft = 1996;
        private const double HeroTop = HeaderTop + 2;
        private const string IconFont = "Segoe MDL2 Assets";

        private readonly StoreController store;
        private readonly UiSounds sounds;
        private readonly Image art;
        private readonly Image artBlurred;
        private readonly Border heroCover;
        private readonly ImageBrush heroCoverBrush;
        private readonly TextBlock title;
        private readonly TextBlock subtitle;
        private readonly ContentControl subtitleIcon;
        private readonly TextBlock meta;
        private readonly TextBlock description;
        private readonly StackPanel actions;
        private readonly TextBlock status;
        private readonly TextBlock rowTitle;
        private readonly Border clearSearchButton;
        private readonly Border searchButton;
        private readonly ScrollViewer scroller;
        private readonly StackPanel row;
        private readonly DispatcherTimer rotateTimer;
        private readonly DispatcherTimer leaveTimer;
        private readonly DispatcherTimer guardTimer;
        private Grid pageRoot;

        private List<StoreGame> featured = new List<StoreGame>();
        private int featuredIndex;
        private StoreGame shown;
        private StoreGame pinned;
        private string searchText;
        private bool built;

        public StorePreviewControl(IPlayniteAPI api, StoreController store)
        {
            this.store = store;
            Current = this;
            sounds = new UiSounds(
                Path.Combine(api.Paths.ConfigurationPath, "Themes", "Fullscreen", "PS5ish_676e10ec-adfe-48d8-a1bd-4d5771b5a2ca", "audio"),
                Path.Combine(api.Paths.ConfigurationPath, "fullscreenConfig.json"));
            Width = DesignWidth;
            Height = DesignHeight;
            Focusable = false;

            var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(18, 18, 20)), ClipToBounds = true };

            var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.0));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 0.38));
            artBlurred = new Image
            {
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Right,
                Width = 1980,
                Opacity = 0,
                OpacityMask = fade,
                Effect = new BlurEffect { Radius = 45 }
            };
            art = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Right, Width = 1980, OpacityMask = fade };
            root.Children.Add(artBlurred);
            root.Children.Add(art);
            root.Children.Add(new Border
            {
                Background = new LinearGradientBrush(Color.FromArgb(0, 18, 18, 20), Color.FromArgb(235, 18, 18, 20), 90),
                IsHitTestVisible = false
            });
            var leftShade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            leftShade.GradientStops.Add(new GradientStop(Color.FromArgb(170, 18, 18, 20), 0.15));
            leftShade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 18, 18, 20), 0.6));
            root.Children.Add(new Border { Background = leftShade, IsHitTestVisible = false });

            heroCoverBrush = new ImageBrush { Stretch = Stretch.UniformToFill };
            heroCover = new Border
            {
                Width = HeroWidth,
                Height = HeroHeight,
                CornerRadius = new CornerRadius(16),
                Background = heroCoverBrush,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(HeroLeft, HeroTop, 0, 0),
                Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.7 },
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            // the same rotating theme ring as the selected store card, around the big cover
            var heroRotation = new RotateTransform { CenterX = 0.5, CenterY = 0.5 };
            var heroRingBrush = new LinearGradientBrush { RelativeTransform = heroRotation };
            heroRingBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, 0x2a, 0x30, 0x39), 0));
            heroRingBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xa2, 0x89, 0x9d), 0.8));
            heroRingBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xdb, 0xbd, 0xa1), 1));
            var heroRing = new Border
            {
                Width = HeroWidth + 14,
                Height = HeroHeight + 14,
                CornerRadius = new CornerRadius(22),
                BorderThickness = new Thickness(3.5),
                BorderBrush = heroRingBrush,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(HeroLeft - 7, HeroTop - 7, 0, 0),
                IsHitTestVisible = false
            };
            var heroGlow = new Border
            {
                Width = HeroWidth + 14,
                Height = HeroHeight + 14,
                CornerRadius = new CornerRadius(22),
                BorderThickness = new Thickness(3.5),
                BorderBrush = new SolidColorBrush(Color.FromArgb(150, 0xdb, 0xbd, 0xa1)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(HeroLeft - 7, HeroTop - 7, 0, 0),
                Effect = new DropShadowEffect { Color = Color.FromRgb(0xdb, 0xbd, 0xa1), BlurRadius = 26, ShadowDepth = 0, Opacity = 0.55 },
                IsHitTestVisible = false
            };
            heroGlow.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("Visibility") { Source = heroCover });
            root.Children.Add(heroGlow);
            heroRing.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("Visibility") { Source = heroCover });
            heroRotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 359, TimeSpan.FromSeconds(4)) { RepeatBehavior = RepeatBehavior.Forever });
            root.Children.Add(heroRing);
            root.Children.Add(heroCover);

            // ----- header (game / store info) -----
            var page = new StackPanel { Margin = new Thickness(ContentLeft - RowPadding, HeaderTop, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            var header = new StackPanel { Height = 340, Width = 1380, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(RowPadding, 0, 0, 0) };
            title = new TextBlock { FontSize = 46, FontWeight = FontWeights.Light, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
            subtitle = new TextBlock { FontSize = 26, FontWeight = FontWeights.Light, Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center };
            subtitleIcon = new ContentControl { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var subtitleLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0), IsHitTestVisible = false };
            subtitleLine.Children.Add(subtitle);
            subtitleLine.Children.Add(subtitleIcon);
            meta = new TextBlock { FontSize = 21, Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), Margin = new Thickness(0, 10, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
            description = new TextBlock
            {
                FontSize = 20,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.WordEllipsis,
                MaxHeight = 58,
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
            status = new TextBlock { FontSize = 18, Foreground = new SolidColorBrush(Color.FromRgb(159, 208, 255)), Margin = new Thickness(0, 10, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
            header.Children.Add(title);
            header.Children.Add(subtitleLine);
            header.Children.Add(meta);
            header.Children.Add(description);
            header.Children.Add(actions);
            header.Children.Add(status);
            page.Children.Add(header);

            // ----- row header: title + search + arrows -----
            var rowHeader = new DockPanel { Margin = new Thickness(RowPadding, 18, 0, 0), Width = VisibleRight - ContentLeft - 40, HorizontalAlignment = HorizontalAlignment.Left };
            var right = CreateRoundButton("", () => ScrollBy((CardWidth + CardSpacing) * 5));
            var left = CreateRoundButton("", () => ScrollBy(-(CardWidth + CardSpacing) * 5));
            searchButton = CreatePill("", "Ara", () => StartSearch(false));
            clearSearchButton = CreatePill("", "Keşfet", ClearSearch);
            clearSearchButton.Visibility = Visibility.Collapsed;
            foreach (var element in new FrameworkElement[] { right, left, searchButton, clearSearchButton })
            {
                DockPanel.SetDock(element, Dock.Right);
                rowHeader.Children.Add(element);
            }
            rowTitle = new TextBlock { Text = "Keşfet", FontSize = 34, FontWeight = FontWeights.Light, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            rowHeader.Children.Add(rowTitle);
            page.Children.Add(rowHeader);

            row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(RowPadding, 26, 40, 30) };
            scroller = new ScrollViewer
            {
                Style = new Style(typeof(ScrollViewer)), // keep the theme's ScrollViewer style out
                Focusable = false,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Width = VisibleRight - ContentLeft + RowPadding,
                HorizontalAlignment = HorizontalAlignment.Left,
                Content = row,
                Background = Brushes.Transparent
            };
            scroller.PreviewMouseWheel += (s, e) =>
            {
                ScrollBy(-e.Delta * 1.6);
                e.Handled = true;
            };
            scroller.ScrollChanged += (s, e) =>
            {
                if (scroller.ScrollableWidth > 0 && scroller.HorizontalOffset >= scroller.ScrollableWidth - (CardWidth + CardSpacing) * 3)
                {
                    AppendMoreIfNeeded();
                }
            };
            scroller.MouseLeave += (s, e) => leaveTimer.Start();
            scroller.MouseEnter += (s, e) => leaveTimer.Stop();
            page.Children.Add(scroller);
            root.Children.Add(page);

            Content = root;
            pageRoot = root;

            // The theme shows this page while the Store tile has keyboard focus. Some screens (settings,
            // menus, game details) open without moving that focus, so the page also hides itself whenever
            // the tile is not focused in the active window.
            guardTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(120) };
            guardTimer.Tick += (s, e) => ApplyGuard();
            // runs all the time: once the page hides itself it is no longer "visible" to WPF, so it
            // cannot rely on visibility events to come back
            Loaded += (s, e) => guardTimer.Start();
            Unloaded += (s, e) => guardTimer.Stop();

            rotateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
            rotateTimer.Tick += (s, e) => ShowFeatured(featuredIndex + 1);
            leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            leaveTimer.Tick += (s, e) =>
            {
                leaveTimer.Stop();
                if (mode == NavMode.Cards && CurrentCard() != null)
                {
                    ShowGame(CurrentCard().Game);
                }
                else if (pinned != null)
                {
                    ShowGame(pinned);
                }
                else
                {
                    ShowStoreInfo();
                }
            };
            IsVisibleChanged += OnVisibleChanged;
        }

        // ---------- controller / keyboard navigation ----------
        // Playnite sends controller input as key presses to the focused element, which is the Store tile.
        // The tile forwards them here, so the page can be browsed without the tile losing focus.

        private class NavItem
        {
            public StoreGame Game;
            public Action Refresh;
            public Action Activate;
            public Action<bool> Highlight;
        }

        private enum NavMode { None, Cards, Actions, Toolbar }

        private NavMode mode = NavMode.None;
        private int cardIndex;
        private int actionIndex;
        private int toolbarIndex;

        private List<NavItem> CardItems
        {
            get { return row.Children.OfType<FrameworkElement>().Select(c => c.Tag as NavItem).Where(n => n != null).ToList(); }
        }

        private List<NavItem> ActionItems
        {
            get { return actions.Children.OfType<FrameworkElement>().Select(c => c.Tag as NavItem).Where(n => n != null).ToList(); }
        }

        private List<NavItem> ToolbarItems
        {
            get
            {
                var list = new List<NavItem> { (NavItem)searchButton.Tag };
                if (clearSearchButton.Visibility == Visibility.Visible)
                {
                    list.Add((NavItem)clearSearchButton.Tag);
                }
                return list;
            }
        }

        public bool IsBrowsing { get { return mode != NavMode.None; } }

        public void ResetNavigation()
        {
            SetMode(NavMode.None);
        }

        // Returns true when the key was used by the page.
        public bool HandleKey(Key key)
        {
            bool accept = key == Key.Enter || key == Key.Space || key == Key.Return;
            if (accept && mode != NavMode.None)
            {
                sounds.Activate();
            }
            bool back = key == Key.Escape || key == Key.Back || key == Key.BrowserBack;
            switch (mode)
            {
                case NavMode.None:
                    if (key == Key.Down && CardItems.Count > 0)
                    {
                        int index = pinned == null ? -1 : CardItems.FindIndex(n => n.Game == pinned);
                        cardIndex = Math.Max(0, index);
                        SetMode(NavMode.Cards);
                        return true;
                    }
                    return false;

                case NavMode.Cards:
                    if (key == Key.Left || key == Key.Right)
                    {
                        MoveCard(key == Key.Right ? 1 : -1);
                    }
                    else if (key == Key.Up)
                    {
                        toolbarIndex = 0;
                        SetMode(NavMode.Toolbar);
                    }
                    else if (accept)
                    {
                        var card = CurrentCard();
                        if (card != null)
                        {
                            Refresh(card.Game);
                            actionIndex = 0;
                            SetMode(NavMode.Actions);
                        }
                    }
                    else if (back)
                    {
                        SetMode(NavMode.None);
                    }
                    return true;

                case NavMode.Actions:
                    if (key == Key.Left || key == Key.Right)
                    {
                        var items = ActionItems;
                        int previous = actionIndex;
                        HighlightAction(false);
                        actionIndex = Math.Max(0, Math.Min(items.Count - 1, actionIndex + (key == Key.Right ? 1 : -1)));
                        HighlightAction(true);
                        if (actionIndex != previous)
                        {
                            sounds.Navigate();
                        }
                    }
                    else if (accept)
                    {
                        var items = ActionItems;
                        if (actionIndex < items.Count)
                        {
                            items[actionIndex].Activate();
                            // the action rebuilt the buttons
                            actionIndex = Math.Min(actionIndex, Math.Max(0, ActionItems.Count - 1));
                            HighlightAction(true);
                        }
                    }
                    else if (back || key == Key.Down)
                    {
                        SetMode(NavMode.Cards);
                    }
                    return true;

                case NavMode.Toolbar:
                    if (key == Key.Left || key == Key.Right)
                    {
                        var items = ToolbarItems;
                        int previous = toolbarIndex;
                        HighlightToolbar(false);
                        toolbarIndex = Math.Max(0, Math.Min(items.Count - 1, toolbarIndex + (key == Key.Right ? 1 : -1)));
                        HighlightToolbar(true);
                        if (toolbarIndex != previous)
                        {
                            sounds.Navigate();
                        }
                    }
                    else if (accept)
                    {
                        var items = ToolbarItems;
                        var item = toolbarIndex < items.Count ? items[toolbarIndex] : null;
                        SetMode(NavMode.Cards);
                        if (item != null)
                        {
                            item.Activate();
                        }
                        cardIndex = 0;
                        SetMode(CardItems.Count > 0 ? NavMode.Cards : NavMode.None);
                    }
                    else if (key == Key.Down)
                    {
                        SetMode(NavMode.Cards);
                    }
                    else if (key == Key.Up || back)
                    {
                        SetMode(NavMode.None);
                    }
                    return true;
            }
            return false;
        }

        private void SetMode(NavMode newMode)
        {
            if (newMode != mode)
            {
                sounds.Navigate();
            }
            HighlightCard(false);
            HighlightAction(false);
            HighlightToolbar(false);
            mode = newMode;
            if (mode == NavMode.Cards)
            {
                cardIndex = Math.Max(0, Math.Min(CardItems.Count - 1, cardIndex));
                HighlightCard(true);
                var card = CurrentCard();
                if (card != null)
                {
                    ShowGame(card.Game);
                }
            }
            else if (mode == NavMode.Actions)
            {
                HighlightAction(true);
            }
            else if (mode == NavMode.Toolbar)
            {
                HighlightToolbar(true);
            }
        }

        private NavItem CurrentCard()
        {
            var items = CardItems;
            return cardIndex >= 0 && cardIndex < items.Count ? items[cardIndex] : null;
        }

        private void MoveCard(int delta)
        {
            var items = CardItems;
            if (items.Count == 0)
            {
                return;
            }
            if (cardIndex + delta >= items.Count - 3)
            {
                AppendMoreIfNeeded();
                items = CardItems;
            }
            int previous = cardIndex;
            HighlightCard(false);
            cardIndex = Math.Max(0, Math.Min(items.Count - 1, cardIndex + delta));
            HighlightCard(true);
            if (cardIndex != previous)
            {
                sounds.Navigate();
            }
            ShowGame(items[cardIndex].Game);

            // keep the selected card on screen
            double left = cardIndex * (CardWidth + CardSpacing);
            double right = left + CardWidth;
            if (left < scroller.HorizontalOffset)
            {
                scroller.ScrollToHorizontalOffset(left);
            }
            else if (right > scroller.HorizontalOffset + scroller.ViewportWidth - 40)
            {
                scroller.ScrollToHorizontalOffset(right - scroller.ViewportWidth + 40);
            }
        }

        private void HighlightCard(bool on)
        {
            var card = CurrentCard();
            if (card != null && card.Highlight != null)
            {
                card.Highlight(on);
            }
        }

        private void HighlightAction(bool on)
        {
            var items = ActionItems;
            if (actionIndex >= 0 && actionIndex < items.Count)
            {
                items[actionIndex].Highlight(on);
            }
        }

        private void HighlightToolbar(bool on)
        {
            var items = ToolbarItems;
            if (toolbarIndex >= 0 && toolbarIndex < items.Count)
            {
                items[toolbarIndex].Highlight(on);
            }
        }

        private void ApplyGuard()
        {
            bool show = StoreTileControl.TileButton == null || StoreTileControl.IsTileFocused || IsSearching;
            // hide the theme's store box too (it also holds the theme's static store picture)
            var host = Parent as FrameworkElement;
            var box = host == null ? null : host.Parent as FrameworkElement;
            var target = box ?? (FrameworkElement)pageRoot;
            var wanted = show ? Visibility.Visible : Visibility.Hidden;
            if (target.Visibility != wanted)
            {
                target.Visibility = wanted;
            }
            if (!show && mode != NavMode.None)
            {
                SetMode(NavMode.None);
            }
        }

        // While the search box (a Playnite dialog) is open the page should stay as it is.
        public bool IsSearching { get; private set; }

        // ---------- small UI factories ----------

        private static TextBlock Icon(string glyph, double size, Brush color)
        {
            return new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily(IconFont),
                FontSize = size,
                Foreground = color,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        // Availability symbol: on this PC (check), in the library without a file (library), not on this PC (download).
        public static FrameworkElement AvailabilityBadge(StoreGame game, double size)
        {
            string glyph;
            Color background;
            Brush foreground;
            if (game.IsOnPc)
            {
                glyph = "";
                background = Color.FromArgb(240, 29, 185, 84);
                foreground = Brushes.White;
            }
            else if (game.InLibrary)
            {
                glyph = "";
                background = Color.FromArgb(235, 255, 255, 255);
                foreground = new SolidColorBrush(Color.FromRgb(16, 19, 24));
            }
            else
            {
                glyph = "";
                background = Color.FromArgb(190, 10, 12, 16);
                foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));
            }
            return new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size / 2),
                Background = new SolidColorBrush(background),
                Child = Icon(glyph, size * 0.5, foreground),
                IsHitTestVisible = false
            };
        }

        private static Border CreateRoundButton(string glyph, Action click)
        {
            var normal = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            var hover = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));
            var button = new Border
            {
                Width = 52,
                Height = 52,
                CornerRadius = new CornerRadius(26),
                Background = normal,
                Margin = new Thickness(12, 0, 0, 0),
                Cursor = Cursors.Hand,
                Child = Icon(glyph, 20, Brushes.White)
            };
            button.MouseEnter += (s, e) => button.Background = hover;
            button.MouseLeave += (s, e) => button.Background = normal;
            button.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                click();
            };
            return button;
        }

        private static Border CreatePill(string glyph, string text, Action click)
        {
            var normal = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            var hover = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255));
            var dark = new SolidColorBrush(Color.FromRgb(16, 19, 24));
            var label = new TextBlock { Text = text, FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            var icon = Icon(glyph, 18, Brushes.White);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(icon);
            content.Children.Add(label);
            var pill = new Border
            {
                Height = 52,
                CornerRadius = new CornerRadius(26),
                Background = normal,
                Padding = new Thickness(22, 0, 24, 0),
                Margin = new Thickness(12, 0, 0, 0),
                Cursor = Cursors.Hand,
                Child = content
            };
            Action<bool> highlight = on =>
            {
                pill.Background = on ? hover : normal;
                label.Foreground = on ? dark : Brushes.White;
                icon.Foreground = on ? dark : Brushes.White;
            };
            pill.Tag = new NavItem { Activate = click, Highlight = highlight };
            pill.MouseEnter += (s, e) => highlight(true);
            pill.MouseLeave += (s, e) => highlight(false);
            pill.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                click();
            };
            return pill;
        }

        // ---------- lifecycle ----------

        private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                ApplyGuard();
                if (built && searchText == null)
                {
                    ShuffleRow();
                }
                Build();
                if (pinned != null)
                {
                    ShowGame(pinned);
                }
                else
                {
                    ShowStoreInfo();
                }
            }
            else
            {
                rotateTimer.Stop();
                leaveTimer.Stop();
                mode = NavMode.None;
            }
        }

        private void Build()
        {
            if (built)
            {
                return;
            }
            built = true;
            try
            {
                ShuffleRow();
            }
            catch (Exception e)
            {
                LogManager.GetLogger().Error(e, "Store page failed");
            }
        }

        // New random store picks every time the page is opened (library games are already on the theme's top row).
        private void ShuffleRow()
        {
            var games = store.GetRandomStoreGames(40);
            pinned = null;
            SetRow(games);
            featured = games.Take(8).ToList();
            featuredIndex = 0;
        }

        // Endless row: when the end comes near, add another random batch (not while showing search results).
        private void AppendMoreIfNeeded()
        {
            if (searchText != null || appending || (DateTime.UtcNow - lastAppend).TotalMilliseconds < 500)
            {
                return;
            }
            appending = true;
            lastAppend = DateTime.UtcNow;
            try
            {
                var present = new HashSet<StoreGame>(CardItems.Select(n => n.Game));
                var more = store.GetRandomStoreGames(30, present);
                foreach (var game in more)
                {
                    row.Children.Add(CreateCard(game));
                }
            }
            finally
            {
                appending = false;
            }
        }

        private bool appending;
        private DateTime lastAppend = DateTime.MinValue;

        private void SetRow(List<StoreGame> games)
        {
            row.Children.Clear();
            foreach (var game in games)
            {
                row.Children.Add(CreateCard(game));
            }
            if (games.Count == 0)
            {
                row.Children.Add(new TextBlock { Text = "Sonuç bulunamadı.", FontSize = 24, Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), Margin = new Thickness(0, 30, 0, 0) });
            }
            scroller.ScrollToHorizontalOffset(0);
            cardIndex = 0;
        }

        // ---------- search ----------

        // fromThemeButton: called by the theme's search button, which has keyboard focus at that point;
        // focus goes back to the Store tile afterwards so this page is shown with the results.
        public void StartSearch(bool fromThemeButton)
        {
            IsSearching = !fromThemeButton;
            string text;
            try
            {
                text = store.AskSearchText(searchText);
            }
            finally
            {
                IsSearching = false;
            }
            if (text != null && text.Trim().Length > 0)
            {
                Build();
                searchText = text.Trim();
                var results = store.Search(searchText, 80);
                int onPc = results.Count(g => g.IsOnPc);
                rowTitle.Text = "\"" + searchText + "\"  ·  " + results.Count + " sonuç" + (onPc > 0 ? "  ·  " + onPc + " tanesi bu PC'de" : "");
                clearSearchButton.Visibility = Visibility.Visible;
                pinned = results.Count > 0 ? results[0] : null;
                SetRow(results);
                if (pinned != null)
                {
                    ShowGame(pinned);
                }
            }
            if (fromThemeButton || text != null)
            {
                Dispatcher.BeginInvoke(new Action(FocusStoreTile), DispatcherPriority.Input);
            }
        }

        private void ClearSearch()
        {
            searchText = null;
            rowTitle.Text = "Keşfet";
            clearSearchButton.Visibility = Visibility.Collapsed;
            ShuffleRow();
            ShowStoreInfo();
        }

        // Puts keyboard focus on the theme's Store tile so the theme shows this page.
        public static void FocusStoreTile()
        {
            var window = Application.Current == null ? null : Application.Current.MainWindow;
            if (window == null)
            {
                return;
            }
            if (!window.IsActive)
            {
                window.Activate();
            }
            var host = FindByName(window, "BDSoftPS2Store_StoreTile") ?? FindByName(window, "DKGThemeModifier_PS5ish_StoreButton");
            var button = host == null ? null : FindDescendant<Button>(host);
            if (button != null)
            {
                button.Focus();
                Keyboard.Focus(button);
            }
        }

        private static FrameworkElement FindByName(DependencyObject parent, string name)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var element = child as FrameworkElement;
                if (element != null && element.Name == name)
                {
                    return element;
                }
                var nested = FindByName(child, name);
                if (nested != null)
                {
                    return nested;
                }
            }
            return null;
        }

        private static T FindDescendant<T>(DependencyObject parent) where T : DependencyObject
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
                var nested = FindDescendant<T>(child);
                if (nested != null)
                {
                    return nested;
                }
            }
            return null;
        }

        // ---------- header ----------

        private void ShowStoreInfo()
        {
            shown = null;
            title.Text = "PlayStation 2 Store";
            subtitle.Text = "PlayStation 2 klasiklerinin tamamı, tek bir yerde.";
            subtitleIcon.Content = null;
            description.Visibility = Visibility.Collapsed;
            actions.Children.Clear();
            status.Text = string.Empty;
            heroCover.Visibility = Visibility.Collapsed;
            if (featured.Count > 0)
            {
                ShowFeatured(featuredIndex);
                rotateTimer.Start();
            }
            else
            {
                meta.Text = string.Empty;
            }
        }

        private void ShowFeatured(int index)
        {
            if (featured.Count == 0 || shown != null)
            {
                return;
            }
            featuredIndex = index % featured.Count;
            var game = featured[featuredIndex];
            meta.Text = "Öne çıkan  ·  " + game.Title + (game.Entry.Year > 0 ? "  ·  " + game.Entry.Year : "");
            var cover = game.Cover;
            heroCoverBrush.ImageSource = cover;
            heroCover.Visibility = cover != null ? Visibility.Visible : Visibility.Collapsed;
            SetBlurredArt(cover);
            if (cover == null)
            {
                // cover still downloading: show it when it arrives
                PropertyChangedEventHandler handler = null;
                handler = (s, e) =>
                {
                    if (e.PropertyName == "Cover")
                    {
                        game.PropertyChanged -= handler;
                        if (shown == null && featured.Count > 0 && featured[featuredIndex] == game)
                        {
                            ShowFeatured(featuredIndex);
                        }
                    }
                };
                game.PropertyChanged += handler;
            }
        }

        private void ShowGame(StoreGame game)
        {
            leaveTimer.Stop();
            rotateTimer.Stop();
            bool changed = shown != game;
            shown = game;
            title.Text = game.Title;
            subtitle.Text = game.HeroMeta;
            subtitleIcon.Content = AvailabilityBadge(game, 34);
            meta.Text = "Yayıncı: " + game.PublishersText + "    ·    Geliştirici: " + game.DevelopersText;
            BuildActions(game);
            if (changed)
            {
                status.Text = string.Empty;
            }

            string background = store.GetLibraryBackground(game);
            if (background != null)
            {
                heroCover.Visibility = Visibility.Collapsed;
                SetArt(background, false);
            }
            else
            {
                heroCoverBrush.ImageSource = game.Cover;
                heroCover.Visibility = game.Cover != null ? Visibility.Visible : Visibility.Collapsed;
                SetBlurredArt(game.Cover);
            }

            if (changed)
            {
                ShowDescription(game);
            }
        }

        private void BuildActions(StoreGame game)
        {
            actions.Children.Clear();
            if (game.IsOnPc)
            {
                actions.Children.Add(CreatePill("", "Oyna", () => store.PlayGame(game)));
            }
            actions.Children.Add(CreatePill("\uE896", "Kur", () =>
            {
                status.Text = store.OpenExternalUrl(game);
            }));
            if (!game.InLibrary)
            {
                actions.Children.Add(CreatePill("", "Kütüphaneye ekle", () =>
                {
                    status.Text = store.AddGameToLibrary(game) ?? string.Empty;
                    Refresh(game);
                }));
            }
            actions.Children.Add(CreatePill("", game.IsOnPc ? "Dosyayı değiştir" : "Oyunu bul", () =>
            {
                status.Text = store.LinkGameFile(game) ?? string.Empty;
                Refresh(game);
                FocusStoreTile();
            }));
            actions.Children.Add(CreatePill(game.IsFavorite ? "" : "", game.IsFavorite ? "Favoride" : "Favori", () =>
            {
                store.ToggleFavorite(game);
                Refresh(game);
            }));
            ((FrameworkElement)actions.Children[0]).Margin = new Thickness(0);
        }

        private void Refresh(StoreGame game)
        {
            pinned = game;
            ShowGame(game);
            foreach (var card in row.Children.OfType<FrameworkElement>())
            {
                var item = card.Tag as NavItem;
                if (item != null && item.Refresh != null)
                {
                    item.Refresh();
                }
            }
            if (mode == NavMode.Cards)
            {
                HighlightCard(true);
            }
        }

        private async void ShowDescription(StoreGame game)
        {
            description.Visibility = Visibility.Collapsed;
            string text = await store.GetShortDescriptionAsync(game);
            if (shown == game && !string.IsNullOrEmpty(text))
            {
                description.Text = text;
                description.Visibility = Visibility.Visible;
            }
        }

        private readonly Dictionary<string, ImageSource> artCache = new Dictionary<string, ImageSource>();
        private int artRequest;

        // Blurred backdrop from an already decoded cover (no disk access on the UI thread).
        private void SetBlurredArt(ImageSource cover)
        {
            artRequest++;
            art.Source = null;
            artBlurred.Source = cover;
            artBlurred.Opacity = cover == null ? 0 : 0.55;
        }

        // Sharp background (library background images): decoded on a worker thread and cached.
        private async void SetArt(string path, bool blurred)
        {
            int request = ++artRequest;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            ImageSource image;
            if (!artCache.TryGetValue(path, out image))
            {
                image = await System.Threading.Tasks.Task.Run(() => File.Exists(path) ? CoverService.LoadImage(path, 1600) : null);
                if (image != null)
                {
                    if (artCache.Count > 24)
                    {
                        artCache.Clear();
                    }
                    artCache[path] = image;
                }
            }
            if (request != artRequest || image == null)
            {
                return;
            }
            artBlurred.Opacity = 0;
            art.Source = image;
            art.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(450)));
        }

        private void ScrollBy(double delta)
        {
            if ((delta < 0 && scroller.HorizontalOffset > 0) || (delta > 0 && scroller.HorizontalOffset < scroller.ScrollableWidth))
            {
                sounds.Navigate();
            }
            double target = Math.Max(0, Math.Min(scroller.ScrollableWidth, scroller.HorizontalOffset + delta));
            scroller.ScrollToHorizontalOffset(target);
        }

        // ---------- cards ----------

        // Loading state: dark card with a moving light sweep.
        private static FrameworkElement CreateSkeleton()
        {
            var shift = new TranslateTransform(-1, 0);
            var shimmer = new LinearGradientBrush { StartPoint = new Point(0, 0.3), EndPoint = new Point(1, 0.7), RelativeTransform = shift };
            shimmer.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.25));
            shimmer.GradientStops.Add(new GradientStop(Color.FromArgb(34, 255, 255, 255), 0.5));
            shimmer.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.75));
            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-1, 1, TimeSpan.FromMilliseconds(1400)) { RepeatBehavior = RepeatBehavior.Forever });

            var grid = new Grid { IsHitTestVisible = false, Tag = shift };
            grid.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromRgb(30, 34, 43)) });
            grid.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = shimmer });
            // faint shapes where the cover header and title usually are
            grid.Children.Add(new Border { Height = 14, CornerRadius = new CornerRadius(7), Margin = new Thickness(16, 18, 60, 0), VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)) });
            grid.Children.Add(new Border { Height = 12, CornerRadius = new CornerRadius(6), Margin = new Thickness(16, 0, 40, 22), VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)) });
            return grid;
        }

        // No cover found: a calm coloured card (colour derived from the title) with the game name.
        private static FrameworkElement CreatePlaceholder(StoreGame game)
        {
            int hash = 0;
            foreach (char c in game.Title)
            {
                hash = unchecked(hash * 31 + c);
            }
            double hue = Math.Abs(hash % 360);
            var top = FromHsv(hue, 0.55, 0.42);
            var bottom = FromHsv((hue + 40) % 360, 0.6, 0.16);

            var grid = new Grid { IsHitTestVisible = false, ClipToBounds = true };
            grid.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = new LinearGradientBrush(top, bottom, 70) });
            grid.Children.Add(new Border
            {
                Width = 150,
                Height = 150,
                CornerRadius = new CornerRadius(75),
                Background = new RadialGradientBrush(Color.FromArgb(80, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -40, -50, 0)
            });
            grid.Children.Add(new TextBlock
            {
                Text = "PlayStation 2",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                Margin = new Thickness(14, 12, 0, 0),
                VerticalAlignment = VerticalAlignment.Top
            });
            grid.Children.Add(new TextBlock
            {
                Text = game.Title,
                FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.WordEllipsis,
                MaxHeight = 104,
                Margin = new Thickness(14, 0, 14, 48),
                VerticalAlignment = VerticalAlignment.Bottom,
                Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Opacity = 0.6 }
            });
            return grid;
        }

        private static Color FromHsv(double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        private FrameworkElement CreateCard(StoreGame game)
        {
            var grid = new Grid
            {
                Width = CardWidth,
                Height = 240,
                Margin = new Thickness(0, 0, CardSpacing, 0),
                Cursor = Cursors.Hand,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Background = Brushes.Transparent
            };
            var scale = new ScaleTransform(1, 1);
            grid.RenderTransform = scale;

            var ringRotation = new RotateTransform { CenterX = 0.5, CenterY = 0.5 };
            var ringBrush = new LinearGradientBrush { RelativeTransform = ringRotation };
            ringBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, 0x2a, 0x30, 0x39), 0));
            ringBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xa2, 0x89, 0x9d), 0.8));
            ringBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xdb, 0xbd, 0xa1), 1));
            // static soft glow (its effect is rendered once) + rotating ring without an effect
            var glowLayer = new Border
            {
                CornerRadius = new CornerRadius(15),
                Margin = new Thickness(-6),
                BorderThickness = new Thickness(3),
                BorderBrush = new SolidColorBrush(Color.FromArgb(150, 0xdb, 0xbd, 0xa1)),
                Opacity = 0,
                Effect = new DropShadowEffect { Color = Color.FromRgb(0xdb, 0xbd, 0xa1), BlurRadius = 22, ShadowDepth = 0, Opacity = 0.55 }
            };
            var glow = new Border
            {
                CornerRadius = new CornerRadius(15),
                Margin = new Thickness(-6),
                BorderThickness = new Thickness(3),
                BorderBrush = ringBrush,
                Opacity = 0
            };
            grid.Children.Add(glowLayer);
            grid.Children.Add(glow);
            // light sweep over the cover, like the theme's focused tiles
            var sweepStop = new GradientStop(Color.FromArgb(0x40, 255, 255, 255), 0);
            var sweepBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            sweepBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
            sweepBrush.GradientStops.Add(sweepStop);
            sweepBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            var sweep = new Border { CornerRadius = new CornerRadius(10), Background = sweepBrush, Opacity = 0, IsHitTestVisible = false };
            var skeleton = CreateSkeleton();
            var placeholder = CreatePlaceholder(game);
            grid.Children.Add(skeleton);
            grid.Children.Add(placeholder);
            var brush = new ImageBrush { Stretch = Stretch.UniformToFill, ImageSource = game.Cover };
            var coverView = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = brush,
                Opacity = game.Cover != null ? 1 : 0
            };
            grid.Children.Add(coverView);
            Action updateCoverState = () =>
            {
                bool hasCover = game.Cover != null;
                bool loading = !hasCover && !game.CoverMissing;
                skeleton.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
                if (!loading)
                {
                    var shimmerShift = skeleton.Tag as TranslateTransform;
                    if (shimmerShift != null)
                    {
                        shimmerShift.BeginAnimation(TranslateTransform.XProperty, null);
                    }
                }
                placeholder.Visibility = !hasCover && game.CoverMissing ? Visibility.Visible : Visibility.Collapsed;
            };
            updateCoverState();

            var badgeHost = new ContentControl
            {
                Margin = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false
            };
            grid.Children.Add(sweep);
            grid.Children.Add(badgeHost);
            var favorite = new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily(IconFont),
                FontSize = 22,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 213, 74)),
                Margin = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 },
                IsHitTestVisible = false
            };
            grid.Children.Add(favorite);

            bool selectedLook = false;
            Action<bool> setSelected = on =>
            {
                if (on == selectedLook)
                {
                    return;
                }
                selectedLook = on;
                double target = on ? 1.08 : 1;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(160)));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(160)));
                glow.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(on ? 300 : 120)));
                glowLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(on ? 300 : 120)));
                if (on)
                {
                    ringRotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 359, TimeSpan.FromSeconds(4)) { RepeatBehavior = RepeatBehavior.Forever });
                    var sweepRun = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600));
                    sweepStop.BeginAnimation(GradientStop.OffsetProperty, sweepRun);
                    var flash = new DoubleAnimationUsingKeyFrames();
                    flash.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
                    flash.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(800))));
                    sweep.BeginAnimation(OpacityProperty, flash);
                }
                else
                {
                    ringRotation.BeginAnimation(RotateTransform.AngleProperty, null);
                    sweep.BeginAnimation(OpacityProperty, null);
                    sweep.Opacity = 0;
                }
            };
            bool hovering = false;
            bool navSelected = false;
            Action update = () => setSelected(hovering || navSelected || game == pinned);
            Action refresh = () =>
            {
                badgeHost.Content = AvailabilityBadge(game, 32);
                favorite.Visibility = game.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
                update();
            };
            Action<bool> highlight = on =>
            {
                navSelected = on;
                update();
            };
            grid.Tag = new NavItem { Game = game, Refresh = refresh, Highlight = highlight, Activate = () => Refresh(game) };
            refresh();

            game.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "CoverMissing")
                {
                    updateCoverState();
                }
                if (e.PropertyName == "Cover")
                {
                    brush.ImageSource = game.Cover;
                    coverView.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));
                    updateCoverState();
                    if (shown == game && heroCover.Visibility != Visibility.Collapsed)
                    {
                        heroCoverBrush.ImageSource = game.Cover;
                    }
                }
            };

            grid.MouseEnter += (s, e) =>
            {
                if (shown != game)
                {
                    sounds.Navigate();
                }
                hovering = true;
                update();
                ShowGame(game);
            };
            grid.MouseLeave += (s, e) =>
            {
                hovering = false;
                update();
            };
            grid.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                sounds.Activate();
                Refresh(game);
            };
            return grid;
        }
    }
}
