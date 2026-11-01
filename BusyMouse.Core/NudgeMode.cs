namespace BusyMouse;

/// <summary>
/// How the keeper nudges the pointer. The app shows its mode tiles in this order.
/// </summary>
public enum NudgeMode
{
  /// <summary>
  /// To the right by a few pixels, then back.
  /// </summary>
  Default,

  /// <summary>
  /// Diagonally down and to the right by a few pixels, then back.
  /// </summary>
  Diagonal,

  /// <summary>
  /// A move of zero pixels. Windows counts it as input, but the pointer stays put.
  /// </summary>
  Quiet,

  /// <summary>
  /// Around a small circle, one step per nudge, ending where it started.
  /// </summary>
  Orbit
}
