using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace BusyMouse.WinUI.Controls;

/// <summary>
/// One recent custom time in the list under the editor.
/// </summary>
public sealed class LimitRow
{
  public required string Label { get; init; }

  public required string Detail { get; init; }

  public required string RemoveName { get; init; }

  public required Action OnPick { get; init; }

  public required Action OnRemove { get; init; }

  public void Pick () => this.OnPick ();

  public void Remove () => this.OnRemove ();
}

/// <summary>
/// "Keep awake for": no limit, 30 min, 1 h, 2 h, or a custom length or end time typed in, with the recent ones
/// to pick from again.
/// </summary>
public sealed partial class TimeLimitPicker : UserControl
{
  public static readonly DependencyProperty ListAboveProperty = DependencyProperty.Register (
      nameof (ListAbove),
      typeof (bool),
      typeof (TimeLimitPicker),
      new PropertyMetadata (false));

  private const string EditIcon = "M13.5 3.5l3 3L7 16H4v-3zM11.5 5.5l3 3";

  private readonly ToggleButton[] segments;
  private TimeLimit? typed;

  public TimeLimitPicker ()
  {
    this.InitializeComponent ();

    this.segments = [this.NoLimit, this.HalfHour, this.OneHour, this.TwoHours, this.Custom];

    this.Loaded += this.TimeLimitPicker_Loaded;
    this.Unloaded += this.TimeLimitPicker_Unloaded;
  }

  public KeeperViewModel Model => App.Model;

  /// <summary>
  /// Open the recent-times list upward, for the flyout at the bottom of the screen. The list may then reach
  /// outside the window. Otherwise it opens in line, below the box.
  /// </summary>
  public bool ListAbove
  {
    get => (bool)this.GetValue (ListAboveProperty);
    set => this.SetValue (ListAboveProperty, value);
  }

  public bool IsEditing => this.Editor.Visibility == Visibility.Visible;

  private bool ListOpen
  {
    get => this.ListPopup.IsOpen || this.ListHost.Visibility == Visibility.Visible;
    set
    {
      if (this.ListHost.Child is null)
        this.ListPopup.IsOpen = value;
      else
        this.ListHost.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }
  }

  /// <summary>
  /// Leaves the editor without changing anything.
  /// </summary>
  public void CloseEditor () => this.CloseEditor (false);

  private void TimeLimitPicker_Loaded (object sender, RoutedEventArgs e)
  {
    if (this.ListAbove)
    {
      this.ListPopup.PlacementTarget = this.Editor;
      this.ListPopup.DesiredPlacement = PopupPlacementMode.TopEdgeAlignedLeft;
      this.ListPopup.ShouldConstrainToRootBounds = false;
    }
    else if (this.ListPopup.Child is { } frame)
    {
      // A popup below the box would cover the settings under it.
      this.ListPopup.Child = null;
      this.ListHost.Child = frame;
      VisualStateManager.GoToState (this, "ListInline", false);
    }

    this.Model.PropertyChanged += this.Model_PropertyChanged;
    this.Model.Recents.CollectionChanged += this.Recents_CollectionChanged;
    this.Sync ();
  }

  private void TimeLimitPicker_Unloaded (object sender, RoutedEventArgs e)
  {
    this.Model.PropertyChanged -= this.Model_PropertyChanged;
    this.Model.Recents.CollectionChanged -= this.Recents_CollectionChanged;
    this.CloseEditor (false);
  }

  private void Model_PropertyChanged (object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName is null or "" or nameof (KeeperViewModel.LimitChoice) or nameof (KeeperViewModel.CustomLimit))
      this.Sync ();
  }

  private void Recents_CollectionChanged (object? sender, NotifyCollectionChangedEventArgs e)
  {
    if (this.ListOpen)
      this.FillList ();
  }

  /// <summary>
  /// Shows the model's choice. The custom segment carries its time while chosen, a pencil otherwise; a long
  /// custom label squeezes out "2 h".
  /// </summary>
  private void Sync ()
  {
    var choice = this.Model.LimitChoice;

    for (var i = 0; i < this.segments.Length; i++)
      this.segments[i].IsChecked = i == choice;

    if (choice == 4 && this.Model.CustomLimit is { } custom)
    {
      var label = Format.ShortLabel (custom);
      var full = Format.Label (custom);
      var wide = label.Length > 7;

      this.Custom.Content = label;
      AutomationProperties.SetName (this.Custom, $"Custom time: {full}. Click to change");
      ToolTipService.SetToolTip (this.Custom,
          (custom.Kind == TimeLimitKind.Until ? $"Turn off at {Format.TimeOfDay (custom.Minutes)}" : $"Turn off after {full}") + ". Click to change it");

      this.TwoHours.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
      this.TwoHoursColumn.Width = wide ? new GridLength (0) : new GridLength (1, GridUnitType.Star);
      this.CustomColumn.Width = new GridLength (wide ? 1.4 : 1, GridUnitType.Star);
    }
    else
    {
      this.Custom.Content = new StrokeIcon { Data = EditIcon };
      AutomationProperties.SetName (this.Custom, "Custom time");
      ToolTipService.SetToolTip (this.Custom, "Pick any length or an end time");

      this.TwoHours.Visibility = Visibility.Visible;
      this.TwoHoursColumn.Width = new GridLength (1, GridUnitType.Star);
      this.CustomColumn.Width = new GridLength (1, GridUnitType.Star);
    }
  }

  private void Segment_Click (object sender, RoutedEventArgs e)
  {
    var index = Array.IndexOf (this.segments, sender);

    if (index == 4)
      this.OpenEditor ();
    else
      this.Model.ChooseLimit (index);

    // A toggle button flips itself on click; put it back to what the model says.
    this.Sync ();
  }

  private void OpenEditor ()
  {
    this.Segments.Visibility = Visibility.Collapsed;
    this.Editor.Visibility = Visibility.Visible;
    this.Entry.Text = string.Empty;
    this.UpdateHint ();

    _ = this.Entry.Focus (FocusState.Programmatic);
    this.OpenList ();
  }

  private void CloseEditor (bool focusCustom)
  {
    if (!this.IsEditing)
      return;

    this.ListOpen = false;
    this.Editor.Visibility = Visibility.Collapsed;
    this.Segments.Visibility = Visibility.Visible;
    this.Hint.Text = string.Empty;
    VisualStateManager.GoToState (this, "HintNormal", false);

    if (focusCustom)
      _ = this.Custom.Focus (FocusState.Programmatic);
  }

  private void Commit (TimeLimit limit)
  {
    this.Model.UseLimit (limit);
    this.CloseEditor (true);
  }

  private void UpdateHint ()
  {
    var result = TimeLimit.TryParse (this.Entry.Text, out var limit);

    this.typed = result == TimeLimitParseResult.Valid ? limit : null;
    this.SaveButton.IsEnabled = this.typed is not null;

    this.Hint.Text = result switch
    {
      TimeLimitParseResult.Empty => "Type a length",
      TimeLimitParseResult.TooLong => "Up to 30 days",
      TimeLimitParseResult.Invalid => "Try 45m, 2h 30m or until 6 pm",
      _ => $"{Format.Label (limit)} · {Format.Detail (limit, DateTime.Now)}"
    };

    VisualStateManager.GoToState (this, result is TimeLimitParseResult.Invalid or TimeLimitParseResult.TooLong ? "HintError" : "HintNormal", false);
  }

  private void OpenList ()
  {
    if (!this.IsEditing)
      return;

    this.FillList ();

    if (this.ListHost.Child is null)
      this.ListFrame.Width = Math.Max (0, this.Editor.ActualWidth > 0 ? this.Editor.ActualWidth : this.ActualWidth);

    this.ListOpen = true;
  }

  private void FillList ()
  {
    var now = DateTime.Now;
    var recents = this.Model.Recents;
    var showTyped = this.typed is { } t && !recents.Contains (t);

    this.TypedRow.Visibility = showTyped ? Visibility.Visible : Visibility.Collapsed;

    if (this.typed is { } limit && showTyped)
    {
      this.TypedLabel.Text = Format.Label (limit);
      this.TypedDetail.Text = Format.Detail (limit, now);
      AutomationProperties.SetName (this.TypedRow, $"{this.TypedLabel.Text}, {this.TypedDetail.Text}");
    }

    this.RecentHeader.Visibility = recents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    this.RecentList.ItemsSource = recents.Select (r => new LimitRow
    {
      Label = Format.Label (r),
      Detail = Format.Detail (r, now),
      RemoveName = $"Remove {Format.Label (r)} from recent times",
      OnPick = () => this.Commit (r),
      OnRemove = () => this.Model.RemoveRecent (r)
    }).ToList ();

    var anything = showTyped || recents.Count > 0;
    this.HelpLine.BorderThickness = new Thickness (0, anything ? 1 : 0, 0, 0);
    this.HelpLine.Margin = new Thickness (0, anything ? 4 : 0, 0, 0);
  }

  private void Entry_TextChanged (object sender, TextChangedEventArgs e)
  {
    this.UpdateHint ();
    this.OpenList ();
  }

  private void Entry_KeyDown (object sender, KeyRoutedEventArgs e)
  {
    switch (e.Key)
    {
      case VirtualKey.Enter:
        if (this.typed is { } limit)
          this.Commit (limit);

        e.Handled = true;
        break;

      case VirtualKey.Escape:
        this.CloseEditor (true);
        e.Handled = true;
        break;

      case VirtualKey.Down:
        this.OpenList ();
        e.Handled = true;
        break;
    }
  }

  private void Entry_FocusChanged (object sender, RoutedEventArgs e) =>
    VisualStateManager.GoToState (this, this.Entry.FocusState == FocusState.Unfocused ? "EntryRest" : "EntryFocused", false);

  private void ListButton_Click (object sender, RoutedEventArgs e)
  {
    if (this.ListOpen)
      this.ListOpen = false;
    else
      this.OpenList ();

    _ = this.Entry.Focus (FocusState.Programmatic);
  }

  private void SaveButton_Click (object sender, RoutedEventArgs e)
  {
    if (this.typed is { } limit)
      this.Commit (limit);
  }

  private void CancelButton_Click (object sender, RoutedEventArgs e) => this.CloseEditor (true);

  private void TypedRow_Click (object sender, RoutedEventArgs e)
  {
    if (this.typed is { } limit)
      this.Commit (limit);
  }

  private void ClearRecents_Click (object sender, RoutedEventArgs e) => this.Model.ClearRecents ();
}
