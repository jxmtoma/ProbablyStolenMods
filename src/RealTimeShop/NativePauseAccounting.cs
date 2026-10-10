namespace RealTimeShop;

internal readonly record struct NativePauseInputs(bool InShop, bool Focused, bool ApplicationPaused,
    bool MenuVisible, bool OptionsVisible, bool ExitConfirmationVisible, bool NativeExitPending)
{
    internal bool Held => InShop && (!Focused || ApplicationPaused || MenuVisible || OptionsVisible ||
        ExitConfirmationVisible || NativeExitPending);
}

internal sealed class NativePauseAccounting
{
    private bool held;
    private int? resumeFrame;
    internal bool Observe(NativePauseInputs inputs, int frame)
    {
        if (!inputs.InShop) { held = false; resumeFrame = null; return false; }
        bool next = inputs.Held;
        if (held && !next) resumeFrame = frame;
        held = next;
        return next;
    }
    internal bool HoldsFrame(int frame) => held || resumeFrame == frame;
}

