using System.Runtime.InteropServices;

namespace BusyMouse;

/// <summary>
/// The Win32 calls the keeper makes.
/// </summary>
internal static partial class NativeMethods
{
  public const uint ES_SYSTEM_REQUIRED = 0x00000001;
  public const uint ES_DISPLAY_REQUIRED = 0x00000002;
  public const uint ES_CONTINUOUS = 0x80000000;

  public const uint INPUT_MOUSE = 0;
  public const uint MOUSEEVENTF_MOVE = 0x0001;

  /// <summary>
  /// Returns the previous execution state, or 0 if it failed.
  /// </summary>
  [LibraryImport ("kernel32.dll", SetLastError = true)]
  public static partial uint SetThreadExecutionState (uint esFlags);

  [LibraryImport ("user32.dll", SetLastError = true)]
  [return: MarshalAs (UnmanagedType.Bool)]
  public static partial bool GetCursorPos (out POINT lpPoint);

  /// <summary>
  /// Returns how many of the inputs were inserted, 0 if blocked.
  /// </summary>
  [LibraryImport ("user32.dll", SetLastError = true)]
  public static unsafe partial uint SendInput (uint cInputs, INPUT* pInputs, int cbSize);

  [StructLayout (LayoutKind.Sequential)]
  public struct POINT
  {
    public int X;
    public int Y;
  }

  [StructLayout (LayoutKind.Sequential)]
  public struct MOUSEINPUT
  {
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public nuint dwExtraInfo;
  }

  // Win32 declares the second member as a union of MOUSEINPUT, KEYBDINPUT and HARDWAREINPUT. MOUSEINPUT is
  // the largest of the three, so declaring just that one still gives the size SendInput checks for.
  [StructLayout (LayoutKind.Sequential)]
  public struct INPUT
  {
    public uint type;
    public MOUSEINPUT mi;
  }
}
