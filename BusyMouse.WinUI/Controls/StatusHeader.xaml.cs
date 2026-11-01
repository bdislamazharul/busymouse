using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI.ViewManagement;

namespace BusyMouse.WinUI.Controls;

/// <summary>
/// The top row of both the flyout and the window: the On/Off button, Off/Awake with one line under it, and the
/// hourglass with the awake time, next nudge and time left taking turns beside it.
/// </summary>
public sealed partial class StatusHeader : UserControl
{
  private static readonly UISettings Settings = new ();

  private bool hovering;
  private bool animating;
  private bool? shownOn;
  private Storyboard? hop;
  private XamlRoot? root;

  // The countdown before its latest change, which is the number it ended on when it starts over.
  private string countdownWas = string.Empty;
  private string countdownIs = string.Empty;

  public StatusHeader ()
  {
    this.InitializeComponent ();

    this.Loaded += this.StatusHeader_Loaded;
    this.Unloaded += this.StatusHeader_Unloaded;
  }

  public KeeperViewModel Model => App.Model;

  public double PaneOpacity (int shown, int pane) => shown == pane ? 1 : 0;

  public string PinName (bool pinned) => pinned ? "Unpin, let it change by itself again" : "Pin, keep showing this one";

  private void StatusHeader_Loaded (object sender, RoutedEventArgs e)
  {
    this.Model.PropertyChanged += this.Model_PropertyChanged;
    this.Model.CountdownRestarted += this.Model_CountdownRestarted;

    this.root = this.XamlRoot;
    this.root.Changed += this.XamlRoot_Changed;

    this.Apply ();
  }

  private void StatusHeader_Unloaded (object sender, RoutedEventArgs e)
  {
    this.Model.PropertyChanged -= this.Model_PropertyChanged;
    this.Model.CountdownRestarted -= this.Model_CountdownRestarted;

    if (this.root is not null)
      this.root.Changed -= this.XamlRoot_Changed;

    this.root = null;
    this.shownOn = null;
    this.StopMotion ();
  }

  private void Model_PropertyChanged (object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName is nameof (KeeperViewModel.Pane1Value) && this.Model.Pane1Value != this.countdownIs)
    {
      this.countdownWas = this.countdownIs;
      this.countdownIs = this.Model.Pane1Value;
    }

    if (e.PropertyName is null or "" or nameof (KeeperViewModel.IsOn) or nameof (KeeperViewModel.PanePinned))
      this.Apply ();
  }

  /// <summary>
  /// The countdown ran out and started over: the glyph gives its little shake, and the number it ended on
  /// dissolves into the new one.
  /// </summary>
  private void Model_CountdownRestarted (object? sender, EventArgs e)
  {
    if (!this.animating)
      return;

    this.NudgeMotion.Begin ();

    this.CountdownDissolve.Stop ();
    this.CountdownGhost.Text = this.countdownWas;
    this.CountdownDissolve.Begin ();
  }

  private void XamlRoot_Changed (XamlRoot sender, XamlRootChangedEventArgs args) => this.UpdateMotion ();

  private void Apply ()
  {
    var on = this.Model.IsOn;

    // Turning on or off while in view hops; showing up already on or off doesn't.
    if (this.shownOn is { } was && was != on && Settings.AnimationsEnabled && this.root is { IsHostVisible: true })
    {
      this.Hop (on);
    }
    else if (this.shownOn != on)
    {
      this.hop?.Stop ();
      this.Glyph.IsOn = on;
    }

    this.shownOn = on;
    this.PowerButton.IsChecked = on;
    this.PinButton.IsChecked = this.Model.PanePinned;
    VisualStateManager.GoToState (this, on ? "On" : "Off", false);

    this.UpdatePin ();
    this.UpdateMotion ();
  }

  /// <summary>
  /// The glyph hops up as it turns on, changing at the top of the hop, and lands with a squash as it turns off,
  /// changing as it squashes. The corded mouse plugs in instead: its cable reaches in from the right and into the
  /// top of the mouse, which closes up and gives a smaller hop; turning off, the cable pulls back out as it
  /// squashes. The old drawing stays set until the hop ends, so a storyboard slow to start shows it a moment
  /// longer rather than flashing the new one early.
  /// </summary>
  private void Hop (bool on)
  {
    this.hop?.Stop ();
    this.Glyph.IsOn = !on;
    this.hop = new Storyboard ();

    var cable = this.Glyph.Cable;
    TimeSpan swap;

    if (on && cable is not null)
    {
      // 0.74 s: 0.3 s for the cable to plug in, then a hop half as high as the plain one, and a small second hop.
      swap = TimeSpan.FromSeconds (0.3);
      this.hop.Children.Add (Dash (cable, this.Glyph.CableHidden, 0, 0.3, new CubicEase { EasingMode = EasingMode.EaseOut }));
      this.hop.Children.Add (Frames (this.HopMove, nameof (CompositeTransform.TranslateY), new SineEase { EasingMode = EasingMode.EaseInOut },
          (0, 0), (0.3, 0), (0.44, -4.2), (0.58, 0), (0.66, -1.3), (0.74, 0)));
    }
    else if (on)
    {
      // 0.64 s: up a third of the glyph's height, down, a small second hop.
      swap = TimeSpan.FromSeconds (0.192);
      this.hop.Children.Add (Frames (this.HopMove, nameof (CompositeTransform.TranslateY), new SineEase { EasingMode = EasingMode.EaseInOut },
          (0, 0), (0.192, -8.3), (0.352, 0), (0.48, -2.6), (0.64, 0)));
    }
    else
    {
      // 0.38 s: squashed wide and low, a little spring back, still. Unplugged, the mouse opens as the cable
      // starts to go, and the cable is gone by the time it springs back.
      swap = TimeSpan.FromSeconds (cable is null ? 0.133 : 0.06);
      var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
      this.hop.Children.Add (Frames (this.HopMove, nameof (CompositeTransform.ScaleX), ease, (0, 1), (0.133, 1.15), (0.266, 0.96), (0.38, 1)));
      this.hop.Children.Add (Frames (this.HopMove, nameof (CompositeTransform.ScaleY), ease, (0, 1), (0.133, 0.78), (0.266, 1.04), (0.38, 1)));

      if (cable is not null)
        this.hop.Children.Add (Dash (cable, 0, this.Glyph.CableHidden, 0.22, new CubicEase { EasingMode = EasingMode.EaseIn }));
    }

    var layers = new List<(UIElement Layer, bool ShownFirst)> { (this.Glyph.OffLayer, on), (this.Glyph.OnLayer, !on) };

    foreach (var (layer, shownFirst) in layers)
    {
      var opacity = new DoubleAnimationUsingKeyFrames ();
      opacity.KeyFrames.Add (new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = shownFirst ? 1 : 0 });
      opacity.KeyFrames.Add (new DiscreteDoubleKeyFrame { KeyTime = swap, Value = shownFirst ? 0 : 1 });
      Storyboard.SetTarget (opacity, layer);
      Storyboard.SetTargetProperty (opacity, nameof (this.Opacity));
      this.hop.Children.Add (opacity);
    }

    // The cable's layer shows throughout; how much of the cable shows is down to its dashes.
    if (cable is not null)
    {
      var shown = new DoubleAnimationUsingKeyFrames ();
      shown.KeyFrames.Add (new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1 });
      Storyboard.SetTarget (shown, this.Glyph.CableLayer);
      Storyboard.SetTargetProperty (shown, nameof (this.Opacity));
      this.hop.Children.Add (shown);
    }

    var board = this.hop;
    board.Completed += (_, _) =>
    {
      this.Glyph.IsOn = on;
      board.Stop ();
    };
    board.Begin ();
  }

  /// <summary>
  /// Slides the cable's dashes along it, which shows or hides it a bit at a time from its far end. Dash offsets
  /// can't be animated off the UI thread, hence the dependent animation; it's a fraction of a second.
  /// </summary>
  private static DoubleAnimation Dash (Shape cable, double from, double to, double seconds, EasingFunctionBase ease)
  {
    var animation = new DoubleAnimation
    {
      From = from,
      To = to,
      Duration = new Duration (TimeSpan.FromSeconds (seconds)),
      EasingFunction = ease,
      EnableDependentAnimation = true
    };

    Storyboard.SetTarget (animation, cable);
    Storyboard.SetTargetProperty (animation, nameof (Shape.StrokeDashOffset));

    return animation;
  }

  private static DoubleAnimationUsingKeyFrames Frames (DependencyObject target, string property, EasingFunctionBase ease, params (double Seconds, double Value)[] frames)
  {
    var animation = new DoubleAnimationUsingKeyFrames ();
    Storyboard.SetTarget (animation, target);
    Storyboard.SetTargetProperty (animation, property);

    foreach (var (seconds, value) in frames)
      animation.KeyFrames.Add (new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds (seconds), Value = value, EasingFunction = ease });

    return animation;
  }

  /// <summary>
  /// Runs the wave and the hourglass while on and on screen; hidden windows don't spend anything on them.
  /// </summary>
  private void UpdateMotion ()
  {
    var wanted = this.Model.IsOn && Settings.AnimationsEnabled && this.root is { IsHostVisible: true };

    if (!wanted)
    {
      this.StopMotion ();

      return;
    }

    if (this.animating)
      return;

    this.animating = true;
    this.PulseMotion.Begin ();
    this.GrainMotion.Begin ();
    this.HourglassMotion.Begin ();

    // The panes count their turns from the start of the run; keep the glass in step with them, even when this
    // view opens part way through.
    var elapsed = DateTimeOffset.UtcNow - this.Model.StartedAt;
    this.HourglassMotion.Seek (TimeSpan.FromMilliseconds (Math.Max (0, elapsed.TotalMilliseconds) % 10000));
  }

  private void StopMotion ()
  {
    if (!this.animating)
      return;

    this.animating = false;
    this.PulseMotion.Stop ();
    this.GrainMotion.Stop ();
    this.HourglassMotion.Stop ();
    this.NudgeMotion.Stop ();
    this.CountdownDissolve.Stop ();
  }

  /// <summary>
  /// The pin shows on hover or keyboard focus, and stays while pinned. It only works while on.
  /// </summary>
  private void UpdatePin ()
  {
    var on = this.Model.IsOn;
    var shown = on && (this.Model.PanePinned || this.hovering || this.PinButton.FocusState == FocusState.Keyboard);

    this.PinButton.IsEnabled = on;
    this.PinButton.Opacity = shown ? 1 : 0;
  }

  private void PowerButton_Click (object sender, RoutedEventArgs e)
  {
    this.Model.Toggle ();
    this.PowerButton.IsChecked = this.Model.IsOn;
  }

  private void TimerButton_Click (object sender, RoutedEventArgs e) => this.Model.NextPane ();

  private void PinButton_Click (object sender, RoutedEventArgs e)
  {
    this.Model.TogglePin ();
    this.PinButton.IsChecked = this.Model.PanePinned;
  }

  private void PinButton_FocusChanged (object sender, RoutedEventArgs e) => this.UpdatePin ();

  private void TimerArea_PointerEntered (object sender, PointerRoutedEventArgs e)
  {
    this.hovering = true;
    this.UpdatePin ();
  }

  private void TimerArea_PointerExited (object sender, PointerRoutedEventArgs e)
  {
    this.hovering = false;
    this.UpdatePin ();
  }
}
