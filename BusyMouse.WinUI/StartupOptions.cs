using System.Globalization;

namespace BusyMouse.WinUI;

/// <summary>
/// The command line. Options left out are null, meaning "use the saved setting".
/// </summary>
internal sealed class StartupOptions
{
  public const string HelpText = """
    BusyMouse: keeps your PC awake by gently nudging the pointer.

    Usage: BusyMouse [options]

      -n, --nudge                   Keep awake as soon as the app starts
      -m, --minimized               Start in the tray, without the window
      -g, --settings                Open the window on start
      -o, --mode MODE               Default, Diagonal, Quiet or Orbit
      -s, --seconds N               Seconds between nudges, 1 to 10800
      -d, --distance N              How far each nudge moves, 1 to 120
      -r, --random                  Wait a random time, up to the seconds, between nudges
      -l, --respect-locked-state    Pause while the PC is locked
          --stop-nudging            Ask the running copy to stop keeping awake, then exit
      -h, --help                    Show this help

    Mode, seconds, distance, random and locked-state are remembered for next time.
    """;

  public bool? Nudge { get; private set; }

  public bool? Minimized { get; private set; }

  public bool? ShowWindow { get; private set; }

  public NudgeMode? Mode { get; private set; }

  public int? Seconds { get; private set; }

  public int? Distance { get; private set; }

  public bool? Random { get; private set; }

  public bool? PauseWhenLocked { get; private set; }

  public bool StopNudging { get; private set; }

  public bool Help { get; private set; }

  /// <summary>
  /// Windows started the app at sign-in through the Store package's startup task, which can't pass "-m" the way
  /// the Run key does: start in the tray unless the command line said otherwise.
  /// </summary>
  public void StartedAtSignIn () => this.Minimized ??= true;

  /// <summary>
  /// Reads the arguments. Accepts "-s 30", "--seconds 30" and "--seconds=30"; switches take an optional
  /// true or false, as in "-r false".
  /// </summary>
  public static StartupOptions Parse (IReadOnlyList<string> args, out string? error)
  {
    var options = new StartupOptions ();
    error = null;

    for (var i = 0; i < args.Count; i++)
    {
      var arg = args[i];
      string? inline = null;
      var equals = arg.StartsWith ("--", StringComparison.Ordinal) ? arg.IndexOf ('=') : -1;

      if (equals > 0)
      {
        inline = arg[(equals + 1)..];
        arg = arg[..equals];
      }

      switch (arg)
      {
        case "-n" or "--nudge":
          options.Nudge = Switch (args, ref i, inline, ref error);
          break;

        case "-m" or "--minimized":
          options.Minimized = Switch (args, ref i, inline, ref error);
          break;

        case "-g" or "--settings":
          options.ShowWindow = Switch (args, ref i, inline, ref error);
          break;

        case "-r" or "--random":
          options.Random = Switch (args, ref i, inline, ref error);
          break;

        case "-l" or "--respect-locked-state":
          options.PauseWhenLocked = Switch (args, ref i, inline, ref error);
          break;

        case "--stop-nudging":
          options.StopNudging = Switch (args, ref i, inline, ref error) ?? false;
          break;

        case "-h" or "-?" or "/?" or "--help":
          options.Help = true;
          break;

        case "-o" or "--mode":
          if (Value (args, ref i, inline, arg, ref error) is { } mode)
          {
            if (Enum.TryParse<NudgeMode> (mode, true, out var parsed) && Enum.IsDefined (parsed) && !int.TryParse (mode, out _))
              options.Mode = parsed;
            else
              error ??= $"'{mode}' isn't a mode. Use Default, Diagonal, Quiet or Orbit.";
          }

          break;

        case "-s" or "--seconds":
          options.Seconds = Number (args, ref i, inline, arg, AppSettings.MinInterval, AppSettings.MaxInterval, "Seconds", ref error);
          break;

        case "-d" or "--distance":
          options.Distance = Number (args, ref i, inline, arg, AppSettings.MinDistance, AppSettings.MaxDistance, "Distance", ref error);
          break;

        default:
          error ??= $"Unknown option '{args[i]}'. Try --help.";
          break;
      }

      if (error is not null)
        break;
    }

    return options;
  }

  /// <summary>
  /// Puts the remembered options into the saved settings.
  /// </summary>
  public void ApplyTo (AppSettings settings)
  {
    if (this.Mode is { } mode)
      settings.Mode = mode;

    if (this.Seconds is { } seconds)
      settings.Interval = seconds;

    if (this.Distance is { } distance)
      settings.Distance = distance;

    if (this.Random is { } random)
      settings.RandomDelay = random;

    if (this.PauseWhenLocked is { } respect)
      settings.PauseWhenLocked = respect;
  }

  private static bool? Switch (IReadOnlyList<string> args, ref int i, string? inline, ref string? error)
  {
    var text = inline;

    if (text is null && i + 1 < args.Count && args[i + 1] is var next
        && (next.Equals ("true", StringComparison.OrdinalIgnoreCase) || next.Equals ("false", StringComparison.OrdinalIgnoreCase)))
    {
      text = next;
      i++;
    }

    if (text is null)
      return true;

    if (bool.TryParse (text, out var value))
      return value;

    error ??= $"'{text}' should be true or false.";

    return null;
  }

  private static string? Value (IReadOnlyList<string> args, ref int i, string? inline, string name, ref string? error)
  {
    if (inline is not null)
      return inline;

    if (i + 1 < args.Count)
      return args[++i];

    error ??= $"{name} needs a value.";

    return null;
  }

  private static int? Number (IReadOnlyList<string> args, ref int i, string? inline, string name, int min, int max, string what, ref string? error)
  {
    if (Value (args, ref i, inline, name, ref error) is not { } text)
      return null;

    if (int.TryParse (text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max)
      return value;

    error ??= $"{what} must be a whole number from {min} to {max}, not '{text}'.";

    return null;
  }
}
