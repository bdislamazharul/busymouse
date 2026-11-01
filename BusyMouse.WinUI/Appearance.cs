using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace BusyMouse.WinUI;

/// <summary>
/// Puts the theme, accent color and transparency settings on the app's windows, and switches between light and
/// dark at the set times for the time-of-day theme.
/// </summary>
internal sealed class Appearance
{
  // Darkest first, the order Accents.Shades gives them in.
  private static readonly string[] AccentKeys =
  [
    "SystemAccentColorDark3", "SystemAccentColorDark2", "SystemAccentColorDark1", "SystemAccentColor",
    "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3"
  ];

  private static readonly UIColorType[] SystemAccentTypes =
  [
    UIColorType.AccentDark3, UIColorType.AccentDark2, UIColorType.AccentDark1, UIColorType.Accent,
    UIColorType.AccentLight1, UIColorType.AccentLight2, UIColorType.AccentLight3
  ];

  // The brushes WinUI's theme dictionaries make in a shade of the accent; the controls' own keys, like
  // ToggleSwitchFillOn, point at these same brushes.
  private static readonly string[] AccentBrushKeys =
  [
    "AccentAAFillColorDefaultBrush", "AccentAAFillColorSecondaryBrush", "AccentAAFillColorTertiaryBrush",
    "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush",
    "AccentFillColorSelectedTextBackgroundBrush", "AccentTextFillColorPrimaryBrush", "AccentTextFillColorSecondaryBrush",
    "AccentTextFillColorTertiaryBrush", "ContentDialogBorderThemeBrush", "SystemControlBackgroundAccentBrush",
    "SystemControlDisabledAccentBrush", "SystemControlForegroundAccentBrush", "SystemControlHighlightAccentBrush",
    "SystemControlHighlightAltAccentBrush", "SystemControlHighlightAltListAccentHighBrush",
    "SystemControlHighlightAltListAccentLowBrush", "SystemControlHighlightAltListAccentMediumBrush",
    "SystemControlHighlightListAccentHighBrush", "SystemControlHighlightListAccentLowBrush",
    "SystemControlHighlightListAccentMediumBrush", "SystemControlHighlightListAccentMediumLowBrush",
    "SystemControlHighlightListAccentVeryHighBrush", "SystemControlHyperlinkTextBrush",
    "SystemControlRevealFocusVisualBrush", "SystemFillColorAttentionBrush"
  ];

  // WinUI keys its dark theme "Default".
  private static readonly string[] ThemeKeys = ["Default", "Dark", "Light"];

  private readonly KeeperViewModel model;
  private readonly DispatcherQueueTimer clock;
  private readonly UISettings system = new ();
  private readonly List<Action> windows = [];
  private ElementTheme theme;

  // The shades the accent brushes show now; they start out as Windows made them.
  private Color[] shown;

  public Appearance (KeeperViewModel model, DispatcherQueue queue)
  {
    this.model = model;
    this.model.PropertyChanged += this.Model_PropertyChanged;

    this.clock = queue.CreateTimer ();
    this.clock.Interval = TimeSpan.FromSeconds (30);
    this.clock.Tick += (_, _) => this.ApplyTheme ();

    this.shown = this.SystemShades ();
    this.system.ColorValuesChanged += (_, _) => queue.TryEnqueue (() =>
    {
      if (this.model.AccentColor is null)
        this.SetAccent ();
    });

    this.SetAccent ();
    this.theme = this.Pick ();
    this.SyncClock ();
  }

  /// <summary>
  /// Takes on a window: its root gets the theme, and the window shows its backdrop or, with transparency off,
  /// the solid fill behind its content.
  /// </summary>
  public void Attach (Window window, FrameworkElement root, UIElement solid, Func<SystemBackdrop> backdrop)
  {
    void Apply ()
    {
      root.RequestedTheme = this.theme;

      var clear = this.model.Transparency;

      if (clear != window.SystemBackdrop is not null)
        window.SystemBackdrop = clear ? backdrop () : null;

      solid.Visibility = clear ? Visibility.Collapsed : Visibility.Visible;
    }

    this.windows.Add (Apply);
    Apply ();
  }

  private static bool IsDay (int lightFrom, int darkFrom, DateTime now)
  {
    var minute = now.Hour * 60 + now.Minute;

    return lightFrom <= darkFrom ? minute >= lightFrom && minute < darkFrom : minute >= lightFrom || minute < darkFrom;
  }

  private void Model_PropertyChanged (object? sender, PropertyChangedEventArgs e)
  {
    switch (e.PropertyName)
    {
      case nameof (KeeperViewModel.ThemeChoice) or nameof (KeeperViewModel.LightFrom) or nameof (KeeperViewModel.DarkFrom):
        this.SyncClock ();
        this.ApplyTheme ();
        break;

      case nameof (KeeperViewModel.Transparency):
        this.ApplyAll ();
        break;

      case nameof (KeeperViewModel.AccentColor):
        this.SetAccent ();
        break;
    }
  }

  private ElementTheme Pick () => this.model.Theme switch
  {
    AppTheme.Light => ElementTheme.Light,
    AppTheme.Dark => ElementTheme.Dark,
    AppTheme.Scheduled => IsDay ((int)this.model.LightFrom.TotalMinutes, (int)this.model.DarkFrom.TotalMinutes, DateTime.Now)
      ? ElementTheme.Light
      : ElementTheme.Dark,
    _ => ElementTheme.Default
  };

  private void ApplyTheme ()
  {
    var pick = this.Pick ();

    if (pick == this.theme)
      return;

    this.theme = pick;
    this.ApplyAll ();
  }

  private void ApplyAll ()
  {
    foreach (var apply in this.windows)
      apply ();
  }

  private void SyncClock ()
  {
    if (this.model.Theme == AppTheme.Scheduled)
      this.clock.Start ();
    else
      this.clock.Stop ();
  }

  /// <summary>
  /// Puts the chosen accent color, or the Windows one, on WinUI's accent brushes.
  /// </summary>
  private void SetAccent ()
  {
    var shades = this.model.AccentColor is { } accent ? Accents.Shades (accent) : this.SystemShades ();
    var resources = Application.Current.Resources;

    // For anything looked up from now on.
    for (var i = 0; i < AccentKeys.Length; i++)
      resources[AccentKeys[i]] = shades[i];

    // The brushes already made took their color from the accent when they were made, and changing the colors
    // above doesn't reach them. Recolor them where they are: every brush in a shade of the old accent gets the
    // same shade of the new one, so the light theme's darker shades and the dark theme's lighter ones both follow.
    if (!shades.SequenceEqual (this.shown))
      Recolor (resources, this.shown, shades);

    this.shown = shades;
  }

  /// <summary>
  /// Looks the accent brushes up by name. Going through the dictionaries' contents instead would make every
  /// style in them there and then, with the theme of the moment baked in, and the app could no longer change theme.
  /// </summary>
  private static void Recolor (ResourceDictionary resources, Color[] from, Color[] to)
  {
    foreach (var merged in resources.MergedDictionaries)
    {
      foreach (var themeKey in ThemeKeys)
      {
        if (!merged.ThemeDictionaries.TryGetValue (themeKey, out var found) || found is not ResourceDictionary theme)
          continue;

        foreach (var key in AccentBrushKeys)
        {
          if (theme.TryGetValue (key, out var value) && value is SolidColorBrush brush && Array.IndexOf (from, brush.Color) is var i and >= 0)
            brush.Color = to[i];
        }
      }
    }
  }

  private Color[] SystemShades () => [.. SystemAccentTypes.Select (this.system.GetColorValue)];
}
