using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace BusyMouse.WinUI.Controls;

/// <summary>
/// A footer's More: opens what it <see cref="Folds"/>, then says Less and folds it away again. It starts folded.
/// The chevron points the way the window grows to open it.
/// </summary>
public sealed partial class MoreButton : Button
{
  public static readonly DependencyProperty FoldsProperty = DependencyProperty.Register (
      nameof (Folds),
      typeof (UIElement),
      typeof (MoreButton),
      new PropertyMetadata (null, (d, _) => ((MoreButton)d).Show ()));

  public static readonly DependencyProperty OpensUpProperty = DependencyProperty.Register (
      nameof (OpensUp),
      typeof (bool),
      typeof (MoreButton),
      new PropertyMetadata (false, (d, _) => ((MoreButton)d).Show ()));

  private const string Down = "M5.5 8L10 12.5L14.5 8";
  private const string Up = "M5.5 12.5L10 8L14.5 12.5";

  private readonly TextBlock text = new ();
  private readonly StrokeIcon chevron = new () { Width = 12, Height = 12, StrokeThickness = 1.6 };
  private bool open;

  public MoreButton ()
  {
    this.Style = (Style)Application.Current.Resources["SubtleButtonStyle"];
    this.Height = 32;
    this.HorizontalAlignment = HorizontalAlignment.Right;

    var face = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
    face.Children.Add (this.text);
    face.Children.Add (this.chevron);
    this.Content = face;

    this.Click += (_, _) => this.IsOpen = !this.IsOpen;
    this.Show ();
  }

  /// <summary>
  /// Raised when it opens or folds.
  /// </summary>
  public event EventHandler? Toggled;

  public UIElement? Folds
  {
    get => (UIElement?)this.GetValue (FoldsProperty);
    set => this.SetValue (FoldsProperty, value);
  }

  /// <summary>
  /// The window grows upward to open it, as a flyout over a taskbar at the bottom does.
  /// </summary>
  public bool OpensUp
  {
    get => (bool)this.GetValue (OpensUpProperty);
    set => this.SetValue (OpensUpProperty, value);
  }

  public bool IsOpen
  {
    get => this.open;
    set
    {
      if (value == this.open)
        return;

      this.open = value;
      this.Show ();
      this.Toggled?.Invoke (this, EventArgs.Empty);
    }
  }

  private void Show ()
  {
    if (this.Folds is { } folds)
      folds.Visibility = this.open ? Visibility.Visible : Visibility.Collapsed;

    this.text.Text = this.open ? "Less" : "More";
    this.chevron.Data = this.open != this.OpensUp ? Up : Down;
    AutomationProperties.SetName (this, this.open ? "Show fewer settings" : "Show more settings");
  }
}
