namespace RealTimeShop;

internal readonly record struct CustomerFacts(string Identifier, string Intent, bool Dismissable,
    bool Security, bool ScriptedFactory, bool ArrivalCallback, bool DismissCallback)
{
    internal string Reason => Identifier.Length == 0 ? "no-customer-id" : !Dismissable ? "non-dismissible" :
        Security ? "security" : ScriptedFactory ? "scripted-factory" : ArrivalCallback ? "arrival-callback" :
        DismissCallback ? "dismiss-callback" : Intent is "BUY" or "SELL" or "SELLNBUY" or "BARTER" or "WHOLESALE" or "APPRAISAL_SERVICE" ? "ordinary" : "unsupported-intent";
}
internal readonly record struct CustomerObservation(long Visit, CustomerFacts Facts, bool Ready, bool Suspended,
    bool Protected, bool SafeToDismiss, string Phase, double Elapsed, double PatienceLeft, double GraceLeft, string HoldReason = "", bool ClosingOnlyDismissal = false);
internal sealed class CustomerDiagnostics
{
    private readonly int limit;
    private int emitted;
    private string previous = "";
    private double lastElapsed = double.NegativeInfinity;
    internal CustomerDiagnostics(int limit) { this.limit = System.Math.Max(0, limit); }
    internal bool Take() => emitted < limit && ++emitted <= limit;
    internal string? Observe(CustomerObservation value)
    {
        string key = $"visit={value.Visit}; id={value.Facts.Identifier}; intent={value.Facts.Intent}; classification={value.Facts.Reason}; dismissable={value.Facts.Dismissable}; security={value.Facts.Security}; scripted={value.Facts.ScriptedFactory}; arrivalCallback={value.Facts.ArrivalCallback}; dismissCallback={value.Facts.DismissCallback}; ready={value.Ready}; suspended={value.Suspended}; protected={value.Protected}; safe={value.SafeToDismiss}; phase={value.Phase}";
        key += "; closingOnly=" + value.ClosingOnlyDismissal + "; hold=" + value.HoldReason;
        if (key == previous && value.Elapsed >= lastElapsed && value.Elapsed - lastElapsed < 15) return null;
        previous = key; lastElapsed = value.Elapsed;
        return key + $"; elapsed={value.Elapsed:0.###}; patience={value.PatienceLeft:0.###}; grace={value.GraceLeft:0.###}";
    }
}
