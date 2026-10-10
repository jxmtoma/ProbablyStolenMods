namespace RealTimeShop;
internal readonly record struct TableItemFacts(bool? Owned, bool KnownCustomerGoods, bool HasNestedItems, bool NativeCleanupScope=true);
internal readonly record struct TableAssessment(int Owned,int CustomerUnsold,int Unknown,int Nested)
{
    internal bool Safe => Unknown==0 && Nested==0;
}
internal static class TableOwnershipPolicy
{
    internal static TableAssessment Assess(System.Collections.Generic.IEnumerable<TableItemFacts> items,bool ordinary)
    {
        int owned=0,unsold=0,unknown=0,nested=0;
        foreach(var item in items)
            if(item.Owned==true) owned++;
            else if(item.Owned==false && ordinary && item.KnownCustomerGoods && item.NativeCleanupScope)
            { if(item.HasNestedItems) nested++; else unsold++; }
            else unknown++;
        return new(owned,unsold,unknown,nested);
    }
}
internal sealed class CustomerGoodsLedger
{
    private long visit;
    private readonly System.Collections.Generic.Dictionary<long,int> items=new();
    internal void Record(long currentVisit,long pointer,int id)
    {
        if(currentVisit==0 || pointer==0) return;
        if(visit!=currentVisit){Clear();visit=currentVisit;}
        if(items.Count<4096) items[pointer]=id;
    }
    internal bool Known(long currentVisit,long pointer,int id) => currentVisit!=0 && visit==currentVisit && items.TryGetValue(pointer,out int prior) && prior==id;
    internal void Clear(){visit=0;items.Clear();}
}
