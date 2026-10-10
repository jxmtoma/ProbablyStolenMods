namespace RealTimeShop;

internal sealed class NativeNoticeQueue
{
    private readonly System.Collections.Generic.List<(string Text, double Age)> pending = new();
    private string hold = "", state = "";
    internal int Count => pending.Count + (state.Length == 0 ? 0 : 1);
    internal void Publish(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || pending.Exists(x => x.Text == text)) return;
        if (pending.Count == 8) pending.RemoveAt(0);
        pending.Add((text, 0));
    }
    internal void SetHold(string reason)
    {
        reason ??= "";
        if (reason == hold) return;
        state = reason.Length > 0 ? reason : "Shop clock resumed.";
        hold = reason;
    }
    internal void Pump(double activeDelta, bool canDisplay, System.Func<string, bool> display)
    {
        if (double.IsNaN(activeDelta) || double.IsInfinity(activeDelta) || activeDelta < 0) activeDelta = 0;
        if (canDisplay)
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var entry = pending[i];
                double age = entry.Age + activeDelta;
                if (age >= 10) pending.RemoveAt(i); else pending[i] = (entry.Text, age);
            }
        if (!canDisplay) return;
        if (pending.Count > 0) { if (display(pending[0].Text)) pending.RemoveAt(0); }
        else if (state.Length > 0 && display(state)) state = "";
    }
    internal void Clear() { pending.Clear(); hold = state = ""; }
}

internal static class NativeHoldText
{
    internal static string Describe(NativePauseInputs input)
    {
        if (!input.InShop) return "";
        if (!input.Focused) return "Shop clock held while the game is unfocused.";
        if (input.ApplicationPaused) return "Shop clock held while the application is paused.";
        if (input.NativeExitPending) return "Shop clock held during the native exit transition.";
        if (input.ExitConfirmationVisible) return "Shop clock held during exit confirmation.";
        if (input.OptionsVisible) return "Shop clock held while options are open.";
        if (input.MenuVisible) return "Shop clock held while the pause menu is open.";
        return "";
    }
}
