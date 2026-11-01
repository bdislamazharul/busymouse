using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace BusyMouse.WinUI;

/// <summary>
/// A tray app: it lives in the notification area, with the flyout on a click and the full window on request.
/// It runs until asked to exit, whatever windows are open.
/// </summary>
public partial class App : Application
{
  private readonly List<(EventWaitHandle Event, RegisteredWaitHandle Wait)> signals = [];
  private DispatcherQueue? queue;
  private TrayHost? tray;
  private MainWindow? window;
  private FlyoutWindow? flyout;
  private TrayMenu? menu;

  public App ()
  {
    this.InitializeComponent ();
    this.DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
  }

  public static new App Current => (App)Application.Current;

  public static KeeperViewModel Model { get; private set; } = null!;

  internal static StartupOptions Options { get; set; } = null!;

  internal static Appearance Appearance { get; private set; } = null!;

  public bool IsQuitting { get; private set; }

  protected override void OnLaunched (LaunchActivatedEventArgs args)
  {
    this.queue = DispatcherQueue.GetForCurrentThread ();

    var settings = AppSettings.Load ();
    Options.ApplyTo (settings);
    Model = new KeeperViewModel (settings, this.queue);
    Appearance = new Appearance (Model, this.queue);

    this.tray = new TrayHost (Model.TrayIcon);
    this.tray.Invoked += (_, _) => this.ToggleFlyout ();
    this.tray.ToggleRequested += (_, _) => Model.Toggle ();
    this.tray.OpenRequested += (_, _) => this.ShowWindow ();
    this.tray.SettingsRequested += (_, _) => this.ShowWindow (MainWindow.Page.Settings);
    this.tray.AboutRequested += (_, _) => this.ShowWindow (MainWindow.Page.About);
    this.tray.ExitRequested += (_, _) => this.Quit ();
    this.tray.MenuRequested += this.Tray_MenuRequested;
    this.tray.LockChanged += (_, locked) => Model.WorkstationLocked = locked;
    this.tray.Resumed += (_, _) => Model.Resumed ();
    this.tray.SessionEnding += (_, _) => Model.SaveNow ();

    Model.PropertyChanged += this.Model_PropertyChanged;
    this.tray.Update (Model.IsOn, Model.TrayTip);

    this.Listen (Program.StopEventName, Model.TurnOff);
    this.Listen (Program.ShowEventName, () => this.ShowWindow ());

    if (Options.ShowWindow == true || !(Options.Minimized ?? settings.StartInTray))
      this.ShowWindow ();

    if (Options.Nudge ?? settings.KeepAwakeOnStart)
      Model.TurnOn ();
  }

  /// <summary>
  /// Opens the full window, closing the flyout if it's up; on the given page, or wherever it was.
  /// </summary>
  public void ShowWindow (MainWindow.Page? page = null)
  {
    this.flyout?.Dismiss ();

    this.window ??= new MainWindow ();
    this.window.ShowAndActivate (page);
  }

  public void Quit ()
  {
    if (this.IsQuitting)
      return;

    this.IsQuitting = true;

    foreach (var (signal, wait) in this.signals)
    {
      _ = wait.Unregister (null);
      signal.Dispose ();
    }

    Model.Dispose ();
    this.tray?.Dispose ();
    this.menu?.Close ();
    this.flyout?.Close ();
    this.window?.Close ();

    this.Exit ();
  }

  private void ToggleFlyout ()
  {
    this.flyout ??= new FlyoutWindow ();

    // The click that lands on the icon has already closed the flyout by taking the focus; it shouldn't reopen it.
    if (this.flyout.IsOpen || this.flyout.JustClosed)
      this.flyout.Dismiss ();
    else
      this.flyout.ShowNear (this.tray?.IconBounds);
  }

  private void Tray_MenuRequested (object? sender, MenuRequestedEventArgs e)
  {
    if (this.tray is not { } tray)
      return;

    try
    {
      this.flyout?.Dismiss ();
      this.menu ??= new TrayMenu (tray);
      this.menu.ShowAt (e.X, e.Y);
      e.Handled = true;
    }
    catch (Exception ex)
    {
      // Fall back to the plain Win32 menu.
      System.Diagnostics.Debug.WriteLine ($"Tray menu: {ex}");
    }
  }

  private void Model_PropertyChanged (object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName is null or "" or nameof (KeeperViewModel.IsOn) or nameof (KeeperViewModel.TrayTip))
      this.tray?.Update (Model.IsOn, Model.TrayTip);

    if (e.PropertyName is null or "" or nameof (KeeperViewModel.TrayIcon))
      this.tray?.SetStyle (Model.TrayIcon);
  }

  /// <summary>
  /// Runs an action on the UI thread whenever another process sets the named event.
  /// </summary>
  private void Listen (string name, Action action)
  {
    var signal = new EventWaitHandle (false, EventResetMode.AutoReset, name);
    var wait = ThreadPool.RegisterWaitForSingleObject (
        signal,
        (_, _) => this.queue?.TryEnqueue (() =>
        {
          if (!this.IsQuitting)
            action ();
        }),
        null,
        Timeout.Infinite,
        false);

    this.signals.Add ((signal, wait));
  }
}
