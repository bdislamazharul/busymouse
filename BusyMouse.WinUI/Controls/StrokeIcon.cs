using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace BusyMouse.WinUI.Controls;

/// <summary>
/// A line-drawn icon on a 20 by 20 grid, stroked in the inherited foreground so it follows the control it
/// sits in (hover, selected, disabled). <see cref="Data"/> takes SVG-style path data.
/// </summary>
public sealed partial class StrokeIcon : UserControl
{
  public static readonly DependencyProperty DataProperty = DependencyProperty.Register (
      nameof (Data),
      typeof (string),
      typeof (StrokeIcon),
      new PropertyMetadata (null, (d, e) => ((StrokeIcon)d).path.Data = Parse (e.NewValue as string)));

  public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register (
      nameof (StrokeThickness),
      typeof (double),
      typeof (StrokeIcon),
      new PropertyMetadata (1.5, (d, e) => ((StrokeIcon)d).path.StrokeThickness = (double)e.NewValue));

  private readonly Path path = new ()
  {
    StrokeThickness = 1.5,
    StrokeLineJoin = PenLineJoin.Round,
    StrokeStartLineCap = PenLineCap.Round,
    StrokeEndLineCap = PenLineCap.Round
  };

  public StrokeIcon ()
  {
    this.IsTabStop = false;
    this.Width = this.Height = 16;

    var canvas = new Canvas { Width = 20, Height = 20 };
    canvas.Children.Add (this.path);
    this.Content = new Viewbox { Child = canvas };

    // A binding to the inherited Foreground misses theme changes, which left the icons black on a dark theme.
    // Re-read it whenever it might have changed instead; after a theme change, once the change has gone through.
    this.RegisterPropertyChangedCallback (ForegroundProperty, (_, _) => this.SyncStroke ());
    this.Loaded += (_, _) => this.SyncStroke ();
    this.ActualThemeChanged += (_, _) =>
    {
      this.SyncStroke ();
      this.DispatcherQueue.TryEnqueue (DispatcherQueuePriority.Low, this.SyncStroke);
    };
  }

  public string? Data
  {
    get => (string?)this.GetValue (DataProperty);
    set => this.SetValue (DataProperty, value);
  }

  public double StrokeThickness
  {
    get => (double)this.GetValue (StrokeThicknessProperty);
    set => this.SetValue (StrokeThicknessProperty, value);
  }

  private void SyncStroke () => this.path.Stroke = this.Foreground;

  internal static Geometry? Parse (string? data)
  {
    if (string.IsNullOrWhiteSpace (data))
      return null;

    // Let XAML's own path-markup parser read it, then lift the geometry out.
    var carrier = (Path)XamlReader.Load ($"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{data}'/>");
    var geometry = carrier.Data;
    carrier.Data = null;

    return geometry;
  }
}
