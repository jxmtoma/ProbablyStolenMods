using System.Globalization;

namespace RealTimeShop;

internal static class SaveIdentity
{
    internal static string Key(int slot, string? run, int day)
    {
        if (slot < 0 || day < 0 || !Guid.TryParseExact(run, "D", out var guid) || guid == Guid.Empty)
            throw new InvalidOperationException("Native save identity has not been allocated.");
        return slot.ToString(CultureInfo.InvariantCulture) + "/" + run + "/" + day.ToString(CultureInfo.InvariantCulture);
    }

    internal static void RequireMatchingClock(ShopClock clock, string nativeKey)
    {
        if (clock.Timed && !string.Equals(clock.DayKey, nativeKey, StringComparison.Ordinal))
            throw new InvalidOperationException("Timer identity differs from the native save; sidecar write refused.");
    }
}
