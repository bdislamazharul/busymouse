using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using BusyMouse.WinUI.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;

namespace BusyMouse.WinUI;

/// <summary>
/// The full window: everything in the flyout, plus distance, interval and the startup options, with Settings and
/// About as pages of their own from the footer menu. Closing it asks whether to exit or keep running in the tray,
/// unless Settings says.
/// </summary>
public sealed partial class MainWindow : Window
{
  public enum Page
  {
    Main,
    Settings,
    About
  }

  private const double ContentWidth = 380;
  private const double TitleBarHeight = 32;
  private const double FooterHeight = 40;

  private static readonly (string Option, string Meaning)[] CommandLine =
  [
    ("-n", "Keep awake on start"),
    ("-m", "Start in the tray"),
    ("-g", "Open this window on start"),
    ("-o MODE", "Default, Diagonal, Quiet or Orbit"),
    ("-s N", "Seconds between nudges, 1–10800"),
    ("-d N", "Distance, 1–120"),
    ("-r", "Random delay"),
    ("-l", "Pause while locked"),
    ("--stop-nudging", "Stop the running copy")
  ];

  private readonly ToggleButton[] tiles;
  private readonly List<ToggleButton> swatches = [];
  private Storyboard? aboutNudge;
  private XamlRoot? root;
  private double pageHeight = 264;
  private double fullHeight = 534;
  private bool placed;
  private bool dialogOpen;
  private Page? firstPage;

  public MainWindow ()
  {
    this.InitializeComponent ();

    this.tiles = [this.DefaultTile, this.DiagonalTile, this.QuietTile, this.OrbitTile];

    App.Appearance.Attach (this, this.Root, this.Solid, () => new MicaBackdrop ());
    this.AppWindow.Title = "BusyMouse";
    this.AppWindow.SetIcon (Path.Combine (AppContext.BaseDirectory, "Assets", "BusyMouse.ico"));

    if (this.AppWindow.Presenter is OverlappedPresenter presenter)
    {
      presenter.IsResizable = false;
      presenter.IsMaximizable = false;
      presenter.SetBorderAndTitleBar (true, false);
    }

    // Start at the design's size, so the first frame is already about right.
    var scale = Native.GetDpiForSystem () / 96.0;
    this.AppWindow.SetContentSize (new SizeInt32 ((int)Math.Round (ContentWidth * scale), (int)Math.Round (336 * scale)));

    this.AppWindow.Closing += this.AppWindow_Closing;
    this.Activated += this.MainWindow_Activated;
    this.Root.Loaded += this.Root_Loaded;
    this.TitleBar.SizeChanged += (_, _) => this.UpdateDragRegion ();

    // The sun on Minimize to tray sinks further below the horizon while pointed at. The button handles some
    // pointer events itself, so listen for handled ones too.
    this.TrayButton.AddHandler (UIElement.PointerEnteredEvent, new PointerEventHandler ((_, _) => this.SinkTraySun (true)), true);
    this.TrayButton.AddHandler (UIElement.PointerExitedEvent, new PointerEventHandler ((_, _) => this.SinkTraySun (false)), true);
    this.TrayButton.AddHandler (UIElement.PointerCanceledEvent, new PointerEventHandler ((_, _) => this.SinkTraySun (false)), true);

    // The custom time's list opens in line, below its box; the window grows to fit it, then shrinks back.
    this.LimitPicker.SizeChanged += (_, e) =>
    {
      if (e.PreviousSize.Height > 0 && e.NewSize.Height != e.PreviousSize.Height)
        this.FitToContent ();
    };

    var assembly = typeof (MainWindow).Assembly;
    var version = assembly.GetName ().Version ?? new Version (0, 0, 0);
    this.VersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
    this.CopyrightText.Text = assembly.GetCustomAttribute<AssemblyCopyrightAttribute> ()?.Copyright ?? string.Empty;
    this.FillCommandLine ();
    this.FillPalette ();
    this.FillRecentAccents ();

    this.Model.PropertyChanged += this.Model_PropertyChanged;
    this.SyncModes ();
  }

  public KeeperViewModel Model => App.Model;

  /// <summary>
  /// Shows the window, restored and in front; on the given page, or wherever it was.
  /// </summary>
  public void ShowAndActivate (Page? page = null)
  {
    // The window sizes itself to the main page when it first loads, so another page waits until then.
    if (page is { } shown)
    {
      if (this.root is null)
        this.firstPage = shown;
      else
        this.ShowPage (shown);
    }

    if (!this.placed)
    {
      this.placed = true;
      this.Center ();
    }

    this.AppWindow.Show ();

    if (this.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
      presenter.Restore ();

    this.Activate ();
  }

  /// <summary>
  /// Asks before exiting: Exit, Minimize to tray, or Cancel.
  /// </summary>
  public async Task AskToExitAsync ()
  {
    if (this.dialogOpen || this.root is null)
      return;

    this.dialogOpen = true;

    try
    {
      var dialog = new ContentDialog
      {
        XamlRoot = this.root,
        RequestedTheme = this.Root.ActualTheme,
        Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        Title = "Exit BusyMouse?",
        Content = this.Model.IsOn
          ? "Your PC is being kept awake. Exiting stops that, so the PC can go to sleep again."
          : "The app will close and its tray icon will go away.",
        PrimaryButtonText = "Exit",
        SecondaryButtonText = "Minimize to tray",
        CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Primary
      };

      dialog.Opened += (_, _) => WidenMiddleButton (dialog);

      switch (await dialog.ShowAsync ())
      {
        case ContentDialogResult.Primary:
          App.Current.Quit ();
          break;

        case ContentDialogResult.Secondary:
          this.HideToTray ();
          break;
      }
    }
    finally
    {
      this.dialogOpen = false;
    }
  }

  public void HideToTray ()
  {
    this.LimitPicker.CloseEditor ();
    this.ShowPage (Page.Main);
    this.AppWindow.Hide ();

    // A hidden window hears no pointer leaving; have the sun back up for when it shows again.
    this.SinkTraySun (false);
  }

  /// <summary>
  /// Does what Settings says the Close button does.
  /// </summary>
  private async Task CloseAsync ()
  {
    switch (this.Model.CloseAction)
    {
      case CloseAction.Tray:
        this.HideToTray ();
        break;

      case CloseAction.Exit:
        App.Current.Quit ();
        break;

      default:
        await this.AskToExitAsync ();
        break;
    }
  }

  private void Root_Loaded (object sender, RoutedEventArgs e)
  {
    this.root = this.Root.XamlRoot;
    this.root.Changed += (_, _) =>
    {
      this.FitToContent ();
      this.UpdateDragRegion ();
      this.SyncPreview ();
    };

    this.FitToContent ();
    this.Center ();
    this.UpdateDragRegion ();
    this.SyncPreview ();

    if (this.firstPage is { } page)
      this.ShowPage (page);
  }

  private void MainWindow_Activated (object sender, WindowActivatedEventArgs args) =>
    this.TitleIcon.Opacity = this.TitleText.Opacity = args.WindowActivationState == WindowActivationState.Deactivated ? 0.6 : 1;

  private async void AppWindow_Closing (AppWindow sender, AppWindowClosingEventArgs args)
  {
    if (App.Current.IsQuitting)
      return;

    // Alt+F4 and the taskbar's Close go the same way as the Close button, rather than quitting the tray app outright.
    args.Cancel = true;
    await this.CloseAsync ();
  }

  private void Model_PropertyChanged (object? sender, PropertyChangedEventArgs e)
  {
    switch (e.PropertyName)
    {
      case nameof (KeeperViewModel.Mode):
        this.SyncModes ();
        break;

      case nameof (KeeperViewModel.IsOn):
        this.SyncPreview ();
        break;

      case nameof (KeeperViewModel.AccentColor):
        this.FillRecentAccents ();
        break;
    }
  }

  private void SyncModes ()
  {
    for (var i = 0; i < this.tiles.Length; i++)
      this.tiles[i].IsChecked = (int)this.Model.Mode == i;
  }

  /// <summary>
  /// The preview moves while on, and only while the window is on screen.
  /// </summary>
  private void SyncPreview ()
  {
    var on = this.Model.IsOn;
    var visible = this.root is { IsHostVisible: true };

    this.Preview.Tone = on ? PreviewTone.Active : PreviewTone.Idle;
    this.Preview.Motion = !on ? PreviewMotion.Still : visible ? PreviewMotion.Running : PreviewMotion.Resting;

    // The one in Settings shows what the choices there look like, on or off.
    this.SettingsPreview.Motion = visible && this.SettingsPage.Visibility == Visibility.Visible ? PreviewMotion.Running : PreviewMotion.Resting;
  }

  /// <summary>
  /// Sizes the window to the main page as it stands, More open or folded; Settings and About scroll within the
  /// height of the main page with everything open. A window that grows past the bottom of the screen moves up.
  /// </summary>
  private void FitToContent ()
  {
    if (this.root is null)
      return;

    if (this.MainPage.Visibility == Visibility.Visible)
    {
      var space = new Size (ContentWidth, double.PositiveInfinity);
      var folded = this.MoreOptions.Visibility;

      this.MoreOptions.Visibility = Visibility.Visible;
      this.MainPage.Measure (space);
      this.fullHeight = this.MainPage.DesiredSize.Height;

      this.MoreOptions.Visibility = folded;
      this.MainPage.Measure (space);
      this.pageHeight = this.MainPage.DesiredSize.Height;
    }

    var scale = this.root.RasterizationScale;
    var height = TitleBarHeight + (this.MainPage.Visibility == Visibility.Visible ? this.pageHeight : this.fullHeight) + FooterHeight;

    this.AppWindow.SetContentSize (new SizeInt32 ((int)Math.Round (ContentWidth * scale), (int)Math.Round (height * scale)));

    var area = DisplayArea.GetFromWindowId (this.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
    var at = this.AppWindow.Position;
    var over = at.Y + this.AppWindow.Size.Height - (area.Y + area.Height);

    if (over > 0)
      this.AppWindow.Move (new PointInt32 (at.X, Math.Max (area.Y, at.Y - over)));
  }

  private void Center ()
  {
    var area = DisplayArea.GetFromWindowId (this.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
    var size = this.AppWindow.Size;

    this.AppWindow.Move (new PointInt32 (area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2));
  }

  /// <summary>
  /// The window has no system title bar, so mark ours, less its buttons, as the place to drag it by.
  /// </summary>
  private void UpdateDragRegion ()
  {
    if (this.root is null)
      return;

    var scale = this.root.RasterizationScale;
    var width = this.TitleBar.ActualWidth - this.TrayButton.ActualWidth - this.MinimizeButton.ActualWidth - this.ExitButton.ActualWidth;
    var drag = new RectInt32 (0, 0, (int)Math.Round (Math.Max (0, width) * scale), (int)Math.Round (TitleBarHeight * scale));

    InputNonClientPointerSource.GetForWindowId (this.AppWindow.Id).SetRegionRects (NonClientRegionKind.Caption, [drag]);
  }

  private void ShowPage (Page page)
  {
    if (page != Page.Main)
      this.LimitPicker.CloseEditor ();

    // Settings opens at the top, unless it's already showing.
    if (page == Page.Settings && this.SettingsPage.Visibility != Visibility.Visible)
      this.SettingsScroll.ChangeView (null, 0, null, true);

    this.MainPage.Visibility = page == Page.Main ? Visibility.Visible : Visibility.Collapsed;
    this.SettingsPage.Visibility = page == Page.Settings ? Visibility.Visible : Visibility.Collapsed;
    this.AboutPage.Visibility = page == Page.About ? Visibility.Visible : Visibility.Collapsed;
    this.MoreButton.Visibility = page == Page.Main ? Visibility.Visible : Visibility.Collapsed;
    this.SyncPreview ();
    this.FitToContent ();

    if (page == Page.About)
    {
      this.AboutScroll.ChangeView (null, 0, null, true);
      this.StartAboutNudge ();
    }
    else
    {
      this.aboutNudge?.Stop ();
    }
  }

  /// <summary>
  /// Every four seconds the About icon gives a little nudge of its own.
  /// </summary>
  private void StartAboutNudge ()
  {
    if (!new Windows.UI.ViewManagement.UISettings ().AnimationsEnabled)
      return;

    if (this.aboutNudge is null)
    {
      this.aboutNudge = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

      foreach (var (property, values) in new[] { ("X", new[] { 0.0, 0, 2, -2, 1, 0 }), ("Y", new[] { 0.0, 0, -2, 2, -1, 0 }) })
      {
        var frames = new DoubleAnimationUsingKeyFrames ();
        double[] times = [0, 3.44, 3.56, 3.68, 3.8, 4];

        for (var i = 0; i < times.Length; i++)
          frames.KeyFrames.Add (new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds (times[i]), Value = values[i] });

        Storyboard.SetTarget (frames, this.AboutIcon.Nudge);
        Storyboard.SetTargetProperty (frames, property);
        this.aboutNudge.Children.Add (frames);
      }
    }

    this.aboutNudge.Begin ();
  }

  private void FillCommandLine ()
  {
    this.OptionsGrid.ColumnDefinitions.Add (new ColumnDefinition { Width = GridLength.Auto });
    this.OptionsGrid.ColumnDefinitions.Add (new ColumnDefinition { Width = new GridLength (1, GridUnitType.Star) });

    var chip = (Style)Application.Current.Resources["ChipStyle"];
    var chipText = (Style)Application.Current.Resources["ChipTextStyle"];

    for (var i = 0; i < CommandLine.Length; i++)
    {
      this.OptionsGrid.RowDefinitions.Add (new RowDefinition { Height = GridLength.Auto });

      var option = new Border { Style = chip, Child = new TextBlock { Style = chipText, Text = CommandLine[i].Option } };
      Grid.SetRow (option, i);
      this.OptionsGrid.Children.Add (option);

      var meaning = new TextBlock { Text = CommandLine[i].Meaning, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
      Grid.SetRow (meaning, i);
      Grid.SetColumn (meaning, 1);
      this.OptionsGrid.Children.Add (meaning);
    }
  }

  /// <summary>
  /// The palette, eight to a row.
  /// </summary>
  private void FillPalette ()
  {
    const int Columns = 8;

    for (var c = 0; c < Columns; c++)
      this.PaletteGrid.ColumnDefinitions.Add (new ColumnDefinition { Width = GridLength.Auto });

    for (var i = 0; i < Accents.Palette.Length; i++)
    {
      if (i % Columns == 0)
        this.PaletteGrid.RowDefinitions.Add (new RowDefinition { Height = GridLength.Auto });

      var (hex, name) = Accents.Palette[i];
      var swatch = this.MakeSwatch (Accents.Parse (hex), name);
      Grid.SetRow (swatch, i / Columns);
      Grid.SetColumn (swatch, i % Columns);
      this.PaletteGrid.Children.Add (swatch);
    }

    this.SyncSwatches ();
  }

  private void FillRecentAccents ()
  {
    foreach (var old in this.RecentAccentsPanel.Children)
      this.swatches.Remove ((ToggleButton)old);

    this.RecentAccentsPanel.Children.Clear ();

    foreach (var color in this.Model.RecentAccents)
      this.RecentAccentsPanel.Children.Add (this.MakeSwatch (color, Accents.NameOf (color)));

    this.RecentAccentsSection.Visibility = this.Model.RecentAccents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    this.SyncSwatches ();
  }

  private ToggleButton MakeSwatch (Windows.UI.Color color, string name)
  {
    var swatch = new ToggleButton
    {
      Style = (Style)Application.Current.Resources["SwatchToggleStyle"],
      Background = new SolidColorBrush (color),
      Foreground = new SolidColorBrush (Accents.Contrast (color)),
      Tag = color
    };

    AutomationProperties.SetName (swatch, name);
    ToolTipService.SetToolTip (swatch, name);
    swatch.Click += this.Swatch_Click;
    this.swatches.Add (swatch);

    return swatch;
  }

  /// <summary>
  /// Ticks the swatches of the accent color in use, wherever it appears.
  /// </summary>
  private void SyncSwatches ()
  {
    var current = this.Model.AccentColor;

    foreach (var swatch in this.swatches)
      swatch.IsChecked = current is { } color && color.Equals ((Windows.UI.Color)swatch.Tag);
  }

  private void ModeTile_Click (object sender, RoutedEventArgs e)
  {
    this.Model.Mode = (NudgeMode)Array.IndexOf (this.tiles, sender);
    this.SyncModes ();
  }

  /// <summary>
  /// On a log scale one arrow-key step of the slider can be a fraction of a pixel or several, so the arrows step
  /// the distance itself, by one.
  /// </summary>
  private void DistanceSlider_PreviewKeyDown (object sender, KeyRoutedEventArgs e)
  {
    var step = e.Key switch
    {
      VirtualKey.Right or VirtualKey.Up => 1,
      VirtualKey.Left or VirtualKey.Down => -1,
      _ => 0
    };

    if (step == 0)
      return;

    this.Model.Distance += step;
    e.Handled = true;
  }

  private void MinimizeButton_Click (object sender, RoutedEventArgs e)
  {
    if (this.AppWindow.Presenter is OverlappedPresenter presenter)
      presenter.Minimize ();
  }

  private async void ExitButton_Click (object sender, RoutedEventArgs e) => await this.CloseAsync ();

  private void OpenSettings_Click (object sender, RoutedEventArgs e) => this.ShowPage (Page.Settings);

  private void SettingsBack_Click (object sender, RoutedEventArgs e) => this.ShowPage (Page.Main);

  private void OpenAbout_Click (object sender, RoutedEventArgs e) => this.ShowPage (Page.About);

  private void AboutBack_Click (object sender, RoutedEventArgs e) => this.ShowPage (Page.Main);

  private void HideToTray_Click (object sender, RoutedEventArgs e) => this.HideToTray ();

  private void MoreButton_Toggled (object? sender, EventArgs e) => this.FitToContent ();

  private void SinkTraySun (bool down) => this.TraySun.Translation = down ? new System.Numerics.Vector3 (0, 1.5f, 0) : System.Numerics.Vector3.Zero;

  private void Swatch_Click (object sender, RoutedEventArgs e)
  {
    this.Model.UseAccent ((Windows.UI.Color)((FrameworkElement)sender).Tag);

    // Clicking the ticked one again unticks it on the way; put the ticks back as they should be.
    this.SyncSwatches ();
  }

  /// <summary>
  /// Any color at all, in a dialog as Windows does it; a flyout this tall wouldn't fit the window.
  /// </summary>
  private async void CustomColor_Click (object sender, RoutedEventArgs e)
  {
    if (this.dialogOpen || this.root is null)
      return;

    var picker = new ColorPicker
    {
      Color = this.Model.AccentColor ?? new Windows.UI.ViewManagement.UISettings ().GetColorValue (Windows.UI.ViewManagement.UIColorType.Accent),
      ColorSpectrumShape = ColorSpectrumShape.Box,
      IsAlphaEnabled = false,
      IsColorChannelTextInputVisible = false,
      IsHexInputVisible = true,
      IsMoreButtonVisible = false
    };

    var dialog = new ContentDialog
    {
      XamlRoot = this.root,
      RequestedTheme = this.Root.ActualTheme,
      Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
      Title = "Custom accent color",
      Content = picker,
      PrimaryButtonText = "Done",
      CloseButtonText = "Cancel",
      DefaultButton = ContentDialogButton.Primary
    };

    this.dialogOpen = true;

    try
    {
      if (await dialog.ShowAsync () == ContentDialogResult.Primary)
        this.Model.UseAccent (picker.Color);
    }
    finally
    {
      this.dialogOpen = false;
    }
  }

  // The license ships next to the app; opens in whatever reads .txt files.
  private void License_Click (object sender, RoutedEventArgs e)
  {
    try
    {
      Process.Start (new ProcessStartInfo (Path.Combine (AppContext.BaseDirectory, "LICENSE.txt")) { UseShellExecute = true })?.Dispose ();
    }
    catch (Win32Exception ex)
    {
      Debug.WriteLine ($"License not opened: {ex.Message}");
    }
  }

  /// <summary>
  /// The dialog's buttons share the width equally, which cuts "Minimize to tray" short in a window this narrow;
  /// give the middle one more of it. The dialog's own visual state sets that width, so only an animation can
  /// override it.
  /// </summary>
  private static void WidenMiddleButton (ContentDialog dialog)
  {
    if (FindByName (dialog, "CommandSpace") is not Grid { ColumnDefinitions.Count: 5 } commands)
      return;

    var widen = new ObjectAnimationUsingKeyFrames ();
    widen.KeyFrames.Add (new DiscreteObjectKeyFrame { KeyTime = TimeSpan.Zero, Value = new GridLength (1.7, GridUnitType.Star) });
    Storyboard.SetTarget (widen, commands.ColumnDefinitions[2]);
    Storyboard.SetTargetProperty (widen, "Width");

    var hold = new Storyboard ();
    hold.Children.Add (widen);
    hold.Begin ();
  }

  private static FrameworkElement? FindByName (DependencyObject parent, string name)
  {
    for (var i = 0; i < VisualTreeHelper.GetChildrenCount (parent); i++)
    {
      var child = VisualTreeHelper.GetChild (parent, i);

      if (child is FrameworkElement element && element.Name == name)
        return element;

      if (FindByName (child, name) is { } found)
        return found;
    }

    return null;
  }
}
