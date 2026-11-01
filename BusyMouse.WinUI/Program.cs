using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace BusyMouse.WinUI;

public static class Program
{
  // Held by the running copy, so a second launch hands over to it instead of starting another.
  private const string InstanceMutexName = "single instance: BusyMouse";

  /// <summary>
  /// Set by "--stop-nudging" to ask the running copy to stop keeping awake.
  /// </summary>
  internal const string StopEventName = "BusyMouse.StopNudgingRequest";

  /// <summary>
  /// Set by a second launch to ask the running copy to show its window.
  /// </summary>
  internal const string ShowEventName = "BusyMouse.ShowWindowRequest";

  [STAThread]
  private static int Main (string[] args)
  {
    var options = StartupOptions.Parse (args, out var error);

    if (error is not null)
    {
      Report (error, true);

      return 1;
    }

    if (options.Help)
    {
      Report (StartupOptions.HelpText, false);

      return 0;
    }

    if (options.StopNudging)
    {
      if (Signal (StopEventName))
        return 0;

      Report ("BusyMouse isn't running, so there's nothing to stop.", true);

      return 1;
    }

    using var instance = new Mutex (false, InstanceMutexName);
    bool owned;

    try
    {
      owned = instance.WaitOne (0);
    }
    catch (AbandonedMutexException)
    {
      // The last copy crashed; the mutex is ours now.
      owned = true;
    }

    if (!owned)
    {
      // Already running: bring that copy's window up instead. It may take the foreground from us to do so.
      _ = Native.AllowSetForegroundWindow (Native.ASFW_ANY);
      _ = Signal (ShowEventName);

      return 0;
    }

    try
    {
      App.Options = options;

      WinRT.ComWrappersSupport.InitializeComWrappers ();
      Application.Start (static _ =>
      {
        var context = new DispatcherQueueSynchronizationContext (DispatcherQueue.GetForCurrentThread ());
        SynchronizationContext.SetSynchronizationContext (context);
        new App ();
      });
    }
    finally
    {
      instance.ReleaseMutex ();
    }

    return 0;
  }

  private static bool Signal (string name)
  {
    if (!EventWaitHandle.TryOpenExisting (name, out var handle))
      return false;

    using (handle)
      return handle.Set ();
  }

  /// <summary>
  /// Writes to the console the app was started from, or shows a message box when there isn't one.
  /// </summary>
  private static void Report (string text, bool isError)
  {
    if (Native.AttachConsole (Native.ATTACH_PARENT_PROCESS) != 0)
    {
      try
      {
        var writer = isError ? Console.Error : Console.Out;
        writer.WriteLine ();
        writer.WriteLine (text);
      }
      finally
      {
        _ = Native.FreeConsole ();
      }

      return;
    }

    _ = Native.MessageBoxW (0, text, "BusyMouse", isError ? Native.MB_ICONERROR : Native.MB_ICONINFORMATION);
  }
}
