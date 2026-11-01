using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;

namespace BusyMouse.WinUI;

/// <summary>
/// The tray icon's right-click menu, drawn by WinUI so it looks like Windows 11's own menus (rounded, acrylic,
/// with icons) rather than a classic Win32 menu. A XAML menu needs a window to belong to, so this is an invisible
/// one parked at the click point; the menu itself is a windowed popup free to extend past it.
/// </summary>
internal sealed partial class TrayMenu : Window
{
  // Tighter than WinUI's default 11,8,11,9, closer to the shell's own menus.
  private static readonly Thickness ItemPadding = new (11, 5, 11, 6);

  // A soft, friendly green for Keep awake's play icon.
  private static readonly Color PlayColor = Color.FromArgb (0xFF, 0x11, 0x96, 0x0D);  // #11960D
  private const double IconHoverMargin = 8;
  private static readonly Color StopColor = Color.FromArgb (0xFF, 0xCC, 0x0F, 0x22);  // #CC0F22

  private readonly TrayHost tray;
  private readonly nint hwnd;
  private readonly Grid root;
  private readonly MenuFlyout menu;
  private readonly StayOpenItem toggle;
  private readonly ImageIcon toggleIcon;
  private readonly SvgImageSource playIcon = new ();
  private readonly SvgImageSource stopIcon = new ();
  private bool showingOn;
  private bool nearIcon;

  public TrayMenu (TrayHost tray)
  {
    this.tray = tray;
    this.hwnd = Win32Interop.GetWindowFromWindowId (this.AppWindow.Id);

    this.AppWindow.Title = "BusyMouse";
    this.AppWindow.IsShownInSwitchers = false;

    if (this.AppWindow.Presenter is OverlappedPresenter presenter)
    {
      presenter.IsResizable = false;
      presenter.IsMaximizable = false;
      presenter.IsMinimizable = false;
      presenter.IsAlwaysOnTop = true;
      presenter.SetBorderAndTitleBar (false, false);
    }

    // Fully transparent, so only the menu is ever seen. The menu's popup is a window of its own and isn't affected.
    var style = Native.GetWindowLongPtrW (this.hwnd, Native.GWL_EXSTYLE);
    _ = Native.SetWindowLongPtrW (this.hwnd, Native.GWL_EXSTYLE, style | (nint)(Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW));
    _ = Native.SetLayeredWindowAttributes (this.hwnd, 0, 0, Native.LWA_ALPHA);

    _ = SetIcon (this.playIcon, MediaIcon (true, PlayColor));
    _ = SetIcon (this.stopIcon, MediaIcon (false, StopColor));
    this.toggleIcon = new ImageIcon { Source = this.stopIcon };
    this.toggle = new StayOpenItem { Icon = this.toggleIcon, Padding = ItemPadding };
    this.toggle.Invoked += this.Toggle_Invoked;
    this.toggle.PointerMoved += this.Toggle_PointerMoved;
    this.toggle.PointerExited += (_, _) => this.SetNearIcon (false);

    this.menu = new MenuFlyout
    {
      ShouldConstrainToRootBounds = false,
      SystemBackdrop = new DesktopAcrylicBackdrop (),
      MenuFlyoutPresenterStyle = TransparentPresenter ()
    };
    this.menu.Items.Add (this.toggle);
    this.menu.Items.Add (new MenuFlyoutSeparator ());
    this.menu.Items.Add (this.Item ("Open BusyMouse", Glyph ("\uE8A7"), TrayHost.MenuOpen));
    this.menu.Items.Add (this.Item ("About", Glyph ("\uE946"), TrayHost.MenuAbout));
    this.menu.Items.Add (this.Item ("Settings", Glyph ("\uE713"), TrayHost.MenuSettings));
    this.menu.Items.Add (new MenuFlyoutSeparator ());
    this.menu.Items.Add (this.Item ("Exit", Glyph ("\uE711", 13), TrayHost.MenuExit));
    this.menu.Closed += (_, _) => this.AppWindow.Hide ();

    this.root = new Grid ();
    this.Content = this.root;

    this.Activated += this.TrayMenu_Activated;
    this.AppWindow.Closing += (_, args) => args.Cancel = !App.Current.IsQuitting;
  }

  /// <summary>
  /// Opens the menu at a point in physical pixels, away from whichever edge the taskbar is on.
  /// </summary>
  public void ShowAt (int x, int y)
  {
    // Like the shell's own menus, follow the taskbar's theme rather than the app's.
    var light = this.tray.LightTaskbar;
    this.root.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;
    this.toggle.KeyboardAcceleratorTextOverride = this.tray.HotKey ?? string.Empty;
    this.nearIcon = false;
    this.ShowState (this.tray.IsOn);

    var work = DisplayArea.GetFromPoint (new PointInt32 (x, y), DisplayAreaFallback.Nearest).WorkArea;
    var below = y < work.Y + work.Height / 2;
    var right = x < work.X + work.Width / 2;
    var placement = (below, right) switch
    {
      (true, true) => FlyoutPlacementMode.BottomEdgeAlignedLeft,
      (true, false) => FlyoutPlacementMode.BottomEdgeAlignedRight,
      (false, true) => FlyoutPlacementMode.TopEdgeAlignedLeft,
      (false, false) => FlyoutPlacementMode.TopEdgeAlignedRight
    };
    var options = new FlyoutShowOptions { Position = new Point (0, 0), Placement = placement };

    this.AppWindow.MoveAndResize (new RectInt32 (x, y, 1, 1));
    this.AppWindow.Show ();
    this.Activate ();

    // The menu only closes on an outside click if its window is in the foreground.
    _ = Native.SetForegroundWindow (this.hwnd);

    // The first time, the window hasn't laid out yet and the menu has nothing to anchor to.
    if (this.root.XamlRoot is not null)
    {
      this.menu.ShowAt (this.root, options);

      return;
    }

    void Loaded (object sender, RoutedEventArgs e)
    {
      this.root.Loaded -= Loaded;
      this.menu.ShowAt (this.root, options);
    }

    this.root.Loaded += Loaded;
  }

  /// <summary>
  /// A media-player play or stop symbol in one color, as SVG on a 16 grid; the matching stroke rounds the corners.
  /// </summary>
  private static string MediaIcon (bool play, Color color)
  {
    var shape = play ? "<path d=\"M4.8,2.9 L13,8 L4.8,13.1 Z\"/>" : "<rect x=\"3.5\" y=\"3.5\" width=\"9\" height=\"9\" rx=\"1.5\"/>";
    var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    return "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\" viewBox=\"0 0 16 16\">" +
      $"<g fill=\"{hex}\" stroke=\"{hex}\" stroke-width=\"1.2\" stroke-linejoin=\"round\">{shape}</g></svg>";
  }

  private static FontIcon Glyph (string glyph, double size = 16) => new () { Glyph = glyph, FontSize = size };

  private static Style TransparentPresenter ()
  {
    // The acrylic backdrop shows only where the presenter itself doesn't paint.
    var style = new Style (typeof (MenuFlyoutPresenter));
    style.Setters.Add (new Setter (Control.BackgroundProperty, new SolidColorBrush (Colors.Transparent)));

    return style;
  }

  /// <summary>
  /// Keep awake's row shows the state: green play while on, red stop while off. Pointing at (or near) the play icon
  /// shows stop, what a click does.
  /// </summary>
  private void ShowState (bool on)
  {
    this.showingOn = on;
    this.toggle.Text = on ? "Awake is ON" : "Awake is OFF";
    ToolTipService.SetToolTip (this.toggle, on ? "Click to turn off" : "Click to turn on");
    this.toggleIcon.Source = on && !this.nearIcon ? this.playIcon : this.stopIcon;
  }

  private void SetNearIcon (bool near)
  {
    if (near == this.nearIcon)
      return;

    this.nearIcon = near;
    this.ShowState (this.showingOn);
  }

  // Only near the icon, not the whole row: within IconHoverMargin of it, which is easier to hit than 16 px.
  private void Toggle_PointerMoved (object sender, PointerRoutedEventArgs e)
  {
    var at = e.GetCurrentPoint (this.toggleIcon).Position;

    this.SetNearIcon (at.X >= -IconHoverMargin && at.X <= this.toggleIcon.ActualWidth + IconHoverMargin
                      && at.Y >= -IconHoverMargin && at.Y <= this.toggleIcon.ActualHeight + IconHoverMargin);
  }

  // Switches right away and keeps the menu open, so the new state shows where it was clicked.
  private void Toggle_Invoked (object? sender, EventArgs e)
  {
    if (App.Current.IsQuitting)
      return;

    this.tray.Run (TrayHost.MenuToggle);
    this.ShowState (this.tray.IsOn);
  }

  private static async Task SetIcon (SvgImageSource icon, string svg)
  {
    try
    {
      using var stream = new InMemoryRandomAccessStream ();
      using (var writer = new DataWriter (stream.GetOutputStreamAt (0)))
      {
        writer.WriteString (svg);
        _ = await writer.StoreAsync ();
        _ = await writer.FlushAsync ();
      }

      _ = await icon.SetSourceAsync (stream);
    }
    catch (Exception ex)
    {
      System.Diagnostics.Debug.WriteLine ($"Tray menu icon: {ex}");
    }
  }

  private MenuFlyoutItem Item (string text, IconElement icon, int command)
  {
    var item = new MenuFlyoutItem { Text = text, Icon = icon, Padding = ItemPadding };
    item.Click += (_, _) => this.Run (command);

    return item;
  }

  /// <summary>Carries out a command once the menu has had the chance to close.</summary>
  private void Run (int command) => _ = this.DispatcherQueue.TryEnqueue (() =>
  {
    if (!App.Current.IsQuitting)
      this.tray.Run (command);
  });

  private void TrayMenu_Activated (object sender, WindowActivatedEventArgs args)
  {
    if (args.WindowActivationState != WindowActivationState.Deactivated || !this.menu.IsOpen)
      return;

    // The menu's popup is owned by this window; moving into it isn't leaving.
    var foreground = Native.GetForegroundWindow ();

    if (foreground != 0 && Native.GetAncestor (foreground, Native.GA_ROOTOWNER) == this.hwnd)
      return;

    this.menu.Hide ();
  }

  /// <summary>
  /// A menu item that doesn't close the menu when clicked: it handles the click (or Enter/Space) itself and raises
  /// Invoked, instead of letting MenuFlyoutItem invoke, which always closes the menu.
  /// </summary>
  private sealed partial class StayOpenItem : MenuFlyoutItem
  {
    public event EventHandler? Invoked;

    protected override void OnPointerReleased (PointerRoutedEventArgs e)
    {
      e.Handled = true;
      this.ReleasePointerCapture (e.Pointer);
      VisualStateManager.GoToState (this, "PointerOver", true);

      if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse || e.GetCurrentPoint (this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        this.Invoked?.Invoke (this, EventArgs.Empty);
    }

    protected override void OnKeyDown (KeyRoutedEventArgs e)
    {
      if (e.Key is VirtualKey.Enter or VirtualKey.Space)
        e.Handled = true;
      else
        base.OnKeyDown (e);
    }

    protected override void OnKeyUp (KeyRoutedEventArgs e)
    {
      if (e.Key is VirtualKey.Enter or VirtualKey.Space)
      {
        e.Handled = true;
        this.Invoked?.Invoke (this, EventArgs.Empty);
      }
      else
        base.OnKeyUp (e);
    }
  }
}
