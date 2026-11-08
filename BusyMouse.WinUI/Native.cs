using System.Runtime.InteropServices;

namespace BusyMouse.WinUI;

/// <summary>
/// The Win32 calls WinUI doesn't cover: the notification-area icon and its menu, system broadcasts, monitors.
/// </summary>
internal static unsafe partial class Native
{
  public const uint WM_NULL = 0x0000;
  public const uint WM_QUERYENDSESSION = 0x0011;
  public const uint WM_ENDSESSION = 0x0016;
  public const uint WM_SETTINGCHANGE = 0x001A;
  public const uint WM_CONTEXTMENU = 0x007B;
  public const uint WM_DISPLAYCHANGE = 0x007E;
  public const uint WM_LBUTTONDBLCLK = 0x0203;
  public const uint WM_POWERBROADCAST = 0x0218;
  public const uint WM_WTSSESSION_CHANGE = 0x02B1;
  public const uint WM_HOTKEY = 0x0312;
  public const uint WM_APP = 0x8000;

  public const uint NIN_SELECT = 0x0400;
  public const uint NIN_KEYSELECT = 0x0401;
  public const uint NIM_ADD = 0;
  public const uint NIM_MODIFY = 1;
  public const uint NIM_DELETE = 2;
  public const uint NIM_SETVERSION = 4;
  public const uint NIF_MESSAGE = 0x01;
  public const uint NIF_ICON = 0x02;
  public const uint NIF_TIP = 0x04;
  public const uint NIF_SHOWTIP = 0x80;
  public const uint NOTIFYICON_VERSION_4 = 4;

  public const nint PBT_APMRESUMESUSPEND = 0x0007;
  public const nint PBT_APMRESUMEAUTOMATIC = 0x0012;
  public const nint WTS_SESSION_LOCK = 7;
  public const nint WTS_SESSION_UNLOCK = 8;

  public const uint MOD_CONTROL = 0x0002;
  public const uint MOD_SHIFT = 0x0004;
  public const uint MOD_NOREPEAT = 0x4000;

  public const uint MF_STRING = 0x0000;
  public const uint MF_CHECKED = 0x0008;
  public const uint MF_SEPARATOR = 0x0800;
  public const uint TPM_LEFTALIGN = 0x0000;
  public const uint TPM_RIGHTALIGN = 0x0008;
  public const uint TPM_TOPALIGN = 0x0000;
  public const uint TPM_BOTTOMALIGN = 0x0020;
  public const uint TPM_RIGHTBUTTON = 0x0002;
  public const uint TPM_RETURNCMD = 0x0100;

  public const uint WS_POPUP = 0x80000000;
  public const uint WS_EX_TOOLWINDOW = 0x00000080;
  public const uint WS_EX_LAYERED = 0x00080000;
  public const int GWL_EXSTYLE = -20;
  public const uint LWA_ALPHA = 0x00000002;
  public const uint IMAGE_ICON = 1;
  public const uint LR_LOADFROMFILE = 0x0010;
  public const int SM_CXSMICON = 49;
  public const uint MONITOR_DEFAULTTONEAREST = 2;
  public const int MDT_EFFECTIVE_DPI = 0;
  public const uint GA_ROOTOWNER = 3;
  public const uint ASFW_ANY = unchecked((uint)-1);
  public const uint MB_ICONERROR = 0x10;
  public const uint MB_ICONINFORMATION = 0x40;
  public const uint ATTACH_PARENT_PROCESS = uint.MaxValue;

  [StructLayout (LayoutKind.Sequential)]
  public struct POINT
  {
    public int X;
    public int Y;
  }

  [StructLayout (LayoutKind.Sequential)]
  public struct RECT
  {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
  }

  [StructLayout (LayoutKind.Sequential)]
  public struct MONITORINFO
  {
    public uint cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
  }

  [StructLayout (LayoutKind.Sequential)]
  public struct WNDCLASSEXW
  {
    public uint cbSize;
    public uint style;
    public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public nint hInstance;
    public nint hIcon;
    public nint hCursor;
    public nint hbrBackground;
    public char* lpszMenuName;
    public char* lpszClassName;
    public nint hIconSm;
  }

  [StructLayout (LayoutKind.Sequential)]
  public struct NOTIFYICONDATAW
  {
    public uint cbSize;
    public nint hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public nint hIcon;
    public fixed char szTip[128];
    public uint dwState;
    public uint dwStateMask;
    public fixed char szInfo[256];
    public uint uVersion;
    public fixed char szInfoTitle[64];
    public uint dwInfoFlags;
    public Guid guidItem;
    public nint hBalloonIcon;
  }

  [StructLayout (LayoutKind.Sequential)]
  public struct NOTIFYICONIDENTIFIER
  {
    public uint cbSize;
    public nint hWnd;
    public uint uID;
    public Guid guidItem;
  }

  private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

  [LibraryImport ("kernel32.dll")]
  private static partial int GetCurrentPackageFullName (ref uint length, char* name);

  /// <summary>
  /// True when running from the Microsoft Store (MSIX) package rather than the installer or a zip.
  /// </summary>
  public static readonly bool IsPackaged = IsRunningPackaged ();

  private static bool IsRunningPackaged ()
  {
    uint length = 0;

    return GetCurrentPackageFullName (ref length, null) != APPMODEL_ERROR_NO_PACKAGE;
  }

  [LibraryImport ("user32.dll", SetLastError = true)]
  public static partial ushort RegisterClassExW (WNDCLASSEXW* wndClass);

  [LibraryImport ("user32.dll", SetLastError = true)]
  public static partial nint CreateWindowExW (uint exStyle, char* className, char* windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

  [LibraryImport ("user32.dll")]
  public static partial nint DefWindowProcW (nint hwnd, uint msg, nint wParam, nint lParam);

  [LibraryImport ("user32.dll")]
  public static partial int DestroyWindow (nint hwnd);

  [LibraryImport ("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
  public static partial uint RegisterWindowMessageW (string name);

  [LibraryImport ("user32.dll")]
  public static partial int SetForegroundWindow (nint hwnd);

  [LibraryImport ("user32.dll")]
  public static partial nint GetForegroundWindow ();

  [LibraryImport ("user32.dll")]
  public static partial int GetWindowRect (nint hwnd, RECT* rect);

  [LibraryImport ("user32.dll")]
  public static partial int GetClientRect (nint hwnd, RECT* rect);

  [LibraryImport ("user32.dll")]
  public static partial nint GetAncestor (nint hwnd, uint flags);

  [LibraryImport ("user32.dll")]
  public static partial int AllowSetForegroundWindow (uint processId);

  [LibraryImport ("user32.dll")]
  public static partial int PostMessageW (nint hwnd, uint msg, nint wParam, nint lParam);

  [LibraryImport ("user32.dll")]
  public static partial nint GetWindowLongPtrW (nint hwnd, int index);

  [LibraryImport ("user32.dll")]
  public static partial nint SetWindowLongPtrW (nint hwnd, int index, nint value);

  [LibraryImport ("user32.dll")]
  public static partial int SetLayeredWindowAttributes (nint hwnd, uint key, byte alpha, uint flags);

  [LibraryImport ("user32.dll")]
  public static partial nint CreatePopupMenu ();

  [LibraryImport ("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
  public static partial int AppendMenuW (nint menu, uint flags, nuint id, string? text);

  [LibraryImport ("user32.dll")]
  public static partial int TrackPopupMenuEx (nint menu, uint flags, int x, int y, nint hwnd, nint parameters);

  [LibraryImport ("user32.dll")]
  public static partial int DestroyMenu (nint menu);

  [LibraryImport ("user32.dll")]
  public static partial int GetCursorPos (POINT* point);

  [LibraryImport ("user32.dll")]
  public static partial int RegisterHotKey (nint hwnd, int id, uint modifiers, uint key);

  [LibraryImport ("user32.dll")]
  public static partial int UnregisterHotKey (nint hwnd, int id);

  [LibraryImport ("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
  public static partial nint LoadImageW (nint instance, string name, uint type, int cx, int cy, uint flags);

  [LibraryImport ("user32.dll")]
  public static partial int DestroyIcon (nint icon);

  [LibraryImport ("user32.dll")]
  public static partial int GetSystemMetricsForDpi (int index, uint dpi);

  [LibraryImport ("user32.dll")]
  public static partial uint GetDpiForSystem ();

  [LibraryImport ("user32.dll")]
  public static partial uint GetDoubleClickTime ();

  [LibraryImport ("user32.dll")]
  public static partial nint MonitorFromPoint (POINT point, uint flags);

  [LibraryImport ("user32.dll")]
  public static partial int GetMonitorInfoW (nint monitor, MONITORINFO* info);

  [LibraryImport ("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
  public static partial int MessageBoxW (nint hwnd, string text, string caption, uint type);

  [LibraryImport ("shell32.dll")]
  public static partial int Shell_NotifyIconW (uint message, NOTIFYICONDATAW* data);

  [LibraryImport ("shell32.dll")]
  public static partial int Shell_NotifyIconGetRect (NOTIFYICONIDENTIFIER* identifier, RECT* rect);

  [LibraryImport ("shcore.dll")]
  public static partial int GetDpiForMonitor (nint monitor, int type, uint* dpiX, uint* dpiY);

  [LibraryImport ("wtsapi32.dll")]
  public static partial int WTSRegisterSessionNotification (nint hwnd, uint flags);

  [LibraryImport ("wtsapi32.dll")]
  public static partial int WTSUnRegisterSessionNotification (nint hwnd);

  [LibraryImport ("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
  public static partial nint GetModuleHandleW (string? name);

  [LibraryImport ("kernel32.dll")]
  public static partial int AttachConsole (uint processId);

  [LibraryImport ("kernel32.dll")]
  public static partial int FreeConsole ();

  /// <summary>
  /// Undocumented uxtheme export (Windows 10 1903 and later) that sets whether this process's Win32 menus draw
  /// light or dark: 2 forces dark, 3 forces light.
  /// </summary>
  [DllImport ("uxtheme.dll", EntryPoint = "#135")]
  public static extern int SetPreferredAppMode (int mode);

  /// <summary>
  /// Undocumented uxtheme export that makes menus pick up a <see cref="SetPreferredAppMode"/> change.
  /// </summary>
  [DllImport ("uxtheme.dll", EntryPoint = "#136")]
  public static extern void FlushMenuThemes ();
}
