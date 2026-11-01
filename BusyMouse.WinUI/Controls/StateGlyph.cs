using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace BusyMouse.WinUI.Controls;

/// <summary>
/// The tray icon's drawing, off or on, in the inherited foreground: the same shapes tools/make-icons.py draws,
/// on the tray's 16 by 16 grid. The off and on drawings are separate layers, so a transition can swap them.
/// </summary>
public sealed partial class StateGlyph : UserControl
{
  public static readonly DependencyProperty LookProperty = DependencyProperty.Register (
      nameof (Look),
      typeof (TrayIcon),
      typeof (StateGlyph),
      new PropertyMetadata (TrayIcon.Rise, (d, _) => ((StateGlyph)d).Build ()));

  public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register (
      nameof (IsOn),
      typeof (bool),
      typeof (StateGlyph),
      new PropertyMetadata (false, (d, _) => ((StateGlyph)d).ShowState ()));

  public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register (
      nameof (StrokeThickness),
      typeof (double),
      typeof (StateGlyph),
      new PropertyMetadata (1.0, (d, _) => ((StateGlyph)d).Build ()));

  public static readonly DependencyProperty CordedProperty = DependencyProperty.Register (
      nameof (Corded),
      typeof (bool),
      typeof (StateGlyph),
      new PropertyMetadata (false, (d, _) => ((StateGlyph)d).Build ()));

  // The mouse, x3.5 y3.5 w8 h11 rx4, open 40 degrees either side of the top; its inside; and the same moved
  // 2 left and 1 down, for Doze.
  private const string Body = "M10.07,4.44 A4,4 0 0 1 11.5,7.5 V10.5 A4,4 0 0 1 3.5,10.5 V7.5 A4,4 0 0 1 4.93,4.44";
  private const string Inside = "M7.5,3.5 A4,4 0 0 1 11.5,7.5 V10.5 A4,4 0 0 1 3.5,10.5 V7.5 A4,4 0 0 1 7.5,3.5 Z";
  private const string LowBody = "M8.07,5.44 A4,4 0 0 1 9.5,8.5 V11.5 A4,4 0 0 1 1.5,11.5 V8.5 A4,4 0 0 1 2.93,5.44";
  private const string LowInside = "M5.5,4.5 A4,4 0 0 1 9.5,8.5 V11.5 A4,4 0 0 1 1.5,11.5 V8.5 A4,4 0 0 1 5.5,4.5 Z";

  private const string Cup = "M1.5,6.5 H11.5 V11 A3.5,3.5 0 0 1 8,14.5 H5 A3.5,3.5 0 0 1 1.5,11 Z M11.5,7.5 H12.5 A2,2 0 0 1 12.5,11.5 H11.5";
  private const string Coffee = "M1.5,9 H11.5 V11 A3.5,3.5 0 0 1 8,14.5 H5 A3.5,3.5 0 0 1 1.5,11 Z";
  private const string Steam = "M5,0.5 C3.8,1.8 6.2,2.7 5,4.5 M8,0.5 C6.8,1.8 9.2,2.7 8,4.5";

  // The plugged-in mouse is Rise's, closed at the top, with its wheel; the cable comes in from the right in a
  // gentle wave and arches down into the top, drawn from its far end so a transition can grow it toward the
  // mouse. It's 9.41 long; one dash of 10.2 shows it all, and the gap after is long enough to hide it all.
  private const string Wheel = "M7.5,5.5 V8";
  private const string CableLine = "M14.6,1.1 C14.1,1.1 13.8,1.35 13.3,1.7 C12.3,2.4 11.3,1.9 10.1,1.3 C8.9,0.7 7.5,1.1 7.5,2.4 V3.5";
  private const double CableDash = 10.2;

  private const double Tint = 0.3;

  private readonly Canvas off = new () { Width = 16, Height = 16 };
  private readonly Canvas on = new () { Width = 16, Height = 16 };
  private readonly Canvas cable = new () { Width = 16, Height = 16 };
  private readonly List<Path> strokes = [];
  private readonly List<Path> fills = [];

  public StateGlyph ()
  {
    this.IsTabStop = false;

    var stage = new Grid { Width = 16, Height = 16 };
    stage.Children.Add (this.off);
    stage.Children.Add (this.on);
    stage.Children.Add (this.cable);
    this.Content = new Viewbox { Child = stage };

    // As in StrokeIcon: re-read the foreground rather than bind to it, which misses theme changes.
    this.RegisterPropertyChangedCallback (ForegroundProperty, (_, _) => this.SyncBrush ());
    this.Loaded += (_, _) => this.SyncBrush ();
    this.ActualThemeChanged += (_, _) =>
    {
      this.SyncBrush ();
      this.DispatcherQueue.TryEnqueue (DispatcherQueuePriority.Low, this.SyncBrush);
    };

    this.Build ();
  }

  public TrayIcon Look
  {
    get => (TrayIcon)this.GetValue (LookProperty);
    set => this.SetValue (LookProperty, value);
  }

  public bool IsOn
  {
    get => (bool)this.GetValue (IsOnProperty);
    set => this.SetValue (IsOnProperty, value);
  }

  /// <summary>
  /// In units of the 16 grid; the tray draws with 1.
  /// </summary>
  public double StrokeThickness
  {
    get => (double)this.GetValue (StrokeThicknessProperty);
    set => this.SetValue (StrokeThicknessProperty, value);
  }

  /// <summary>
  /// Draws on as the corded mouse, plugged in, for Doze and Rise; Coffee keeps its cup. The On/Off button uses
  /// it, the tray doesn't.
  /// </summary>
  public bool Corded
  {
    get => (bool)this.GetValue (CordedProperty);
    set => this.SetValue (CordedProperty, value);
  }

  public UIElement OffLayer => this.off;

  public UIElement OnLayer => this.on;

  /// <summary>
  /// The cable, shown with on, on a layer of its own. Null unless the drawing is corded.
  /// </summary>
  public Path? Cable { get; private set; }

  public UIElement CableLayer => this.cable;

  /// <summary>
  /// The cable's StrokeDashOffset with none of it showing; 0 shows all of it, from its far end in.
  /// </summary>
  public double CableHidden => (CableDash + 0.4) / this.StrokeThickness;

  private void Build ()
  {
    this.off.Children.Clear ();
    this.on.Children.Clear ();
    this.cable.Children.Clear ();
    this.strokes.Clear ();
    this.fills.Clear ();
    this.Cable = null;

    var corded = this.Corded && this.Look != TrayIcon.Coffee;

    switch (this.Look)
    {
      case TrayIcon.Coffee:
        this.Draw (this.off, 0, [Cup]);
        this.Draw (this.on, 0, [Cup, Steam], Coffee);
        break;

      case TrayIcon.Doze:
        this.Draw (this.off, 0.5, [Body, "M7.5,1.5 V8"]);

        if (!corded)
          this.Draw (this.on, 0, [LowBody, "M5.5,2.5 V9", "M12.5,0.5 V2.5 M15,2 L13.6,3.4 M15.5,5.5 H13.5"], LowInside);

        break;

      default:
        this.Draw (this.off, 0.5, [Body, "M7.5,5.5 V8"]);

        if (!corded)
          this.Draw (this.on, 0.5, [Body, "M7.5,1.5 V8"], Inside);

        break;
    }

    if (corded)
    {
      this.Draw (this.on, 0.5, [Inside, Wheel]);
      this.Draw (this.cable, 0.5, [CableLine]);

      var dash = CableDash / this.StrokeThickness;
      this.Cable = this.strokes[^1];
      this.Cable.StrokeDashArray = new DoubleCollection { dash, 3 * dash };
      this.Cable.StrokeDashCap = PenLineCap.Round;
    }

    this.SyncBrush ();
    this.ShowState ();
  }

  /// <summary>
  /// Puts a drawing on a layer, moved right by <paramref name="shift"/> when it's the mouse alone, which sits
  /// half a unit left of the middle on the tray grid.
  /// </summary>
  private void Draw (Canvas layer, double shift, string[] lines, string? inside = null)
  {
    layer.RenderTransform = new TranslateTransform { X = shift };

    if (inside is not null)
    {
      var fill = new Path { Data = StrokeIcon.Parse (inside), Opacity = Tint };
      this.fills.Add (fill);
      layer.Children.Add (fill);
    }

    foreach (var data in lines)
    {
      var line = new Path
      {
        Data = StrokeIcon.Parse (data),
        StrokeThickness = this.StrokeThickness,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round
      };

      this.strokes.Add (line);
      layer.Children.Add (line);
    }
  }

  private void ShowState ()
  {
    this.off.Opacity = this.IsOn ? 0 : 1;
    this.on.Opacity = this.IsOn ? 1 : 0;
    this.cable.Opacity = this.IsOn ? 1 : 0;
  }

  private void SyncBrush ()
  {
    foreach (var path in this.strokes)
      path.Stroke = this.Foreground;

    foreach (var path in this.fills)
      path.Fill = this.Foreground;
  }
}
