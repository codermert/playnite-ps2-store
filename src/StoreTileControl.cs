using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Playnite.SDK.Controls;

namespace BDSoftPS2Store
{
    // The PlayStation Store tile of the PS5ish theme (theme element: BDSoftPS2Store_StoreTile).
    // Same look as the DKG Theme Modifier tile, but it stays inside Playnite: focusing or clicking it
    // shows the store page (no browser, no extra window).
    public class StoreTileControl : PluginUserControl
    {
        private const string TileXaml = @"
<Button xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Width='105' Height='105' VerticalAlignment='Top' FocusVisualStyle='{x:Null}' Cursor='Hand'>
  <Button.Template>
    <ControlTemplate TargetType='Button'>
      <Grid Background='Transparent' HorizontalAlignment='Left' VerticalAlignment='Top' RenderTransformOrigin='0,0'>
        <Grid.RenderTransform>
          <ScaleTransform x:Name='TileScale' ScaleX='1' ScaleY='1' />
        </Grid.RenderTransform>
        <Border Background='#80100100' CornerRadius='18' />
        <Image x:Name='TileImage' RenderOptions.BitmapScalingMode='Fant' Width='105' />
        <Border x:Name='TileRing' BorderThickness='1.55' CornerRadius='20' Width='111' Height='111' Margin='-3' Opacity='0'>
          <Border.BorderBrush>
            <LinearGradientBrush>
              <GradientStop Color='#802a3039' Offset='0' />
              <GradientStop Color='#a2899d' Offset='0.80' />
              <GradientStop Color='#dbbda1' Offset='1' />
            </LinearGradientBrush>
          </Border.BorderBrush>
        </Border>
      </Grid>
      <ControlTemplate.Triggers>
        <Trigger Property='IsKeyboardFocused' Value='True'>
          <Setter TargetName='TileRing' Property='Opacity' Value='1' />
          <Trigger.EnterActions>
            <BeginStoryboard>
              <Storyboard>
                <DoubleAnimation Storyboard.TargetName='TileScale' Storyboard.TargetProperty='ScaleX' To='1.6' Duration='0:0:0.2' />
                <DoubleAnimation Storyboard.TargetName='TileScale' Storyboard.TargetProperty='ScaleY' To='1.6' Duration='0:0:0.2' />
              </Storyboard>
            </BeginStoryboard>
          </Trigger.EnterActions>
          <Trigger.ExitActions>
            <BeginStoryboard>
              <Storyboard>
                <DoubleAnimation Storyboard.TargetName='TileScale' Storyboard.TargetProperty='ScaleX' To='1' Duration='0:0:0.2' />
                <DoubleAnimation Storyboard.TargetName='TileScale' Storyboard.TargetProperty='ScaleY' To='1' Duration='0:0:0.2' />
              </Storyboard>
            </BeginStoryboard>
          </Trigger.ExitActions>
        </Trigger>
        <Trigger Property='IsMouseOver' Value='True'>
          <Setter TargetName='TileRing' Property='Opacity' Value='1' />
        </Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate>
  </Button.Template>
</Button>";

        public static Button TileButton { get; private set; }

        // True while the Store tile is the focused element of the main window and no other Playnite
        // window (settings, menus, dialogs) is open on top of it. Window activation is not used: after
        // closing a menu Windows can leave the main window briefly inactive.
        public static bool IsTileFocused
        {
            get
            {
                var tile = TileButton;
                var window = tile == null ? null : Window.GetWindow(tile);
                if (window == null)
                {
                    return false;
                }
                bool focused = tile.IsKeyboardFocused || System.Windows.Input.FocusManager.GetFocusedElement(window) == tile;
                if (!focused)
                {
                    return false;
                }
                foreach (Window other in Application.Current.Windows)
                {
                    if (other != window && other.IsVisible && other.WindowState != WindowState.Minimized)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        private readonly Action activate;
        private readonly Button button;
        private DateTime lastActivation = DateTime.MinValue;

        public StoreTileControl(string iconPath, Action activate)
        {
            this.activate = activate;
            button = (Button)XamlReader.Parse(TileXaml);
            TileButton = button;
            button.ApplyTemplate();
            var image = button.Template.FindName("TileImage", button) as Image;
            if (image != null && File.Exists(iconPath))
            {
                image.Source = CoverService.LoadImage(iconPath, 210);
            }
            button.Click += (s, e) =>
            {
                var page = StorePreviewControl.Current;
                if (page == null || !page.IsBrowsing)
                {
                    Activate();
                }
            };
            button.Loaded += (s, e) =>
            {
                var tileImage = button.Template.FindName("TileImage", button) as Image;
                if (tileImage != null && tileImage.Source == null && File.Exists(iconPath))
                {
                    tileImage.Source = CoverService.LoadImage(iconPath, 210);
                }
            };

            // Playnite turns controller input into key presses for the focused element (this tile).
            // Arrow keys / Enter / Escape are handed to the store page so the controller can browse it
            // while the tile keeps focus (the theme only shows the page while the tile is focused).
            button.PreviewKeyDown += (s, e) =>
            {
                var page = StorePreviewControl.Current;
                if (page != null && page.HandleKey(e.Key))
                {
                    e.Handled = true;
                }
            };
            button.LostKeyboardFocus += (s, e) =>
            {
                var page = StorePreviewControl.Current;
                if (page != null)
                {
                    page.ResetNavigation();
                }
            };
            Content = button;
        }

        private void Activate()
        {
            // Playnite or the keyboard can deliver the same press as a click: handle it once.
            if ((DateTime.UtcNow - lastActivation).TotalMilliseconds < 800)
            {
                return;
            }
            lastActivation = DateTime.UtcNow;
            activate();
            lastActivation = DateTime.UtcNow;
        }
    }
}
