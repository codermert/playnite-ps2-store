using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Playnite.SDK;

namespace BDSoftPS2Store
{
    // "Geliştirici" screen (Extensions menu): developer card with GitHub and press links.
    // Works with mouse and controller (Playnite sends the controller as arrow / Enter / Escape keys).
    public static class DeveloperWindow
    {
        private const string DeveloperHandle = "codermert";
        private const string GitHubUrl = "https://github.com/codermert";
        private const string Version = "1.0.0 Beta";

        private static readonly ILogger logger = LogManager.GetLogger();

        private class PressItem
        {
            public string Publication;
            public string Headline;
            public string Url;
        }

        private static readonly PressItem[] Press =
        {
            new PressItem
            {
                Publication = "KIBRIS SOSYETE",
                Headline = "Mert Salık yapay zekaya skill yapılarıyla uzmanlık kazandırıyor",
                Url = "https://www.kibrissosyete.com/haber/mert-salik-yapay-zekaya-skill-yapilariyla-uzmanlik-kazandiriyor-4690"
            },
            new PressItem
            {
                Publication = "BEST LIFE MAGAZIN",
                Headline = "Mert Salık bugün rotasını tamamen geleceğin teknolojisine çevirdi",
                Url = "https://www.bestlifemagazin.com/mert-salik-bugun-rotasini-tamamen-gelecegin-teknolojisine-cevirdi/6124/"
            }
        };

        // GitHub mark (used only as the link icon of the GitHub button)
        private const string GitHubMark = "M12 .297c-6.63 0-12 5.373-12 12 0 5.303 3.438 9.8 8.205 11.385.6.113.82-.258.82-.577 0-.285-.01-1.04-.015-2.04-3.338.724-4.042-1.61-4.042-1.61C4.422 18.07 3.633 17.7 3.633 17.7c-1.087-.744.084-.729.084-.729 1.205.084 1.838 1.236 1.838 1.236 1.07 1.835 2.809 1.305 3.495.998.108-.776.417-1.305.76-1.605-2.665-.3-5.466-1.332-5.466-5.93 0-1.31.465-2.38 1.235-3.22-.135-.303-.54-1.523.105-3.176 0 0 1.005-.322 3.3 1.23.96-.267 1.98-.399 3-.405 1.02.006 2.04.138 3 .405 2.28-1.552 3.285-1.23 3.285-1.23.645 1.653.24 2.873.12 3.176.765.84 1.23 1.91 1.23 3.22 0 4.61-2.805 5.625-5.475 5.92.42.36.81 1.096.81 2.22 0 1.606-.015 2.896-.015 3.286 0 .315.21.69.825.57C20.565 22.092 24 17.592 24 12.297c0-6.627-5.373-12-12-12";

        private const string ButtonTemplate = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
  <Grid>
    <Border x:Name='Ring' CornerRadius='{TemplateBinding Tag}' Margin='-6' BorderThickness='2.5' BorderBrush='#dbbda1' Opacity='0'>
      <Border.Effect><DropShadowEffect Color='#dbbda1' BlurRadius='18' ShadowDepth='0' Opacity='0.6' /></Border.Effect>
    </Border>
    <Border x:Name='Body' CornerRadius='{TemplateBinding Tag}' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'>
      <ContentPresenter HorizontalAlignment='Left' VerticalAlignment='Center' />
    </Border>
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property='IsKeyboardFocused' Value='True'>
      <Setter TargetName='Ring' Property='Opacity' Value='1' />
    </Trigger>
    <Trigger Property='IsMouseOver' Value='True'>
      <Setter TargetName='Ring' Property='Opacity' Value='1' />
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";

        private static Window current;

        public static void Show(IPlayniteAPI api, string pluginDir)
        {
            if (current != null)
            {
                current.Activate();
                return;
            }

            var window = api.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true, ShowMaximizeButton = false, ShowMinimizeButton = false });
            window.Title = "BD Soft PS2 Store · Geliştirici";
            window.Background = new SolidColorBrush(Color.FromRgb(10, 11, 15));
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
                window.Width = 1280;
                window.Height = 720;
                window.WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            }

            var buttons = new List<Button>();
            window.Content = BuildContent(System.IO.Path.Combine(pluginDir, "assets", "developer.jpg"), buttons, () => window.Close());
            window.PreviewKeyDown += (s, e) =>
            {
                var focused = Keyboard.FocusedElement as Button;
                int index = focused == null ? -1 : buttons.IndexOf(focused);
                if (e.Key == Key.Escape || e.Key == Key.Back || e.Key == Key.BrowserBack)
                {
                    window.Close();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter || e.Key == Key.Space)
                {
                    if (focused != null)
                    {
                        focused.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    }
                    e.Handled = true;
                }
                else if (e.Key == Key.Down || e.Key == Key.Right)
                {
                    buttons[Math.Min(buttons.Count - 1, index + 1)].Focus();
                    e.Handled = true;
                }
                else if (e.Key == Key.Up || e.Key == Key.Left)
                {
                    buttons[Math.Max(0, index - 1)].Focus();
                    e.Handled = true;
                }
            };
            window.Closed += (s, e) =>
            {
                current = null;
                if (window.Owner != null)
                {
                    window.Owner.Activate();
                }
            };
            window.Loaded += (s, e) => buttons[0].Focus();
            current = window;
            window.Show();
            window.Activate();
        }

        private static FrameworkElement BuildContent(string photoPath, List<Button> buttons, Action close)
        {
            ImageSource photo = File.Exists(photoPath) ? CoverService.LoadImage(photoPath, 900) : null;

            var page = new Grid { Width = 1920, Height = 1080, ClipToBounds = true };

            // backdrop: the photo, blurred, behind a dark gradient
            if (photo != null)
            {
                page.Children.Add(new Image
                {
                    Source = photo,
                    Stretch = Stretch.UniformToFill,
                    Opacity = 0.32,
                    Margin = new Thickness(-120),
                    Effect = new BlurEffect { Radius = 70 }
                });
            }
            var shade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(235, 10, 11, 15), 0));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(200, 10, 11, 15), 0.6));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(240, 10, 11, 15), 1));
            page.Children.Add(new Border { Background = shade });

            var layout = new Grid { Margin = new Thickness(150, 0, 150, 0), VerticalAlignment = VerticalAlignment.Center };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // ----- full photo in a rounded portrait card with the rotating theme ring -----
            const double photoWidth = 400;
            const double photoHeight = 536; // the photo is 3:4
            var avatarHost = new Grid { Width = photoWidth + 20, Height = photoHeight + 20, VerticalAlignment = VerticalAlignment.Center };
            var rotation = new RotateTransform { CenterX = 0.5, CenterY = 0.5 };
            var ringBrush = new LinearGradientBrush { RelativeTransform = rotation };
            ringBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, 0x2a, 0x30, 0x39), 0));
            ringBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xa2, 0x89, 0x9d), 0.8));
            ringBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xdb, 0xbd, 0xa1), 1));
            avatarHost.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(34),
                BorderThickness = new Thickness(4),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 0xdb, 0xbd, 0xa1)),
                Effect = new DropShadowEffect { Color = Color.FromRgb(0xdb, 0xbd, 0xa1), BlurRadius = 44, ShadowDepth = 0, Opacity = 0.55 }
            });
            avatarHost.Children.Add(new Border { CornerRadius = new CornerRadius(34), BorderThickness = new Thickness(4), BorderBrush = ringBrush });
            rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 359, TimeSpan.FromSeconds(4)) { RepeatBehavior = RepeatBehavior.Forever });
            avatarHost.Children.Add(new Border
            {
                Width = photoWidth,
                Height = photoHeight,
                CornerRadius = new CornerRadius(26),
                Background = new ImageBrush { ImageSource = photo, Stretch = Stretch.UniformToFill }
            });
            layout.Children.Add(avatarHost);
            // ----- text, GitHub, press -----
            var info = new StackPanel { Margin = new Thickness(90, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(info, 1);
            info.Children.Add(new TextBlock
            {
                Text = "GELİŞTİRİCİ",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xdb, 0xbd, 0xa1))
            });
            info.Children.Add(new TextBlock
            {
                Text = DeveloperHandle,
                FontSize = 96,
                FontWeight = FontWeights.Light,
                Foreground = Brushes.White,
                Margin = new Thickness(-4, 4, 0, 0)
            });
            info.Children.Add(new TextBlock
            {
                Text = "BD Soft PS2 Store  ·  Sürüm " + Version + "  ·  Playnite için PlayStation 2 mağazası",
                FontSize = 24,
                Foreground = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)),
                Margin = new Thickness(0, 6, 0, 36)
            });

            var github = CreateButton(new SolidColorBrush(Color.FromRgb(240, 240, 242)), 34, new Thickness(30, 0, 36, 0), 68, () => Open(GitHubUrl));
            var githubContent = new StackPanel { Orientation = Orientation.Horizontal };
            githubContent.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(GitHubMark),
                Fill = new SolidColorBrush(Color.FromRgb(20, 22, 27)),
                Stretch = Stretch.Uniform,
                Width = 30,
                Height = 30,
                VerticalAlignment = VerticalAlignment.Center
            });
            githubContent.Children.Add(new TextBlock
            {
                Text = "@" + DeveloperHandle,
                FontSize = 26,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(20, 22, 27)),
                Margin = new Thickness(16, 0, 0, 2),
                VerticalAlignment = VerticalAlignment.Center
            });
            githubContent.Children.Add(new TextBlock
            {
                Text = "GitHub",
                FontSize = 20,
                Foreground = new SolidColorBrush(Color.FromArgb(150, 20, 22, 27)),
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            github.Content = githubContent;
            github.HorizontalAlignment = HorizontalAlignment.Left;
            buttons.Add(github);
            info.Children.Add(github);

            info.Children.Add(new TextBlock
            {
                Text = "Basında",
                FontSize = 30,
                FontWeight = FontWeights.Light,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 54, 0, 18)
            });
            var pressRow = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var item in Press)
            {
                var url = item.Url;
                var card = CreateButton(new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)), 18, new Thickness(28, 22, 28, 22), double.NaN, () => Open(url));
                card.Width = 470;
                card.Margin = new Thickness(0, 0, 26, 0);
                var body = new StackPanel();
                body.Children.Add(new TextBlock
                {
                    Text = item.Publication,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xdb, 0xbd, 0xa1))
                });
                body.Children.Add(new TextBlock
                {
                    Text = item.Headline,
                    FontSize = 22,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 10, 0, 14),
                    MinHeight = 62
                });
                body.Children.Add(new TextBlock
                {
                    Text = "Yazıyı oku  ›",
                    FontSize = 18,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255))
                });
                card.Content = body;
                buttons.Add(card);
                pressRow.Children.Add(card);
            }
            info.Children.Add(pressRow);
            layout.Children.Add(info);
            page.Children.Add(layout);

            // close
            var closeButton = CreateButton(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 26, new Thickness(26, 0, 30, 0), 52, close);
            closeButton.Content = new TextBlock { Text = "✕   Kapat", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
            closeButton.HorizontalAlignment = HorizontalAlignment.Right;
            closeButton.VerticalAlignment = VerticalAlignment.Top;
            closeButton.Margin = new Thickness(0, 50, 70, 0);
            buttons.Add(closeButton);
            page.Children.Add(closeButton);

            page.Children.Add(new TextBlock
            {
                Text = "Ⓐ Aç   ·   Ⓑ Kapat   ·   Bağlantılar varsayılan tarayıcıda açılır",
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(170, 0, 0, 50)
            });

            return new Viewbox { Stretch = Stretch.Uniform, Child = page };
        }

        private static Button CreateButton(Brush background, double radius, Thickness padding, double height, Action click)
        {
            var button = new Button
            {
                Style = new Style(typeof(Button)),
                Template = (ControlTemplate)XamlReader.Parse(ButtonTemplate),
                MinHeight = 0,
                MinWidth = 0,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = background,
                Padding = padding,
                Tag = new CornerRadius(radius),
                Cursor = Cursors.Hand,
                FocusVisualStyle = null
            };
            button.Height = height; // NaN = size to content
            button.Click += (s, e) => click();
            return button;
        }

        private static void Open(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception e)
            {
                logger.Error(e, "Link could not be opened: " + url);
            }
        }
    }
}
