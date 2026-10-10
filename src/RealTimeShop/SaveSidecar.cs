using System.Security.Cryptography;
using System.Text.Json;

namespace RealTimeShop;

internal static class SaveSidecar
{
    private sealed record Envelope(int Version, string NativeHash, ClockState Clock);
    public static string PathFor(string nativeSave) => nativeSave + ".realtime-shop.json";
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    // Call only after the game's native write has successfully completed.
    public static void Write(string nativeSave, ShopClock clock)
    {
        string path = PathFor(nativeSave);
        if (!clock.Timed)
        {
            // Never leave an old matching sidecar behind after an untimed save.
            File.Delete(path);
            return;
        }
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(new Envelope(2, Hash(nativeSave), clock.Capture()));
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(json);
                file.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static bool TryRestore(string nativeSave, ShopClock clock, string dayKey, string? customer, out string reason)
    {
        clock.Deactivate();
        reason = "No matching timer save; timing begins next morning.";
        try
        {
            string path = PathFor(nativeSave);
            if (!File.Exists(path) || new FileInfo(path).Length > 16384) return false;
            var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path));
            if (envelope == null || envelope.Version is not (1 or 2) || envelope.Clock?.Timing == null || envelope.NativeHash != Hash(nativeSave) ||
                !clock.Restore(envelope.Clock, dayKey, customer)) return false;
            reason = "Timers restored.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            reason = "Timer save unavailable (" + ex.GetType().Name + "); timing begins next morning.";
            return false;
        }
    }
}
