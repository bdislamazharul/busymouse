using System.Diagnostics;
using System.Security;
using Microsoft.Win32;

namespace BusyMouse.WinUI;

/// <summary>
/// Starting with Windows: a value under the current user's Run key that opens the app in the tray at sign-in.
/// Windows keeps it, not the settings file, so it stays right if someone removes it in Task Manager.
/// </summary>
internal static class Autostart
{
  private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
  private const string ValueName = "BusyMouse";

  public static bool IsOn
  {
    get
    {
      try
      {
        using var key = Registry.CurrentUser.OpenSubKey (RunKey);

        return key?.GetValue (ValueName) is string;
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
      {
        return false;
      }
    }
  }

  /// <summary>
  /// The exe Windows should start. In the "no runtime" bundle the app lives in an "app" folder next to a native
  /// launcher that installs .NET if it's missing; start through the launcher so that still works later.
  /// </summary>
  private static string? StartPath
  {
    get
    {
      var self = Environment.ProcessPath;
      var folder = Path.GetDirectoryName (self);

      if (folder is not null && string.Equals (Path.GetFileName (folder), "app", StringComparison.OrdinalIgnoreCase))
      {
        var launcher = Path.Combine (Path.GetDirectoryName (folder) ?? folder, "BusyMouse.exe");

        if (File.Exists (launcher))
          return launcher;
      }

      return self;
    }
  }

  /// <summary>
  /// Turns it on or off. Leaves it as it was if Windows won't allow the change.
  /// </summary>
  public static void Set (bool on)
  {
    try
    {
      using var key = Registry.CurrentUser.CreateSubKey (RunKey);

      if (on)
        key.SetValue (ValueName, $"\"{StartPath}\" -m");
      else
        key.DeleteValue (ValueName, false);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
    {
      Debug.WriteLine ($"Start with Windows not changed: {ex.Message}");
    }
  }
}
