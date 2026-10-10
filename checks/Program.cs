using RealTimeShop;

int count = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
    count++;
}
void Near(double actual, double expected, string description) => Check(Math.Abs(actual - expected) < 0.00001, description);
ShopClock Fresh(Timing? timing = null)
{
    var value = new ShopClock(timing ?? new Timing());
    value.StartDay("slot1/run1/day1");
    return value;
}

var clock = Fresh();
var empty = new ShopFrame();
Check(clock.DisplayTime == "08:00", "morning clock");
clock.Advance(150, empty);
Check(clock.DisplayTime == "09:00", "preparation uses the day budget");
clock.StartDay(clock.DayKey);
Near(clock.Elapsed, 150, "opening/repeated day notifications do not reset the day");
foreach (var frame in new[] { new ShopFrame(Suspended: true), new ShopFrame(Protected: true, Suspended:true) })
{
    clock.Advance(9999, frame);
    Near(clock.Elapsed, 150, "paused/loading/protected frames do not advance");
}
clock.Advance(1, empty);
Near(clock.Elapsed, 151, "resume only consumes the supplied active delta");
var warnings = clock.Advance(1650 - 151, empty);
Check(warnings == ClockEvent.OneHour, "19:00 warning");
Check(clock.Advance(0, empty) == ClockEvent.None, "warning emitted once");
Check(clock.Advance(112.5, empty) == ClockEvent.FifteenMinutes, "19:45 warning");
Check(clock.Advance(37.5, empty) == ClockEvent.None && clock.AdmissionClosed, "20:00 stops admission");
Check(clock.Advance(0, new(CanClose: true)) == ClockEvent.Close, "close only when native checks permit");
Check(clock.Advance(1, new(CanClose: true)) == ClockEvent.None, "close request exactly once");
clock.ConfirmClosed();
clock.Advance(3600, empty);
Check(clock.DisplayTime == "20:00 | Closed" && clock.Elapsed == 1800, "business clock remains closed until explicit evening accounting");
clock.StartDay("slot1/run1/day2");
Check(clock.Phase == Phase.Running && clock.Elapsed == 0, "new morning resets once");

var shopper = new ShopFrame("buyer/visit1", true, SafeToDismiss: true);
clock = Fresh();
clock.Advance(40, shopper with { Ready = false });
Check(clock.Customer == null, "arrival animation does not start patience");
clock.Advance(0, shopper);
Near(clock.PatienceLeft, 120, "patience starts on readiness");
clock.Advance(89, shopper);
Near(clock.PatienceLeft, 31, "trading consumes patience");
clock.Advance(999, shopper with { Suspended = true });
Near(clock.PatienceLeft, 31, "break pause preserves patience");
Check(clock.Advance(1, shopper) == ClockEvent.PatienceWarning, "30 second warning");
clock.Advance(40, shopper with { SafeToDismiss = false });
Check(clock.PatienceLeft == 0, "unsafe transaction can finish after expiry");
Check(clock.Advance(0, shopper) == ClockEvent.Dismiss, "dismiss at next safe point");
Check(clock.Advance(5, shopper) == ClockEvent.None, "dismissal emitted once");
clock.CustomerDeparted();
clock.Advance(0, shopper);
Near(clock.PatienceLeft, 120, "same-blueprint new visit resets after native dismissal acknowledgement");
clock.Advance(0, empty);
clock.Advance(0, shopper with { Customer = "buyer/visit2" });
Near(clock.PatienceLeft, 120, "next visit gets its own patience");
clock.Advance(0, shopper with { Customer = "inspector", Protected = true });
var protectedSnapshot = clock.Capture();
var protectedEvents = clock.Advance(9999, shopper with { Customer = "inspector", Protected = true });
Check(clock.Elapsed==clock.Timing.DaySeconds && clock.Phase==Phase.Closing && clock.PatienceLeft==0,
    "protected visitor consumes active day time without ordinary patience");
Check(!protectedEvents.HasFlag(ClockEvent.Dismiss)&&!protectedEvents.HasFlag(ClockEvent.Close),
    "protected visit remains safe beyond day cutoff and grace");

clock = Fresh();
clock.Advance(1790, empty);
clock.Advance(0, shopper);
Check(clock.Advance(15, shopper).HasFlag(ClockEvent.Dismiss), "cutoff dismisses immediately without sixty second grace");
Near(clock.GraceLeft, 55, "crossing deadline charges only the post-deadline part");
Near(clock.PatienceLeft, 105, "patience continues through closing");
clock.Advance(999, shopper with { Suspended = true });
Near(clock.GraceLeft, 55, "grace freezes during break pause");
Check(!clock.Advance(55, shopper).HasFlag(ClockEvent.Dismiss), "closing dismissal request remains one shot");
clock = Fresh();
clock.Advance(1690, empty);
clock.Advance(115, shopper with { SafeToDismiss = false });
Check(clock.Advance(5, shopper).HasFlag(ClockEvent.Dismiss), "shorter remaining patience bounds grace");
var closingSave = clock.Capture();
var closingRestore = Fresh();
Check(closingRestore.Restore(closingSave, clock.DayKey, shopper.Customer), "closing save restores");
Near(closingRestore.GraceLeft, clock.GraceLeft, "closing grace restores");
Check(closingRestore.AdmissionClosed, "closing save cannot admit ordinary visitors");
Check(closingRestore.Advance(0, shopper).HasFlag(ClockEvent.Dismiss), "pending expiry continues after reload");
Check(!closingRestore.Advance(0, shopper).HasFlag(ClockEvent.Dismiss), "restored expiry executes once");
clock = Fresh(new Timing(60, 120, 0));
Check(clock.Advance(60, shopper).HasFlag(ClockEvent.Dismiss), "zero grace closes immediately when safe");

clock = Fresh();
clock.Advance(14, shopper);
var state = clock.Capture();
var restored = new ShopClock(new Timing(600, 45, 10));
Check(restored.Restore(state, clock.DayKey, shopper.Customer), "matching restore");
Near(restored.Elapsed, 14, "elapsed time restored");
Near(restored.PatienceLeft, 106, "patience restored");
Near(restored.Timing.DaySeconds, 1800, "current day retains saved balancing settings");
Check(!restored.Restore(state, "other-slot", shopper.Customer) && !restored.Timed, "slot isolation");
Check(!restored.Restore(state, clock.DayKey, "different-customer"), "customer mismatch rejected");
foreach (double invalid in new[] { -1.0, double.NaN, double.PositiveInfinity, 999999 })
    Check(!restored.Restore(state with { Elapsed = invalid }, clock.DayKey, shopper.Customer), "invalid state rejected");
Check(!restored.Restore(state with { Phase = (Phase)99 }, clock.DayKey, shopper.Customer), "unknown phase rejected");
Check(!restored.Restore(state with { Phase = Phase.Closing }, clock.DayKey, shopper.Customer), "inconsistent closing state rejected");
Check(!restored.Restore(state with { Timing = new Timing(-1) }, clock.DayKey, shopper.Customer), "invalid saved configuration rejected");
try { _ = new ShopClock(new Timing(double.NaN)); Check(false, "invalid config must throw"); }
catch (ArgumentException) { count++; }
try { clock.Advance(double.NaN, empty); Check(false, "invalid elapsed input must throw"); }
catch (ArgumentException) { count++; }

// Frame-rate independence, including a frame crossing the closing boundary.
var coarse = Fresh(); var fine = Fresh();
coarse.Advance(1790, empty); fine.Advance(1790, empty);
coarse.Advance(30, shopper);
for (int i = 0; i < 300; i++) fine.Advance(0.1, shopper);
Near(fine.Elapsed, coarse.Elapsed, "clock independent of frame rate");
Near(fine.PatienceLeft, coarse.PatienceLeft, "patience independent of frame rate");
Near(fine.GraceLeft, coarse.GraceLeft, "grace independent of frame rate");

string folder = Path.Combine(Path.GetTempPath(), "realtime-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    string native = Path.Combine(folder, "slot.es3");
    File.WriteAllText(native, "native-save-bytes");
    clock = Fresh(); clock.Advance(10, shopper);
    SaveSidecar.Write(native, clock);
    Check(File.ReadAllText(native) == "native-save-bytes", "native file untouched");
    Check(SaveSidecar.TryRestore(native, restored, clock.DayKey, shopper.Customer, out _), "sidecar round trip");
    Near(restored.Elapsed, 10, "sidecar elapsed restored");
    Check(!Directory.EnumerateFiles(folder, "*.tmp").Any(), "atomic write leaves no temporary file");
    File.WriteAllText(native, "different-native-save");
    Check(!SaveSidecar.TryRestore(native, restored, clock.DayKey, shopper.Customer, out _) && !restored.Timed, "native fingerprint mismatch");
    File.WriteAllText(SaveSidecar.PathFor(native), "{bad json");
    Check(!SaveSidecar.TryRestore(native, restored, clock.DayKey, shopper.Customer, out _), "corrupt JSON fails safely");
    File.WriteAllText(SaveSidecar.PathFor(native), "{\"Version\":1,\"Clock\":null}");
    Check(!SaveSidecar.TryRestore(native, restored, clock.DayKey, shopper.Customer, out _), "null saved state fails safely");
    File.Delete(SaveSidecar.PathFor(native));
    Check(!SaveSidecar.TryRestore(native, restored, clock.DayKey, shopper.Customer, out _), "missing sidecar leaves day untimed");
    SaveSidecar.Write(native, clock);
    string second = Path.Combine(folder, "slot2.es3");
    File.Copy(native, second);
    File.Copy(SaveSidecar.PathFor(native), SaveSidecar.PathFor(second));
    Check(!SaveSidecar.TryRestore(second, restored, "slot2/run2/day1", shopper.Customer, out _), "identical native bytes still cannot leak across slots");
    clock.ConfirmClosed();
    SaveSidecar.Write(native, clock);
    Check(SaveSidecar.TryRestore(native, restored, clock.DayKey, null, out _) && restored.Phase == Phase.Closed, "early closed day survives sidecar round trip");
    clock.Deactivate(); SaveSidecar.Write(native, clock);
    Check(!File.Exists(SaveSidecar.PathFor(native)), "untimed save invalidates stale sidecar");
}
finally { Directory.Delete(folder, true); }
string run = "b46be0c5-be5f-4f63-8290-3e24f3225c3d";
string allocated = SaveIdentity.Key(0, run, 1);
Check(allocated == "0/" + run + "/1", "allocated native GUID identity preserved exactly");
foreach (var identity in new[] { (-1, run, 1), (0, "", 1), (0, "bad", 1), (0, Guid.Empty.ToString(), 1), (0, run, -1) })
{
    bool refused = false;
    try { SaveIdentity.Key(identity.Item1, identity.Item2, identity.Item3); }
    catch (InvalidOperationException) { refused = true; }
    Check(refused, "provisional or malformed native identity refused");
}
var identityClock = new ShopClock(new Timing());
Check(!identityClock.Timed, "new game remains untimed before allocation");
identityClock.StartDay(allocated);
SaveIdentity.RequireMatchingClock(identityClock, allocated);
Check(identityClock.Elapsed == 0 && identityClock.DayKey == allocated, "first native save initializes zero elapsed with allocated identity");
identityClock.Advance(45, new ShopFrame());
double preservedElapsed = identityClock.Elapsed;
SaveIdentity.RequireMatchingClock(identityClock, allocated);
Check(identityClock.Elapsed == preservedElapsed, "later matching saves preserve elapsed time");
foreach (string other in new[] { "-1//1", SaveIdentity.Key(1, run, 1), SaveIdentity.Key(0, Guid.NewGuid().ToString(), 1), SaveIdentity.Key(0, run, 2) })
{
    bool refused = false;
    try { SaveIdentity.RequireMatchingClock(identityClock, other); }
    catch (InvalidOperationException) { refused = true; }
    Check(refused && identityClock.DayKey == allocated && identityClock.Elapsed == preservedElapsed,
        "mismatched save rejected without rebinding or changing timers");
}
// Retained weighing selection is safe only with known hidden panels and quiet native work.
for(int mask=0;mask<64;mask++)
{
    var nf=new NegotiationFacts((mask&1)!=0,(mask&2)!=0,(mask&4)!=0,(mask&8)!=0,(mask&16)!=0,(mask&32)!=0);
    bool held=NegotiationSafety.Held(nf);
    Check(held!=(mask==49),"negotiation states fail closed except known hidden quiet weighing selection");
    var ordinary=Fresh();
    var frame=shopper with {ClosingBlockers=held?ClosingBlocker.Negotiation:ClosingBlocker.None};
    Check(ordinary.Advance(1800,frame).HasFlag(ClockEvent.Dismiss)==!held,"native negotiation facts gate ordinary cutoff");
    var protectedClock=Fresh();
    Check(!protectedClock.Advance(1800,frame with {Protected=true}).HasFlag(ClockEvent.Dismiss),"protected visitor retained even if stale selection is safe");
}
var staleFacts=new NegotiationFacts(true,false,false,false,true,true);
Check(!NegotiationSafety.Held(staleFacts),"hidden known weighing reference is not pending negotiation");
Check(NegotiationSafety.Held(staleFacts with {OfferVisible=true}),"visible offer retained");
Check(NegotiationSafety.Held(staleFacts with {ConfirmationVisible=true}),"visible confirmation retained");
Check(NegotiationSafety.Held(staleFacts with {BargainVisible=true}),"visible bargain retained");
Check(NegotiationSafety.Held(staleFacts with {NativeQuiet=false}),"inflight trade/native work retained even with hidden panels");
Check(NegotiationSafety.Held(staleFacts with {SelectionKnown=false}),"unknown retained selection held");
Check(NegotiationSafety.Held(staleFacts with {PanelsKnown=false}),"missing native UI fails closed");
// New-game consent is separate from installed capability and legacy clock state.
{
    const string consentRun="14c5cb68-89b4-4472-96a5-1db76cb187a3";
    const string otherRun="6422bdc5-86b6-4a5c-998b-8bd383f65d8c";
    var selection=new CreationChoice();
    Check(!selection.Consume(),"creation defaults OFF without a UI selection");
    selection.Select(true); Check(selection.Consume(),"explicit enable consumed at creation");
    Check(!selection.Consume(),"enable cannot leak into another new game");
    selection.Select(true); selection.Clear(); Check(!selection.Consume(),"menu close/load/cancel clears pending enable");
    selection.Select(false); Check(!selection.Consume(),"explicit vanilla stays OFF");
    var mode=new SaveOptInSession();
    Check(!mode.Matches(7,consentRun),"installed capability does not opt in a loaded save");
    mode.BeginCreation(false); mode.CompleteCreation(7,consentRun);
    Check(!mode.Matches(7,consentRun),"new vanilla creation never activates");
    mode.BeginCreation(true); Check(!mode.Matches(7,consentRun),"ON waits for native identity and persistence");
    mode.CompleteCreation(7,consentRun);
    Check(mode.Matches(7,consentRun),"persisted new-game consent activates exact native slot/run");
    Check(!mode.Matches(8,consentRun)&&!mode.Matches(7,otherRun),"slot or run changes cannot inherit enabled session");
    mode.Reset();Check(!mode.Matches(7,consentRun),"load/creation reset closes previous consent");
    bool refused=false;mode.BeginCreation(true);
    try{mode.CompleteCreation(-1,consentRun);}catch(InvalidOperationException){refused=true;}
    Check(refused&&!mode.Matches(7,consentRun),"provisional identity fails safely OFF");
    var consentFolder=Path.Combine(Path.GetTempPath(),"realtime-opt-in-"+Guid.NewGuid()); Directory.CreateDirectory(consentFolder);
    try
    {
        string native=Path.Combine(consentFolder,"save_7.es3");File.WriteAllText(native,"native-v1");
        var timed=new ShopClock(new Timing(60,120,60));timed.StartDay(SaveIdentity.Key(7,consentRun,1));
        SaveSidecar.Write(native,timed); // Deliberately create a matching legacy timer first.
        string legacy=File.ReadAllText(SaveSidecar.PathFor(native));
        Check(!SaveOptInStore.TryRead(native,7,consentRun),"matching legacy clock is not consent");
        Check(File.ReadAllText(SaveSidecar.PathFor(native))==legacy,"legacy load does not convert/delete old sidecar");
        bool rejected=false;try{SaveOptInStore.Write(native,7,consentRun,false);}catch(InvalidOperationException){rejected=true;}
        Check(rejected&&!File.Exists(SaveOptInStore.PathFor(native)),"default/OFF cannot create consent");
        SaveOptInStore.Write(native,7,consentRun,true);
        Check(SaveOptInStore.TryRead(native,7,consentRun),"explicit consent record round trip");
        Check(File.ReadAllText(native)=="native-v1","consent persistence never writes native save");
        var loaded=new ShopClock(new Timing());
        bool Restore(int slot,string run,int day,string? customer=null)
        {
            mode.Reset();loaded.Deactivate();
            bool valid=SaveOptInStore.TryRead(native,slot,run)&&SaveSidecar.TryRestore(native,loaded,SaveIdentity.Key(slot,run,day),customer,out _);
            mode.Restore(valid,slot,run);return mode.Matches(slot,run);
        }
        Check(Restore(7,consentRun,1)&&loaded.Timed,"restart restores ON only with valid choice and clock");
        Check(!Restore(8,consentRun,1),"copied consent does not activate another slot");
        Check(!Restore(7,otherRun,1),"new run overwrite cannot inherit ON");
        Check(!Restore(7,consentRun,2),"wrong clock day fails OFF even with valid consent");
        string consent=File.ReadAllText(SaveOptInStore.PathFor(native));
        foreach(string invalid in new[]{"{bad",consent.Replace("\"Version\":1","\"Version\":2"),
            consent.Replace("explicit-new-game-v1","legacy-timer"),consent.Replace("\"Enabled\":true","\"Enabled\":false"),
            "{}",new string('x',4097)})
        {
            File.WriteAllText(SaveOptInStore.PathFor(native),invalid);
            Check(!Restore(7,consentRun,1)&&!loaded.Timed,"corrupt/missing fields/future versions fail OFF without migration");
            Check(File.ReadAllText(native)=="native-v1","bad consent cannot alter vanilla save");
        }
        File.WriteAllText(SaveOptInStore.PathFor(native),consent);
        File.Delete(SaveSidecar.PathFor(native));
        Check(!Restore(7,consentRun,1),"missing clock fails OFF despite valid consent");
        File.WriteAllText(SaveSidecar.PathFor(native),"{bad");
        Check(!Restore(7,consentRun,1),"corrupt clock fails OFF despite valid consent");
        SaveSidecar.Write(native,timed);
        File.WriteAllText(native,"native-v2-after-gameplay");SaveSidecar.Write(native,timed);
        Check(!Restore(7,consentRun,1),"interrupted clock/choice pair fails OFF on native hash mismatch");
        SaveOptInStore.Write(native,7,consentRun,true);
        Check(Restore(7,consentRun,1),"normal authorized native save updates consent fingerprint");
        timed.ConfirmClosed();SaveSidecar.Write(native,timed);SaveOptInStore.Write(native,7,consentRun,true);
        Check(Restore(7,consentRun,1)&&loaded.Phase==Phase.Closed,"closed opted-in day reload preserves closure");
        timed.StartDay(SaveIdentity.Key(7,consentRun,2));File.WriteAllText(native,"native-day2");
        SaveSidecar.Write(native,timed);SaveOptInStore.Write(native,7,consentRun,true);
        Check(Restore(7,consentRun,2)&&loaded.Elapsed==0,"opted-in day rollover persists exact new day");
        File.WriteAllText(native,"native-new-run-OFF"); // Native overwrite alone: no new consent.
        Check(!Restore(7,otherRun,1),"OFF overwrite leaves old pair inert, not silently converted");
        timed=new ShopClock(new Timing(60,120,60));timed.StartDay(SaveIdentity.Key(7,otherRun,1));
        SaveSidecar.Write(native,timed);SaveOptInStore.Write(native,7,otherRun,true);
        Check(Restore(7,otherRun,1)&&!SaveOptInStore.TryRead(native,7,consentRun),"explicit ON overwrite binds new run only");
        File.Delete(SaveOptInStore.PathFor(native));
        Check(!Restore(7,otherRun,1)&&!mode.Matches(7,otherRun),"deleted choice stays OFF, including later mornings");
        Check(!Directory.EnumerateFiles(consentFolder,"*.tmp").Any(),"atomic choice writes leave no temporary files");
    }
    finally{Directory.Delete(consentFolder,true);}
}
Console.WriteLine($"PASS: {count} real-time shop checks.");
string calendar = "DAY 5 FRIDAY\nRENT IN 2 DAYS";
Check(CalendarText.Append(calendar,"08:00",false)==calendar+"\n08:00", "plain native calendar text preserved exactly");
Check(CalendarText.Append(calendar,"08:00",true)==calendar+"\n<size=65%>08:00</size>", "native calendar rich text preserved with relative clock font");
Check(CalendarText.Append(calendar,"Closed",false).StartsWith(calendar,StringComparison.Ordinal), "day/rent text survives closed clock");
Console.WriteLine($"PASS: {count} checks including calendar text.");

var pause = new NativePauseAccounting();
var active = new NativePauseInputs(true, true, false, false, false, false, false);
Check(!pause.Observe(active, 1) && !pause.HoldsFrame(1), "ordinary native gameplay advances timers");
foreach (var held in new[] {
    active with { MenuVisible = true }, active with { OptionsVisible = true },
    active with { ExitConfirmationVisible = true }, active with { NativeExitPending = true },
    active with { Focused = false }, active with { ApplicationPaused = true } })
{
    Check(pause.Observe(held, 2) && pause.HoldsFrame(2), "native UI/focus pause holds timers");
    Check(!pause.Observe(active, 3) && pause.HoldsFrame(3), "native resume excludes its input frame");
    Check(!pause.Observe(active, 3) && pause.HoldsFrame(3), "repeated update in resume frame retains hold");
    Check(!pause.Observe(active, 4) && !pause.HoldsFrame(4), "timers resume next frame without sticky pause");
}
Check(pause.Observe(active with { MenuVisible = true }, 5), "native menu opens");
Check(pause.Observe(active with { OptionsVisible = true }, 6) && pause.HoldsFrame(6),
    "switch from menu to native options keeps timers paused");
Check(pause.Observe(active with { NativeExitPending = true }, 7), "native exit pending holds after menu hides");
Check(!pause.Observe(active with { InShop = false }, 8) && !pause.HoldsFrame(8),
    "scene exit clears pause accounting without overriding native transition");
Check(!pause.Observe(active, 9) && !pause.HoldsFrame(9), "later shop load has no inherited pause latch");
var pauseClock = Fresh();
pauseClock.Advance(10, new ShopFrame());
pause.Observe(active with { MenuVisible = true }, 10);
pauseClock.Advance(120, new ShopFrame(Suspended: pause.HoldsFrame(10)));
Near(pauseClock.Elapsed, 10, "two minutes in native menu add no clock time");
pause.Observe(active, 11);
pauseClock.Advance(120, new ShopFrame(Suspended: pause.HoldsFrame(11)));
Near(pauseClock.Elapsed, 10, "native resume frame cannot charge pause backlog");
pause.Observe(active, 12);
pauseClock.Advance(1, new ShopFrame(Suspended: pause.HoldsFrame(12)));
Near(pauseClock.Elapsed, 11, "ordinary time resumes after native menu closes");
var notices = new NativeNoticeQueue();
string shown = "";
notices.Publish("warning"); notices.Publish("warning");
Check(notices.Count == 1, "notification duplicates coalesce");
notices.Pump(500, false, text => { shown = text; return true; });
Check(shown == "" && notices.Count == 1, "paused notices neither expire nor display");
notices.Pump(1, true, text => false);
Check(notices.Count == 1, "busy native sink retains notice");
notices.Pump(0, true, text => { shown = text; return true; });
Check(shown == "warning" && notices.Count == 0, "published notification reaches display sink");
notices.Publish("stale"); notices.Pump(10, true, text => { shown = text; return true; });
Check(shown == "warning" && notices.Count == 0, "event expires after ten active seconds");
for (int i=0;i<20;i++) notices.Publish("event"+i);
Check(notices.Count == 8, "event queue bounded");
notices.Clear(); Check(notices.Count == 0, "lifecycle cleanup clears owned queue");
notices.SetHold("paused"); notices.SetHold("");
notices.Pump(0,true,text => { shown=text;return true; });
Check(shown == "Shop clock resumed.", "resume replaces stale pause explanation");
notices.SetHold(""); Check(notices.Count == 0,"unchanged state does not repeat");
Check(NativeHoldText.Describe(active with { MenuVisible=true }).Contains("pause menu"), "menu hold explanation");
Check(NativeHoldText.Describe(active with { Focused=false }).Contains("unfocused"), "focus hold explanation");
Check(NativeHoldText.Describe(active with { OptionsVisible=true }).Contains("options"), "options hold explanation");
Check(NativeHoldText.Describe(active with { ApplicationPaused=true }).Contains("application"), "application hold explanation");
Check(NativeHoldText.Describe(active with { ExitConfirmationVisible=true }).Contains("confirmation"), "exit hold explanation");
Check(NativeHoldText.Describe(active with { NativeExitPending=true }).Contains("transition"), "transition hold explanation");
Check(NativeHoldText.Describe(active)=="", "active state has no hold explanation");
var facts=new CustomerFacts("buyer","BUY",true,false,false,false,false);
Check(facts.Reason=="ordinary","ordinary diagnostic matches policy");
Check((facts with {ArrivalCallback=true}).Reason=="arrival-callback","callback buyer protected diagnostic");
Check((facts with {DismissCallback=true}).Reason=="dismiss-callback","dismiss callback protected diagnostic");
Check((facts with {ScriptedFactory=true}).Reason=="scripted-factory","factory visitor protected diagnostic");
Check((facts with {Security=true}).Reason=="security","security protected diagnostic");
Check((facts with {Dismissable=false}).Reason=="non-dismissible","non-dismissible protected diagnostic");
Check((facts with {Identifier=""}).Reason=="no-customer-id","missing customer diagnostic");
Check((facts with {Intent="DIALOGUE"}).Reason=="unsupported-intent","dialogue protected diagnostic");
var diagnostic=new CustomerDiagnostics(2);
var observation=new CustomerObservation(1,facts,true,false,false,true,"Running",0,120,60);
Check(diagnostic.Observe(observation)!=null,"initial customer telemetry");
Check(diagnostic.Observe(observation with {Elapsed=14})==null,"per-frame telemetry suppressed");
Check(diagnostic.Observe(observation with {Elapsed=15})!=null,"fifteen second countdown telemetry");
Check(diagnostic.Observe(observation with {Protected=true})!=null,"classification/frame transition telemetry");
Check(diagnostic.Take() && diagnostic.Take() && !diagnostic.Take(),"telemetry emission cap");
// Every combination of native safety blockers must prevent destructive closing work.
for (int mask=0; mask<1024; mask++)
{
    var blockers=(ClosingBlocker)mask;
    var candidate=Fresh();candidate.Advance(1799,empty);
    var atCutoff=shopper with {ClosingBlockers=blockers};
    var result=candidate.Advance(1,atCutoff);
    var idleCandidate=Fresh();
    Check(idleCandidate.Advance(1800,new ShopFrame(CanClose:true,ClosingBlockers:blockers)).HasFlag(ClockEvent.Close)==(mask==0),"idle closure respects every safety blocker");
    Check(candidate.AdmissionClosed && candidate.DisplayTime=="20:00", "cutoff independent of transaction safety");
    Check(result.HasFlag(ClockEvent.Dismiss)==(mask==0), "all safety combinations gate closing dismissal");
    if(mask!=0)
    {
        Check(!candidate.Advance(3600,atCutoff).HasFlag(ClockEvent.Dismiss),"unsafe closing never forces a timeout");
        Check(candidate.Advance(0,shopper).HasFlag(ClockEvent.Dismiss),"safe point releases delayed closing immediately");
    }
    Check(!candidate.Advance(0,shopper).HasFlag(ClockEvent.Dismiss),"closing request is one shot");
}
var cutoffAnimation=Fresh();cutoffAnimation.Advance(1799,empty);
Check(!cutoffAnimation.Advance(1,shopper with {Ready=false,ClosingBlockers=ClosingBlocker.Animation}).HasFlag(ClockEvent.Dismiss),"arrival animation defers closing cleanup");
Check(cutoffAnimation.Advance(0,shopper).HasFlag(ClockEvent.Dismiss),"finished arrival dismisses immediately at closing");
var cutoffProtected=Fresh();cutoffProtected.Advance(1800,empty);
Check(!cutoffProtected.Advance(500,shopper with {Protected=true}).HasFlag(ClockEvent.Dismiss),"protected closing visitor never forced out");
var waitingSave=Fresh();waitingSave.Advance(1800,shopper with {ClosingBlockers=ClosingBlocker.Confirmation});
var waitingRestore=Fresh();Check(waitingRestore.Restore(waitingSave.Capture(),waitingSave.DayKey,shopper.Customer),"unsafe closing save restores");
Check(!waitingRestore.Advance(0,shopper with {ClosingBlockers=ClosingBlocker.Confirmation}).HasFlag(ClockEvent.Dismiss),"reload does not bypass live safety confirmation");
Check(waitingRestore.Advance(0,shopper).HasFlag(ClockEvent.Dismiss),"reload dismisses when live transaction finishes");
waitingRestore.ConfirmClosed();waitingRestore.StartDay("slot1/run1/day3");
Check(!waitingRestore.AdmissionClosed&&waitingRestore.Elapsed==0,"day rollover clears closing cutoff");
var ownedStock=new TableItemFacts(true,false,false);
var customerGoods=new TableItemFacts(false,true,false);
Check(TableOwnershipPolicy.Assess(new[]{ownedStock},false).Safe,"owned stock permits idle closure because native cleanup preserves ownership tag");
Check(TableOwnershipPolicy.Assess(new[]{ownedStock with {HasNestedItems=true}},false).Safe,"owned container preserved by native owned-parent branch");
Check(TableOwnershipPolicy.Assess(new[]{customerGoods},true).Safe,"verified ordinary seller goods permit native dismissal");
Check(TableOwnershipPolicy.Assess(new[]{ownedStock,customerGoods},true).Safe,"mixed owned stock and ordinary unsold goods permit native dismissal");
Check(!TableOwnershipPolicy.Assess(new[]{customerGoods},false).Safe,"protected/no-current visitor unsold goods held");
Check(!TableOwnershipPolicy.Assess(new[]{customerGoods with {KnownCustomerGoods=false}},true).Safe,"unknown unowned goods held");
Check(!TableOwnershipPolicy.Assess(new[]{customerGoods with {Owned=null}},true).Safe,"unknown ownership held");
Check(!TableOwnershipPolicy.Assess(new[]{customerGoods with {HasNestedItems=true}},true).Safe,"unowned container contents cannot lose nested owned goods");
Check(!TableOwnershipPolicy.Assess(new[]{customerGoods with {NativeCleanupScope=false}},true).Safe,"unowned counter outside verified cleanup held");
var ledger=new CustomerGoodsLedger();ledger.Record(10,20,30);
Check(ledger.Known(10,20,30),"native placement establishes exact visit/pointer/id provenance");
Check(!ledger.Known(11,20,30)&&!ledger.Known(10,20,31),"other visit or reused native object id cannot inherit provenance");
ledger.Record(11,21,31);Check(!ledger.Known(10,20,30),"next visit clears previous goods");
ledger.Clear();Check(!ledger.Known(11,21,31),"lifecycle clears provenance");
foreach(var blocker in new[]{ClosingBlocker.Negotiation,ClosingBlocker.Confirmation,ClosingBlocker.Dragging,ClosingBlocker.Animation,ClosingBlocker.TradeCommit,ClosingBlocker.Dialogue})
{
    var seller=Fresh();var policy=TableOwnershipPolicy.Assess(new[]{ownedStock,customerGoods},true);
    var sellerFrame=shopper with {ClosingBlockers=blocker,SafeToDismiss=policy.Safe};
    Check(!seller.Advance(1800,sellerFrame).HasFlag(ClockEvent.Dismiss),"pending native transaction blocks even proven unsold goods");
    Check(seller.Advance(0,sellerFrame with {ClosingBlockers=ClosingBlocker.None}).HasFlag(ClockEvent.Dismiss),"finished transaction permits ordinary goods dismissal");
}
Check(ownedStock==new TableItemFacts(true,false,false),"ownership policy cannot mutate items/money");
for(int mask=0; mask<32; mask++)
{
    bool clicked=(mask&1)!=0,start=(mask&2)!=0,load=(mask&4)!=0,otherData=(mask&8)!=0,cancel=(mask&16)!=0;
    var setup=new NewGameSetupSelection();long ticket=setup.Begin(10,20);
    Check(!setup.Enabled,"fresh setup defaults OFF");
    if(clicked)Check(setup.Select(ticket,10,20,true,false),"visible setup click accepted");
    if(cancel)setup.Clear();
    if(start)setup.Start(10,20,load);
    Check(setup.Consume(otherData?21:20,load)==(clicked&&start&&!load&&!otherData&&!cancel),"choice requires explicit click, same creation identity and native start");
    Check(!setup.Consume(20,false),"choice consumed once");
}
{
    var setup=new NewGameSetupSelection();long ticket=setup.Begin(10,20);
    setup.Select(ticket,10,20,true,false);setup.Select(ticket,10,20,false,false);setup.Start(10,20,false);
    Check(!setup.Consume(20,false),"turning checkbox back OFF creates vanilla");
    ticket=setup.Begin(10,20);setup.Select(ticket,10,20,true,false);setup.Back();
    Check(!setup.Enabled,"perks back navigation resets visual selection OFF");
    setup.Start(10,20,false);Check(!setup.Consume(20,false),"back navigation removes pending consent");
    ticket=setup.Begin(10,20);setup.Select(ticket,10,20,true,false);long next=setup.Begin(10,20);
    Check(!setup.Select(ticket,10,20,true,false)&&!setup.Enabled,"old callback cannot enable a fresh setup");
    Check(!setup.Select(next,11,20,true,false)&&!setup.Select(next,10,21,true,false),"foreign menu/data callbacks rejected");
    setup.Select(next,10,20,true,false);setup.Start(11,20,false);Check(!setup.Consume(20,false),"different menu cannot authorize creation");
    ticket=setup.Begin(10,20);setup.Select(ticket,10,20,true,false);setup.Start(10,20,false);
    Check(!setup.Select(ticket,10,20,false,false)&&setup.Consume(20,false),"captured Start cannot be rewritten by late events");
    ticket=setup.Begin(0,20);Check(!setup.Select(ticket,0,20,true,false),"unknown owner fails OFF");
    ticket=setup.Begin(10,20);setup.Select(ticket,10,20,true,false);setup.Back(true);setup.Start(10,20,false);
    Check(setup.Consume(20,false),"native final Start internally closes perks without treating it as cancellation");
}
// Visible dialogue suspends every timer even for an ordinary customer, at either phase.
foreach (var closing in new[] { false, true })
{
    var talking = Fresh(new Timing(60, 120, 60));
    var ordinary = new ShopFrame("ordinary", Ready:true, SafeToDismiss:true);
    talking.Advance(closing ? 60 : 59, ordinary with { ClosingBlockers=ClosingBlocker.Negotiation });
    var before = talking.Capture();
    for (int i=0; i<600; i++)
        Check(talking.Advance(1, ordinary with { Suspended=true, ClosingBlockers=ClosingBlocker.Dialogue }) == ClockEvent.None,
            "visible ordinary dialogue cannot warn, dismiss or close even past grace");
    Check(talking.Capture() == before, "dialogue holds elapsed, patience, grace and phase exactly");
    var dialogueRestored = Fresh();
    Check(dialogueRestored.Restore(before, before.DayKey, ordinary.Customer), "dialogue state remains compatible with existing sidecars");
    Check(dialogueRestored.Advance(600, ordinary with { Suspended=true }) == ClockEvent.None && dialogueRestored.Capture()==before,
        "reload with visible dialogue cannot consume timers");
    var resumed = talking.Advance(0.25, ordinary);
    Near(talking.PatienceLeft, before.PatienceLeft-0.25, "dialogue close charges only the new active frame");
    if(closing) { Near(talking.GraceLeft, before.GraceLeft-0.25, "closing dialogue resumes grace without catchup"); Check(resumed.HasFlag(ClockEvent.Dismiss), "closing resumes native safe dismissal after dialogue closes"); }
    else { Near(talking.Elapsed, 59.25, "ordinary dialogue resumes day clock"); Check(talking.Phase==Phase.Running, "long dialogue cannot secretly cross cutoff"); }
}
foreach (var tradeBlock in new[] { ClosingBlocker.Negotiation, ClosingBlocker.Confirmation, ClosingBlocker.TradeCommit })
{
    var impatientTrade = Fresh(new Timing(1800, 120, 60));
    var pendingTrade = new ShopFrame("ordinary", Ready:true, SafeToDismiss:true, ClosingBlockers:tradeBlock);
    Check(!impatientTrade.Advance(121,pendingTrade).HasFlag(ClockEvent.Dismiss),
        "expired patience cannot force dismissal during unfinished trade before cutoff");
    Check(impatientTrade.Phase==Phase.Running && impatientTrade.PatienceLeft==0,
        "trade consumes patience to zero while still Running");
    Check(impatientTrade.Advance(0,pendingTrade with {ClosingBlockers=ClosingBlocker.None}).HasFlag(ClockEvent.Dismiss),
        "expired patience dismisses safely only after trade resolves");
    var trading = Fresh(new Timing(60, 120, 60));
    var tradeFrame = new ShopFrame("ordinary", Ready:true, SafeToDismiss:true, ClosingBlockers:tradeBlock);
    Check(!trading.Advance(50,tradeFrame).HasFlag(ClockEvent.Dismiss), "ordinary trade before cutoff does not force dismissal");
    Near(trading.Elapsed,50,"trade panel alone consumes day time");
    Near(trading.PatienceLeft,70,"trade panel alone consumes patience");
    var speechOverTrade=trading.Capture();
    Check(trading.Advance(10,tradeFrame with {Suspended=true,ClosingBlockers=tradeBlock|ClosingBlocker.Dialogue})==ClockEvent.None,
        "actual speech over trade pauses time");
    Check(trading.Capture()==speechOverTrade,"speech overlay holds timers despite underlying trade");
    Check(!trading.Advance(10,tradeFrame).HasFlag(ClockEvent.Dismiss),"speech closes but open trade independently defers cutoff dismissal");
    Check(trading.Phase==Phase.Closing,"remaining trade panel permits reaching cutoff");
    Near(trading.PatienceLeft,60,"post-speech trade resumes patience without charging paused time");
    Check(!trading.Advance(100,tradeFrame).HasFlag(ClockEvent.Dismiss),"expired grace and patience cannot force unfinished trade");
    Near(trading.GraceLeft,0,"trade alone consumes closing grace");
    Check(trading.Advance(0,tradeFrame with {ClosingBlockers=ClosingBlocker.None}).HasFlag(ClockEvent.Dismiss),
        "finished trade releases safe cutoff dismissal");
    var emptyClosing=Fresh(new Timing(60,120,60));
    Check(!emptyClosing.Advance(160,new ShopFrame(CanClose:true,ClosingBlockers:tradeBlock)).HasFlag(ClockEvent.Close),
        "unfinished panel or commit prevents automatic close even with no visitor");
}
foreach (bool protectedVisit in new[] { false, true })
{
    var speechThenTrade=Fresh(new Timing(60,120,60));
    var baseFrame=new ShopFrame("visitor", Ready:true, Protected:protectedVisit, SafeToDismiss:true,
        CanClose:true,ClosingBlockers:ClosingBlocker.Negotiation);
    speechThenTrade.Advance(10,baseFrame);
    var beforeSpeech=speechThenTrade.Capture();
    Check(speechThenTrade.Advance(20,baseFrame with {Suspended=true})==ClockEvent.None && speechThenTrade.Capture()==beforeSpeech,
        "actual speech holds ordinary and protected visitor clocks");
    speechThenTrade.Advance(5,baseFrame);
    Near(speechThenTrade.Elapsed,15,"speech ends and clock resumes while underlying trade remains open for any visitor");
    var beyond=speechThenTrade.Advance(200,baseFrame);
    Check(!beyond.HasFlag(ClockEvent.Dismiss)&&!beyond.HasFlag(ClockEvent.Close),"open trade remains safe even past grace");
    if(protectedVisit)
    {
        var noTrade=baseFrame with {ClosingBlockers=ClosingBlocker.None};
        Check(speechThenTrade.Advance(1,noTrade)==ClockEvent.None,"protected visitor is never dismissed or closed through after trade resolves");
        Check(speechThenTrade.Customer==null&&speechThenTrade.PatienceLeft==0,"protected visitor never inherits ordinary patience");
        Check(speechThenTrade.Advance(0,new ShopFrame(CanClose:true)).HasFlag(ClockEvent.Close),"shop can close safely after protected visitor naturally departs");
    }
}
PatrolClosingChecks.Run(Check,Near);
EveningChecks.Run(Check);
Console.WriteLine($"PASS: {count} checks; native item/money preservation still needs runtime validation.");

