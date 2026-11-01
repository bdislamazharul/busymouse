using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace BusyMouse.WinUI;

internal static class WindowSizing
{
  /// <summary>
  /// Sizes the area the content gets, in physical pixels. AppWindow.ResizeClient still leaves room for a title bar
  /// once the presenter has taken it away, so go by the frame the window really has.
  /// </summary>
  public static unsafe void SetContentSize (this AppWindow window, SizeInt32 size)
  {
    var hwnd = Win32Interop.GetWindowFromWindowId (window.Id);
    Native.RECT outer, inner;

    if (Native.GetWindowRect (hwnd, &outer) == 0 || Native.GetClientRect (hwnd, &inner) == 0)
    {
      window.ResizeClient (size);

      return;
    }

    var frameWidth = outer.Right - outer.Left - inner.Right;
    var frameHeight = outer.Bottom - outer.Top - inner.Bottom;

    window.Resize (new SizeInt32 (size.Width + frameWidth, size.Height + frameHeight));
  }
}
