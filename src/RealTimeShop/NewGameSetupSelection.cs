namespace RealTimeShop;

internal sealed class NewGameSetupSelection
{
    internal long Generation { get; private set; }
    private long menu, data;
    private bool open, selected, starting;
    internal bool Enabled => open && selected;
    internal bool Starting => starting;
    internal void Clear() { Generation++; menu=0; data=0; open=selected=starting=false; }
    internal long Begin(long menuIdentity,long dataIdentity)
    { Clear(); if(menuIdentity!=0&&dataIdentity!=0) { menu=menuIdentity;data=dataIdentity;open=true; } return Generation; }
    internal bool Select(long ticket,long menuIdentity,long dataIdentity,bool enabled,bool load)
    {
        if(!open||starting||load||ticket!=Generation||menu!=menuIdentity||data!=dataIdentity)return false;
        selected=enabled;return true;
    }
    internal void Back(bool finalStartInProgress=false) { if(!finalStartInProgress){selected=false;starting=false;} }
    internal void Start(long menuIdentity,long dataIdentity,bool load)
    { if(!open||load||menu!=menuIdentity||data!=dataIdentity){Clear();return;} starting=true; }
    internal bool Consume(long dataIdentity,bool load)
    { bool enabled=open&&starting&&selected&&!load&&data==dataIdentity; Clear();return enabled; }
}
