using System.Globalization;

namespace BusyMouse.WinUI;

/// <summary>
/// The short time and length texts the UI shows.
/// </summary>
internal static class Format
{
  /// <summary>"4:05": minutes and seconds.</summary>
  public static string Clock (int seconds) => $"{seconds / 60}:{seconds % 60:00}";

  /// <summary>"1:04:05": hours, minutes and seconds.</summary>
  public static string Hms (int seconds) => $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}";

  /// <summary>A countdown: "42 s", "4:05" or "1:04:05".</summary>
  public static string Countdown (int seconds) =>
    seconds < 60 ? $"{seconds} s" : seconds < 3600 ? Clock (seconds) : Hms (seconds);

  /// <summary>Time running or left: "4:05", "1:04:05" or "2d 3h".</summary>
  public static string Elapsed (int seconds) =>
    seconds >= 86400 ? $"{seconds / 86400}d {seconds % 86400 / 3600}h" : seconds >= 3600 ? Hms (seconds) : Clock (seconds);

  /// <summary>An interval: "30 s", "5 min", "2 h", or "1:30" when it isn't whole minutes.</summary>
  public static string Every (int seconds)
  {
    if (seconds < 60)
      return $"{seconds} s";

    if (seconds % 3600 == 0)
      return $"{seconds / 3600} h";

    return seconds % 60 == 0 ? $"{seconds / 60} min" : Clock (seconds);
  }

  /// <summary>A length of time: "45 min", "2 h", "2 h 30 min", "3 d 4 h", or compact "2h 30m".</summary>
  public static string Span (int minutes, bool compact)
  {
    var days = minutes >= 2880 ? minutes / 1440 : 0;
    var hours = (minutes - days * 1440) / 60;
    var mins = minutes % 60;

    if (days == 0 && hours == 0)
      return $"{mins} min";

    if (days == 0 && mins == 0)
      return $"{hours} h";

    var parts = new List<string> (3);

    if (days > 0)
      parts.Add (compact ? $"{days}d" : $"{days} d");

    if (hours > 0)
      parts.Add (compact ? $"{hours}h" : $"{hours} h");

    if (mins > 0)
      parts.Add (compact ? $"{mins}m" : $"{mins} min");

    return string.Join (' ', parts);
  }

  public static string TimeOfDay (DateTime time) => time.ToString ("t", CultureInfo.CurrentCulture);

  public static string TimeOfDay (int minuteOfDay) => TimeOfDay (DateTime.Today.AddMinutes (minuteOfDay));

  /// <summary>"Until 6:00 PM" or "2 h 30 min".</summary>
  public static string Label (TimeLimit limit) =>
    limit.Kind == TimeLimitKind.Until ? $"Until {TimeOfDay (limit.Minutes)}" : Span (limit.Minutes, false);

  /// <summary>"6:00 PM" or "2h 30m", for the narrow custom segment.</summary>
  public static string ShortLabel (TimeLimit limit) =>
    limit.Kind == TimeLimitKind.Until ? TimeOfDay (limit.Minutes) : Span (limit.Minutes, true);

  /// <summary>When something ends: "6:42 PM", "tomorrow 6:42 PM", "Tue 6:42 PM" or "Nov 3 6:42 PM".</summary>
  public static string Ends (DateTime end, DateTime now)
  {
    var time = TimeOfDay (end);
    var days = (end.Date - now.Date).Days;

    return days switch
    {
      0 => time,
      1 => $"tomorrow {time}",
      < 7 => $"{end.ToString ("ddd", CultureInfo.CurrentCulture)} {time}",
      _ => $"{end.ToString ("MMM d", CultureInfo.CurrentCulture)} {time}"
    };
  }

  /// <summary>What a limit means from now: "ends 6:42 PM" for a length, "in 2 h 5 min" for an end time.</summary>
  public static string Detail (TimeLimit limit, DateTime now)
  {
    var end = limit.EndsAt (now);

    if (limit.Kind == TimeLimitKind.Duration)
      return $"ends {Ends (end, now)}";

    return $"in {Span (Math.Max (1, (int)Math.Round ((end - now).TotalMinutes)), false)}";
  }
}
