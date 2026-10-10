namespace RealTimeShop;

internal sealed record Timing(double DaySeconds = 1800, double PatienceSeconds = 120,
    double GraceSeconds = 60)
{
    public void Validate()
    {
        if (!double.IsFinite(DaySeconds) || DaySeconds < 60 || DaySeconds > 86400 ||
            !double.IsFinite(PatienceSeconds) || PatienceSeconds < 1 || PatienceSeconds > 86400 ||
            !double.IsFinite(GraceSeconds) || GraceSeconds < 0 || GraceSeconds > 86400)
            throw new ArgumentOutOfRangeException(nameof(Timing), "Day: 60-86400 seconds; patience: 1-86400; grace: 0-86400.");
    }
}

internal enum Phase { Untimed, Running, Closing, Closed }

[Flags]
internal enum ClockEvent { None = 0, OneHour = 1, FifteenMinutes = 2, PatienceWarning = 4, Dismiss = 8, Close = 16 }

internal sealed record ShopFrame(string? Customer = null, bool Ready = false, bool Protected = false,
    bool Suspended = false, bool SafeToDismiss = false, bool CanClose = false, ClosingBlocker ClosingBlockers = ClosingBlocker.None, bool ClosingOnlyDismissal = false);

internal sealed record ClockState(string DayKey, Timing Timing, Phase Phase, double Elapsed,
    string? Customer, double PatienceLeft, double GraceLeft, bool PatienceWarned, double? EveningMinute = null);

/// <summary>All time is active session time. The adapter owns game objects and confirms transitions.</summary>
internal sealed class ShopClock
{
    public Timing Timing { get; private set; }
    public string DayKey { get; private set; } = "";
    public Phase Phase { get; private set; }
    public double Elapsed { get; private set; }
    public string? Customer { get; private set; }
    public double PatienceLeft { get; private set; }
    public double GraceLeft { get; private set; }
    public bool PatienceWarned { get; private set; }
    private bool dismissalRequested, closureRequested;
    private string? closingOnlyDismissalRequested;
    public double? EveningMinute { get; private set; }
    public bool SleepRequested { get; private set; }
    public int PendingWakeMinute { get; private set; } = 480;
    public bool SleepDue => Phase == Phase.Closed && EveningMinute >= 1620;

    public ShopClock(Timing timing) { timing.Validate(); Timing = timing; }
    public bool Timed => Phase != Phase.Untimed;
    public bool AdmissionClosed => Phase is Phase.Closing or Phase.Closed;
    public int MinuteOfDay => Phase == Phase.Closed && EveningMinute.HasValue ? (int)Math.Floor(EveningMinute.Value) : 480 + (int)Math.Floor(Math.Min(1, Elapsed / Timing.DaySeconds) * 720);
    public string DisplayTime => $"{(MinuteOfDay / 60) % 24:00}:{MinuteOfDay % 60:00}" + (Phase == Phase.Closed ? " | Closed" : "");

    public void StartDay(string dayKey, int wakeMinute = 480)
    {
        if (string.IsNullOrWhiteSpace(dayKey)) throw new ArgumentException("A save/run/day identity is required.");
        if (DayKey == dayKey && Timed) return;
        if (wakeMinute < 480 || wakeMinute > 660) throw new ArgumentOutOfRangeException(nameof(wakeMinute));
        closingOnlyDismissalRequested = null;
        DayKey = dayKey;
        Phase = Phase.Running;
        Elapsed = Timing.DaySeconds * (wakeMinute - 480) / 720.0;
        EveningMinute = null; SleepRequested = false; PendingWakeMinute = 480;
        GraceLeft = Timing.GraceSeconds;
        ClearCustomer();
        closureRequested = false;
    }

    public void Deactivate()
    {
        EveningMinute = null; SleepRequested = false; PendingWakeMinute = 480;
        closingOnlyDismissalRequested = null;
        DayKey = "";
        Phase = Phase.Untimed;
        Elapsed = 0;
        GraceLeft = 0;
        closureRequested = false;
        ClearCustomer();
    }

    public void ConfirmClosed()
    {
        closingOnlyDismissalRequested = null;
        if (Timed && Phase != Phase.Closed) { EveningMinute = MinuteOfDay; Phase = Phase.Closed; }
        ClearCustomer();
    }

    public void AdvanceEvening(double seconds, bool held)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Phase != Phase.Closed || held || SleepRequested) return;
        EveningMinute = Math.Min(1620, (EveningMinute ?? MinuteOfDay) + seconds * 720 / Timing.DaySeconds);
    }
    public bool RequestSleep(bool nativeOuting)
    {
        if (Phase != Phase.Closed || SleepRequested || EveningMinute == null) return false;
        // Native outings retain their own customer penalty and Short Sleeper handling.
        // Do not stack the new wake delay on the same outing night.
        PendingWakeMinute = nativeOuting ? 480 : Math.Clamp((int)Math.Floor(EveningMinute.Value) - 960, 480, 660);
        SleepRequested = true;
        return true;
    }
    public void CancelSleepRequest() { SleepRequested = false; PendingWakeMinute = 480; }

    public void CustomerDeparted() { closingOnlyDismissalRequested = null; ClearCustomer(); }

    private void ClearCustomer()
    {
        Customer = null;
        PatienceLeft = 0;
        PatienceWarned = false;
        dismissalRequested = false;
    }

    public ClockEvent Advance(double seconds, ShopFrame frame)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Phase is Phase.Untimed or Phase.Closed || frame.Suspended) return ClockEvent.None;

        if (closingOnlyDismissalRequested != frame.Customer) closingOnlyDismissalRequested = null;
        // Observe disappearance even during protected interactions; never carry patience to another visit.
        if (frame.Customer != Customer) ClearCustomer();
        if (frame.Protected) ClearCustomer();
        if (!frame.Protected && frame.Customer != null && frame.Ready && Customer == null)
        {
            Customer = frame.Customer;
            PatienceLeft = Timing.PatienceSeconds;
        }

        var result = ClockEvent.None;
        double previous = Elapsed;
        Elapsed = Math.Min(Timing.DaySeconds, Elapsed + seconds);
        if (previous < Timing.DaySeconds * 11 / 12 && Elapsed >= Timing.DaySeconds * 11 / 12)
            result |= ClockEvent.OneHour;
        if (previous < Timing.DaySeconds * 47 / 48 && Elapsed >= Timing.DaySeconds * 47 / 48)
            result |= ClockEvent.FifteenMinutes;

        double closingSeconds = seconds;
        if (Phase == Phase.Running && Elapsed >= Timing.DaySeconds)
        {
            Phase = Phase.Closing;
            // Only charge the part of this frame that actually falls after closing time.
            closingSeconds = Math.Max(0, previous + seconds - Timing.DaySeconds);
        }
        if (Phase == Phase.Closing) GraceLeft = Math.Max(0, GraceLeft - closingSeconds);

        // Visitor protection prevents destructive actions, independently of active-time accounting.
        // Speech/choice UI, cinematics, tutorial and native pause still suspend all timers above.
        if (frame.Protected)
        {
            if (Phase == Phase.Closing && frame.ClosingOnlyDismissal && frame.Customer != null &&
                frame.Ready && frame.SafeToDismiss && ClosingSafety.Safe(frame.ClosingBlockers) &&
                closingOnlyDismissalRequested != frame.Customer)
            {
                closingOnlyDismissalRequested = frame.Customer;
                result |= ClockEvent.Dismiss;
            }
            return result;
        }

        if (Customer != null && frame.Ready)
        {
            PatienceLeft = Math.Max(0, PatienceLeft - seconds);
            if (!PatienceWarned && PatienceLeft <= Math.Min(30, Timing.PatienceSeconds / 4))
            {
                PatienceWarned = true;
                result |= ClockEvent.PatienceWarning;
            }
            if ((PatienceLeft == 0 || Phase == Phase.Closing) &&
                frame.SafeToDismiss && ClosingSafety.Safe(frame.ClosingBlockers) && !dismissalRequested)
            {
                dismissalRequested = true;
                result |= ClockEvent.Dismiss;
            }
        }
        if (Phase == Phase.Closing && frame.Customer == null && frame.CanClose && ClosingSafety.Safe(frame.ClosingBlockers) && !closureRequested)
        {
            closureRequested = true;
            result |= ClockEvent.Close;
        }
        return result;
    }

    public ClockState Capture() => new(DayKey, Timing, Phase, Elapsed, Customer, PatienceLeft, GraceLeft, PatienceWarned, EveningMinute);

    public bool Restore(ClockState state, string dayKey, string? customer)
    {
        Deactivate();
        try { state.Timing.Validate(); }
        catch (ArgumentException) { return false; }
        if (state.DayKey != dayKey || string.IsNullOrWhiteSpace(dayKey) || state.Customer != customer ||
            !Enum.IsDefined(typeof(Phase), state.Phase) || state.Phase == Phase.Untimed ||
            !InRange(state.Elapsed, state.Timing.DaySeconds) ||
            !InRange(state.PatienceLeft, state.Timing.PatienceSeconds) ||
            !InRange(state.GraceLeft, state.Timing.GraceSeconds) ||
            (state.Phase == Phase.Running && state.Elapsed >= state.Timing.DaySeconds) ||
            (state.Phase == Phase.Closing && state.Elapsed != state.Timing.DaySeconds) ||
            (state.Customer == null && state.PatienceLeft != 0) ||
            (state.Phase == Phase.Closed && state.Customer != null) ||
            (state.EveningMinute.HasValue && (state.Phase != Phase.Closed || !InRange(state.EveningMinute.Value, 1620) ||
                state.EveningMinute.Value < 480 + Math.Floor(state.Elapsed / state.Timing.DaySeconds * 720)))) return false;
        DayKey = dayKey;
        Timing = state.Timing;
        Phase = state.Phase;
        Elapsed = state.Elapsed;
        Customer = customer;
        PatienceLeft = state.PatienceLeft;
        GraceLeft = state.GraceLeft;
        PatienceWarned = state.PatienceWarned;
        // A request is transient. Only the next native save commits a new day.
        EveningMinute = state.Phase == Phase.Closed ? state.EveningMinute ?? MinuteOfDay : null;
        return true;
    }

    private static bool InRange(double value, double maximum) => double.IsFinite(value) && value >= 0 && value <= maximum;
}
