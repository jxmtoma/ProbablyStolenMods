namespace RealTimeShop;

[System.Flags]
internal enum ClosingBlocker
{
    None=0, Paused=1, Transition=2, Animation=4, Confirmation=8, Negotiation=16,
    Dragging=32, Dialogue=64, UnknownTable=128, TableItems=256, TradeCommit=512
}
internal static class ClosingSafety
{
    internal static bool Safe(ClosingBlocker blockers) => blockers == ClosingBlocker.None;
}

internal readonly record struct NegotiationFacts(bool PanelsKnown, bool OfferVisible,
    bool ConfirmationVisible, bool BargainVisible, bool NativeQuiet, bool SelectionKnown);
internal static class NegotiationSafety
{
    // Hidden presentation is insufficient: native work and selection provenance must also be known.
    internal static bool Held(NegotiationFacts facts) => !facts.PanelsKnown || facts.OfferVisible ||
        facts.ConfirmationVisible || facts.BargainVisible || !facts.NativeQuiet || !facts.SelectionKnown;
}
