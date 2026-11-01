namespace BusyMouse;

/// <summary>
/// The repeating sequence of relative pointer moves for each nudge mode.
/// </summary>
internal static class NudgePattern
{
  private const int OrbitSteps = 12;

  /// <summary>
  /// Builds the moves for a mode, one per nudge. Over the whole sequence they add up to zero, so the
  /// pointer ends where it started.
  /// </summary>
  public static (int X, int Y)[] Build (NudgeMode mode, int distance)
  {
    // Saturate instead of overflowing for absurdly large distances.
    var step = (int)Math.Min (3L * distance, int.MaxValue);

    return mode switch
    {
      NudgeMode.Default => [(step, 0), (-step, 0)],
      NudgeMode.Diagonal => [(step, step), (-step, -step)],
      NudgeMode.Quiet => [(0, 0)],
      NudgeMode.Orbit => Orbit (step),
      _ => throw new ArgumentOutOfRangeException (nameof (mode), mode, "Not a known nudge mode."),
    };
  }

  private static (int X, int Y)[] Orbit (int radius)
  {
    var points = new (int X, int Y)[OrbitSteps];

    for (var i = 0; i < OrbitSteps; i++)
    {
      var angle = 2 * Math.PI * i / OrbitSteps;
      points[i] = (ToPixels (radius, Math.Cos (angle)), ToPixels (radius, Math.Sin (angle)));
    }

    // Each move is the difference between neighbouring rounded points, so the loop telescopes to exactly
    // zero. Rounding the moves themselves would let the rounding errors pile up instead.
    var moves = new (int X, int Y)[OrbitSteps];

    for (var i = 0; i < OrbitSteps; i++)
    {
      var from = points[i];
      var to = points[(i + 1) % OrbitSteps];
      moves[i] = (to.X - from.X, to.Y - from.Y);
    }

    return moves;
  }

  private static int ToPixels (int radius, double unit)
  {
    // Math.Sin (Math.PI / 6) and the like come out a hair off 0.5. Trimming that noise before rounding
    // keeps the circle symmetric.
    return (int)Math.Round (radius * Math.Round (unit, 12), MidpointRounding.AwayFromZero);
  }
}
