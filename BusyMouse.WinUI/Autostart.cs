using System.Diagnostics;
using System.Security;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace BusyMouse.WinUI;

/// <summary>
/// Starting with Windows, in the tray, at sign-in. Windows keeps this, not the settings file, so it stays right if
/// someone turns it off in Task Manager.
/// Installed or portable: a value under the current user's Run key. Store (MSIX) package: the package's startup task,
/// since a packaged app's writes to the Run key never reach the real registry.
/// </summary>
internal static class Autostart
{
  private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
  private const string ValueName = "BusyMouse";

  /// <summary>
  /// The TaskId of the startup task in Package.appxmanifest.
  /// </summary>
  private const string StartupTaskId = "BusyMouseStartup";

  public static bool IsOn => Native.IsPackaged ? StartupTaskIsOn : RunKeyIsOn;

  /// <summary>
  /// Turns it on or off. Leaves it as it was if Windows won't allow the change.
  /// </summary>
  public static void Set (bool on)
  {
    if (Native.IsPackaged)
      SetStartupTask (on);
    else
      SetRunKey (on);
  }

  // ---- Run key

  private static bool RunKeyIsOn
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

  private static void SetRunKey (bool on)
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

  // ---- MSIX startup task

  /// <summary>
  /// The startup task, fetched off the UI thread so waiting on it can't deadlock. Null if Windows won't give it.
  /// </summary>
  private static StartupTask? GetStartupTask ()
  {
    try
    {
      return Task.Run (async () => await StartupTask.GetAsync (StartupTaskId)).GetAwaiter ().GetResult ();
    }
    catch (Exception ex)
    {
      Debug.WriteLine ($"Startup task unavailable: {ex.Message}");

      return null;
    }
  }

  private static bool StartupTaskIsOn =>
    GetStartupTask ()?.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

  private static void SetStartupTask (bool on)
  {
    var task = GetStartupTask ();

    if (task is null)
      return;

    try
    {
      if (on)
      {
        // Asks Windows; it refuses without asking if the user turned it off in Task Manager or Settings > Apps >
        // Startup, and only they can turn it back on there.
        var state = Task.Run (async () => await task.RequestEnableAsync ()).GetAwaiter ().GetResult ();

        if (state == StartupTaskState.DisabledByUser)
          Debug.WriteLine ("Start with Windows was turned off in Settings > Apps > Startup; turn it on there.");
      }
      else
        task.Disable ();
    }
    catch (Exception ex)
    {
      Debug.WriteLine ($"Start with Windows not changed: {ex.Message}");
    }
  }
}
