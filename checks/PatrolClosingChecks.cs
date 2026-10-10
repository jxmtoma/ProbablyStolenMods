using RealTimeShop;

internal static class PatrolClosingChecks
{
    internal static void Run(Action<bool,string> check, Action<double,double,string> near)
    {
        var allowed = new PatrolClosingFacts(true,"patrolOfficer","BUY",true,false,false,false,true,true,true,true,true,true,ClosingBlocker.None);
        check(PatrolClosingPolicy.Allows(allowed),"idle native patrol eligible for closing only");
        // Every safety input is independently necessary. Exhaustive combinations catch accidental ORs.
        for(int mask=0;mask<4096;mask++)
        {
            bool b(int bit) => (mask&(1<<bit))!=0;
            var facts=allowed with {NativeFactory=b(0),Security=b(1),Scripted=b(2),SpecialOfficer=b(3),
                DismissCallback=b(4),NativeDismissable=b(5),Ready=b(6),IntroductionComplete=b(7),
                DialogueQuiet=b(8),NativeQuiet=b(9),Blockers=b(10)?ClosingBlocker.Dialogue:ClosingBlocker.None,InspectionQuiet=b(11)};
            bool expected=b(0)&&b(1)&&!b(2)&&!b(3)&&!b(4)&&b(5)&&b(6)&&b(7)&&b(8)&&b(9)&&!b(10)&&b(11);
            check(PatrolClosingPolicy.Allows(facts)==expected,"patrol admission safety combination");
        }
        for(int mask=0;mask<64;mask++)
        {
            bool b(int bit) => (mask&(1<<bit))!=0;
            var inspection=new PatrolInspectionFacts(b(0),b(1),b(2),b(3),b(4),b(5));
            check(PatrolInspectionSafety.Quiet(inspection)==(b(0)&&!b(1)&&!b(2)&&!b(3)&&!b(4)&&!b(5)),
                "inspection, confrontation, appraisal, selected item and security alarm block patrol closing");
        }
        foreach(string id in new[]{"","PatrolOfficer","securityOfficer","securityOfficerFine","patrolOfficerStory"})
            check(!PatrolClosingPolicy.Allows(allowed with {Identifier=id}),"no other officer identifier bypasses protection");
        foreach(string intent in new[]{"","SELL","SELLNBUY","BARTER","WHOLESALE","APPRAISAL_SERVICE"})
            check(!PatrolClosingPolicy.Allows(allowed with {Intent=intent}),"only reviewed patrol buying lifecycle admitted");

        var gate=new PatrolVisitGate();
        check(!gate.Settled(0)&&!gate.Settled(10),"no absent or unobserved arrival receipt");
        gate.BeginArrival(10);
        check(!gate.Settled(10),"native arrival callback in progress blocks closing");
        gate.ScheduleAction(10,100);
        gate.EndArrival(10,true);
        check(!gate.Settled(10),"arrival return cannot erase pending delayed action");
        gate.BeginAction(10);
        check(!gate.Settled(10),"executed flag before callback return is insufficient");
        gate.EndAction(10,100,true);
        check(gate.Settled(10),"successful callback completion releases patrol");
        for(int cycle=0;cycle<12;cycle++)
        {
            gate.ScheduleAction(10,200+cycle); gate.ScheduleAction(10,200+cycle);
            check(!gate.Settled(10),"repeated dialogue and duplicate scheduled notification block closing");
            gate.BeginAction(10); gate.BeginAction(10);
            gate.EndAction(10,200+cycle,true);
            check(!gate.Settled(10),"nested callbacks must all return");
            gate.EndAction(10,200+cycle,true);
            check(gate.Settled(10),"repeated completed conversations release without residual hold");
        }
        gate.ScheduleAction(10,300); // Bubbles/EndConversation/Interrupt do not clear this receipt.
        check(!gate.Settled(10),"hidden or interrupted conversation retains unexecuted consequence");
        gate.BeginAction(10); gate.EndAction(10,300,false);
        check(!gate.Settled(10),"throwing native action stays protected even if native flag says executed");
        gate.BeginAction(10); gate.EndAction(10,300,true);
        check(!gate.Settled(10),"a later repeat cannot silently clear native failure");
        gate.BeginArrival(20); gate.EndArrival(20,true);
        check(gate.Settled(20)&&!gate.Settled(10),"a distinct native visit gets fresh state");
        gate.ScheduleAction(20,400);
        gate.EndAction(10,400,true); gate.EndArrival(10,true); gate.Fault(10);
        check(!gate.Settled(20),"stale previous visit callbacks cannot release or poison new pending visit");
        gate.BeginAction(20); gate.EndAction(20,400,true);
        check(gate.Settled(20),"current visit completion still succeeds after stale callbacks");
        gate.BeginArrival(20); gate.BeginArrival(20);
        gate.EndArrival(20,true);
        check(!gate.Settled(20),"repeated nested arrival checks do not prematurely finish");
        gate.EndArrival(20,true);
        check(gate.Settled(20),"both successful arrival checks must return");
        gate.BeginArrival(20); gate.EndArrival(20,false);
        check(!gate.Settled(20),"failed arrival never grants receipt");
        gate.Clear(); gate.BeginArrival(30); gate.EndArrival(30,true);
        check(gate.Settled(30),"session clear permits a fresh fully observed visit");
        gate.Fault(30); check(!gate.Settled(30),"unknown native observation fails closed");
        gate.Clear(); gate.BeginArrival(40); gate.EndArrival(40,true);
        for(long i=1;i<=257;i++) gate.ScheduleAction(40,i);
        for(long i=1;i<=257;i++) { gate.BeginAction(40); gate.EndAction(40,i,true); }
        check(!gate.Settled(40),"bounded action ledger overflow remains protected");

        ShopClock fresh() { var c=new ShopClock(new Timing(60,1,60)); c.StartDay("slot9/run/day1"); return c; }
        var patrol=new ShopFrame("patrolOfficer",Ready:true,Protected:true,SafeToDismiss:true,ClosingOnlyDismissal:true);
        var clock=fresh();
        check(!clock.Advance(59,patrol).HasFlag(ClockEvent.Dismiss),"patrol never leaves for ordinary patience before closing");
        check(clock.Customer==null&&clock.PatienceLeft==0,"patrol has no patience or customer sidecar state");
        check(clock.Advance(1,patrol).HasFlag(ClockEvent.Dismiss),"eligible patrol uses ordinary immediate cutoff departure");
        for(int i=0;i<8;i++)
        {
            check(!clock.Advance(1,patrol with {ClosingOnlyDismissal=false}).HasFlag(ClockEvent.Dismiss),"blocked repeat remains protected");
            check(!clock.Advance(1,patrol).HasFlag(ClockEvent.Dismiss),"patrol departure requested once despite repeated classification changes");
        }
        clock.CustomerDeparted();
        check(clock.Advance(0,patrol).HasFlag(ClockEvent.Dismiss),"next same-identifier visit requires new native departure acknowledgement");
        clock.Advance(0,new ShopFrame());
        check(clock.Advance(0,patrol).HasFlag(ClockEvent.Dismiss),"observed disappearance separates same-identifier visits");
        check(clock.Advance(0,new ShopFrame(CanClose:true)).HasFlag(ClockEvent.Close),"shop closes only after patrol is absent");
        check(!clock.Advance(0,new ShopFrame(CanClose:true)).HasFlag(ClockEvent.Close),"closure remains one shot");
        clock.ConfirmClosed();
        check(clock.Advance(100,patrol)==ClockEvent.None,"closed day ignores patrol override");
        clock.StartDay("slot9/run/day2");
        check(clock.Advance(60,patrol).HasFlag(ClockEvent.Dismiss),"new day clears prior patrol dismissal receipt");
        clock.Deactivate();
        check(clock.Advance(100,patrol)==ClockEvent.None,"vanilla OFF ignores patrol override");

        foreach(ClosingBlocker blocker in Enum.GetValues<ClosingBlocker>().Where(x=>x!=ClosingBlocker.None))
        {
            check(!PatrolClosingPolicy.Allows(allowed with {Blockers=blocker}),"every existing closing blocker applies to patrol");
            clock=fresh();
            var blocked=patrol with {ClosingBlockers=blocker};
            check(!clock.Advance(200,blocked).HasFlag(ClockEvent.Dismiss),"blocked patrol remains safe beyond cutoff and expired grace");
            check(clock.Phase==Phase.Closing&&clock.Customer==null&&clock.PatienceLeft==0,"blocked patrol consumes active day and grace without patience");
            near(clock.GraceLeft,0,"blocked closing consumes grace without forced departure");
            check(clock.Advance(0,patrol).HasFlag(ClockEvent.Dismiss),"settled interaction releases at next safe frame");
            check(!clock.Advance(0,patrol).HasFlag(ClockEvent.Dismiss),"release repeats do not duplicate dismissal");
        }
        foreach(var denied in new[]{patrol with {Ready=false},patrol with {SafeToDismiss=false},
            patrol with {ClosingOnlyDismissal=false},patrol with {Customer=null}})
        {
            clock=fresh();
            var events=clock.Advance(200,denied);
            check(!events.HasFlag(ClockEvent.Dismiss)&&!events.HasFlag(ClockEvent.Close),"unready, nondismissable, special or absent protected visits cannot force actions");
        }
        clock=fresh(); clock.Advance(59,patrol);
        for(int cycle=0;cycle<8;cycle++)
        {
            var before=clock.Capture();
            check(clock.Advance(500,patrol with {Suspended=true})==ClockEvent.None&&clock.Capture()==before,
                "repeated speech, native pause and focus holds preserve patrol timers");
            clock.Advance(0,patrol with {ClosingOnlyDismissal=false});
        }
        check(clock.Advance(1,patrol).HasFlag(ClockEvent.Dismiss),"repeated interrupted holds release only with completed safety receipt at cutoff");
        var state=clock.Capture(); var restored=fresh();
        check(restored.Restore(state,state.DayKey,null),"patrol protection keeps existing sidecar schema restorable with null patience customer");
        check(!restored.Advance(0,patrol with {ClosingOnlyDismissal=false}).HasFlag(ClockEvent.Dismiss),"reload cannot grant adapter arrival/factory receipts");
        check(restored.Advance(0,patrol).HasFlag(ClockEvent.Dismiss),"fully reverified native visit may release after clock restore");
    }
}
