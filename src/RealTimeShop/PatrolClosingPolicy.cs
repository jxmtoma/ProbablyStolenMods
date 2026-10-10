namespace RealTimeShop;

internal readonly record struct PatrolClosingFacts(bool NativeFactory, string Identifier, string Intent,
    bool Security, bool Scripted, bool SpecialOfficer, bool DismissCallback, bool NativeDismissable,
    bool Ready, bool IntroductionComplete, bool DialogueQuiet, bool InspectionQuiet, bool NativeQuiet, ClosingBlocker Blockers);

internal static class PatrolClosingPolicy
{
    // This is a closing-only exception. Patrols keep their security callbacks and never get patience.
    internal static bool Allows(PatrolClosingFacts f) => f.NativeFactory && f.Identifier == "patrolOfficer" &&
        f.Intent == "BUY" && f.Security && !f.Scripted && !f.SpecialOfficer && !f.DismissCallback &&
        f.NativeDismissable && f.Ready && f.IntroductionComplete && f.DialogueQuiet && f.InspectionQuiet && f.NativeQuiet &&
        ClosingSafety.Safe(f.Blockers);
}

internal readonly record struct PatrolInspectionFacts(bool PanelsKnown, bool PanelVisible,
    bool ConfrontVisible, bool AppraisalVisible, bool ItemSelected, bool SecurityAlarm);
internal static class PatrolInspectionSafety
{
    internal static bool Quiet(PatrolInspectionFacts f) => f.PanelsKnown && !f.PanelVisible &&
        !f.ConfrontVisible && !f.AppraisalVisible && !f.ItemSelected && !f.SecurityAlarm;
}

/// <summary>Receipts belong to a native visit, never to a reusable customer identifier or save.</summary>
internal sealed class PatrolVisitGate
{
    private long visit;
    private int arrivalDepth, actionDepth;
    private bool arrivalComplete, faulted;
    private readonly HashSet<long> pendingActions = new();
    internal void Clear()
    { visit=0; arrivalDepth=actionDepth=0; arrivalComplete=faulted=false; pendingActions.Clear(); }
    internal void BeginArrival(long token)
    {
        if(token==0) return;
        if(visit!=token) { Clear(); visit=token; }
        arrivalComplete=false; arrivalDepth++;
    }
    internal void EndArrival(long token, bool success)
    {
        if(token==0 || token!=visit) return;
        if(arrivalDepth==0) { faulted=true; return; }
        arrivalDepth--; faulted |= !success;
        if(arrivalDepth==0 && success) arrivalComplete=true;
    }
    internal void ScheduleAction(long token, long action)
    {
        if(token==0 || token!=visit || action==0) return;
        if(pendingActions.Count>=256 && !pendingActions.Contains(action)) { faulted=true; return; }
        pendingActions.Add(action);
    }
    internal void BeginAction(long token)
    { if(token!=0 && token==visit) actionDepth++; }
    internal void EndAction(long token, long action, bool success)
    {
        if(token==0 || token!=visit) return;
        if(actionDepth==0) { faulted=true; return; }
        actionDepth--; faulted |= !success;
        if(success) pendingActions.Remove(action);
    }
    internal void Fault(long token) { if(token!=0 && token==visit) faulted=true; }
    internal bool Settled(long token) => token!=0 && token==visit && arrivalComplete &&
        arrivalDepth==0 && actionDepth==0 && pendingActions.Count==0 && !faulted;
}
