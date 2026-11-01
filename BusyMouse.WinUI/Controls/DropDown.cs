using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Windows.System;
using Windows.UI.Core;

namespace BusyMouse.WinUI.Controls;

/// <summary>
/// A combo box whose list always opens below it, lined up with its left edge and as wide as it is; the stock one
/// opens over itself, up or down depending on which item is chosen. The items are strings, or DataTemplates for
/// richer rows, loaded once for the list and again for the box; a template's root names its row with
/// AutomationProperties.Name.
/// </summary>
[ContentProperty (Name = nameof (Items))]
public sealed partial class DropDown : Button
{
  public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register (
      nameof (SelectedIndex),
      typeof (int),
      typeof (DropDown),
      new PropertyMetadata (-1, (d, _) => ((DropDown)d).ShowSelected ()));

  private readonly ContentPresenter shown = new () { VerticalAlignment = VerticalAlignment.Center };
  private readonly ListView list = new () { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };
  private readonly List<ListViewItem> rows = [];
  private readonly Flyout flyout;
  private FocusState openedBy;
  private string shownName = string.Empty;

  public DropDown ()
  {
    var resources = Application.Current.Resources;

    // The stock combo box's padding; the chevron sits in the right-hand 38. The corners are the list's 8, so the box
    // and its list look of a piece.
    this.Padding = new Thickness (12, 5, 0, 7);
    this.CornerRadius = new CornerRadius (8);
    this.HorizontalContentAlignment = HorizontalAlignment.Stretch;

    var glyph = new FontIcon { Style = (Style)resources["DropDownGlyphStyle"] };
    Grid.SetColumn (glyph, 1);

    var face = new Grid ();
    face.ColumnDefinitions.Add (new ColumnDefinition ());
    face.ColumnDefinitions.Add (new ColumnDefinition { Width = new GridLength (38) });
    face.Children.Add (this.shown);
    face.Children.Add (glyph);
    this.Content = face;

    this.list.ItemClick += this.List_ItemClick;

    this.flyout = new Flyout
    {
      Content = this.list,
      FlyoutPresenterStyle = (Style)resources["DropDownPresenterStyle"],
      Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
      // Free to reach past the window, so there's room below it rather than a flip to above.
      ShouldConstrainToRootBounds = false
    };
    this.flyout.Opening += this.Flyout_Opening;
    this.flyout.Opened += this.Flyout_Opened;
    this.Flyout = this.flyout;

    this.Loaded += (_, _) => this.ShowSelected ();
  }

  public IList<object> Items { get; } = new List<object> ();

  public int SelectedIndex
  {
    get => (int)this.GetValue (SelectedIndexProperty);
    set => this.SetValue (SelectedIndexProperty, value);
  }

  protected override AutomationPeer OnCreateAutomationPeer () => new DropDownAutomationPeer (this);

  protected override void OnKeyDown (KeyRoutedEventArgs e)
  {
    var alt = InputKeyboardSource.GetKeyStateForCurrentThread (VirtualKey.Menu).HasFlag (CoreVirtualKeyStates.Down);

    if (e.Key == VirtualKey.F4 || (alt && e.Key == VirtualKey.Down))
    {
      this.flyout.ShowAt (this);
      e.Handled = true;
    }
    else if (e.Key is VirtualKey.Up or VirtualKey.Down && this.Items.Count > 0)
    {
      // As in a combo box, the arrows step through the items without opening the list.
      this.SelectedIndex = Math.Clamp (this.SelectedIndex + (e.Key == VirtualKey.Down ? 1 : -1), 0, this.Items.Count - 1);
      e.Handled = true;
    }
    else
    {
      base.OnKeyDown (e);
    }
  }

  private static UIElement Make (object item) => item switch
  {
    DataTemplate template => (UIElement)template.LoadContent (),
    _ => new TextBlock { Text = item.ToString (), TextTrimming = TextTrimming.CharacterEllipsis }
  };

  private static string NameOf (object item, UIElement made) =>
    item is DataTemplate ? AutomationProperties.GetName (made) : item.ToString () ?? string.Empty;

  private void ShowSelected ()
  {
    var index = this.SelectedIndex;

    if (index < 0 || index >= this.Items.Count)
    {
      this.shown.Content = null;
      this.shownName = string.Empty;

      return;
    }

    var made = Make (this.Items[index]);
    this.shown.Content = made;
    this.shownName = NameOf (this.Items[index], made);
  }

  private void Flyout_Opening (object? sender, object e)
  {
    if (this.rows.Count != this.Items.Count)
    {
      var style = (Style)Application.Current.Resources["DropDownItemStyle"];

      this.rows.Clear ();
      this.list.Items.Clear ();

      foreach (var item in this.Items)
      {
        var made = Make (item);
        var row = new ListViewItem { Content = made, Style = style };
        AutomationProperties.SetName (row, NameOf (item, made));

        this.rows.Add (row);
        this.list.Items.Add (row);
      }
    }

    // As wide as the box, less the frame's border either side.
    this.list.Width = Math.Max (0, this.ActualWidth - 2);
    this.list.SelectedIndex = this.SelectedIndex;
    this.openedBy = this.FocusState == FocusState.Keyboard ? FocusState.Keyboard : FocusState.Programmatic;
  }

  private void Flyout_Opened (object? sender, object e)
  {
    if (this.SelectedIndex >= 0 && this.SelectedIndex < this.rows.Count)
      _ = this.rows[this.SelectedIndex].Focus (this.openedBy);
  }

  private void List_ItemClick (object sender, ItemClickEventArgs e)
  {
    // The rows are their own containers, so a click names either the row or what's in it.
    var index = this.rows.FindIndex (row => ReferenceEquals (row, e.ClickedItem) || ReferenceEquals (row.Content, e.ClickedItem));

    if (index >= 0)
      this.SelectedIndex = index;

    this.flyout.Hide ();
  }

  private sealed partial class DropDownAutomationPeer (DropDown owner) : ButtonAutomationPeer (owner)
  {
    // The label, then what's chosen, as a combo box reads.
    protected override string GetNameCore ()
    {
      var label = base.GetNameCore ();
      var chosen = ((DropDown)this.Owner).shownName;

      return chosen.Length == 0 ? label : label.Length == 0 ? chosen : $"{label}, {chosen}";
    }
  }
}
