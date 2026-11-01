# BusyMouse.Core: behaviour spec

BusyMouse is a Windows tray app that keeps a PC awake. Its WinUI 3 front end is done. It needs a small
class library, `BusyMouse.Core`, that does the actual work: it holds a keep-awake request and, every so
often, nudges the mouse pointer by a few pixels and back, so that Windows and chat or presence apps see the
user as active.

This document is the whole brief. Build the library from it alone.

## Project

- Folder: `D:\Apps\busymouse-core\BusyMouse.Core\` (WSL path `/mnt/d/apps/busymouse-core/BusyMouse.Core/`).
- `BusyMouse.Core.csproj`, SDK style:
  - `TargetFramework` `net10.0-windows10.0.19041.0`, `SupportedOSPlatformVersion` `10.0.17763.0`
  - `Nullable` enable, `ImplicitUsings` enable, `AllowUnsafeBlocks` true, `Platforms` `x64;ARM64`
  - `RootNamespace` `BusyMouse`
- No NuGet packages. Call Win32 with `[LibraryImport]` (source-generated P/Invoke) in an internal static
  partial class.
- Namespace `BusyMouse` for everything.
- The public surface is exactly what's below. Anything else is `internal` or `private`.

## Public API

```csharp
namespace BusyMouse;

public enum NudgeMode { Default, Diagonal, Quiet, Orbit }   // in this order; the app lays its mode tiles out the same way

public sealed class AwakeKeeper : IDisposable
{
  public AwakeKeeper ();

  public event EventHandler? Nudged;    // after each nudge that actually happened
  public event EventHandler? Expired;   // after the keeper stopped itself because Deadline came

  public bool IsActive { get; }
  public DateTimeOffset ActiveSince { get; }     // when the current run started (UTC)
  public DateTimeOffset NextNudgeAt { get; }     // when the next nudge is due (UTC); meaningless while inactive or suspended
  public int NudgeCount { get; }                 // nudges made in the current run

  public DateTimeOffset? Deadline { get; set; }  // stop by itself at this time; null = run until stopped
  public bool Suspended { get; set; }            // hold off without stopping (the app sets it while the PC is locked)

  public NudgeMode Mode { get; set; }            // default Default
  public int Distance { get; set; }              // >= 1, default 1; scales how far a nudge moves
  public int IntervalSeconds { get; set; }       // >= 1, default 60; seconds between nudges
  public bool RandomInterval { get; set; }       // default false

  public void Start ();
  public void Stop ();
  public void Resume ();                         // call after the PC wakes from sleep
  public void Dispose ();
}
```

Setting `Distance` or `IntervalSeconds` below 1 throws `ArgumentOutOfRangeException`.

## Behaviour

### Keeping the PC awake

- While the keeper is active and not suspended, it holds a continuous request that keeps both the system
  and the display awake (`SetThreadExecutionState` with `ES_CONTINUOUS | ES_SYSTEM_REQUIRED |
  ES_DISPLAY_REQUIRED`). When it stops or is suspended, it clears the request (`ES_CONTINUOUS` alone).
- That continuous request belongs to the thread that makes it. So the keeper makes and clears it only on
  the thread that constructed it (the app's UI thread). `Start`, `Stop`, `Resume` and the `Suspended`
  setter are always called on that thread. Anything that has to happen on a timer thread and changes the
  request (stopping at the deadline) must be posted back to that thread (see Threading).
- At each due nudge, before deciding whether to move the pointer, it also resets the idle timers once with
  a one-shot call (`ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED` without `ES_CONTINUOUS`). That call leaves
  nothing behind, so it's fine from a timer thread. It guards against the continuous request having
  lapsed.

### Nudging

- A nudge is one relative pointer move sent as synthetic mouse input (`SendInput`, one `INPUT_MOUSE`
  with `MOUSEEVENTF_MOVE`, relative dx/dy).
- Each mode has its own repeating sequence of moves. Successive nudges walk through the sequence, one move
  per nudge, and wrap around. Over one full sequence the moves add up to zero, so the pointer ends where it
  started. Let `s = 3 * Distance` pixels:
  - Diagonal: diagonal, down-right by (s, s), then back by (-s, -s).
  - Quiet: a single move of (0, 0). Windows counts it as input, but the pointer doesn't move.
  - Orbit: a small closed loop of 12 moves around a circle of radius about s. Work the steps out from
    evenly spaced points on the circle, rounding to whole pixels in a way that still sums exactly to zero
    over the loop. For example, take the rounded points first, then the differences between them.
  - Default: horizontal, right by (s, 0), then back by (-s, 0).
- Changing `Mode` or `Distance` takes effect from the next nudge, starting again from the first move of the
  new sequence.

### Not fighting the user

- If the user has moved the pointer since the keeper last looked, a due nudge is skipped: no move, not
  counted, no `Nudged` event. The keeper still schedules the next one.
- "Last looked" means: the keeper records the pointer position (`GetCursorPos`) every time it schedules a
  nudge, which is after any move it just made. So its own nudges are never mistaken for the user's. At a
  due nudge it compares the current position with that record.
- If the position can't be read, treat it as not moved and go ahead with the nudge.

### Timing

- The wait before each nudge is `IntervalSeconds`. With `RandomInterval`, it's instead a whole number of
  seconds picked uniformly from 1 to `IntervalSeconds`, freshly for each wait.
- `NextNudgeAt` is the moment the pending nudge is due.
- If `IntervalSeconds` is lowered while active and not suspended, and the pending nudge is due later than
  now plus the new interval, bring it forward to now plus the new interval. Raising the interval doesn't
  move the pending nudge; the new value applies from the next wait. `RandomInterval` likewise applies from
  the next wait.

### Starting and stopping

- `Start`: does nothing if already active. Otherwise it becomes active, sets `ActiveSince` to now, resets
  `NudgeCount` to 0, starts each mode's sequence from its first move, schedules the first nudge (unless
  suspended), arms the deadline, and makes the keep-awake request (unless suspended).
- `Stop`: does nothing if inactive. Otherwise it cancels the pending nudge and the deadline and clears the
  keep-awake request. No event is raised.
- `Deadline`: can be set at any time and takes effect at once. While active, setting it re-arms (or with
  null, disarms) the deadline timer. When the deadline arrives, the keeper stops itself on the owning
  thread and then raises `Expired`. Re-check there that it's still active and the deadline has really
  passed (allow up to 0.5 s early, since timers can fire a little early). If not, do nothing.
- `Suspended`: can be set at any time.
  - Setting it to the value it already has does nothing.
  - Setting it true while active cancels the pending nudge and clears the keep-awake request.
  - Setting it false while active schedules a fresh nudge (a full wait from now) and makes the request
    again.
  - Setting it while inactive just remembers it for the next `Start`.
  - It doesn't stop the run, `ActiveSince` or `NudgeCount`.
- `Resume`: when active, makes the keep-awake request again (unless suspended). It re-arms the nudge timer
  for the time left until `NextNudgeAt` (at once if that's already past; not while suspended), and re-arms
  the deadline. Some Modern Standby PCs silently drop both timers and the request during sleep. When
  inactive, it does nothing.
- `Dispose`: stops, then disposes the timers. Safe to call more than once.

### Threading

- The constructor captures `SynchronizationContext.Current` (it may be null). `Nudged` and `Expired` are
  raised through that context with `Post` when there is one, or inline when there isn't. Work that has to
  run on the owning thread (the deadline stop) is posted the same way.
- Timers are `System.Threading.Timer` (thread-pool callbacks), single-shot and re-armed each time.
- All state is guarded by one lock (`System.Threading.Lock`). Every public getter can be called from any
  thread and returns a consistent value. Don't raise events or make the continuous keep-awake call while
  holding the lock.
- A timer callback must never throw. Win32 failures (zero return values) are written with `Debug.WriteLine`
  (including `Marshal.GetLastPInvokeError ()`) and otherwise ignored.

## Code style

Match the app's code, which looks like this:

```csharp
internal static class Autostart
{
  private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

  /// <summary>
  /// Turns it on or off. Leaves it as it was if Windows won't allow the change.
  /// </summary>
  public static void Set (bool on)
  {
    try
    {
      using var key = Registry.CurrentUser.CreateSubKey (RunKey);

      if (on)
        key.SetValue (ValueName, $"\"{Environment.ProcessPath}\" -m");
      else
        key.DeleteValue (ValueName, false);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
    {
      Debug.WriteLine ($"Start with Windows not changed: {ex.Message}");
    }
  }
}
```

- Two-space indents, Allman braces.
- A space before the parentheses of every call and declaration: `Foo (x)`, `new Timer (...)`.
- `this.` on instance members.
- No braces around single-statement `if` bodies.
- File-scoped namespace.
- `<summary>` comments on public members, written plainly for someone using the class. Comments elsewhere
  only where the reason isn't obvious.
- One type per file, named after the type.
