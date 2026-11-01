using System.Globalization;
using System.Text.RegularExpressions;

namespace BusyMouse;

public enum TimeLimitKind
{
  /// <summary>Stop after <see cref="TimeLimit.Minutes"/> minutes.</summary>
  Duration,

  /// <summary>Stop at a time of day, <see cref="TimeLimit.Minutes"/> minutes after midnight.</summary>
  Until
}

public enum TimeLimitParseResult
{
  Empty,
  Invalid,
  TooLong,
  Valid
}

/// <summary>
/// When keeping awake should end by itself: after a length of time, or at the next given time of day.
/// </summary>
public readonly partial record struct TimeLimit (TimeLimitKind Kind, int Minutes)
{
  /// <summary>
  /// The longest duration allowed: 30 days.
  /// </summary>
  public const int MaxMinutes = 30 * 24 * 60;

  public static TimeLimit After (int minutes) => new (TimeLimitKind.Duration, minutes);

  public static TimeLimit At (int minuteOfDay) => new (TimeLimitKind.Until, minuteOfDay);

  /// <summary>
  /// Returns when a run started at <paramref name="start"/> (local time) ends.
  /// </summary>
  public DateTime EndsAt (DateTime start)
  {
    if (this.Kind == TimeLimitKind.Duration)
      return start.AddMinutes (this.Minutes);

    var end = start.Date.AddMinutes (this.Minutes);

    return end <= start ? end.AddDays (1) : end;
  }

  /// <summary>
  /// Reads what someone typed: a length such as "45m", "2h 30m", "1:15", "90" (minutes) or "3d", or an end time
  /// such as "until 6 pm", "at 18:30", "6pm", "noon" or "midnight".
  /// </summary>
  public static TimeLimitParseResult TryParse (string? text, out TimeLimit limit)
  {
    limit = default;

    var t = (text ?? string.Empty).Trim ().ToLowerInvariant ();
    t = Separators ().Replace (t, " ");
    t = Spaces ().Replace (t, " ").Trim ();

    if (t.Length == 0)
      return TimeLimitParseResult.Empty;

    var word = NamedTime ().Match (t);

    if (word.Success)
    {
      limit = At (word.Groups[1].Value == "noon" ? 720 : 0);

      return TimeLimitParseResult.Valid;
    }

    var at = TimeOfDay ().Match (t);

    if (at.Success && (at.Groups[1].Success || at.Groups[4].Success))
    {
      var h = int.Parse (at.Groups[2].Value, CultureInfo.InvariantCulture);
      var m = at.Groups[3].Success ? int.Parse (at.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
      var ampm = at.Groups[4].Success ? at.Groups[4].Value[0] : '\0';

      if (m > 59 || (ampm != '\0' ? h < 1 || h > 12 : h > 23))
        return TimeLimitParseResult.Invalid;

      if (ampm == 'p' && h < 12)
        h += 12;
      else if (ampm == 'a' && h == 12)
        h = 0;

      limit = At (h * 60 + m);

      return TimeLimitParseResult.Valid;
    }

    double minutes = 0;
    var clock = Clock ().Match (t);

    if (clock.Success)
    {
      var mm = int.Parse (clock.Groups[2].Value, CultureInfo.InvariantCulture);

      if (mm > 59)
        return TimeLimitParseResult.Invalid;

      minutes = int.Parse (clock.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + mm;
    }
    else if (Digits ().IsMatch (t))
    {
      if (!int.TryParse (t, NumberStyles.None, CultureInfo.InvariantCulture, out var whole))
        return TimeLimitParseResult.TooLong;

      minutes = whole;
    }
    else
    {
      if (Amount ().Replace (t, string.Empty).Trim ().Length != 0)
        return TimeLimitParseResult.Invalid;

      var last = '\0';

      foreach (Match part in Amount ().Matches (t))
      {
        var n = double.Parse (part.Groups[1].Value, CultureInfo.InvariantCulture);
        var unit = part.Groups[2].Value;

        if (DayUnit ().IsMatch (unit))
        {
          minutes += n * 1440;
          last = 'd';
        }
        else if (HourUnit ().IsMatch (unit))
        {
          minutes += n * 60;
          last = 'h';
        }
        else if (MinuteUnit ().IsMatch (unit) || (unit.Length == 0 && last == 'h'))
        {
          // A bare number straight after hours means minutes, as in "2h 30".
          minutes += n;
          last = 'm';
        }
        else
        {
          return TimeLimitParseResult.Invalid;
        }
      }
    }

    minutes = Math.Round (minutes, MidpointRounding.AwayFromZero);

    if (minutes < 1)
      return TimeLimitParseResult.Invalid;

    if (minutes > MaxMinutes)
      return TimeLimitParseResult.TooLong;

    limit = After ((int)minutes);

    return TimeLimitParseResult.Valid;
  }

  [GeneratedRegex (@",|\band\b")]
  private static partial Regex Separators ();

  [GeneratedRegex (@"\s+")]
  private static partial Regex Spaces ();

  [GeneratedRegex (@"^(?:(?:until|till|til|at|@) ?)?(midnight|noon)$")]
  private static partial Regex NamedTime ();

  [GeneratedRegex (@"^(until |till |til |at |@ ?|until|till|til|at)?([0-9]{1,2})(?::([0-9]{2}))? ?(am|pm|a|p)?$")]
  private static partial Regex TimeOfDay ();

  [GeneratedRegex (@"^([0-9]{1,3}):([0-9]{2})$")]
  private static partial Regex Clock ();

  [GeneratedRegex (@"^[0-9]+$")]
  private static partial Regex Digits ();

  [GeneratedRegex (@"([0-9]+(?:\.[0-9]+)?) ?([a-z]*)")]
  private static partial Regex Amount ();

  [GeneratedRegex (@"^d(ays?)?$")]
  private static partial Regex DayUnit ();

  [GeneratedRegex (@"^h(rs?|ours?)?$")]
  private static partial Regex HourUnit ();

  [GeneratedRegex (@"^m(ins?|inutes?)?$")]
  private static partial Regex MinuteUnit ();
}
