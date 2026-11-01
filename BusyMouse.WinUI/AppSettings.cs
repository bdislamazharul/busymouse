using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BusyMouse.WinUI;

/// <summary>
/// The choices that last between runs, kept as JSON under %LOCALAPPDATA%.
/// </summary>
public sealed class AppSettings
{
  public const int MinDistance = 1;
  public const int MaxDistance = 120;
  public const int MinInterval = 1;
  public const int MaxInterval = 10800;
  public const int MaxRecents = 5;

  private static readonly string FilePath = Path.Combine (
      Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData),
      "BusyMouse",
      "settings.json");

  public NudgeMode Mode { get; set; } = NudgeMode.Default;

  public int Distance { get; set; } = 1;

  public int Interval { get; set; } = 60;

  public bool RandomDelay { get; set; }

  public bool PauseWhenLocked { get; set; }

  public bool StartInTray { get; set; }

  public bool KeepAwakeOnStart { get; set; }

  /// <summary>
  /// 0 for no limit, 1 to 3 for 30 min, 1 h and 2 h, 4 for <see cref="CustomTimeLimit"/>.
  /// </summary>
  public int TimeLimitChoice { get; set; }

  public TimeLimit? CustomTimeLimit { get; set; }

  public List<TimeLimit> RecentTimeLimits { get; set; } = [];

  public AppTheme Theme { get; set; } = AppTheme.System;

  /// <summary>
  /// For <see cref="AppTheme.Scheduled"/>: when the light theme starts, in minutes after midnight.
  /// </summary>
  public int LightFrom { get; set; } = 7 * 60;

  /// <summary>
  /// For <see cref="AppTheme.Scheduled"/>: when the dark theme starts, in minutes after midnight.
  /// </summary>
  public int DarkFrom { get; set; } = 19 * 60;

  public bool Transparency { get; set; } = true;

  /// <summary>
  /// True to use <see cref="AccentColor"/> rather than the Windows accent color.
  /// </summary>
  public bool CustomAccent { get; set; }

  /// <summary>
  /// The chosen accent color, as #RRGGBB.
  /// </summary>
  public string? AccentColor { get; set; }

  public List<string> RecentAccentColors { get; set; } = [];

  public CloseAction CloseAction { get; set; } = CloseAction.Ask;

  public TrayIcon TrayIcon { get; set; } = TrayIcon.Rise;

  /// <summary>
  /// What moves in the mode previews.
  /// </summary>
  public CursorLook CursorLook { get; set; } = CursorLook.MouseArrow;

  /// <summary>
  /// How it moves there.
  /// </summary>
  public CursorMotion CursorMotion { get; set; } = CursorMotion.Pointer;

  public static AppSettings Load ()
  {
    AppSettings? settings = null;

    try
    {
      if (File.Exists (FilePath))
        settings = JsonSerializer.Deserialize (File.ReadAllText (FilePath), SettingsJson.Default.AppSettings);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
      Debug.WriteLine ($"Settings unreadable, using defaults: {ex.Message}");
    }

    settings ??= new AppSettings ();
    settings.Sanitize ();

    return settings;
  }

  public void Save ()
  {
    try
    {
      Directory.CreateDirectory (Path.GetDirectoryName (FilePath)!);

      var temp = FilePath + ".tmp";
      File.WriteAllText (temp, JsonSerializer.Serialize (this, SettingsJson.Default.AppSettings));
      File.Move (temp, FilePath, true);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      Debug.WriteLine ($"Settings not saved: {ex.Message}");
    }
  }

  private static bool IsValid (TimeLimit limit) => limit.Kind switch
  {
    TimeLimitKind.Duration => limit.Minutes is >= 1 and <= TimeLimit.MaxMinutes,
    TimeLimitKind.Until => limit.Minutes is >= 0 and < 1440,
    _ => false
  };

  private void Sanitize ()
  {
    if (!Enum.IsDefined (this.Mode))
      this.Mode = NudgeMode.Default;

    this.Distance = Math.Clamp (this.Distance, MinDistance, MaxDistance);
    this.Interval = Math.Clamp (this.Interval, MinInterval, MaxInterval);

    if (this.CustomTimeLimit is { } custom && !IsValid (custom))
      this.CustomTimeLimit = null;

    if (this.TimeLimitChoice is < 0 or > 4 || (this.TimeLimitChoice == 4 && this.CustomTimeLimit is null))
      this.TimeLimitChoice = 0;

    this.RecentTimeLimits = (this.RecentTimeLimits ?? []).Where (IsValid).Distinct ().Take (MaxRecents).ToList ();

    if (!Enum.IsDefined (this.Theme))
      this.Theme = AppTheme.System;

    if (!Enum.IsDefined (this.CloseAction))
      this.CloseAction = CloseAction.Ask;

    if (!Enum.IsDefined (this.TrayIcon))
      this.TrayIcon = TrayIcon.Rise;

    if (!Enum.IsDefined (this.CursorLook))
      this.CursorLook = CursorLook.MouseArrow;

    if (!Enum.IsDefined (this.CursorMotion))
      this.CursorMotion = CursorMotion.Pointer;

    this.LightFrom = Math.Clamp (this.LightFrom, 0, 1439);
    this.DarkFrom = Math.Clamp (this.DarkFrom, 0, 1439);

    if (!Accents.TryParse (this.AccentColor, out _))
    {
      this.AccentColor = null;
      this.CustomAccent = false;
    }

    this.RecentAccentColors = (this.RecentAccentColors ?? [])
      .Where (c => Accents.TryParse (c, out _))
      .Select (c => c.ToUpperInvariant ())
      .Distinct ()
      .Take (MaxRecents)
      .ToList ();
  }
}

public enum AppTheme
{
  Light,
  Dark,

  /// <summary>
  /// Light by day and dark at night, at the times in <see cref="AppSettings.LightFrom"/> and
  /// <see cref="AppSettings.DarkFrom"/>.
  /// </summary>
  Scheduled,

  /// <summary>
  /// Whatever Windows uses for apps.
  /// </summary>
  System
}

/// <summary>
/// What the window's Close button does.
/// </summary>
public enum CloseAction
{
  Ask,
  Tray,
  Exit
}

/// <summary>
/// The tray icon's look; also the glyph on the On/Off button. In A to Z order, as Settings lists them.
/// </summary>
public enum TrayIcon
{
  /// <summary>An empty cup when off; full and steaming when on.</summary>
  Coffee,

  /// <summary>The plain mouse when off; tinted, with rays, when on.</summary>
  Doze,

  /// <summary>The mouse with its line down inside when off; the line risen through the top when on.</summary>
  Rise
}

/// <summary>
/// What moves in the mode previews, in A to Z order.
/// </summary>
public enum CursorLook
{
  /// <summary>A dot.</summary>
  Bubble,

  /// <summary>A dot with a fading tail.</summary>
  Comet,

  /// <summary>A rounded pointer arrow.</summary>
  MouseArrow
}

/// <summary>
/// How the mode previews move, in A to Z order.
/// </summary>
public enum CursorMotion
{
  /// <summary>Glides, leaving fading copies behind.</summary>
  Comet,

  /// <summary>Glides along a dotted guide; Quiet clicks in place.</summary>
  Pointer,

  /// <summary>Glides, with rings spreading where it turns.</summary>
  Ripple,

  /// <summary>Jumps between the points the real nudge visits.</summary>
  TrueSteps
}

[JsonSourceGenerationOptions (WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable (typeof (AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
