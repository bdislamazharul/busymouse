using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI.ViewManagement;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace BusyMouse.WinUI.Controls;

public enum PreviewTone
{
  /// <summary>Muted, as while the app is off.</summary>
  Idle,

  /// <summary>Accent cursor, as while the app is on.</summary>
  Active,

  /// <summary>Text-coloured, for an unselected tile.</summary>
  Tile,

  /// <summary>Accent-coloured, for the selected tile.</summary>
  TileSelected
}

public enum PreviewMotion
{
  /// <summary>Cursor parked in the middle.</summary>
  Still,

  /// <summary>Motion paused wherever it is; at its starting pose if it hasn't run yet.</summary>
  Resting,

  /// <summary>Moving.</summary>
  Running
}

/// <summary>
/// A cursor acting out a nudge mode's movement: <see cref="Look"/> is what moves and <see cref="Movement"/> how. Redrawn every frame while running, and not at all otherwise.
/// </summary>
public sealed partial class ModePreview : UserControl
{
  public static readonly DependencyProperty ModeProperty = DependencyProperty.Register (
      nameof (Mode),
      typeof (NudgeMode),
      typeof (ModePreview),
      new PropertyMetadata (NudgeMode.Default, (d, _) => ((ModePreview)d).Rebuild ()));

  public static readonly DependencyProperty ToneProperty = DependencyProperty.Register (
      nameof (Tone),
      typeof (PreviewTone),
      typeof (ModePreview),
      new PropertyMetadata (PreviewTone.Idle, (d, _) => ((ModePreview)d).ApplyTone ()));

  public static readonly DependencyProperty MotionProperty = DependencyProperty.Register (
      nameof (Motion),
      typeof (PreviewMotion),
      typeof (ModePreview),
      new PropertyMetadata (PreviewMotion.Still, (d, _) => ((ModePreview)d).ApplyMotion ()));

  public static readonly DependencyProperty CompactProperty = DependencyProperty.Register (
      nameof (Compact),
      typeof (bool),
      typeof (ModePreview),
      new PropertyMetadata (false, (d, _) => ((ModePreview)d).Rebuild ()));

  public static readonly DependencyProperty LookProperty = DependencyProperty.Register (
      nameof (Look),
      typeof (CursorLook),
      typeof (ModePreview),
      new PropertyMetadata (CursorLook.MouseArrow, (d, _) => ((ModePreview)d).Rebuild ()));

  public static readonly DependencyProperty MovementProperty = DependencyProperty.Register (
      nameof (Movement),
      typeof (CursorMotion),
      typeof (ModePreview),
      new PropertyMetadata (CursorMotion.Pointer, (d, _) => ((ModePreview)d).Rebuild ()));

  // The stage is 114 units square.
  private const double Mid = 57;
  private const double Tau = Math.PI * 2;

  // The preview's clock runs at four fifths of real time, for an easier pace; the times below are on its clock.
  private const double Pace = 0.8;

  // The pointer arrow, drawn with its tip up and left of the point it's at, so the arrow as a whole sits there.
  private static readonly Point[] Arrow = [new (-5, -8.5), new (-5, 7), new (-1, 3.3), new (1.6, 8.9), new (4.6, 7.6), new (2.1, 2.1), new (7.4, 2.1)];

  private static readonly UISettings Settings = new ();

  // Shapes drawn in the tone's colours: the guide's (in Backdrop) or the cursor's, as a fill or a stroke.
  private readonly List<(Shape Shape, bool Guide, bool Stroke)> painted = [];
  private Action<double>? frame;
  private Action? park;
  private double time;
  private long last;
  private bool ticking;

  public ModePreview ()
  {
    this.InitializeComponent ();

    this.Ink.RegisterPropertyChangedCallback (Border.BackgroundProperty, (_, _) => this.Paint ());
    this.GuideInk.RegisterPropertyChangedCallback (Border.BackgroundProperty, (_, _) => this.Paint ());
    this.GuideInk.RegisterPropertyChangedCallback (OpacityProperty, (_, _) => this.Paint ());

    this.Loaded += (_, _) =>
    {
      this.Rebuild ();
      this.ApplyTone ();
    };

    this.Unloaded += (_, _) => this.Tick (false);

    this.ActualThemeChanged += (_, _) =>
    {
      this.Paint ();
      this.DispatcherQueue.TryEnqueue (DispatcherQueuePriority.Low, this.Paint);
    };
  }

  public NudgeMode Mode
  {
    get => (NudgeMode)this.GetValue (ModeProperty);
    set => this.SetValue (ModeProperty, value);
  }

  public PreviewTone Tone
  {
    get => (PreviewTone)this.GetValue (ToneProperty);
    set => this.SetValue (ToneProperty, value);
  }

  public PreviewMotion Motion
  {
    get => (PreviewMotion)this.GetValue (MotionProperty);
    set => this.SetValue (MotionProperty, value);
  }

  /// <summary>
  /// Bigger cursor and heavier marks, for drawing small.
  /// </summary>
  public bool Compact
  {
    get => (bool)this.GetValue (CompactProperty);
    set => this.SetValue (CompactProperty, value);
  }

  /// <summary>
  /// What moves.
  /// </summary>
  public CursorLook Look
  {
    get => (CursorLook)this.GetValue (LookProperty);
    set => this.SetValue (LookProperty, value);
  }

  /// <summary>
  /// How it moves.
  /// </summary>
  public CursorMotion Movement
  {
    get => (CursorMotion)this.GetValue (MovementProperty);
    set => this.SetValue (MovementProperty, value);
  }

  /// <summary>
  /// Where a smoothly moving cursor is at a time, from the middle: side to side, corner to corner, round
  /// clockwise as the real nudge goes, or nowhere for Quiet.
  /// </summary>
  private static Point Glide (NudgeMode mode, double t)
  {
    switch (mode)
    {
      case NudgeMode.Diagonal:
        var d = -12 * Math.Cos (Tau * t / 1.6);
        return new Point (d, d);

      case NudgeMode.Orbit:
        var a = Tau * t / 2.4;
        return new Point (18 * Math.Cos (a), 18 * Math.Sin (a));

      case NudgeMode.Quiet:
        return new Point (0, 0);

      default:
        return new Point (-18 * Math.Cos (Tau * t / 1.6), 0);
    }
  }

  /// <summary>
  /// The points the real nudge visits, from the middle: there and back, or twelve clockwise round a circle.
  /// </summary>
  private static Point[] StepPoints (NudgeMode mode) => mode switch
  {
    NudgeMode.Default => [new (-12, 0), new (12, 0)],
    NudgeMode.Diagonal => [new (-9, -9), new (9, 9)],
    NudgeMode.Orbit => [.. Enumerable.Range (0, 12).Select (i => new Point (18 * Math.Cos (i * Math.PI / 6), 18 * Math.Sin (i * Math.PI / 6)))],
    _ => [new (0, 0)]
  };

  /// <summary>
  /// A cursor that holds still at each point, then jumps to the next in a tenth of a second.
  /// </summary>
  private static Func<double, Point> Steps (NudgeMode mode)
  {
    var points = StepPoints (mode);
    var hold = mode == NudgeMode.Orbit ? 0.22 : 0.8;

    return t =>
    {
      var step = (long)Math.Floor (t / hold);
      var into = t - step * hold;
      var to = points[Mod (step, points.Length)];
      var from = points[Mod (step - 1, points.Length)];
      var e = 1 - Math.Pow (1 - Math.Min (1, into / 0.1), 3);

      return new Point (from.X + (to.X - from.X) * e, from.Y + (to.Y - from.Y) * e);
    };
  }

  private static int Mod (long value, int n) => (int)(((value % n) + n) % n);

  private static Geometry ArrowGeometry ()
  {
    var segment = new PolyLineSegment ();

    foreach (var point in Arrow.Skip (1))
      segment.Points.Add (point);

    var figure = new PathFigure { StartPoint = Arrow[0], IsClosed = true, IsFilled = true };
    figure.Segments.Add (segment);

    var geometry = new PathGeometry ();
    geometry.Figures.Add (figure);

    return geometry;
  }

  /// <summary>
  /// Moves a fixed-size dot so it's centred on a point of the stage.
  /// </summary>
  private static void Put (FrameworkElement mark, double x, double y)
  {
    var move = (TranslateTransform)mark.RenderTransform;
    move.X = x - mark.Width / 2;
    move.Y = y - mark.Height / 2;
  }

  private static void ShowRing (Ellipse ring, double x, double y, double r, double opacity)
  {
    ring.Width = ring.Height = Math.Max (0, 2 * r);
    Put (ring, x, y);
    ring.Opacity = opacity;
  }

  private void ApplyTone () => VisualStateManager.GoToState (this, this.Tone.ToString (), false);

  private void Paint ()
  {
    var ink = this.Ink.Background;
    var guide = this.GuideInk.Background;

    foreach (var (shape, isGuide, isStroke) in this.painted)
    {
      if (isStroke)
        shape.Stroke = isGuide ? guide : ink;
      else
        shape.Fill = isGuide ? guide : ink;
    }

    this.Backdrop.Opacity = this.GuideInk.Opacity;
  }

  private Ellipse Dot (double r, bool guide = false)
  {
    var dot = new Ellipse { Width = 2 * r, Height = 2 * r, RenderTransform = new TranslateTransform () };
    this.painted.Add ((dot, guide, false));

    return dot;
  }

  private Ellipse Ring (double thickness)
  {
    var ring = new Ellipse { StrokeThickness = thickness, Opacity = 0, RenderTransform = new TranslateTransform () };
    this.painted.Add ((ring, false, true));

    return ring;
  }

  /// <summary>
  /// The mode's path as a line of dots.
  /// </summary>
  private Path Guide (NudgeMode mode, double thickness, double spacing)
  {
    Geometry geometry = mode switch
    {
      NudgeMode.Diagonal => new LineGeometry { StartPoint = new Point (45, 45), EndPoint = new Point (69, 69) },
      NudgeMode.Quiet => new EllipseGeometry { Center = new Point (Mid, Mid), RadiusX = 10, RadiusY = 10 },
      NudgeMode.Orbit => new EllipseGeometry { Center = new Point (Mid, Mid), RadiusX = 18, RadiusY = 18 },
      _ => new LineGeometry { StartPoint = new Point (39, Mid), EndPoint = new Point (75, Mid) }
    };

    // Dashes of no length with round ends are dots; dash lengths are in stroke widths.
    var path = new Path
    {
      Data = geometry,
      StrokeThickness = thickness,
      StrokeDashArray = new DoubleCollection { 0, spacing / thickness },
      StrokeDashCap = PenLineCap.Round,
      StrokeStartLineCap = PenLineCap.Round,
      StrokeEndLineCap = PenLineCap.Round,
      Opacity = 0.7
    };

    this.painted.Add ((path, true, true));

    return path;
  }

  /// <summary>
  /// The cursor, centred on 0,0. A comet's tail isn't part of it; that's drawn behind it as it goes.
  /// </summary>
  private Canvas MakeCursor (CursorLook look)
  {
    var cursor = new Canvas ();
    var white = new SolidColorBrush (Colors.White);

    switch (look)
    {
      case CursorLook.MouseArrow:
        // Rounded and chubby: the outline and the body are stroked with round joins, the outline wider, in white.
        cursor.Children.Add (new Path { Data = ArrowGeometry (), Fill = white, Stroke = white, StrokeThickness = 4.4, StrokeLineJoin = PenLineJoin.Round, Opacity = 0.95 });
        var body = new Path { Data = ArrowGeometry (), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
        this.painted.Add ((body, false, false));
        this.painted.Add ((body, false, true));
        cursor.Children.Add (body);
        break;

      case CursorLook.Bubble:
        var ball = new Ellipse { Width = 12, Height = 12 };
        Canvas.SetLeft (ball, -6);
        Canvas.SetTop (ball, -6);
        this.painted.Add ((ball, false, false));
        cursor.Children.Add (ball);

        var shine = new Ellipse { Width = 3.6, Height = 3.6, Fill = white, Opacity = 0.65 };
        Canvas.SetLeft (shine, -4.2);
        Canvas.SetTop (shine, -4.2);
        cursor.Children.Add (shine);
        break;

      default:
        var head = new Ellipse { Width = 11, Height = 11 };
        Canvas.SetLeft (head, -5.5);
        Canvas.SetTop (head, -5.5);
        this.painted.Add ((head, false, false));
        cursor.Children.Add (head);
        break;
    }

    return cursor;
  }

  private void Rebuild ()
  {
    this.Backdrop.Children.Clear ();
    this.Effects.Children.Clear ();
    this.Front.Children.Clear ();
    this.painted.Clear ();

    var mode = this.Mode;
    var look = this.Look;
    var quiet = mode == NudgeMode.Quiet;
    // Marks are heavier and the cursor bigger when drawing small.
    var k = this.Compact ? 1.5 : 1;
    var size = this.Compact ? 1.9 : 1.3;
    var at = this.Movement == CursorMotion.TrueSteps ? Steps (mode) : t => Glide (mode, t);

    // Where the cursor points: an arrow's tip, or a dot's middle. Rings start from there.
    var hot = look == CursorLook.MouseArrow ? new Point (Arrow[0].X * size, Arrow[0].Y * size) : new Point (0, 0);

    var parts = new List<Action<double>> ();
    var press = 1.0;

    switch (this.Movement)
    {
      case CursorMotion.Pointer:
        // Quiet's ring would only hide behind an arrow.
        if (!(quiet && look == CursorLook.MouseArrow))
          this.Backdrop.Children.Add (this.Guide (mode, 2.2 * k, 5 * k));

        if (quiet)
        {
          // A click every 1.6 s: the cursor dips and a ring spreads from its point.
          var click = this.Ring (1.8 * k);
          this.Effects.Children.Add (click);
          parts.Add (t =>
          {
            var a = t % 1.6;
            press = a < 0.12 ? 0.9 : 1;
            ShowRing (click, Mid + hot.X, Mid + hot.Y, a < 0.75 ? (2 + 14 * a / 0.75) * k : 0, a < 0.75 ? 0.75 * (1 - a / 0.75) : 0);
          });
        }

        break;

      case CursorMotion.Ripple:
        // A ring where the cursor turns, or at each beat for Orbit and Quiet; two can be spreading at once.
        var every = mode == NudgeMode.Orbit ? 0.6 : quiet ? 0.7 : 0.8;

        for (var j = 0; j < 2; j++)
        {
          var ring = this.Ring (1.6 * k);
          var behind = j * every;
          this.Effects.Children.Add (ring);
          parts.Add (t =>
          {
            var age = t % every + behind;

            if (age >= 1.2)
            {
              ring.Opacity = 0;
              return;
            }

            var p = at (t - age);
            ShowRing (ring, Mid + p.X + hot.X, Mid + p.Y + hot.Y, (5 + 18 * age / 1.2) * k, 0.6 * (1 - age / 1.2));
          });
        }

        break;

      case CursorMotion.TrueSteps:
        foreach (var point in StepPoints (mode))
        {
          var station = this.Dot (2.4 * k, guide: true);
          station.Opacity = 0.8;
          Put (station, Mid + point.X, Mid + point.Y);
          this.Backdrop.Children.Add (station);
        }

        if (quiet)
        {
          // A tick every 0.8 s: the cursor swells and a ring goes out.
          var tick = this.Ring (1.6 * k);
          this.Effects.Children.Add (tick);
          parts.Add (t =>
          {
            var p = Math.Max (0, 1 - t % 0.8 / 0.3);
            press = 1 + 0.3 * p;
            ShowRing (tick, Mid + hot.X, Mid + hot.Y, (6 + 10 * (1 - p)) * k, 0.6 * p);
          });
        }

        break;

      default:
        if (quiet)
        {
          // A little comet circles the parked cursor.
          for (var i = 7; i >= 0; i--)
          {
            var dot = this.Dot (2.6 * k * (1 - i / 9.0));
            var lag = i * 0.035;
            dot.Opacity = 1 - i / 8.0;
            this.Effects.Children.Add (dot);
            parts.Add (t =>
            {
              var a = Tau * (t - lag) / 1.1;
              Put (dot, Mid + 15 * k * Math.Cos (a), Mid + 15 * k * Math.Sin (a));
            });
          }
        }
        else
        {
          // Fading copies of the cursor along the way it came, oldest at the back.
          for (var j = 4; j >= 1; j--)
          {
            var ghost = this.MakeCursor (look);
            var ghostMove = new CompositeTransform { ScaleX = size * (1 - 0.07 * j), ScaleY = size * (1 - 0.07 * j) };
            var lag = j * 0.1;
            ghost.RenderTransform = ghostMove;
            ghost.Opacity = 0.45 * (1 - j / 5.0);
            this.Effects.Children.Add (ghost);
            parts.Add (t =>
            {
              var p = at (t - lag);
              ghostMove.TranslateX = Mid + p.X;
              ghostMove.TranslateY = Mid + p.Y;
            });
          }
        }

        break;
    }

    if (look == CursorLook.Comet)
    {
      // The comet's tail: smaller and fainter the further back.
      for (var i = 7; i >= 1; i--)
      {
        var dot = this.Dot (5.5 * size * (1 - i / 10.0));
        var lag = i * 0.035;
        dot.Opacity = 0.7 * Math.Pow (1 - i / 8.0, 1.3);
        this.Effects.Children.Add (dot);
        parts.Add (t =>
        {
          var p = at (t - lag);
          Put (dot, Mid + p.X, Mid + p.Y);
        });
      }
    }

    var cursor = this.MakeCursor (look);
    var move = new CompositeTransform ();
    cursor.RenderTransform = move;
    this.Front.Children.Add (cursor);

    void Place (Point p, double scale)
    {
      move.TranslateX = Mid + p.X;
      move.TranslateY = Mid + p.Y;
      move.ScaleX = move.ScaleY = size * scale;
    }

    this.frame = t =>
    {
      press = 1;

      foreach (var part in parts)
        part (t);

      Place (at (t), press);
    };

    this.park = () => Place (new Point (0, 0), 1);

    this.Paint ();
    this.ApplyMotion ();
  }

  private void ApplyMotion ()
  {
    if (this.frame is null || !this.IsLoaded)
      return;

    var wanted = this.Motion;

    if (wanted == PreviewMotion.Running && !Settings.AnimationsEnabled)
      wanted = PreviewMotion.Resting;

    switch (wanted)
    {
      case PreviewMotion.Still:
        this.Tick (false);
        this.time = 0;
        this.Effects.Visibility = Visibility.Collapsed;
        this.park?.Invoke ();
        break;

      case PreviewMotion.Resting:
        this.Tick (false);
        this.Effects.Visibility = Visibility.Visible;
        this.frame (this.time);
        break;

      default:
        this.Effects.Visibility = Visibility.Visible;
        this.frame (this.time);
        this.Tick (true);
        break;
    }
  }

  /// <summary>
  /// Draws a frame each time the screen does, while running.
  /// </summary>
  private void Tick (bool on)
  {
    if (on == this.ticking)
      return;

    this.ticking = on;

    if (on)
    {
      this.last = Stopwatch.GetTimestamp ();
      CompositionTarget.Rendering += this.CompositionTarget_Rendering;
    }
    else
    {
      CompositionTarget.Rendering -= this.CompositionTarget_Rendering;
    }
  }

  private void CompositionTarget_Rendering (object? sender, object e)
  {
    var now = Stopwatch.GetTimestamp ();
    this.time += Pace * Stopwatch.GetElapsedTime (this.last, now).TotalSeconds;
    this.last = now;
    this.frame?.Invoke (this.time);
  }
}
