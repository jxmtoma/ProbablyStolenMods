using RealTimeShop;
internal static class EveningChecks {
 internal static void Run(Action<bool,string> check){
  foreach(double duration in new[]{60.0,1800.0,86400.0})for(int minute=1200;minute<=1620;minute++){
   var clock=new ShopClock(new Timing(duration));clock.StartDay("9/run/3");clock.Advance(duration,new ShopFrame());clock.ConfirmClosed();
   clock.AdvanceEvening((minute-1200)*duration/720,false);
   check(Math.Abs(clock.EveningMinute!.Value-minute)<1e-7,"same rate across day duration");
   var snapshot=clock.Capture();clock.AdvanceEvening(100,true);check(clock.Capture()==snapshot,"night pause holds exact state");
   var restored=new ShopClock(new Timing());check(restored.Restore(snapshot,"9/run/3",null),"night roundtrip");
   check(restored.RequestSleep(false)&&!restored.RequestSleep(false),"one native transition per request");
   check(restored.PendingWakeMinute==Math.Clamp(minute-960,480,660),"eight hour rest earliest 08 latest 11");
   check(restored.Capture()==snapshot,"transient request is not committed progress");
   var reload=new ShopClock(new Timing());check(reload.Restore(snapshot,"9/run/3",null)&&!reload.SleepRequested,"reload never replays in-flight transition");
   reload.RequestSleep(true);check(reload.PendingWakeMinute==480,"native outing does not stack wake delay");
   int wake=restored.PendingWakeMinute;restored.StartDay("9/run/4",wake);
   check(Math.Abs(restored.Elapsed-duration*(wake-480)/720)<1e-7&&restored.EveningMinute==null&&!restored.SleepRequested,"wake offset committed once with new day");
   double elapsed=restored.Elapsed;restored.StartDay("9/run/4",480);check(restored.Elapsed==elapsed,"repeated fresh-day callback cannot refund lateness");
  }
  var c=new ShopClock(new Timing());c.StartDay("9/run/3");c.Advance(1800,new ShopFrame());c.ConfirmClosed();
  c.AdvanceEvening(100000,false);check(c.SleepDue&&c.EveningMinute==1620&&c.DisplayTime=="03:00 | Closed","cutoff caps while safety defers");
  var good=c.Capture();foreach(double bad in new[]{double.NaN,double.PositiveInfinity,-1,479,1199,1621})check(!new ShopClock(new Timing()).Restore(good with{EveningMinute=bad},"9/run/3",null),"invalid evening rejected");
  check(new ShopClock(new Timing()).Restore(good with{EveningMinute=null},"9/run/3",null),"v1 closed state migrates at closing time");
  c.RequestSleep(false);c.CancelSleepRequest();check(!c.SleepRequested&&c.RequestSleep(false),"synchronous native failure can safely retry later");
  c=new ShopClock(new Timing());c.StartDay("9/run/3");c.Advance(300,new ShopFrame());c.ConfirmClosed();
  check(c.MinuteOfDay==600&&c.RequestSleep(false)&&c.PendingWakeMinute==480,"early closure and early sleep never wake before 08");
 }
}
