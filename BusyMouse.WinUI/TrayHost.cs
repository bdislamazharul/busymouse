using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Foundation;
using static BusyMouse.WinUI.Native;

namespace BusyMouse.WinUI;

/// <summary>
/// The notification-area icon and its menu, plus a hidden top-level window that receives the icon's clicks and
/// the system broadcasts the app reacts to: session lock, resume from sleep, the toggle hotkey and theme changes.
/// </summary>
internal sealed unsafe class TrayHost : IDisposable
{
  private const uint CallbackMessage = WM_APP + 1;
  private const uint IconId = 1;
  private const int HotKeyId = 1;
  private const uint HotKeyKey = 'J';
  private const string HotKeyText = "Ctrl+Shift+J";
  private const string ClassName = "BusyMouse.Tray";

  internal const int MenuToggle = 1;
  internal const int MenuOpen = 2;
  internal const int MenuSettings = 3;
  internal const int MenuAbout = 4;
  internal const int MenuExit = 5;

  private static readonly uint TaskbarCreatedMessage = RegisterWindowMessageW ("TaskbarCreated");
  private static TrayHost? current;

  private readonly nint hwnd;
  private readonly bool hotKeyRegistered;
  private TrayIcon style;
  private nint iconOn;
  private nint iconOff;
  private bool lightTaskbar;
  private bool isOn;
  private string tip = "BusyMouse";
  private bool added;
  private long doubleClickedAt = long.MinValue;

  public TrayHost (TrayIcon style)
  {
    current = this;
    this.style = style;

    var instance = GetModuleHandleW (null);

    fixed (char* className = ClassName)
    {
      var wndClass = new WNDCLASSEXW
      {
        cbSize = (uint)sizeof (WNDCLASSEXW),
        lpfnWndProc = &WndProc,
        hInstance = instance,
        lpszClassName = className
      };

      _ = RegisterClassExW (&wndClass);

      // A real top-level window, though never shown: message-only windows miss the broadcasts we need.
      fixed (char* title = "BusyMouse")
        this.hwnd = CreateWindowExW (WS_EX_TOOLWINDOW, className, title, WS_POPUP, 0, 0, 0, 0, 0, 0, instance, 0);
    }

    if (this.hwnd == 0)
      throw new InvalidOperationException ($"Couldn't create the tray window (error {Marshal.GetLastPInvokeError ()}).");

    _ = WTSRegisterSessionNotification (this.hwnd, 0);
    this.hotKeyRegistered = RegisterHotKey (this.hwnd, HotKeyId, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, HotKeyKey) != 0;

    this.LoadIcons ();
    this.Add ();
  }

  /// <summary>Left click, or Enter or Space on the icon.</summary>
  public event EventHandler? Invoked;

  public event EventHandler? ToggleRequested;

  /// <summary>Open window in the menu, or a double click on the icon.</summary>
  public event EventHandler? OpenRequested;

  public event EventHandler? SettingsRequested;

  public event EventHandler? AboutRequested;

  public event EventHandler? ExitRequested;

  /// <summary>
  /// Right click on the icon, with the anchor point in physical pixels. If nothing handles it, the plain Win32 menu
  /// shows instead.
  /// </summary>
  public event EventHandler<MenuRequestedEventArgs>? MenuRequested;

  /// <summary>The toggle hotkey's text, if it registered.</summary>
  public string? HotKey => this.hotKeyRegistered ? HotKeyText : null;

  /// <summary>Whether the app is keeping the PC awake, as last shown on the icon.</summary>
  public bool IsOn => this.isOn;

  /// <summary>Whether the taskbar is light, which the menu follows rather than the app's theme.</summary>
  public bool LightTaskbar => this.lightTaskbar;

  /// <summary>Windows was locked (true) or unlocked (false).</summary>
  public event EventHandler<bool>? LockChanged;

  public event EventHandler? Resumed;

  /// <summary>Windows is shutting down or signing out.</summary>
  public event EventHandler? SessionEnding;

  /// <summary>
  /// The icon's bounds on screen in physical pixels, if the shell will say.
  /// </summary>
  public Rect? IconBounds
  {
    get
    {
      var id = new NOTIFYICONIDENTIFIER { cbSize = (uint)sizeof (NOTIFYICONIDENTIFIER), hWnd = this.hwnd, uID = IconId };
      RECT rect;

      if (Shell_NotifyIconGetRect (&id, &rect) != 0)
        return null;

      return new Rect (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }
  }

  /// <summary>
  /// Shows whether the app is keeping the PC awake, and the hover text.
  /// </summary>
  public void Update (bool on, string text)
  {
    if (on == this.isOn && text == this.tip)
      return;

    this.isOn = on;
    this.tip = text;
    this.Notify (NIM_MODIFY);
  }

  /// <summary>
  /// Changes the icon's look.
  /// </summary>
  public void SetStyle (TrayIcon style)
  {
    if (style == this.style)
      return;

    this.style = style;
    this.LoadIcons ();
    this.Notify (NIM_MODIFY);
  }

  public void Dispose ()
  {
    if (this.added)
    {
      var data = this.Data (0);
      _ = Shell_NotifyIconW (NIM_DELETE, &data);
      this.added = false;
    }

    if (this.hotKeyRegistered)
      _ = UnregisterHotKey (this.hwnd, HotKeyId);

    _ = WTSUnRegisterSessionNotification (this.hwnd);
    _ = DestroyWindow (this.hwnd);
    this.FreeIcons ();
    current = null;
  }

  private static bool ReadLightTaskbar ()
  {
    using var key = Registry.CurrentUser.OpenSubKey (@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

    return key?.GetValue ("SystemUsesLightTheme") is int value && value != 0;
  }

  [UnmanagedCallersOnly]
  private static nint WndProc (nint hwnd, uint msg, nint wParam, nint lParam)
  {
    try
    {
      if (current is { } host && host.hwnd == hwnd && host.Handle (msg, wParam, lParam) is { } result)
        return result;
    }
    catch (Exception ex)
    {
      // An exception can't cross back into Windows; note it and carry on.
      System.Diagnostics.Debug.WriteLine ($"Tray window: {ex}");
    }

    return DefWindowProcW (hwnd, msg, wParam, lParam);
  }

  private nint? Handle (uint msg, nint wParam, nint lParam)
  {
    switch (msg)
    {
      case CallbackMessage:
        switch ((uint)(lParam & 0xFFFF))
        {
          case NIN_SELECT:
          case NIN_KEYSELECT:
            // The second click of a double click ends in a select too; the double click has dealt with it.
            // Added, not subtracted: before any double click, now minus long.MinValue overflows to below zero,
            // which swallowed every click until the first double click.
            if (Environment.TickCount64 > this.doubleClickedAt + GetDoubleClickTime ())
              this.Invoked?.Invoke (this, EventArgs.Empty);

            break;

          case WM_LBUTTONDBLCLK:
            // The first click has opened the flyout by now; opening the window puts it away.
            this.doubleClickedAt = Environment.TickCount64;
            this.OpenRequested?.Invoke (this, EventArgs.Empty);
            break;

          case WM_CONTEXTMENU:
            // With version 4 the anchor point comes in wParam.
            var x = (short)(wParam & 0xFFFF);
            var y = (short)((wParam >> 16) & 0xFFFF);
            var request = new MenuRequestedEventArgs (x, y);

            this.MenuRequested?.Invoke (this, request);

            if (!request.Handled)
              this.ShowMenu (x, y);
            break;
        }

        return 0;

      case WM_HOTKEY when wParam == HotKeyId:
        this.ToggleRequested?.Invoke (this, EventArgs.Empty);

        return 0;

      case WM_WTSSESSION_CHANGE when wParam is WTS_SESSION_LOCK or WTS_SESSION_UNLOCK:
        this.LockChanged?.Invoke (this, wParam == WTS_SESSION_LOCK);

        return 0;

      case WM_POWERBROADCAST when wParam is PBT_APMRESUMEAUTOMATIC or PBT_APMRESUMESUSPEND:
        this.Resumed?.Invoke (this, EventArgs.Empty);

        return 1;

      case WM_ENDSESSION when wParam != 0:
        this.SessionEnding?.Invoke (this, EventArgs.Empty);

        return 0;

      case WM_SETTINGCHANGE when lParam != 0 && new string ((char*)lParam) == "ImmersiveColorSet":
      case WM_DISPLAYCHANGE:
        this.LoadIcons ();
        this.Notify (NIM_MODIFY);

        return null;
    }

    if (msg == TaskbarCreatedMessage && msg != 0)
    {
      // Explorer restarted, so the icon is gone; put it back.
      this.added = false;
      this.LoadIcons ();
      this.Add ();
    }

    return null;
  }

  private void ShowMenu (int x, int y)
  {
    var menu = CreatePopupMenu ();

    try
    {
      var toggle = this.hotKeyRegistered ? $"Keep awake\t{HotKeyText}" : "Keep awake";

      _ = AppendMenuW (menu, MF_STRING | (this.isOn ? MF_CHECKED : 0), (nuint)MenuToggle, toggle);
      _ = AppendMenuW (menu, MF_SEPARATOR, 0, null);
      _ = AppendMenuW (menu, MF_STRING, (nuint)MenuOpen, "Open window");
      _ = AppendMenuW (menu, MF_STRING, (nuint)MenuSettings, "Settings");
      _ = AppendMenuW (menu, MF_STRING, (nuint)MenuAbout, "About");
      _ = AppendMenuW (menu, MF_SEPARATOR, 0, null);
      _ = AppendMenuW (menu, MF_STRING, (nuint)MenuExit, "Exit");

      this.ThemeMenus ();

      // Open away from the taskbar's edge, whichever edge that is.
      var monitor = MonitorFromPoint (new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
      var info = new MONITORINFO { cbSize = (uint)sizeof (MONITORINFO) };
      var flags = TPM_RIGHTBUTTON | TPM_RETURNCMD;

      if (GetMonitorInfoW (monitor, &info) != 0)
      {
        if (y > (info.rcWork.Top + info.rcWork.Bottom) / 2)
          flags |= TPM_BOTTOMALIGN;

        if (x > (info.rcWork.Left + info.rcWork.Right) / 2)
          flags |= TPM_RIGHTALIGN;
      }

      // The menu only closes on an outside click if our window is in the foreground.
      _ = SetForegroundWindow (this.hwnd);
      var command = TrackPopupMenuEx (menu, flags, x, y, this.hwnd, 0);
      _ = PostMessageW (this.hwnd, WM_NULL, 0, 0);

      this.Run (command);
    }
    finally
    {
      _ = DestroyMenu (menu);
    }
  }

  /// <summary>
  /// Carries out a menu command, whichever menu it came from.
  /// </summary>
  internal void Run (int command)
  {
    switch (command)
    {
      case MenuToggle:
        this.ToggleRequested?.Invoke (this, EventArgs.Empty);
        break;

      case MenuOpen:
        this.OpenRequested?.Invoke (this, EventArgs.Empty);
        break;

      case MenuSettings:
        this.SettingsRequested?.Invoke (this, EventArgs.Empty);
        break;

      case MenuAbout:
        this.AboutRequested?.Invoke (this, EventArgs.Empty);
        break;

      case MenuExit:
        this.ExitRequested?.Invoke (this, EventArgs.Empty);
        break;
    }
  }

  private void ThemeMenus ()
  {
    // The menu belongs to the taskbar, so it follows the taskbar's theme rather than the app's.
    if (!OperatingSystem.IsWindowsVersionAtLeast (10, 0, 18362))
      return;

    try
    {
      _ = SetPreferredAppMode (this.lightTaskbar ? 3 : 2);
      FlushMenuThemes ();
    }
    catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
    {
      // Plain light menus, then.
    }
  }

  private void LoadIcons ()
  {
    this.FreeIcons ();
    this.lightTaskbar = ReadLightTaskbar ();

    var size = GetSystemMetricsForDpi (SM_CXSMICON, GetDpiForSystem ());
    var theme = this.lightTaskbar ? "Light" : "Dark";
    var folder = Path.Combine (AppContext.BaseDirectory, "Assets");

    this.iconOn = LoadImageW (0, Path.Combine (folder, $"Tray{this.style}On{theme}.ico"), IMAGE_ICON, size, size, LR_LOADFROMFILE);
    this.iconOff = LoadImageW (0, Path.Combine (folder, $"Tray{this.style}Off{theme}.ico"), IMAGE_ICON, size, size, LR_LOADFROMFILE);
  }

  private void FreeIcons ()
  {
    if (this.iconOn != 0)
      _ = DestroyIcon (this.iconOn);

    if (this.iconOff != 0)
      _ = DestroyIcon (this.iconOff);

    this.iconOn = this.iconOff = 0;
  }

  private void Add ()
  {
    var data = this.Data (NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);

    if (Shell_NotifyIconW (NIM_ADD, &data) == 0)
      return;

    data.uVersion = NOTIFYICON_VERSION_4;
    _ = Shell_NotifyIconW (NIM_SETVERSION, &data);
    this.added = true;
  }

  private void Notify (uint message)
  {
    if (!this.added)
      return;

    var data = this.Data (NIF_ICON | NIF_TIP | NIF_SHOWTIP);
    _ = Shell_NotifyIconW (message, &data);
  }

  private NOTIFYICONDATAW Data (uint flags)
  {
    var data = new NOTIFYICONDATAW
    {
      cbSize = (uint)sizeof (NOTIFYICONDATAW),
      hWnd = this.hwnd,
      uID = IconId,
      uFlags = flags,
      uCallbackMessage = CallbackMessage,
      hIcon = this.isOn ? this.iconOn : this.iconOff
    };

    var text = this.tip.AsSpan (0, Math.Min (this.tip.Length, 127));
    text.CopyTo (new Span<char> (data.szTip, 128));

    return data;
  }
}

internal sealed class MenuRequestedEventArgs (int x, int y) : EventArgs
{
  public int X { get; } = x;

  public int Y { get; } = y;

  public bool Handled { get; set; }
}
