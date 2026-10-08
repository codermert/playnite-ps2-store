using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;

namespace BDSoftPS2Store
{
    // Theme-facing commands: {PluginSettings Plugin=BDSoftPS2Store, Path=OpenSearchCommand}
    public class ThemeCommands
    {
        public ICommand OpenStoreCommand { get; set; }
        public ICommand OpenSearchCommand { get; set; }
    }

    public class BDSoftPS2StorePlugin : GenericPlugin
    {
        public const string UriSource = "bdps2store";

        private static readonly ILogger logger = LogManager.GetLogger();
        private StoreController store;
        private BackgroundService backgrounds;

        public override Guid Id
        {
            get { return Guid.Parse("9411ebc2-4eeb-439b-84a2-ca7011456b22"); }
        }

        public ThemeCommands Theme { get; private set; }

        public BDSoftPS2StorePlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties { HasSettings = false };
            Theme = new ThemeCommands
            {
                OpenStoreCommand = new RelayCommand(p => OpenStore()),
                OpenSearchCommand = new RelayCommand(p => OpenSearch())
            };
            AddSettingsSupport(new AddSettingsSupportArgs { SourceName = "BDSoftPS2Store", SettingsRoot = "Theme" });
            AddCustomElementSupport(new AddCustomElementSupportArgs
            {
                SourceName = "BDSoftPS2Store",
                ElementList = new List<string> { "StorePreview", "StoreTile" }
            });
        }

        private bool IsFullscreen
        {
            get { return PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen; }
        }

        private StoreController GetStore()
        {
            if (store == null)
            {
                string pluginDir = Path.GetDirectoryName(GetType().Assembly.Location);
                store = new StoreController(PlayniteApi, pluginDir, GetPluginUserDataPath());
                store.Backgrounds = GetBackgrounds();
            }
            return store;
        }

        private BackgroundService GetBackgrounds()
        {
            if (backgrounds == null)
            {
                backgrounds = new BackgroundService(PlayniteApi, GetPluginUserDataPath(), Application.Current.Dispatcher);
            }
            return backgrounds;
        }

        public override Control GetGameViewControl(GetGameViewControlArgs args)
        {
            if (args.Name == "StorePreview")
            {
                return new StorePreviewControl(PlayniteApi, GetStore());
            }
            if (args.Name == "StoreTile")
            {
                string icon = Path.Combine(PlayniteApi.Paths.ConfigurationPath, "Themes", "Fullscreen", ThemeIntegration.ThemeFolder, "Images", "PlaystationStore.png");
                return new StoreTileControl(icon, StorePreviewControl.FocusStoreTile);
            }
            return null;
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            PlayniteApi.UriHandler.RegisterSource(UriSource, uriArgs =>
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(OpenStore));
            });

            // PS2 games without a home-screen background (e.g. added from the store) get one,
            // a little after start-up so Playnite opens without delay.
            var startTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            startTimer.Tick += (s, e) =>
            {
                startTimer.Stop();
                try
                {
                    GetBackgrounds().UpgradeIfNeeded();
                    GetBackgrounds().FillMissing();
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Background check failed");
                }
            };
            startTimer.Start();

            // First run with the PS5ish theme: offer the one-click theme integration.
            string config = PlayniteApi.Paths.ConfigurationPath;
            if (ThemeIntegration.IsThemeInstalled(config) && !ThemeIntegration.IsApplied(config))
            {
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    "bdps2store-theme",
                    "BD Soft PS2 Store: PS5ish teması bulundu. Mağazayı temanın PlayStation Store kutusuna eklemek için tıklayın.",
                    NotificationType.Info,
                    () => IntegrateTheme()));
            }
        }

        private string ThemeBackupDir
        {
            get { return Path.Combine(GetPluginUserDataPath(), "theme-backup"); }
        }

        public void IntegrateTheme()
        {
            try
            {
                string report = ThemeIntegration.Apply(PlayniteApi.Paths.ConfigurationPath, ThemeBackupDir);
                PlayniteApi.Notifications.Remove("bdps2store-theme");
                PlayniteApi.Dialogs.ShowMessage(report + "\n\nDeğişikliklerin görünmesi için Playnite'ı yeniden başlatın.", "BD Soft PS2 Store");
            }
            catch (Exception e)
            {
                logger.Error(e, "Theme integration failed");
                PlayniteApi.Dialogs.ShowErrorMessage(e.Message, "BD Soft PS2 Store");
            }
        }

        public void RestoreTheme()
        {
            try
            {
                string report = ThemeIntegration.Restore(PlayniteApi.Paths.ConfigurationPath, ThemeBackupDir);
                PlayniteApi.Dialogs.ShowMessage(report + "\n\nDeğişikliklerin görünmesi için Playnite'ı yeniden başlatın.", "BD Soft PS2 Store");
            }
            catch (Exception e)
            {
                logger.Error(e, "Theme restore failed");
                PlayniteApi.Dialogs.ShowErrorMessage(e.Message, "BD Soft PS2 Store");
            }
        }

        private string PluginDir
        {
            get { return Path.GetDirectoryName(GetType().Assembly.Location); }
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            string icon = Path.Combine(PluginDir, "icon.png");
            yield return new MainMenuItem
            {
                MenuSection = "@BD Soft PS2 Store",
                Description = "Mağazayı aç",
                Icon = icon,
                Action = a => OpenStore()
            };
            yield return new MainMenuItem
            {
                MenuSection = "@BD Soft PS2 Store",
                Description = "Mağazada ara",
                Icon = icon,
                Action = a => OpenSearch()
            };
            yield return new MainMenuItem
            {
                MenuSection = "@BD Soft PS2 Store",
                Description = "PS2 arka planlarını yeniden oluştur",
                Icon = icon,
                Action = a =>
                {
                    int count = GetBackgrounds().RegenerateAll();
                    GetBackgrounds().FillMissing();
                    PlayniteApi.Dialogs.ShowMessage(count + " arka plan yeniden oluşturuluyor. Eksik olanlar da tamamlanıyor.", "BD Soft PS2 Store");
                }
            };
            yield return new MainMenuItem
            {
                MenuSection = "@BD Soft PS2 Store",
                Description = "PS5ish temasına entegre et",
                Icon = icon,
                Action = a => IntegrateTheme()
            };
            yield return new MainMenuItem
            {
                MenuSection = "@BD Soft PS2 Store",
                Description = "Tema entegrasyonunu geri al",
                Icon = icon,
                Action = a => RestoreTheme()
            };
            yield return new MainMenuItem
            {
                MenuSection = "@BD Soft PS2 Store",
                Description = "Geliştirici  ·  codermert",
                Icon = icon,
                Action = a => ShowDeveloper()
            };
        }

        public void ShowDeveloper()
        {
            try
            {
                DeveloperWindow.Show(PlayniteApi, PluginDir);
            }
            catch (Exception e)
            {
                logger.Error(e, "Developer screen could not be opened.");
            }
        }

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            yield return new SidebarItem
            {
                Title = "PS2 Store",
                Type = SiderbarItemType.Button,
                Icon = new TextBlock
                {
                    Text = "PS2",
                    FontWeight = FontWeights.Bold,
                    FontSize = 13,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                Activated = OpenStore
            };
        }

        // Fullscreen: the store lives on the theme's Store page, so just bring that page up.
        // Desktop mode has no such page and uses the store window.
        public void OpenStore()
        {
            try
            {
                if (IsFullscreen)
                {
                    StorePreviewControl.FocusStoreTile();
                }
                else
                {
                    GetStore().Show();
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "BD Soft PS2 Store could not be opened.");
                PlayniteApi.Dialogs.ShowErrorMessage("PS2 Store açılamadı: " + e.Message, "BD Soft PS2 Store");
            }
        }

        public void OpenSearch()
        {
            try
            {
                if (IsFullscreen)
                {
                    if (StorePreviewControl.Current != null)
                    {
                        StorePreviewControl.Current.StartSearch(true);
                    }
                }
                else
                {
                    GetStore().ShowSearch();
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "BD Soft PS2 Store search could not be opened.");
            }
        }
    }
}
