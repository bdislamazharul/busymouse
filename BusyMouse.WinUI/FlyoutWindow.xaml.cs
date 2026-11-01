using System.ComponentModel;
using BusyMouse.WinUI.Controls;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;

namespace BusyMouse.WinUI;

/// <summary>
/// The tray flyout: On/Off and the timers, the mode, and how long to keep awake. It sits in the corner by the
/// tray and goes away as soon as it loses the focus, like the system's own flyouts.
/// </summary>
public sealed partial class FlyoutWindow : Window
{
  private const double ContentWidth = 360;
  private const double EdgeMargin = 12;

  // Anything shorter than this after closing came from the click that closed it.
  private const long ReopenDelayMs = 300;

  private readonly ToggleButton[] tiles;
  private readonly ModePreview[] previews;
  private readonly nint hwnd;
  private bool open;
  private long closedAt;
  private RectInt32 workArea;
  private bool alongTop;
  private bool alongLeft;
  private double scale = 1;

  public FlyoutWindow ()
  {
    this.InitializeComponent ();

    this.tiles = [this.DefaultTile, this.DiagonalTile, this.QuietTile, this.OrbitTile];
    this.previews = [.. this.tiles.Select (t => (ModePreview)t.Content)];
    this.hwnd = Win32Interop.GetWindowFromWindowId (this.AppWindow.Id);

    App.Appearance.Attach (this, this.Root, this.Solid, () => new DesktopAcrylicBackdrop ());
    this.AppWindow.Title = "BusyMouse";
    this.AppWindow.IsShownInSwitchers = false;
    this.AppWindow.SetIcon (Path.Combine (AppContext.BaseDirectory, "Assets", "BusyMouse.ico"));

    if (this.AppWindow.Presenter is OverlappedPresenter presenter)
    {
      presenter.IsResizable = false;
      presenter.IsMaximizable = false;
      presenter.IsMinimizable = false;
      presenter.IsAlwaysOnTop = true;
      presenter.SetBorderAndTitleBar (true, false);
    }

    this.Activated += this.FlyoutWindow_Activated;
    this.AppWindow.Closing += (_, args) => args.Cancel = !App.Current.IsQuitting;
    this.Body.SizeChanged += (_, _) =>
    {
      if (this.open)
        this.Place ();
    };

    this.Model.PropertyChanged += this.Model_PropertyChanged;
    this.SyncModes ();
  }

  public KeeperViewModel Model => App.Model;

  public bool IsOpen => this.open;

  /// <summary>
  /// True for a moment after the flyout closes.
  /// </summary>
  public bool JustClosed => Environment.TickCount64 - this.closedAt < ReopenDelayMs;

  /// <summary>
  /// Opens in the corner of the screen by the tray icon (in physical pixels), or by the pointer if there's no
  /// icon to go by.
  /// </summary>
  public void ShowNear (Rect? icon)
  {
    PointInt32 anchor;

    if (icon is { } r)
      anchor = new PointInt32 ((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
    else
      anchor = GetCursor ();

    var display = DisplayArea.GetFromPoint (anchor, DisplayAreaFallback.Nearest);
    var outer = display.OuterBounds;
    this.workArea = display.WorkArea;

    // Keep to the taskbar's side of the screen, at its right-hand end (or bottom end when it stands upright).
    this.alongTop = this.workArea.Y > outer.Y;
    this.alongLeft = this.workArea.X > outer.X;
    this.MoreButton.OpensUp = !this.alongTop;
    this.scale = MonitorScale (anchor);

    // Move onto that screen first, so a change of DPI on the way doesn't resize what we set next.
    this.AppWindow.Move (new PointInt32 (this.workArea.X, this.workArea.Y));

    this.open = true;
    this.Place ();
    this.AppWindow.Show ();
    this.Activate ();
    _ = Native.SetForegroundWindow (this.hwnd);
  }

  public void Dismiss ()
  {
    if (!this.open)
      return;

    this.open = false;
    this.closedAt = Environment.TickCount64;
    this.LimitPicker.CloseEditor ();

    foreach (var preview in this.previews)
      preview.Motion = PreviewMotion.Resting;

    this.AppWindow.Hide ();
  }

  private static PointInt32 GetCursor ()
  {
    unsafe
    {
      Native.POINT point;
      _ = Native.GetCursorPos (&point);

      return new PointInt32 (point.X, point.Y);
    }
  }

  private static double MonitorScale (PointInt32 at)
  {
    unsafe
    {
      var monitor = Native.MonitorFromPoint (new Native.POINT { X = at.X, Y = at.Y }, Native.MONITOR_DEFAULTTONEAREST);
      uint dpiX, dpiY;

      return Native.GetDpiForMonitor (monitor, Native.MDT_EFFECTIVE_DPI, &dpiX, &dpiY) == 0 ? dpiX / 96.0 : Native.GetDpiForSystem () / 96.0;
    }
  }

  /// <summary>
  /// Sizes the flyout to its content and keeps it in its corner, growing away from the taskbar.
  /// </summary>
  private void Place ()
  {
    this.Root.Measure (new Size (ContentWidth, double.PositiveInfinity));

    var height = this.Root.DesiredSize.Height;

    if (height < 1)
      height = 352;

    var width = (int)Math.Round (ContentWidth * this.scale);
    var tall = (int)Math.Round (height * this.scale);
    var margin = (int)Math.Round (EdgeMargin * this.scale);

    this.AppWindow.SetContentSize (new SizeInt32 (width, tall));

    var size = this.AppWindow.Size;
    var x = this.alongLeft ? this.workArea.X + margin : this.workArea.X + this.workArea.Width - size.Width - margin;
    var y = this.alongTop ? this.workArea.Y + margin : this.workArea.Y + this.workArea.Height - size.Height - margin;

    this.AppWindow.Move (new PointInt32 (x, y));
  }

  private void FlyoutWindow_Activated (object sender, WindowActivatedEventArgs args)
  {
    if (args.WindowActivationState != WindowActivationState.Deactivated || !this.open)
      return;

    // The recent-times list is a window of its own, owned by this one; using it isn't leaving the flyout.
    var foreground = Native.GetForegroundWindow ();

    if (foreground != 0 && Native.GetAncestor (foreground, Native.GA_ROOTOWNER) == this.hwnd)
      return;

    this.Dismiss ();
  }

  private void Model_PropertyChanged (object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName == nameof (KeeperViewModel.Mode))
      this.SyncModes ();
  }

  private void SyncModes ()
  {
    for (var i = 0; i < this.tiles.Length; i++)
    {
      var selected = (int)this.Model.Mode == i;

      this.tiles[i].IsChecked = selected;
      this.previews[i].Tone = selected ? PreviewTone.TileSelected : PreviewTone.Tile;
    }
  }

  private void ModeTile_Click (object sender, RoutedEventArgs e)
  {
    this.Model.Mode = (NudgeMode)Array.IndexOf (this.tiles, sender);
    this.SyncModes ();
  }

  // Each tile shows its movement only while the pointer is over it.
  private void ModeTile_PointerEntered (object sender, PointerRoutedEventArgs e) =>
    this.previews[Array.IndexOf (this.tiles, sender)].Motion = PreviewMotion.Running;

  private void ModeTile_PointerExited (object sender, PointerRoutedEventArgs e) =>
    this.previews[Array.IndexOf (this.tiles, sender)].Motion = PreviewMotion.Resting;

  private void Root_KeyDown (object sender, KeyRoutedEventArgs e)
  {
    if (e.Key != VirtualKey.Escape)
      return;

    this.Dismiss ();
    e.Handled = true;
  }

  private void Open_Click (object sender, RoutedEventArgs e) => App.Current.ShowWindow ();
}
