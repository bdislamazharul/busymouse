using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace BusyMouse.WinUI.Controls;

/// <summary>
/// The app icon as tools/make-icons.py draws it, the critter on a squircle shading from blue to violet, for
/// About. Drawn rather than loaded, so it's crisp at any size.
/// </summary>
public sealed partial class AppIcon : UserControl
{
  // The critter is drawn on its own 24 grid; its 19.8 units of width fill 68% of the tile, centred on (12, 11.85).
  private const double CritterScale = 24 * 0.68 / 19.8;

  public AppIcon ()
  {
    this.IsTabStop = false;

    var sweep = new LinearGradientBrush { StartPoint = new Point (0, 0), EndPoint = new Point (1, 1) };

    foreach (var (offset, hex) in new[] { (0.0, "#1E6BFF"), (0.5, "#4B3FF2"), (1.0, "#8B2BE8") })
      sweep.GradientStops.Add (new GradientStop { Offset = offset, Color = Accents.Parse (hex) });

    // Android's "squircle" icon mask, the one Samsung's One UI uses.
    var tile = new Path { Data = StrokeIcon.Parse ("M12,0 C2.4,0 0,2.4 0,12 0,21.6 2.4,24 12,24 21.6,24 24,21.6 24,12 24,2.4 21.6,0 12,0 Z"), Fill = sweep };

    var critter = new Canvas
    {
      RenderTransform = new CompositeTransform { ScaleX = CritterScale, ScaleY = CritterScale, TranslateX = 12 - 12 * CritterScale, TranslateY = 12 - 11.85 * CritterScale }
    };

    var white = Colors.White;
    var ink = Accents.Parse ("#2A1240");
    var ear = Accents.Parse ("#FFB3C8");

    critter.Children.Add (Oval (5.9, 7, 3.8, 3.8, white));
    critter.Children.Add (Oval (18.1, 7, 3.8, 3.8, white));
    critter.Children.Add (Oval (6.2, 7.3, 2.1, 2.1, ear));
    critter.Children.Add (Oval (17.8, 7.3, 2.1, 2.1, ear));
    critter.Children.Add (Oval (12, 14.2, 7.2, 6.3, white));
    critter.Children.Add (Oval (9.6, 12.9, 1.2, 1.2, ink));
    critter.Children.Add (Oval (14.4, 12.9, 1.2, 1.2, ink));
    critter.Children.Add (Oval (12, 16.4, 0.9, 0.9, ink));
    critter.Children.Add (Oval (9.95, 12.5, 0.38, 0.38, white));
    critter.Children.Add (Oval (14.75, 12.5, 0.38, 0.38, white));

    var face = new Canvas { RenderTransform = this.Nudge };
    face.Children.Add (critter);

    var stage = new Grid { Width = 24, Height = 24 };
    stage.Children.Add (tile);
    stage.Children.Add (face);
    this.Content = new Viewbox { Child = stage };
  }

  /// <summary>
  /// Moves the critter within its tile, for a nudge of its own.
  /// </summary>
  public TranslateTransform Nudge { get; } = new ();

  private static Ellipse Oval (double x, double y, double rx, double ry, Color color)
  {
    var oval = new Ellipse { Width = 2 * rx, Height = 2 * ry, Fill = new SolidColorBrush (color) };
    Canvas.SetLeft (oval, x - rx);
    Canvas.SetTop (oval, y - ry);

    return oval;
  }
}
