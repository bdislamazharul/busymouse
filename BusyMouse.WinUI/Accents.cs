using System.Globalization;
using Windows.UI;

namespace BusyMouse.WinUI;

/// <summary>
/// Accent colors: the Windows palette to choose from, and the lighter and darker shades WinUI draws with.
/// </summary>
internal static class Accents
{
  /// <summary>
  /// Sixteen of the accent colors Windows offers in Settings: one from each group of near-twins there, warm to
  /// cool and then the muted ones. Any other color is a click away under Custom color.
  /// </summary>
  public static readonly (string Hex, string Name)[] Palette =
  [
    ("#FFB900", "Yellow gold"), ("#F7630C", "Orange bright"), ("#CA5010", "Orange dark"), ("#E81123", "Red"),
    ("#EA005E", "Rose bright"), ("#E3008C", "Plum light"), ("#881798", "Violet red"), ("#744DA9", "Iris spring"),
    ("#6B69D6", "Purple shadow dark"), ("#0078D7", "Default blue"), ("#0099BC", "Cool blue bright"), ("#038387", "Seafoam teal"),
    ("#00B294", "Mint light"), ("#107C10", "Green"), ("#847545", "Camouflage desert"), ("#68768A", "Steel blue")
  ];

  public static bool TryParse (string? hex, out Color color)
  {
    color = default;

    if (hex is not { Length: 7 } || hex[0] != '#' || !uint.TryParse (hex.AsSpan (1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
      return false;

    color = Color.FromArgb (255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    return true;
  }

  public static Color Parse (string hex) => TryParse (hex, out var color) ? color : throw new FormatException ($"Not a #RRGGBB color: {hex}");

  public static string ToHex (Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

  /// <summary>
  /// The palette's name for a color, or its hex code.
  /// </summary>
  public static string NameOf (Color color)
  {
    var hex = ToHex (color);

    foreach (var (paletteHex, name) in Palette)
    {
      if (paletteHex == hex)
        return name;
    }

    return hex;
  }

  /// <summary>
  /// Black or white, whichever reads better on the color.
  /// </summary>
  public static Color Contrast (Color color) =>
    0.2126 * Linear (color.R) + 0.7152 * Linear (color.G) + 0.0722 * Linear (color.B) > 0.18 ? Color.FromArgb (255, 0, 0, 0) : Color.FromArgb (255, 255, 255, 255);

  /// <summary>
  /// The seven shades WinUI's accent brushes come from, darkest first: Dark3, Dark2, Dark1, the color itself,
  /// Light1, Light2 and Light3. Same hue and saturation, with lightness spaced as Windows spaces its own.
  /// </summary>
  public static Color[] Shades (Color accent)
  {
    var (h, s, l) = ToHsl (accent);
    double[] lightness = [l * 0.49, l * 0.68, l * 0.85, l, l + (1 - l) * 0.2, l + (1 - l) * 0.4, l + (1 - l) * 0.65];

    return [.. lightness.Select (shade => FromHsl (h, s, shade))];
  }

  private static double Linear (byte channel)
  {
    var c = channel / 255.0;

    return c <= 0.04045 ? c / 12.92 : Math.Pow ((c + 0.055) / 1.055, 2.4);
  }

  private static (double H, double S, double L) ToHsl (Color color)
  {
    double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
    var max = Math.Max (r, Math.Max (g, b));
    var min = Math.Min (r, Math.Min (g, b));
    var l = (max + min) / 2;

    if (max == min)
      return (0, 0, l);

    var d = max - min;
    var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
    var h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;

    return (h / 6, s, l);
  }

  private static Color FromHsl (double h, double s, double l)
  {
    if (s == 0)
      return Color.FromArgb (255, Channel (l), Channel (l), Channel (l));

    var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
    var p = 2 * l - q;

    return Color.FromArgb (255, Channel (Hue (p, q, h + 1 / 3.0)), Channel (Hue (p, q, h)), Channel (Hue (p, q, h - 1 / 3.0)));

    static double Hue (double p, double q, double t)
    {
      if (t < 0)
        t += 1;

      if (t > 1)
        t -= 1;

      return t < 1 / 6.0 ? p + (q - p) * 6 * t : t < 1 / 2.0 ? q : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6 : p;
    }

    static byte Channel (double value) => (byte)Math.Round (Math.Clamp (value, 0, 1) * 255);
  }
}
