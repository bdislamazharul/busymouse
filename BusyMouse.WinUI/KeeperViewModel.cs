using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace BusyMouse.WinUI;

/// <summary>
/// The state behind both the tray flyout and the window: the keeper, the settings, the time limit, and the
/// texts both of them show. Lives on the UI thread.
/// </summary>
public sealed partial class KeeperViewModel : INotifyPropertyChanged, IDisposable
{
  /// <summary>
  /// The intervals the "Nudge every" slider steps through, in seconds.
  /// </summary>
  public static readonly int[] IntervalPresets = [1, 5, 10, 15, 30, 45, 60, 90, 120, 180, 300, 600, 900, 1800, 3600, 7200, 10800];

  /// <summary>
  /// The far end of the "Distance" slider; see <see cref="DistancePosition"/>.
  /// </summary>
  public const double DistanceSteps = 1000;

  /// <summary>
  /// Minutes for the fixed time-limit choices; 0 is no limit.
  /// </summary>
  private static readonly int[] PresetMinutes = [0, 30, 60, 120];

  private readonly AppSettings settings;
  private readonly AwakeKeeper keeper = new ();
  private readonly DispatcherQueueTimer clock;
  private readonly DispatcherQueueTimer paneTimer;
  private readonly DispatcherQueueTimer saveTimer;

  private double distancePosition = double.NaN;
  private bool isOn;
  private bool locked;
  private DateTime? stopsAt;

  // When the countdown last shown runs out, to tell a countdown starting over from the interval being changed.
  private DateTimeOffset countdownEnd;
  private int pane;
  private bool panePinned;

  private int paneIndex;
  private string statusTitle = string.Empty;
  private string statusLine = string.Empty;
  private string statusTip = string.Empty;
  private string powerTip = string.Empty;
  private string timerName = string.Empty;
  private string timerTip = string.Empty;
  private string trayTip = string.Empty;
  private string pane0Caption = string.Empty;
  private string pane0Value = string.Empty;
  private string pane1Caption = string.Empty;
  private string pane1Value = string.Empty;
  private string pane2Caption = string.Empty;
  private string pane2Value = string.Empty;

  public KeeperViewModel (AppSettings settings, DispatcherQueue queue)
  {
    this.settings = settings;

    this.keeper.Mode = settings.Mode;
    this.keeper.Distance = settings.Distance;
    this.keeper.IntervalSeconds = settings.Interval;
    this.keeper.RandomInterval = settings.RandomDelay;
    this.keeper.Nudged += this.Keeper_Nudged;
    this.keeper.Expired += this.Keeper_Expired;

    this.Recents = new ObservableCollection<TimeLimit> (settings.RecentTimeLimits);
    this.RecentAccents = new ObservableCollection<Color> (settings.RecentAccentColors.Select (Accents.Parse));

    // Fast enough that the seconds never visibly skip; the texts only notify when they change.
    this.clock = queue.CreateTimer ();
    this.clock.Interval = TimeSpan.FromMilliseconds (250);
    this.clock.Tick += (_, _) => this.Refresh ();

    this.paneTimer = queue.CreateTimer ();
    this.paneTimer.Interval = TimeSpan.FromSeconds (5);
    this.paneTimer.IsRepeating = false;
    this.paneTimer.Tick += (_, _) => this.AdvancePane ();

    this.saveTimer = queue.CreateTimer ();
    this.saveTimer.Interval = TimeSpan.FromMilliseconds (400);
    this.saveTimer.IsRepeating = false;
    this.saveTimer.Tick += (_, _) => this.settings.Save ();

    this.Refresh ();
  }

  public event PropertyChangedEventHandler? PropertyChanged;

  /// <summary>
  /// Raised each time the next-nudge countdown runs out and starts over, for the little nudge animations; after
  /// the values have changed. That's whether or not the pointer moved: a nudge is skipped when the user has
  /// moved it themselves, but the countdown restarts all the same.
  /// </summary>
  public event EventHandler? CountdownRestarted;

  #region Running

  public bool IsOn => this.isOn;

  /// <summary>
  /// When the current run started; the hourglass keeps time from it.
  /// </summary>
  public DateTimeOffset StartedAt => this.keeper.ActiveSince;

  /// <summary>
  /// Tells the keeper whether Windows is locked, so it can pause if <see cref="PauseWhenLocked"/> is on.
  /// </summary>
  public bool WorkstationLocked
  {
    get => this.locked;
    set
    {
      this.locked = value;
      this.ApplyPause ();
    }
  }

  public void Toggle ()
  {
    if (this.isOn)
      this.TurnOff ();
    else
      this.TurnOn ();
  }

  public void TurnOn ()
  {
    if (this.isOn)
      return;

    this.isOn = true;

    if (!this.panePinned)
      this.pane = 0;

    this.ArmStop (DateTime.Now);
    this.keeper.Start ();
    this.clock.Start ();

    if (!this.panePinned)
      this.RestartPaneTimer ();

    this.OnChanged (nameof (this.IsOn));
    this.OnChanged (nameof (this.StartedAt));
    this.Refresh ();
  }

  public void TurnOff ()
  {
    if (!this.isOn)
      return;

    this.keeper.Stop ();
    this.Stopped ();
  }

  /// <summary>
  /// Call after the PC wakes from sleep.
  /// </summary>
  public void Resumed ()
  {
    this.keeper.Resume ();
    this.Refresh ();
  }

  #endregion Running

  #region Settings

  public NudgeMode Mode
  {
    get => this.settings.Mode;
    set
    {
      if (value == this.settings.Mode)
        return;

      this.settings.Mode = value;
      this.keeper.Mode = value;
      this.OnChanged ();
      this.SaveSoon ();
    }
  }

  public int Distance
  {
    get => this.settings.Distance;
    set
    {
      value = Math.Clamp (value, AppSettings.MinDistance, AppSettings.MaxDistance);

      if (value == this.settings.Distance)
        return;

      this.settings.Distance = value;
      this.keeper.Distance = value;
      this.OnChanged ();
      this.OnChanged (nameof (this.DistancePosition));
      this.OnChanged (nameof (this.DistanceText));
      this.SaveSoon ();
    }
  }

  /// <summary>
  /// <see cref="Distance"/> as a slider position from 0 to <see cref="DistanceSteps"/>, on a log scale so the
  /// small distances get as much room as the large ones: 1 to 10 takes about half the track.
  /// </summary>
  public double DistancePosition
  {
    // Keep the thumb where it was dragged to, as long as that still rounds to the distance shown.
    get => DistanceAt (this.distancePosition) == this.Distance ? this.distancePosition : PositionOf (this.Distance);
    set
    {
      this.distancePosition = value;
      this.Distance = DistanceAt (value);
    }
  }

  public string DistanceText => $"{this.Distance}×";

  /// <summary>
  /// Seconds between nudges, 1 to 10800. May be any value, though the slider only offers the presets.
  /// </summary>
  public int Interval
  {
    get => this.settings.Interval;
    set
    {
      value = Math.Clamp (value, AppSettings.MinInterval, AppSettings.MaxInterval);

      if (value == this.settings.Interval)
        return;

      this.settings.Interval = value;
      this.keeper.IntervalSeconds = value;
      this.OnChanged ();
      this.OnChanged (nameof (this.IntervalStep));
      this.OnChanged (nameof (this.IntervalText));
      this.SaveSoon ();
      this.Refresh ();
    }
  }

  /// <summary>
  /// <see cref="Interval"/> as a slider position: the first preset at least as long.
  /// </summary>
  public double IntervalStep
  {
    get
    {
      var index = Array.FindIndex (IntervalPresets, p => p >= this.Interval);

      return index < 0 ? IntervalPresets.Length - 1 : index;
    }
    set
    {
      var index = Math.Clamp ((int)Math.Round (value), 0, IntervalPresets.Length - 1);

      // Leave an in-between interval (from the command line, say) alone until the slider really moves.
      if (index != (int)this.IntervalStep)
        this.Interval = IntervalPresets[index];
    }
  }

  public string IntervalText => Format.Every (this.Interval);

  public bool RandomDelay
  {
    get => this.settings.RandomDelay;
    set
    {
      if (value == this.settings.RandomDelay)
        return;

      this.settings.RandomDelay = value;
      this.keeper.RandomInterval = value;
      this.OnChanged ();
      this.SaveSoon ();
      this.Refresh ();
    }
  }

  public bool PauseWhenLocked
  {
    get => this.settings.PauseWhenLocked;
    set
    {
      if (value == this.settings.PauseWhenLocked)
        return;

      this.settings.PauseWhenLocked = value;
      this.OnChanged ();
      this.OnChanged (nameof (this.StayActiveWhenLocked));
      this.SaveSoon ();
      this.ApplyPause ();
    }
  }

  /// <summary>
  /// <see cref="PauseWhenLocked"/> the way round the switch shows it: on, the default, carries on nudging while
  /// Windows is locked.
  /// </summary>
  public bool StayActiveWhenLocked
  {
    get => !this.PauseWhenLocked;
    set => this.PauseWhenLocked = !value;
  }

  public bool StartInTray
  {
    get => this.settings.StartInTray;
    set
    {
      if (value == this.settings.StartInTray)
        return;

      this.settings.StartInTray = value;
      this.OnChanged ();
      this.SaveSoon ();
    }
  }

  public bool KeepAwakeOnStart
  {
    get => this.settings.KeepAwakeOnStart;
    set
    {
      if (value == this.settings.KeepAwakeOnStart)
        return;

      this.settings.KeepAwakeOnStart = value;
      this.OnChanged ();
      this.SaveSoon ();
    }
  }

  #endregion Settings

  #region App settings

  /// <summary>
  /// The order of <see cref="AppTheme"/>, which is the order of the choices on the Settings page.
  /// </summary>
  public int ThemeChoice
  {
    get => (int)this.settings.Theme;
    set
    {
      var theme = (AppTheme)value;

      // A ComboBox reports -1 while its items are replaced; that isn't a choice.
      if (!Enum.IsDefined (theme) || theme == this.settings.Theme)
        return;

      this.settings.Theme = theme;
      this.OnChanged ();
      this.OnChanged (nameof (this.IsScheduled));
      this.SaveSoon ();
    }
  }

  public AppTheme Theme => this.settings.Theme;

  public bool IsScheduled => this.settings.Theme == AppTheme.Scheduled;

  /// <summary>
  /// For the time-of-day theme: when light starts.
  /// </summary>
  public TimeSpan LightFrom
  {
    get => TimeSpan.FromMinutes (this.settings.LightFrom);
    set
    {
      var minutes = MinuteOfDay (value);

      if (minutes == this.settings.LightFrom)
        return;

      this.settings.LightFrom = minutes;
      this.OnChanged ();
      this.SaveSoon ();
    }
  }

  /// <summary>
  /// For the time-of-day theme: when dark starts.
  /// </summary>
  public TimeSpan DarkFrom
  {
    get => TimeSpan.FromMinutes (this.settings.DarkFrom);
    set
    {
      var minutes = MinuteOfDay (value);

      if (minutes == this.settings.DarkFrom)
        return;

      this.settings.DarkFrom = minutes;
      this.OnChanged ();
      this.SaveSoon ();
    }
  }

  public bool Transparency
  {
    get => this.settings.Transparency;
    set
    {
      if (value == this.settings.Transparency)
        return;

      this.settings.Transparency = value;
      this.OnChanged ();
      this.SaveSoon ();
    }
  }

  /// <summary>
  /// 0 to use the Windows accent color, 1 for one picked here.
  /// </summary>
  public int AccentChoice
  {
    get => this.settings.CustomAccent ? 1 : 0;
    set
    {
      if (value is not (0 or 1) || (value == 1) == this.settings.CustomAccent)
        return;

      this.settings.CustomAccent = value == 1;

      // Start from the color already in use, so nothing changes until a color is picked.
      this.settings.AccentColor ??= Accents.ToHex (new UISettings ().GetColorValue (UIColorType.Accent));

      this.OnAccentChanged ();
    }
  }

  public bool IsCustomAccent => this.settings.CustomAccent;

  /// <summary>
  /// The picked accent color while it's in use; null while the app follows Windows.
  /// </summary>
  public Color? AccentColor => this.settings.CustomAccent && Accents.TryParse (this.settings.AccentColor, out var color) ? color : null;

  /// <summary>
  /// Accent colors picked lately, newest first.
  /// </summary>
  public ObservableCollection<Color> RecentAccents { get; }

  public void UseAccent (Color color)
  {
    color.A = 255;

    this.RecentAccents.Remove (color);
    this.RecentAccents.Insert (0, color);

    while (this.RecentAccents.Count > AppSettings.MaxRecents)
      this.RecentAccents.RemoveAt (this.RecentAccents.Count - 1);

    this.settings.RecentAccentColors = [.. this.RecentAccents.Select (Accents.ToHex)];
    this.settings.CustomAccent = true;
    this.settings.AccentColor = Accents.ToHex (color);
    this.OnAccentChanged ();
  }

  /// <summary>
  /// Whether Windows opens the app, in the tray, when you sign in. Windows keeps this, not the settings file.
  /// </summary>
  public bool StartWithWindows
  {
    get => Autostart.IsOn;
    set
    {
      if (value != Autostart.IsOn)
        Autostart.Set (value);

      // Even if Windows refused, so the switch goes back to how things are.
      this.OnChanged ();
    }
  }

  /// <summary>
  /// The order of <see cref="WinUI.CloseAction"/>, which is the order of the choices on the Settings page.
  /// </summary>
  public int CloseChoice
  {
    get => (int)this.settings.CloseAction;
    set
    {
      var action = (CloseAction)value;

      if (!Enum.IsDefined (action) || action == this.settings.CloseAction)
        return;

      this.settings.CloseAction = action;
      this.OnChanged ();
      this.SaveSoon ();
    }
  }

  public CloseAction CloseAction => this.settings.CloseAction;

  /// <summary>
  /// The order of <see cref="WinUI.TrayIcon"/>, which is the order of the choices on the Settings page.
  /// </summary>
  public int TrayIconChoice
  {
    get => (int)this.settings.TrayIcon;
    set
    {
      var icon = (TrayIcon)value;

      if (!Enum.IsDefined (icon) || icon == this.settings.TrayIcon)
        return;

      this.settings.TrayIcon = icon;
      this.OnChanged ();
      this.OnChanged (nameof (this.TrayIcon));
      this.SaveSoon ();
    }
  }

  public TrayIcon TrayIcon => this.settings.TrayIcon;

  /// <summary>
  /// The order of <see cref="WinUI.CursorLook"/>, which is the order of the choices on the Settings page.
  /// </summary>
  public int CursorLookChoice
  {
    get => (int)this.settings.CursorLook;
    set
    {
      var look = (CursorLook)value;

      if (!Enum.IsDefined (look) || look == this.settings.CursorLook)
        return;

      this.settings.CursorLook = look;
      this.OnChanged ();
      this.OnChanged (nameof (this.CursorLook));
      this.SaveSoon ();
    }
  }

  public CursorLook CursorLook => this.settings.CursorLook;

  /// <summary>
  /// The order of <see cref="WinUI.CursorMotion"/>, which is the order of the choices on the Settings page.
  /// </summary>
  public int CursorMotionChoice
  {
    get => (int)this.settings.CursorMotion;
    set
    {
      var motion = (CursorMotion)value;

      if (!Enum.IsDefined (motion) || motion == this.settings.CursorMotion)
        return;

      this.settings.CursorMotion = motion;
      this.OnChanged ();
      this.OnChanged (nameof (this.CursorMotion));
      this.SaveSoon ();
    }
  }

  public CursorMotion CursorMotion => this.settings.CursorMotion;

  #endregion App settings

  #region Time limit

  /// <summary>
  /// 0 for no limit, 1 to 3 for 30 min, 1 h and 2 h, 4 for <see cref="CustomLimit"/>.
  /// </summary>
  public int LimitChoice => this.settings.TimeLimitChoice;

  public TimeLimit? CustomLimit => this.settings.CustomTimeLimit;

  /// <summary>
  /// Custom limits used lately, newest first.
  /// </summary>
  public ObservableCollection<TimeLimit> Recents { get; }

  private TimeLimit? CurrentLimit => this.LimitChoice switch
  {
    0 => null,
    4 => this.CustomLimit,
    var choice => TimeLimit.After (PresetMinutes[choice])
  };

  public void ChooseLimit (int choice)
  {
    if (choice is < 0 or > 4 || (choice == 4 && this.CustomLimit is null))
      return;

    this.settings.TimeLimitChoice = choice;

    if (this.isOn)
      this.ArmStop (DateTime.Now);

    this.OnChanged (nameof (this.LimitChoice));
    this.SaveSoon ();
    this.Refresh ();
  }

  /// <summary>
  /// Picks a limit someone typed or chose from the recent ones. A preset length selects that preset.
  /// </summary>
  public void UseLimit (TimeLimit limit)
  {
    var preset = limit.Kind == TimeLimitKind.Duration ? Array.IndexOf (PresetMinutes, limit.Minutes, 1) : -1;

    if (preset > 0)
    {
      this.ChooseLimit (preset);

      return;
    }

    this.Recents.Remove (limit);
    this.Recents.Insert (0, limit);

    while (this.Recents.Count > AppSettings.MaxRecents)
      this.Recents.RemoveAt (this.Recents.Count - 1);

    this.settings.RecentTimeLimits = [.. this.Recents];
    this.settings.CustomTimeLimit = limit;
    this.OnChanged (nameof (this.CustomLimit));
    this.ChooseLimit (4);
  }

  public void RemoveRecent (TimeLimit limit)
  {
    this.Recents.Remove (limit);
    this.settings.RecentTimeLimits = [.. this.Recents];
    this.SaveSoon ();
  }

  public void ClearRecents ()
  {
    this.Recents.Clear ();
    this.settings.RecentTimeLimits = [];
    this.SaveSoon ();
  }

  #endregion Time limit

  #region Status texts

  public string StatusTitle => this.statusTitle;

  /// <summary>
  /// The line under Off/Awake: the cadence while off, the end time while on.
  /// </summary>
  public string StatusLine => this.statusLine;

  public string StatusTip => this.statusTip;

  public string PowerTip => this.powerTip;

  /// <summary>
  /// Which of the three panes beside the hourglass is showing.
  /// </summary>
  public int PaneIndex => this.paneIndex;

  public bool PanePinned => this.panePinned;

  public string Pane0Caption => this.pane0Caption;

  public string Pane0Value => this.pane0Value;

  public string Pane1Caption => this.pane1Caption;

  public string Pane1Value => this.pane1Value;

  public string Pane2Caption => this.pane2Caption;

  public string Pane2Value => this.pane2Value;

  public string TimerName => this.timerName;

  public string TimerTip => this.timerTip;

  public string TrayTip => this.trayTip;

  /// <summary>
  /// Shows the next pane now. Unless pinned, the panes then carry on changing every 5 seconds from here.
  /// </summary>
  public void NextPane ()
  {
    if (!this.isOn)
      return;

    this.pane = (this.pane + 1) % 3;

    if (!this.panePinned)
      this.RestartPaneTimer ();

    this.Refresh ();
  }

  /// <summary>
  /// Pins the pane showing now, so it stops changing by itself, or unpins it.
  /// </summary>
  public void TogglePin ()
  {
    if (!this.isOn)
      return;

    this.panePinned = !this.panePinned;

    if (this.panePinned)
      this.paneTimer.Stop ();
    else
      this.RestartPaneTimer ();

    this.OnChanged (nameof (this.PanePinned));
    this.Refresh ();
  }

  #endregion Status texts

  /// <summary>
  /// Writes any pending settings change now.
  /// </summary>
  public void SaveNow ()
  {
    this.saveTimer.Stop ();
    this.settings.Save ();
  }

  public void Dispose ()
  {
    this.clock.Stop ();
    this.paneTimer.Stop ();
    this.keeper.Dispose ();
    this.SaveNow ();
  }

  private static double PositionOf (int distance) =>
    Math.Log (distance) / Math.Log (AppSettings.MaxDistance) * DistanceSteps;

  private static int DistanceAt (double position) =>
    double.IsNaN (position) ? 0 : (int)Math.Round (Math.Pow (AppSettings.MaxDistance, position / DistanceSteps));

  private static int MinuteOfDay (TimeSpan time) => ((int)time.TotalMinutes % 1440 + 1440) % 1440;

  private void OnAccentChanged ()
  {
    this.OnChanged (nameof (this.AccentChoice));
    this.OnChanged (nameof (this.IsCustomAccent));
    this.OnChanged (nameof (this.AccentColor));
    this.SaveSoon ();
  }

  private void Keeper_Nudged (object? sender, EventArgs e) => this.Refresh ();

  private void Keeper_Expired (object? sender, EventArgs e)
  {
    if (this.isOn)
      this.Stopped ();
  }

  private void Stopped ()
  {
    this.isOn = false;
    this.stopsAt = null;
    this.keeper.Deadline = null;
    this.clock.Stop ();
    this.paneTimer.Stop ();
    this.OnChanged (nameof (this.IsOn));
    this.Refresh ();
  }

  private void ArmStop (DateTime now)
  {
    this.stopsAt = this.CurrentLimit?.EndsAt (now);
    this.keeper.Deadline = this.stopsAt is { } end ? new DateTimeOffset (end) : null;
  }

  private void ApplyPause ()
  {
    this.keeper.Suspended = this.locked && this.PauseWhenLocked;
    this.Refresh ();
  }

  private void AdvancePane ()
  {
    if (!this.isOn || this.panePinned)
      return;

    this.pane = (this.pane + 1) % 3;
    this.RestartPaneTimer ();
    this.Refresh ();
  }

  private void RestartPaneTimer ()
  {
    this.paneTimer.Stop ();
    this.paneTimer.Start ();
  }

  private void SaveSoon ()
  {
    this.saveTimer.Stop ();
    this.saveTimer.Start ();
  }

  private void Refresh ()
  {
    var now = DateTime.Now;
    var utcNow = DateTimeOffset.UtcNow;
    var cadence = (this.RandomDelay ? "Random, up to " : "Nudge every ") + Format.Every (this.Interval);
    (string Caption, string Value)[] panes;
    var next = string.Empty;

    if (this.isOn)
    {
      var awake = Format.Elapsed (Math.Max (0, (int)(utcNow - this.keeper.ActiveSince).TotalSeconds));

      next = this.keeper.Suspended
        ? "Paused"
        : Format.Countdown (Math.Max (0, (int)Math.Ceiling ((this.keeper.NextNudgeAt - utcNow).TotalSeconds)));

      var limit = this.stopsAt is { } end
        ? ("Time left", Format.Elapsed (Math.Max (0, (int)Math.Ceiling ((end - now).TotalSeconds))))
        : ("Time limit", "None");

      panes = [("Awake for", awake), ("Next nudge", next), limit];
    }
    else
    {
      panes = this.CurrentLimit switch
      {
        null => [("Time limit", "None")],
        { Kind: TimeLimitKind.Until } until => [("Stops at", Format.TimeOfDay (until.Minutes))],
        { } after => [("Time limit", Format.Span (after.Minutes, true))]
      };
    }

    var shown = this.isOn ? this.pane : 0;
    var count = this.keeper.NudgeCount;
    string line;

    if (!this.isOn)
    {
      line = cadence;
    }
    else if (this.stopsAt is { } end)
    {
      var ends = Format.Ends (end, now);
      line = char.IsDigit (ends[0]) ? $"Stops at {ends}" : $"Stops {ends}";
    }
    else
    {
      line = "No time limit";
    }

    this.Set (ref this.paneIndex, shown, nameof (this.PaneIndex));
    this.Set (ref this.pane0Caption, panes[0].Caption, nameof (this.Pane0Caption));
    this.Set (ref this.pane0Value, panes[0].Value, nameof (this.Pane0Value));
    this.Set (ref this.pane1Caption, panes.Length > 1 ? panes[1].Caption : string.Empty, nameof (this.Pane1Caption));
    this.Set (ref this.pane1Value, panes.Length > 1 ? panes[1].Value : string.Empty, nameof (this.Pane1Value));
    this.Set (ref this.pane2Caption, panes.Length > 2 ? panes[2].Caption : string.Empty, nameof (this.Pane2Caption));
    this.Set (ref this.pane2Value, panes.Length > 2 ? panes[2].Value : string.Empty, nameof (this.Pane2Value));
    this.Set (ref this.statusTitle, this.isOn ? "Awake" : "Off", nameof (this.StatusTitle));
    this.Set (ref this.statusLine, line, nameof (this.StatusLine));
    this.Set (ref this.statusTip, this.isOn ? (count == 1 ? "1 nudge so far" : $"{count} nudges so far") : "Click to start", nameof (this.StatusTip));
    this.Set (ref this.powerTip, this.isOn ? "Stop keeping the PC awake" : "Keep the PC awake by nudging the pointer", nameof (this.PowerTip));

    this.Set (ref this.timerName,
        this.isOn
          ? $"{panes[shown].Caption} {panes[shown].Value}{(this.panePinned ? ". Pinned" : string.Empty)}. Click for the next one"
          : $"{panes[0].Caption}: {panes[0].Value}",
        nameof (this.TimerName));

    this.Set (ref this.timerTip,
        !this.isOn
          ? "Shows the time limit now, and the awake time and next nudge while running"
          : this.panePinned
            ? "Pinned, so it stays on this one. Click for the next one, or unpin to let it change by itself"
            : "Shows awake time, next nudge and time left in turn, every 5 seconds. Click for the next one, or pin it to keep it",
        nameof (this.TimerTip));

    this.Set (ref this.trayTip, this.isOn ? $"BusyMouse: awake, next nudge in {next}" : "BusyMouse: off", nameof (this.TrayTip));

    // A nudge came due: the countdown has started over. Skipped nudges don't report themselves, so this is the
    // one place that sees them all. Short of due, the end moved because the interval changed.
    var nudgeAt = this.isOn && !this.keeper.Suspended ? this.keeper.NextNudgeAt : default;
    var restarted = nudgeAt > this.countdownEnd && this.countdownEnd != default && this.countdownEnd <= utcNow.AddSeconds (1);

    this.countdownEnd = nudgeAt;

    if (restarted)
      this.CountdownRestarted?.Invoke (this, EventArgs.Empty);
  }

  private void Set<T> (ref T field, T value, string name)
  {
    if (EqualityComparer<T>.Default.Equals (field, value))
      return;

    field = value;
    this.OnChanged (name);
  }

  private void OnChanged ([System.Runtime.CompilerServices.CallerMemberName] string? name = null) =>
    this.PropertyChanged?.Invoke (this, new PropertyChangedEventArgs (name));
}
