using RealTimeShop;
int count=0;
void Check(bool condition,string text){if(!condition)throw new Exception(text);count++;}
var eligible=new PatrolClosingFacts(true,"patrolOfficer","BUY",true,false,false,false,true,true,true,true,true,true,ClosingBlocker.None);
ShopFrame Frame(bool alarm,bool scripted=false,bool suspended=false){
    bool inspectionQuiet=PatrolInspectionSafety.Quiet(new(true,false,false,false,false,alarm));
    bool leave=PatrolClosingPolicy.Allows(eligible with {InspectionQuiet=inspectionQuiet,Scripted=scripted});
    return new("patrolOfficer",Ready:true,Protected:true,Suspended:suspended,SafeToDismiss:leave,ClosingOnlyDismissal:leave);
}
foreach(double start in new[]{0d,20d,59d,60d}) {
    var clock=new ShopClock(new Timing(60,1,60));clock.StartDay("slot9/synthetic-security/day1");
    clock.Advance(start,Frame(true));
    Check(!clock.Advance(200,Frame(true)).HasFlag(ClockEvent.Dismiss),"active alarm blocks dismissal beyond cutoff and grace");
    Check(clock.Phase==Phase.Closing&&clock.Elapsed==60&&clock.GraceLeft==0,"alarm alone consumes active time through closing/grace");
    Check(clock.Customer==null&&clock.PatienceLeft==0,"alarm does not give patrol patience");
    Check(!clock.Advance(0,Frame(true)).HasFlag(ClockEvent.Close),"no day closure with active visitor");
    Check(clock.Advance(0,Frame(false)).HasFlag(ClockEvent.Dismiss),"settled alarm releases exactly at next safe frame");
    Check(!clock.Advance(0,Frame(false)).HasFlag(ClockEvent.Dismiss),"no duplicate dismissal");
    Check(!clock.Advance(0,Frame(true)).HasFlag(ClockEvent.Dismiss),"alarm recurrence never duplicates departure request");
    clock.CustomerDeparted();
    Check(clock.Advance(0,new(CanClose:true)).HasFlag(ClockEvent.Close),"closure after native departure acknowledgement");
    Check(!clock.Advance(0,new(CanClose:true)).HasFlag(ClockEvent.Close),"single closure request");
}
var paused=new ShopClock(new Timing(60,1,60));paused.StartDay("slot9/synthetic-security/day2");
Check(paused.Advance(200,Frame(true,suspended:true))==ClockEvent.None&&paused.Elapsed==0,"arrest/cinematic/speech suspension holds time");
Check(!paused.Advance(200,Frame(false,scripted:true)).HasFlag(ClockEvent.Dismiss),"special scripted security remains protected even when alarm clears");
Console.WriteLine($"PASS: {count} security alarm/clock regression assertions. Pure managed simulation; no native security event, save, inventory or game executed.");
