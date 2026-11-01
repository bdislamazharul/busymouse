using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BusyMouse;

/// <summary>
/// Keeps the PC awake. While active it holds a request that keeps the system and the display on and, every
/// so often, nudges the mouse pointer a few pixels and back, so that Windows and presence apps see the user
/// as active. Create it on the app's UI thread and call <see cref="Start"/>, <see cref="Stop"/>,
/// <see cref="Resume"/>, <see cref="Dispose"/> and the <see cref="Suspended"/> setter on that thread too.
/// Everything else can be used from any thread.
/// </summary>
public sealed class AwakeKeeper : IDisposable
{
  // The longest single wait System.Threading.Timer accepts, about 49.7 days.
  private const long MaxTimerWait = 0xFFFFFFFE;

  // Timers can fire a little early, so anything due this soon counts as due.
  private static readonly TimeSpan EarlyAllowance = TimeSpan.FromMilliseconds (500);

  private readonly Lock gate = new ();
  private readonly SynchronizationContext? context;
  private readonly Timer nudgeTimer;
  private readonly Timer deadlineTimer;

  private bool isActive;
  private bool suspended;
  private bool disposed;
  private DateTimeOffset activeSince;
  private DateTimeOffset nextNudgeAt;
  private int nudgeCount;
  private DateTimeOffset? deadline;
  private NudgeMode mode = NudgeMode.Default;
  private int distance = 1;
  private int intervalSeconds = 60;
  private bool randomInterval;
  private (int X, int Y)[] moves;
  private int nextMove;
  private (int X, int Y)? lastPointer;

  /// <summary>
  /// Creates a keeper that isn't active yet. Call it on the app's UI thread: the keep-awake request is made
  /// on this thread, and the events are raised on it.
  /// </summary>
  public AwakeKeeper ()
  {
    this.context = SynchronizationContext.Current;
    this.moves = NudgePattern.Build (this.mode, this.distance);
    this.nudgeTimer = new Timer (this.OnNudgeTimer);
    this.deadlineTimer = new Timer (this.OnDeadlineTimer);
  }

  /// <summary>
  /// Raised after each nudge that actually happened. Nudges skipped because the user moved the pointer
  /// don't raise it.
  /// </summary>
  public event EventHandler? Nudged;

  /// <summary>
  /// Raised after the keeper has stopped itself because <see cref="Deadline"/> came.
  /// </summary>
  public event EventHandler? Expired;

  /// <summary>
  /// True from <see cref="Start"/> until <see cref="Stop"/>, <see cref="Dispose"/> or the deadline.
  /// Still true while suspended.
  /// </summary>
  public bool IsActive
  {
    get
    {
      lock (this.gate)
        return this.isActive;
    }
  }

  /// <summary>
  /// When the current run started, in UTC.
  /// </summary>
  public DateTimeOffset ActiveSince
  {
    get
    {
      lock (this.gate)
        return this.activeSince;
    }
  }

  /// <summary>
  /// When the next nudge is due, in UTC. Meaningless while inactive or suspended.
  /// </summary>
  public DateTimeOffset NextNudgeAt
  {
    get
    {
      lock (this.gate)
        return this.nextNudgeAt;
    }
  }

  /// <summary>
  /// How many nudges the current run has made.
  /// </summary>
  public int NudgeCount
  {
    get
    {
      lock (this.gate)
        return this.nudgeCount;
    }
  }

  /// <summary>
  /// When the keeper should stop by itself, raising <see cref="Expired"/>; null to run until stopped.
  /// Takes effect at once, even mid-run.
  /// </summary>
  public DateTimeOffset? Deadline
  {
    get
    {
      lock (this.gate)
        return this.deadline;
    }
    set
    {
      lock (this.gate)
      {
        this.deadline = value;

        if (this.isActive)
          this.ArmDeadline (DateTimeOffset.UtcNow);
      }
    }
  }

  /// <summary>
  /// Holds off without stopping: no nudges and no keep-awake request, but the run, its start time and its
  /// nudge count carry on. The app sets it while the PC is locked. Set it on the UI thread.
  /// </summary>
  public bool Suspended
  {
    get
    {
      lock (this.gate)
        return this.suspended;
    }
    set
    {
      lock (this.gate)
      {
        if (this.suspended == value)
          return;

        this.suspended = value;

        if (!this.isActive)
          return;

        if (value)
          this.CancelNudge ();
        else
          this.ScheduleNudge (DateTimeOffset.UtcNow);
      }

      if (value)
        AllowSleep ();
      else
        KeepAwake ();
    }
  }

  /// <summary>
  /// How the pointer is nudged. A change starts the new mode's sequence from the beginning at the next
  /// nudge. Defaults to <see cref="NudgeMode.Default"/>.
  /// </summary>
  public NudgeMode Mode
  {
    get
    {
      lock (this.gate)
        return this.mode;
    }
    set
    {
      if (!Enum.IsDefined (value))
        throw new ArgumentOutOfRangeException (nameof (value), value, "Not a known nudge mode.");

      lock (this.gate)
      {
        if (this.mode == value)
          return;

        this.mode = value;
        this.RestartPattern ();
      }
    }
  }

  /// <summary>
  /// How far a nudge moves the pointer, in units of 3 pixels (for Orbit, the radius). At least 1; defaults
  /// to 1. A change takes effect from the next nudge, starting the sequence from the beginning.
  /// </summary>
  public int Distance
  {
    get
    {
      lock (this.gate)
        return this.distance;
    }
    set
    {
      ArgumentOutOfRangeException.ThrowIfLessThan (value, 1);

      lock (this.gate)
      {
        if (this.distance == value)
          return;

        this.distance = value;
        this.RestartPattern ();
      }
    }
  }

  /// <summary>
  /// Seconds between nudges. At least 1; defaults to 60. Lowering it brings a pending nudge forward if it
  /// would otherwise come later than the new interval from now. Raising it applies from the next wait.
  /// </summary>
  public int IntervalSeconds
  {
    get
    {
      lock (this.gate)
        return this.intervalSeconds;
    }
    set
    {
      ArgumentOutOfRangeException.ThrowIfLessThan (value, 1);

      lock (this.gate)
      {
        var lowered = value < this.intervalSeconds;
        this.intervalSeconds = value;

        if (!lowered || !this.isActive || this.suspended)
          return;

        var now = DateTimeOffset.UtcNow;
        var latest = now.AddSeconds (value);

        if (this.nextNudgeAt > latest)
        {
          this.nextNudgeAt = latest;
          this.ArmNudgeTimer (now);
        }
      }
    }
  }

  /// <summary>
  /// When true, each wait is a whole number of seconds picked at random from 1 to
  /// <see cref="IntervalSeconds"/>. Applies from the next wait. Defaults to false.
  /// </summary>
  public bool RandomInterval
  {
    get
    {
      lock (this.gate)
        return this.randomInterval;
    }
    set
    {
      lock (this.gate)
        this.randomInterval = value;
    }
  }

  /// <summary>
  /// Starts a new run: resets the nudge count, schedules the first nudge, arms the deadline and asks Windows
  /// to keep the system and display awake (no nudges or request while suspended). Does nothing if already
  /// active. Call it on the UI thread.
  /// </summary>
  public void Start ()
  {
    bool keepAwake;

    lock (this.gate)
    {
      ObjectDisposedException.ThrowIf (this.disposed, this);

      if (this.isActive)
        return;

      var now = DateTimeOffset.UtcNow;

      this.isActive = true;
      this.activeSince = now;
      this.nudgeCount = 0;
      this.nextMove = 0;

      if (!this.suspended)
        this.ScheduleNudge (now);

      this.ArmDeadline (now);
      keepAwake = !this.suspended;
    }

    if (keepAwake)
      KeepAwake ();
  }

  /// <summary>
  /// Ends the run: cancels the pending nudge and the deadline and lets Windows sleep again. Does nothing if
  /// not active. Raises no event. Call it on the UI thread.
  /// </summary>
  public void Stop ()
  {
    lock (this.gate)
    {
      if (!this.isActive)
        return;

      this.StopLocked ();
    }

    AllowSleep ();
  }

  /// <summary>
  /// Call after the PC wakes from sleep. Some PCs drop the keep-awake request and the timers while asleep;
  /// this puts them back, nudging at once if a nudge fell due meanwhile. Does nothing if not active. Call it
  /// on the UI thread.
  /// </summary>
  public void Resume ()
  {
    bool keepAwake;

    lock (this.gate)
    {
      if (!this.isActive)
        return;

      var now = DateTimeOffset.UtcNow;

      if (!this.suspended)
        this.ArmNudgeTimer (now);

      this.ArmDeadline (now);
      keepAwake = !this.suspended;
    }

    if (keepAwake)
      KeepAwake ();
  }

  /// <summary>
  /// Stops the keeper and releases its timers. Safe to call more than once. Call it on the UI thread.
  /// </summary>
  public void Dispose ()
  {
    bool wasActive;

    lock (this.gate)
    {
      if (this.disposed)
        return;

      this.disposed = true;
      wasActive = this.isActive;

      if (wasActive)
        this.StopLocked ();

      this.nudgeTimer.Dispose ();
      this.deadlineTimer.Dispose ();
    }

    if (wasActive)
      AllowSleep ();
  }

  private void OnNudgeTimer (object? state)
  {
    try
    {
      if (this.NudgeIfDue ())
        this.Post (() => this.Nudged?.Invoke (this, EventArgs.Empty));
    }
    catch (Exception ex)
    {
      // An exception escaping a thread-pool callback would end the process.
      Debug.WriteLine ($"Nudge failed: {ex}");
    }
  }

  /// <summary>
  /// Makes the pending nudge if it's due and schedules the next one. Returns true if a nudge happened.
  /// </summary>
  private bool NudgeIfDue ()
  {
    lock (this.gate)
    {
      if (!this.isActive || this.suspended)
        return false;

      var now = DateTimeOffset.UtcNow;

      // Not due: a callback already queued when the nudge was rescheduled, or a wait longer than the timer
      // can do in one go. Wait out the rest.
      if (now + EarlyAllowance < this.nextNudgeAt)
      {
        this.ArmNudgeTimer (now);
        return false;
      }

      // One-shot, so it leaves nothing behind on this pool thread. Covers a lapsed continuous request.
      SetExecutionState (NativeMethods.ES_SYSTEM_REQUIRED | NativeMethods.ES_DISPLAY_REQUIRED);

      var nudged = !this.UserMovedPointer () && this.MovePointer ();

      this.ScheduleNudge (DateTimeOffset.UtcNow);
      return nudged;
    }
  }

  /// <summary>
  /// True if the pointer is somewhere other than where it was when this nudge was scheduled. If either
  /// position couldn't be read, assumes it hasn't moved.
  /// </summary>
  private bool UserMovedPointer ()
  {
    var current = ReadPointer ();

    return this.lastPointer is not null && current is not null && current != this.lastPointer;
  }

  /// <summary>
  /// Sends the next move in the sequence. Returns false, leaving the sequence where it was, if Windows
  /// refused the input.
  /// </summary>
  private bool MovePointer ()
  {
    var (dx, dy) = this.moves[this.nextMove];

    if (!SendMove (dx, dy))
      return false;

    this.nextMove = (this.nextMove + 1) % this.moves.Length;
    this.nudgeCount++;
    return true;
  }

  /// <summary>
  /// Starts a fresh wait for the next nudge. Also notes where the pointer is now, after any nudge just
  /// made, so the keeper's own moves are never taken for the user's.
  /// </summary>
  private void ScheduleNudge (DateTimeOffset now)
  {
    var seconds = this.randomInterval ? Random.Shared.NextInt64 (1, this.intervalSeconds + 1L) : this.intervalSeconds;

    this.nextNudgeAt = now.AddSeconds (seconds);
    this.lastPointer = ReadPointer ();
    this.ArmNudgeTimer (now);
  }

  private void ArmNudgeTimer (DateTimeOffset now)
  {
    this.nudgeTimer.Change (TimerWait (this.nextNudgeAt - now), Timeout.Infinite);
  }

  private void CancelNudge ()
  {
    this.nudgeTimer.Change (Timeout.Infinite, Timeout.Infinite);
  }

  private void ArmDeadline (DateTimeOffset now)
  {
    if (this.deadline is { } due)
      this.deadlineTimer.Change (TimerWait (due - now), Timeout.Infinite);
    else
      this.deadlineTimer.Change (Timeout.Infinite, Timeout.Infinite);
  }

  private void RestartPattern ()
  {
    this.moves = NudgePattern.Build (this.mode, this.distance);
    this.nextMove = 0;
  }

  /// <summary>
  /// Ends the run and cancels both timers. The caller clears the keep-awake request once out of the lock.
  /// </summary>
  private void StopLocked ()
  {
    this.isActive = false;
    this.CancelNudge ();
    this.deadlineTimer.Change (Timeout.Infinite, Timeout.Infinite);
  }

  private void OnDeadlineTimer (object? state)
  {
    try
    {
      lock (this.gate)
      {
        if (!this.isActive || this.deadline is not { } due)
          return;

        var now = DateTimeOffset.UtcNow;

        // Not due: a deadline further off than the timer can wait in one go, or one moved since the timer
        // fired. Wait out the rest.
        if (now + EarlyAllowance < due)
        {
          this.ArmDeadline (now);
          return;
        }
      }

      // Stopping clears the keep-awake request, which only works on the thread that made it.
      this.Post (this.ExpireIfDue);
    }
    catch (Exception ex)
    {
      // An exception escaping a thread-pool callback would end the process.
      Debug.WriteLine ($"Deadline check failed: {ex}");
    }
  }

  /// <summary>
  /// Runs on the owning thread. Stops and raises <see cref="Expired"/> if the run is still going and its
  /// deadline has really passed; things may have changed while this waited to run.
  /// </summary>
  private void ExpireIfDue ()
  {
    lock (this.gate)
    {
      if (!this.isActive || this.deadline is not { } due || DateTimeOffset.UtcNow + EarlyAllowance < due)
        return;

      this.StopLocked ();
    }

    AllowSleep ();
    this.Expired?.Invoke (this, EventArgs.Empty);
  }

  /// <summary>
  /// Runs the action on the owning thread through its synchronization context, or right here if it had none.
  /// </summary>
  private void Post (Action action)
  {
    if (this.context is null)
      action ();
    else
      this.context.Post (static state => ((Action)state!) (), action);
  }

  private static long TimerWait (TimeSpan wait)
  {
    return Math.Clamp ((long)Math.Ceiling (wait.TotalMilliseconds), 0, MaxTimerWait);
  }

  private static void KeepAwake ()
  {
    SetExecutionState (NativeMethods.ES_CONTINUOUS | NativeMethods.ES_SYSTEM_REQUIRED | NativeMethods.ES_DISPLAY_REQUIRED);
  }

  private static void AllowSleep ()
  {
    SetExecutionState (NativeMethods.ES_CONTINUOUS);
  }

  private static void SetExecutionState (uint flags)
  {
    if (NativeMethods.SetThreadExecutionState (flags) == 0)
      Debug.WriteLine ($"SetThreadExecutionState (0x{flags:X8}) failed: error {Marshal.GetLastPInvokeError ()}");
  }

  private static (int X, int Y)? ReadPointer ()
  {
    if (NativeMethods.GetCursorPos (out var point))
      return (point.X, point.Y);

    Debug.WriteLine ($"GetCursorPos failed: error {Marshal.GetLastPInvokeError ()}");
    return null;
  }

  private static unsafe bool SendMove (int dx, int dy)
  {
    var input = new NativeMethods.INPUT
    {
      type = NativeMethods.INPUT_MOUSE,
      mi = new NativeMethods.MOUSEINPUT { dx = dx, dy = dy, dwFlags = NativeMethods.MOUSEEVENTF_MOVE },
    };

    if (NativeMethods.SendInput (1, &input, sizeof (NativeMethods.INPUT)) != 0)
      return true;

    Debug.WriteLine ($"SendInput failed: error {Marshal.GetLastPInvokeError ()}");
    return false;
  }
}
