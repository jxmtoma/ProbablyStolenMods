using System.Security.Cryptography;
using System.Text.Json;

namespace RealTimeShop;

// Installed capability and consent for a particular native save are separate states.
internal sealed class SaveOptInSession
{
    internal bool PendingCreation { get; private set; }
    internal bool PendingEnabled { get; private set; }
    private bool active;
    private int slot = -1;
    private string run = "";
    internal void Reset() { active=false; slot=-1; run=""; PendingCreation=false; PendingEnabled=false; }
    internal void BeginCreation(bool explicitlyEnabled)
    { Reset(); PendingCreation=true; PendingEnabled=explicitlyEnabled; }
    internal bool Matches(int nativeSlot, string? nativeRun) => active && slot==nativeSlot && run==nativeRun;
    internal void CompleteCreation(int nativeSlot, string nativeRun)
    {
        bool enabled=PendingCreation && PendingEnabled;
        Reset();
        if(enabled) Restore(true,nativeSlot,nativeRun);
    }
    internal void Restore(bool validConsentAndClock, int nativeSlot, string nativeRun)
    {
        Reset();
        if(!validConsentAndClock) return;
        _=SaveIdentity.Key(nativeSlot,nativeRun,0);
        slot=nativeSlot; run=nativeRun; active=true;
    }
}

internal sealed class CreationChoice
{
    private bool armed, enabled;
    internal void Clear() { armed=false; enabled=false; }
    internal void Select(bool explicitlyEnabled) { armed=true; enabled=explicitlyEnabled; }
    internal bool Consume() { bool result=armed && enabled; Clear(); return result; }
}

internal static class SaveOptInStore
{
    private const string Origin="explicit-new-game-v1";
    private sealed record Consent(int Version, string Origin, bool Enabled, int Slot, string Run, string NativeHash);
    internal static string PathFor(string nativeSave) => nativeSave+".realtime-choice.json";
    private static string Hash(string path)
    { using var file=File.OpenRead(path); using var sha=SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(file)); }
    internal static void Write(string nativeSave, int slot, string run, bool explicitlyAuthorized)
    {
        if(!explicitlyAuthorized) throw new InvalidOperationException("No explicit new-game opt-in.");
        _=SaveIdentity.Key(slot,run,0);
        string path=PathFor(nativeSave),temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            byte[] data=JsonSerializer.SerializeToUtf8Bytes(new Consent(1,Origin,true,slot,run,Hash(nativeSave)));
            using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            { file.Write(data); file.Flush(true); }
            File.Move(temp,path,true);
        }
        finally { if(File.Exists(temp)) File.Delete(temp); }
    }
    internal static bool TryRead(string nativeSave, int slot, string run)
    {
        try
        {
            _=SaveIdentity.Key(slot,run,0);
            string path=PathFor(nativeSave);
            if(!File.Exists(path) || new FileInfo(path).Length>4096) return false;
            var saved=JsonSerializer.Deserialize<Consent>(File.ReadAllText(path));
            return saved?.Version==1 && saved.Origin==Origin && saved.Enabled && saved.Slot==slot &&
                saved.Run==run && saved.NativeHash==Hash(nativeSave);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or InvalidOperationException or NotSupportedException)
        { return false; }
    }
}
