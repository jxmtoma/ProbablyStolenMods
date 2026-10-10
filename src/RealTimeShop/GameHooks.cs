using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RealTimeShop;

internal static class GameHooks
{
    private static int busyDepth, tradeDepth;
    private static readonly CustomerGoodsLedger goods = new();
    private static readonly PatrolVisitGate patrolVisit = new();
    private static readonly Dictionary<long, Dialogue[]> patrolIntroductions = new();
    internal static void ClearGoods() { goods.Clear(); patrolVisit.Clear(); patrolIntroductions.Clear(); }
    private static bool pendingLoad, openingForClose, pendingNewGame;
    private static readonly HashSet<string> scriptedIds = new(StringComparer.Ordinal);
    private static readonly PropertyInfo[] tutorialHints = typeof(TutorialUIManager)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(p => p.Name.StartsWith("tutorial", StringComparison.Ordinal) && p.PropertyType == typeof(GameObject)).ToArray();
    private static Plugin Mod => Plugin.Current!;
    internal static bool Busy => busyDepth > 0 || pendingLoad;
    internal static bool InShop => PlayerStore.IsInstanceExist() && StoreUIManager.Instance != null;
    internal static string DayKey() => SaveIdentity.Key(PlayerStore.Instance.saveSlotId, PlayerStore.Instance.runID, StoreStation.Instance.dayCounter);
    private static string SavePath() => new ES3Settings(SaveFiles.Slot(PlayerStore.Instance.saveSlotId), new Il2CppSystem.Enum[0]).FullPath;

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        if (tutorialHints.Length == 0) throw new MissingMemberException("Tutorial visibility hooks are unavailable.");
        // Resolve the complete patch set before applying any changes. No guessed overloads.
        var patches = new List<(MethodInfo Target, string? Prefix, string? Postfix, string? Finalizer)>();
        void Add(Type type, string method, string? prefix = null, string? postfix = null, string? finalizer = null, params Type[] args)
        {
            var target = AccessTools.DeclaredMethod(type, method, args)
                ?? throw new MissingMethodException(type.FullName, method);
            patches.Add((target, prefix, postfix, finalizer));
        }
        Add(typeof(PlayerStore), nameof(PlayerStore.StartNewGame), nameof(NewGamePrefix), nameof(FreshDayPostfix), nameof(NewGameFinalizer));
        Add(typeof(MainMenuUIController), nameof(MainMenuUIController.StartNewGameClick), nameof(CreationChoicePrefix));
        Add(typeof(MainMenuUIController), nameof(MainMenuUIController.OnNewGameClick), nameof(ClearCreationChoicePrefix), nameof(OpenCreationSetupPostfix));
        Add(typeof(PerkUIController), nameof(PerkUIController.CloseUI), postfix: nameof(BackCreationSetupPostfix));
        Add(typeof(MainMenuUIController), nameof(MainMenuUIController.OnPerkFinishedClick), nameof(FinalCreationSetupPrefix), finalizer: nameof(FinalCreationSetupFinalizer));
        Add(typeof(MainMenuUIController), nameof(MainMenuUIController.CloseNewGameMenu), nameof(ClearCreationChoicePrefix));
        Add(typeof(PlayerStore), nameof(PlayerStore.BeginDay), postfix: nameof(FreshDayPostfix));
        Add(typeof(PlayerStore), nameof(PlayerStore.EndDay), postfix: nameof(ClosedPostfix));
        Add(typeof(PlayerStore), nameof(PlayerStore.DismissCurrentClient), nameof(DepartureDiagnosticPrefix), nameof(DepartedPostfix),
            args: new[] { typeof(bool), typeof(bool), typeof(bool) });
        Add(typeof(PlayerStore), nameof(PlayerStore.SaveGame), nameof(BusyPrefix), finalizer: nameof(BusyFinalizer));
        Add(typeof(PlayerStore), nameof(PlayerStore.WriteSlotFile), postfix: nameof(SavedPostfix));
        Add(typeof(PlayerStore), nameof(PlayerStore.LoadGame), nameof(LoadPrefix), finalizer: nameof(LoadFinalizer));
        Add(typeof(PlayerStore), nameof(PlayerStore.EndNight), nameof(BusyPrefix), finalizer: nameof(BusyFinalizer));
        Add(typeof(EndOfDayUIManager), nameof(EndOfDayUIManager.OnEndDayButtonClick), nameof(SleepPrefix), finalizer: nameof(SleepFinalizer));
        Add(typeof(StoreClientListMinor), nameof(StoreClientListMinor.CreatePatrolOfficer), postfix: nameof(PatrolFactoryPostfix));
        Add(typeof(StoreClientMono), nameof(StoreClientMono.OnArrived), nameof(PatrolArrivalPrefix), finalizer: nameof(PatrolArrivalFinalizer));
        Add(typeof(DialogUIManager), nameof(DialogUIManager.OnCurrentTextDisplayed), nameof(PatrolTextPrefix), finalizer: nameof(PatrolTextFinalizer));
        Add(typeof(Dialogue), nameof(Dialogue.ExecuteEndAction), nameof(PatrolActionPrefix), finalizer: nameof(PatrolActionFinalizer));
        Add(typeof(PlayerStore), nameof(PlayerStore.GetNextClient), nameof(AdmissionPrefix));
        Add(typeof(PlayerStore), nameof(PlayerStore.TryCallNextClient), nameof(AdmissionPrefix));
        Add(typeof(PlayerStore), nameof(PlayerStore.CanNextClient), postfix: nameof(CanNextPostfix));
        Add(typeof(StoreShutterButton), nameof(StoreShutterButton.OpenShutter), nameof(OpenPrefix));
        Add(typeof(StoreCalendar), "Update", postfix: nameof(CalendarPostfix));
        Add(typeof(PlayerStore), nameof(PlayerStore.AddDirectSellingItemToTable), postfix: nameof(CustomerGoodsPostfix),
            args: new[] { typeof(GameItem), typeof(bool), typeof(bool), typeof(bool), typeof(int) });
        Add(typeof(PlayerStore), nameof(PlayerStore.AddDirectSellingItemToTableMiddle), postfix: nameof(CustomerGoodsPostfix),
            args: new[] { typeof(GameItem), typeof(bool) });
        Add(typeof(PlayerStore), nameof(PlayerStore.BuyItem), nameof(TradePrefix), finalizer: nameof(TradeFinalizer), args: new[] { typeof(GameItem) });
        Add(typeof(PlayerStore), nameof(PlayerStore.SellItem), nameof(TradePrefix), finalizer: nameof(TradeFinalizer), args: new[] { typeof(GameItem) });
        Add(typeof(PlayerStore), nameof(PlayerStore.BuyItemFromTable), nameof(TradePrefix), finalizer: nameof(TradeFinalizer));
        Add(typeof(PlayerStore), nameof(PlayerStore.SellItemFromTable), nameof(TradePrefix), finalizer: nameof(TradeFinalizer));

        foreach (var type in new[] { typeof(StoreClientListStory), typeof(StoreClientListSecurity), typeof(StoreClientListMorning), typeof(StoreClientListEvent) })
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                if (method.ReturnType == typeof(StoreClient)) patches.Add((method, null, nameof(ScriptedFactoryPostfix), null));
        HarmonyMethod? Hook(string? name) => name == null ? null : new HarmonyMethod(typeof(GameHooks), name);
        foreach (var patch in patches)
            harmony.Patch(patch.Target, Hook(patch.Prefix), Hook(patch.Postfix), finalizer: Hook(patch.Finalizer));
    }

    private static void Safe(Action action)
    {
        if (!Mod.RealTimeActive) return;
        try { action(); }
        catch (Exception ex) { Mod.Disable(ex); }
    }

    private static void Lifecycle(Action action)
    {
        if(!Mod.Enabled) return;
        try { action(); }
        catch(Exception ex)
        {
            pendingLoad=false; pendingNewGame=false; openingForClose=false; NewGameChoiceUI.Clear();
            Mod.ResetSession(); Mod.LogChoice("State unavailable; vanilla remains active: "+ex.GetType().Name);
        }
    }
    private static bool CreationChoicePrefix(MainMenuUIController __instance) => NewGameChoiceUI.BeforeStart(__instance);
    private static void OpenCreationSetupPostfix(MainMenuUIController __instance) => Lifecycle(()=>NewGameChoiceUI.Open(__instance));
    private static void BackCreationSetupPostfix() => Lifecycle(()=>NewGameChoiceUI.Back());
    private static void FinalCreationSetupPrefix() => NewGameChoiceUI.EnterFinalStart();
    private static Exception? FinalCreationSetupFinalizer(Exception? __exception) => NewGameChoiceUI.LeaveFinalStart(__exception);
    private static void ClearCreationChoicePrefix() => Lifecycle(() =>
    {
        NewGameChoiceUI.Clear(); Mod.ResetSession();
        pendingLoad=false; pendingNewGame=false; openingForClose=false;
    });

    private static void NewGamePrefix() => Lifecycle(() =>
    {
        bool selected=NewGameChoiceUI.Consume();
        Mod.ResetSession(); Mod.Session.BeginCreation(selected);
        goods.Clear();
        Mod.ClearNotices();
        Mod.Clock.Deactivate(); pendingLoad = false; openingForClose = false; pendingNewGame = true;
        scriptedIds.Clear(); Mod.Rebase();
    });
    private static Exception? NewGameFinalizer(Exception? __exception)
    {
        if(__exception!=null) { pendingNewGame=false; Lifecycle(()=> { NewGameChoiceUI.Clear(); Mod.ResetSession(); }); }
        return __exception;
    }
    private static void FreshDayPostfix() => Safe(() =>
    {
        // StartNewGame precedes InitialSave's slot/GUID allocation. Its clock begins
        // only after the first native slot write, never with the provisional -1/empty identity.
        if (!pendingLoad && !pendingNewGame) { Mod.FreshDay(); openingForClose = false; }
    });
    private static void ClosedPostfix() => Safe(() => { goods.Clear(); Mod.Trace("native-EndDay observed; key=" + Mod.Clock.DayKey); Mod.Clock.ConfirmClosed(); });
    private static void CalendarPostfix(StoreCalendar __instance) => Safe(() => Mod.Calendar(__instance));
    private static void DepartedPostfix() => Safe(() =>
    {
        if (PlayerStore.Instance.currentClientInstance == null) { goods.Clear(); patrolVisit.Clear(); Mod.Clock.CustomerDeparted(); }
    });
    private static void TradePrefix(out bool __state) { __state = Mod.RealTimeActive; if (__state) tradeDepth++; }
    private static Exception? TradeFinalizer(bool __state, Exception? __exception)
    { if (__state) tradeDepth = Math.Max(0, tradeDepth - 1); return __exception; }
    private static void CustomerGoodsPostfix(GameItem gameItem, bool isOwend)
    {
        if(!Mod.RealTimeActive) return;
        try
        {
            var visit = PlayerStore.Instance.currentClientInstance;
            // Only successful native placement for this ordinary visitor establishes provenance.
            if (!Mod.RealTimeActive || isOwend || visit == null || !IsOrdinary(visit.storeClient) || gameItem == null ||
                gameItem.state == null || gameItem.parentInventory == null || GeneralHelper.IsItemOwned(gameItem)) return;
            var entry=EmporiumEntry.Instance;
            if (entry == null) return;
            var parent=gameItem.parentInventory.Pointer;
            if (entry.frontInvinvElement != null && parent == entry.frontInvinvElement.Pointer ||
                entry.backInvinvElement != null && parent == entry.backInvinvElement.Pointer)
                goods.Record(visit.Pointer.ToInt64(), gameItem.Pointer.ToInt64(), gameItem.uniqueId);
        }
        catch (Exception ex) { Mod.Trace("customer-goods provenance unavailable: " + ex.GetType().Name); }
    }

    private static void BusyPrefix() { if (Mod.Enabled) { busyDepth++; Mod.Rebase(); } }
    private static Exception? BusyFinalizer(Exception? __exception)
    {
        busyDepth = Math.Max(0, busyDepth - 1);
        Mod.Rebase();
        return __exception; // Never swallow the game's own failures.
    }
    private static void LoadPrefix() => Lifecycle(() =>
    {
        NewGameChoiceUI.Clear(); Mod.ResetSession();
        goods.Clear();
        Mod.ClearNotices();
        Mod.Clock.Deactivate(); pendingLoad = true; openingForClose = false; pendingNewGame = false;
        Mod.Paused = false; scriptedIds.Clear(); BusyPrefix();
    });
    private static Exception? LoadFinalizer(Exception? __exception)
    {
        if (__exception != null) { pendingLoad = false; Mod.ResetSession(); }
        return BusyFinalizer(__exception);
    }

    internal static bool FinishPendingLoad()
    {
        if (!pendingLoad || busyDepth != 0 || !InShop || IsBlackScreen()) return false;
        pendingLoad = false;
        var store = PlayerStore.Instance;
        string? customer = IsOrdinary(store.currentClientInstance?.storeClient) ? CustomerKey() : null;
        bool consent=SaveOptInStore.TryRead(SavePath(),store.saveSlotId,store.runID);
        bool restored=consent && SaveSidecar.TryRestore(SavePath(),Mod.Clock,DayKey(),customer,out _);
        Mod.Session.Restore(restored,store.saveSlotId,store.runID);
        if(restored) Mod.Notify("Real-time enabled for this opted-in save. Timers restored.");
        else { Mod.ResetSession(); Mod.LogChoice("No valid explicit new-game consent and timer state; existing save stays vanilla."); }
        Mod.Rebase();
        return true;
    }
    private static void SavedPostfix() => Lifecycle(() =>
    {
        if (pendingLoad || !InShop) return;
        string nativeKey = DayKey();
        if (pendingNewGame)
        {
            pendingNewGame = false;
            openingForClose = false;
            if(!Mod.Session.PendingEnabled)
            { Mod.ResetSession(); Mod.LogChoice("New game created in vanilla mode; no timer or consent sidecar written."); return; }
            Mod.InitializeNewClock(nativeKey);
            // Consent commits only after native identity allocation and successful timer persistence.
            SaveSidecar.Write(SavePath(),Mod.Clock);
            SaveOptInStore.Write(SavePath(),PlayerStore.Instance.saveSlotId,PlayerStore.Instance.runID,true);
            Mod.Session.CompleteCreation(PlayerStore.Instance.saveSlotId,PlayerStore.Instance.runID);
            Mod.Notify("Real-time enabled for this new game.");
            return;
        }
        if(!Mod.RealTimeActive) return; // Vanilla/legacy saves and their sidecars are untouched.
        // Never move elapsed time across runs, slots or days, or persist a provisional key.
        SaveIdentity.RequireMatchingClock(Mod.Clock, nativeKey);
        try
        {
            SaveSidecar.Write(SavePath(), Mod.Clock);
            SaveOptInStore.Write(SavePath(),PlayerStore.Instance.saveSlotId,PlayerStore.Instance.runID,true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Stop writing timed state after a failed sidecar write; the native save remains valid.
            Mod.ResetSession();
            Mod.LogChoice("Could not persist opted-in state; vanilla remains active: " + ex.GetType().Name);
        }
    });

    private static string? CustomerKey() => PlayerStore.Instance.currentClientInstance?.storeClient?.identifier;
    private static void ScriptedFactoryPostfix(StoreClient? __result) => Lifecycle(() =>
    {
        if(!Mod.RealTimeActive && !(Mod.Session.PendingCreation && Mod.Session.PendingEnabled)) return;
        if (!string.IsNullOrEmpty(__result?.identifier)) scriptedIds.Add(__result.identifier);
    });
    private static bool IsOrdinary(StoreClient? client)
    {
        if (client == null || !client.dismissable || client.isSecurity || string.IsNullOrEmpty(client.identifier) ||
            scriptedIds.Contains(client.identifier) || client.OnArrival != null || client.OnDismiss != null) return false;
        // ponytail: conservative intent allowlist; add specialty visitors only after their scripts are verified.
        return client.clientIntent is StoreClient.ClientIntent.BUY or StoreClient.ClientIntent.SELL or
            StoreClient.ClientIntent.SELLNBUY or StoreClient.ClientIntent.BARTER or
            StoreClient.ClientIntent.WHOLESALE or StoreClient.ClientIntent.APPRAISAL_SERVICE;
    }

    // Only this reviewed native factory establishes patrol provenance. Deserialized/unrecognized
    // officers stay protected; no identifier-only bypass of story/security factories.
    private static void PatrolFactoryPostfix(StoreClient? __result) => Safe(() =>
    {
        if (__result == null || __result.identifier != "patrolOfficer" || !__result.isSecurity ||
            __result.clientIntent != StoreClient.ClientIntent.BUY || __result.OnDismiss != null ||
            __result.OnArrival == null || __result.isSentenceOfficer || patrolIntroductions.Count >= 256) return;
        var lines = new List<Dialogue>();
        var seen = new HashSet<long>();
        var line = __result.mainDialogue;
        bool hasEndAction = false;
        while (line != null)
        {
            if (lines.Count >= 64 || !seen.Add(line.Pointer.ToInt64()) || line.HasChoices()) return;
            lines.Add(line); hasEndAction |= line.endAction != null; line = line.nextDialogue;
        }
        if (lines.Count > 0 && hasEndAction) patrolIntroductions[__result.Pointer.ToInt64()] = lines.ToArray();
    });
    private static long CurrentPatrolVisit()
    {
        if (!Mod.RealTimeActive || !InShop) return 0;
        var instance = PlayerStore.Instance.currentClientInstance;
        var client = instance?.storeClient;
        return instance != null && client != null && patrolIntroductions.ContainsKey(client.Pointer.ToInt64()) ?
            instance.Pointer.ToInt64() : 0;
    }
    private static long ObservePatrol(Func<long> observe)
    {
        if (!Mod.Enabled) return 0;
        try { return observe(); }
        catch (Exception ex) { Mod.Disable(ex); return 0; }
    }
    private static void ObservePatrol(Action observe) => ObservePatrol(() => { observe(); return 0L; });
    private static void PatrolArrivalPrefix(out long __state)
    {
        __state = ObservePatrol(() =>
        {
            long visit = CurrentPatrolVisit();
            if (visit != 0) patrolVisit.BeginArrival(visit);
            return visit;
        });
    }
    private static Exception? PatrolArrivalFinalizer(long __state, Exception? __exception)
    {
        ObservePatrol(() =>
        {
            if (__state != 0) patrolVisit.EndArrival(__state, __exception == null && CurrentPatrolVisit() == __state);
        });
        return __exception;
    }
    private static void PatrolTextPrefix(DialogUIManager __instance, out long __state)
    {
        __state = ObservePatrol(() =>
        {
            long visit = CurrentPatrolVisit();
            var line = __instance.currentDialog;
            if (visit != 0 && line != null && line.endAction != null && !line.isEndActionExecuted)
                patrolVisit.ScheduleAction(visit, line.Pointer.ToInt64());
            return visit;
        });
    }
    private static Exception? PatrolTextFinalizer(long __state, Exception? __exception)
    {
        ObservePatrol(() => { if (__exception != null) patrolVisit.Fault(__state); });
        return __exception;
    }
    private static void PatrolActionPrefix(out long __state)
    {
        __state = ObservePatrol(() =>
        {
            long visit = CurrentPatrolVisit();
            patrolVisit.BeginAction(visit);
            return visit;
        });
    }
    private static Exception? PatrolActionFinalizer(Dialogue __instance, long __state, Exception? __exception)
    {
        // Native sets isEndActionExecuted before invoking the callback: success must be witnessed
        // after it returns. Interrupted/hidden bubbles cannot erase a scheduled consequence.
        ObservePatrol(() =>
        {
            if (__state != 0) patrolVisit.EndAction(__state,
                __instance.isEndActionExecuted ? __instance.Pointer.ToInt64() : 0, __exception == null);
        });
        return __exception;
    }
    private static bool PatrolCanLeaveAtClosing(StoreClient? client, bool ready, bool nativeQuiet, ClosingBlocker blockers)
    {
        if (client == null) return false;
        bool known = patrolIntroductions.TryGetValue(client.Pointer.ToInt64(), out var intro);
        bool introComplete = known && client.isMainDialogueStarted &&
            intro!.All(line => line.endAction == null || line.isEndActionExecuted);
        var dialog = DialogUIManager.Instance;
        bool dialogueQuiet = dialog != null && dialog.currentDialog == null &&
            dialog.displayingUncompletedDialog == null && !dialog.isWaitingForManualAdvance &&
            !dialog.isSecMonologuePlaying && !DialogueVisible();
        var store = PlayerStore.Instance;
        return patrolVisit.Settled(CurrentPatrolVisit()) && PatrolClosingPolicy.Allows(new PatrolClosingFacts(
            known, client.identifier ?? "", client.clientIntent.ToString(), client.isSecurity,
            scriptedIds.Contains(client.identifier ?? ""), client.isSentenceOfficer || client.isWanted ||
                client.isStealthWanted || !string.IsNullOrEmpty(client.eventSourceId), client.OnDismiss != null,
            client.dismissable && store.IsClientDimissable(), ready, introComplete, dialogueQuiet, PatrolInspectionQuiet(),
            nativeQuiet && store.storeState == PlayerStore.StoreState.OPEN && store.lockStorePhysicalFlag == 0 &&
                store.lockNextBellFlag == 0 && store.lockShutterButtonFlag == 0 && tradeDepth == 0, blockers));
    }

    private static bool PatrolInspectionQuiet()
    {
        var inspection = InspectionUIManager.Instance;
        var store = PlayerStore.Instance;
        if (inspection == null || store == null) return false;
        return PatrolInspectionSafety.Quiet(new PatrolInspectionFacts(
            inspection.panel != null && inspection.confrontPanel != null && inspection.appraisalPanel != null,
            inspection.panel != null && inspection.panel.activeInHierarchy,
            inspection.confrontPanel != null && inspection.confrontPanel.activeInHierarchy,
            inspection.appraisalPanel != null && inspection.appraisalPanel.activeInHierarchy,
            inspection.currentInspectingItem != null, store.isSecurityAlarmActive));
    }

    internal static bool NativeNoticeSafe => InShop && !IsBlackScreen() && !PlayerStore.Instance.cinematicMode;
    private static CustomerFacts Facts(StoreClient? client) => client == null ? new CustomerFacts("", "", false, false, false, false, false) :
        new CustomerFacts(client.identifier ?? "", client.clientIntent.ToString(), client.dismissable, client.isSecurity,
            scriptedIds.Contains(client.identifier ?? ""), client.OnArrival != null, client.OnDismiss != null);
    internal static void TraceCustomer(ShopFrame frame, string holdReason)
    {
        try
        {
            var instance = PlayerStore.Instance.currentClientInstance;
            var c = Mod.Clock;
            Mod.TraceObservation(new CustomerObservation(instance == null ? 0 : instance.Pointer.ToInt64(), Facts(instance?.storeClient),
                frame.Ready, frame.Suspended, frame.Protected, frame.SafeToDismiss, c.Phase.ToString(), c.Elapsed, c.PatienceLeft, c.GraceLeft, holdReason, frame.ClosingOnlyDismissal));
        }
        catch (Exception ex) { Mod.Trace("customer observation failed: " + ex.GetType().Name); }
    }
    private static void DepartureDiagnosticPrefix()
    {
        if(!Mod.RealTimeActive) return;
        try { var f=Facts(PlayerStore.Instance.currentClientInstance?.storeClient); Mod.Trace("native-dismiss-enter id="+f.Identifier+"; classification="+f.Reason); }
        catch (Exception ex) { Mod.Trace("dismiss observation failed: "+ex.GetType().Name); }
    }
    private static string lastAdmission = "";
    private static void TraceAdmission(bool allowed)
    {
        try
        {
            var queue=PlayerStore.Instance.storeClientManager.clientStack;
            var first=Facts(queue.Count==0?null:queue[0]);
            string state="native-admission phase="+Mod.Clock.Phase+"; allowed="+allowed+"; queued="+queue.Count+"; first="+first.Identifier+"; reason="+first.Reason;
            if(state!=lastAdmission){lastAdmission=state;Mod.Trace(state);}
        }
        catch(Exception ex){Mod.Trace("admission observation failed: "+ex.GetType().Name);}
    }

    private static bool IsBlackScreen()
    {
        var image = StoreUIManager.Instance.blackscreen;
        return image != null && image.gameObject.activeInHierarchy && image.color.a > 0.01f;
    }
    private static bool TutorialActive()
    {
        var tutorial = TutorialUIManager.Instance;
        if (tutorial == null) return false;
        // Only visible hints suspend time; tutorialEnable remains set beyond the tutorial itself.
        // Check the actual hint panels, not parent containers that may stay active when empty.
        foreach (var hint in tutorialHints)
            if (Visible((GameObject?)hint.GetValue(tutorial))) return true;
        return false;
    }
    private static bool SafeToAct()
    {
        var store = PlayerStore.Instance;
        var ui = StoreUIManager.Instance;
        return !Busy && !Mod.TimersHeld && !IsBlackScreen() && !store.cinematicMode &&
            StoreShutterButton.Instance != null && (StoreClientMono.Instance == null || !StoreClientMono.Instance.IsMoving()) &&
            (ui.confirmPanelUIManager == null || !Visible(ui.confirmPanelUIManager.panel)) &&
            (NegociationUIManager.Instance == null || !Visible(NegociationUIManager.Instance.confirmPanel)) &&
            store.lockAllFlag == 0 && store.lockClientClickFlag == 0 && store.lockAllInventoryFlag == 0 &&
            !store.isClientBeingArrested && !store.isHandlingShootout && !ui.isTyping && !ui.isAboutToStartMainDialog &&
            !UIDragHandler.isDraggingAnyUI && (ItemMouseDragHandler.current == null || !ItemMouseDragHandler.current.IsDraggingItem) &&
            !Input.GetMouseButton(0) && !Input.GetMouseButton(1);
    }

    private static ClosingBlocker ClosingBlockers()
    {
        var store = PlayerStore.Instance;
        var ui = StoreUIManager.Instance;
        ClosingBlocker result = ClosingBlocker.None;
        if (Mod.TimersHeld) result |= ClosingBlocker.Paused;
        if (Busy || IsBlackScreen() || store.cinematicMode) result |= ClosingBlocker.Transition;
        if (StoreClientMono.Instance != null && StoreClientMono.Instance.IsMoving()) result |= ClosingBlocker.Animation;
        if (ui.confirmPanelUIManager != null && Visible(ui.confirmPanelUIManager.panel)) result |= ClosingBlocker.Confirmation;
        if (UIDragHandler.isDraggingAnyUI || ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem ||
            Input.GetMouseButton(0) || Input.GetMouseButton(1)) result |= ClosingBlocker.Dragging;
        var dialog = DialogUIManager.Instance;
        if (ui.isTyping || ui.isAboutToStartMainDialog || dialog != null &&
            (dialog.isWaitingForManualAdvance || dialog.IsChoice() || !dialog.IsCurrentDialogEnd())) result |= ClosingBlocker.Dialogue;
        if (tradeDepth > 0) result |= ClosingBlocker.TradeCommit;
        result |= NegotiationBlocker(result);
        result |= InspectTables();
        return result;
    }

    private static string? lastNegotiationTrace;
    private static ClosingBlocker NegotiationBlocker(ClosingBlocker other)
    {
        try
        {
            var store=PlayerStore.Instance;
            var nego=NegociationUIManager.Instance;
            var bargain=BargainUIManager.Instance;
            var back=EmporiumEntry.Instance?.backInvinvElement;
            bool known=nego!=null && nego.uiObject!=null && nego.confirmPanel!=null &&
                bargain!=null && bargain.uiObject!=null && back!=null;
            var storeItem=store.currentNegociatedItem;
            var uiItem=nego?.currentNegociatedItem;
            bool KnownSelection(GameItem? item) => item==null || back!=null && item.state!=null &&
                !string.IsNullOrEmpty(item.identifier) && item.parentInventory!=null &&
                item.parentInventory.Pointer==back.Pointer;
            bool quiet=other==ClosingBlocker.None && SafeToAct() && tradeDepth==0 &&
                store.lockStorePhysicalFlag==0 && store.lockNextBellFlag==0;
            var facts=new NegotiationFacts(known,Visible(nego?.uiObject),Visible(nego?.confirmPanel),
                Visible(bargain?.uiObject),quiet,KnownSelection(storeItem)&&KnownSelection(uiItem));
            bool held=NegotiationSafety.Held(facts);
            string trace=$"negotiation facts known={known} offer={facts.OfferVisible} confirm={facts.ConfirmationVisible} bargain={facts.BargainVisible} quiet={quiet} selectionKnown={facts.SelectionKnown} storeItem={storeItem?.Pointer.ToInt64()??0} uiItem={uiItem?.Pointer.ToInt64()??0} visit={store.currentClientInstance?.Pointer.ToInt64()??0} tradeDepth={tradeDepth} held={held}";
            if(trace!=lastNegotiationTrace) { lastNegotiationTrace=trace; Mod.Trace(trace); }
            return held?ClosingBlocker.Negotiation:ClosingBlocker.None;
        }
        catch(Exception ex)
        {
            string trace="negotiation facts unknown: "+ex.GetType().Name;
            if(trace!=lastNegotiationTrace) { lastNegotiationTrace=trace; Mod.Trace(trace); }
            return ClosingBlocker.Negotiation;
        }
    }

    private static ClosingBlocker InspectTables()
    {
        try
        {
            var entry=EmporiumEntry.Instance;
            if(entry==null || entry.frontInvinvElement==null || entry.backInvinvElement==null || entry.backInvinvElementCounter==null)
                return ClosingBlocker.UnknownTable;
            var instance=PlayerStore.Instance.currentClientInstance;
            long visit=instance==null?0:instance.Pointer.ToInt64();
            bool ordinary=instance!=null && IsOrdinary(instance.storeClient);
            var facts=new List<TableItemFacts>();
            void Inspect(GameGridInventory inventory, bool cleanupScope)
            {
                var items=inventory.childItems;
                if(items==null || items.Count>4096) throw new InvalidOperationException("Unknown table inventory");
                for(int i=0;i<items.Count;i++)
                {
                    var item=items[i];
                    if(item==null || item.state==null || item.parentInventory==null || string.IsNullOrEmpty(item.identifier) ||
                        item.parentInventory.Pointer!=inventory.Pointer)
                    { facts.Add(new TableItemFacts(null,false,false,cleanupScope)); continue; }
                    bool owned=GeneralHelper.IsItemOwned(item);
                    bool nested=false;
                    if(!owned)
                    {
                        var children=item.FindAllChildItems();
                        if(children==null) { facts.Add(new TableItemFacts(null,false,false,cleanupScope)); continue; }
                        for(int j=0;j<children.Count;j++)
                            if(children[j]==null || children[j].Pointer!=item.Pointer) { nested=true; break; }
                    }
                    facts.Add(new TableItemFacts(owned,goods.Known(visit,item.Pointer.ToInt64(),item.uniqueId),nested,cleanupScope));
                }
            }
            Inspect(entry.frontInvinvElement,true); Inspect(entry.backInvinvElement,true);
            var counter=entry.backInvinvElementCounter;
            bool alias=counter.Pointer==entry.frontInvinvElement.Pointer || counter.Pointer==entry.backInvinvElement.Pointer;
            if(!alias) Inspect(counter,false);
            var assessment=TableOwnershipPolicy.Assess(facts,ordinary);
            Mod.TraceTableAssessment(assessment);
            return assessment.Safe?ClosingBlocker.None:ClosingBlocker.UnknownTable;
        }
        catch(Exception ex) { Mod.Trace("table ownership unknown: "+ex.GetType().Name); return ClosingBlocker.UnknownTable; }
    }

    internal static NativePauseInputs PauseInputs(bool focused, bool applicationPaused)
    {
        var ui = StoreUIManager.Instance;
        var escape = ui.escapeUIManager;
        return new NativePauseInputs(true, focused, applicationPaused,
            escape != null && Visible(escape.menuPanel),
            ui.gameOptionUIManager != null && Visible(ui.gameOptionUIManager.optionPanel),
            escape != null && Visible(escape.playtestExitUI),
            escape != null && escape.isPendingTransition);
    }

    internal static bool NativeExitPending => StoreUIManager.Instance.escapeUIManager != null &&
        StoreUIManager.Instance.escapeUIManager.isPendingTransition;


    private static bool Visible(GameObject? value) => value != null && value.activeInHierarchy;
    internal static bool NativeReportVisible =>
        (StartOfDayUIManager.Instance != null && Visible(StartOfDayUIManager.Instance.UIPanel)) ||
        (AdvCalendarUIManager.Instance != null && Visible(AdvCalendarUIManager.Instance.UIPanel) &&
            Visible(AdvCalendarUIManager.Instance.nightlyReportSection));

    private static bool DialogueVisible()
    {
        // Native Display*Text/HideBubbles toggle these exact bubble objects.
        // Conversation/current-item references can survive the UI: never use them as a timer hold.
        var dialog = DialogUIManager.Instance;
        if (Visible(dialog?.clientBubble?.gameObject) || Visible(dialog?.playerBubble?.gameObject) ||
            Visible(dialog?.secBubble?.gameObject)) return true;
        var choices = dialog?.dialogChoiceBubbles;
        if (choices != null)
            for (int i = 0; i < choices.Count; i++)
                if (Visible(choices[i]?.gameObject)) return true;
        // Trade, bargain and confirmation panels alone consume active time.
        // ClosingBlockers independently protects unfinished transactions, including after grace expires.
        return false;
    }

    internal static ShopFrame Frame(out string reason)
    {
        var store = PlayerStore.Instance;
        var client = store.currentClientInstance?.storeClient;
        bool protectedVisit = client != null && !IsOrdinary(client);
        bool tutorial = TutorialActive();
        bool dialogue = DialogueVisible();
        bool suspended = dialogue || Busy || IsBlackScreen() || store.cinematicMode || tutorial ||
            NativeReportVisible ||
            store.isClientBeingArrested || store.isHandlingShootout || store.playerLocation != PlayerStore.PlayerLocation.IN_STORE;
        reason = dialogue ? "Clock held for dialogue." : tutorial ? "Clock held for the tutorial." : suspended ? "Clock held during transition." : "";
        bool canAct = SafeToAct();
        var closingBlockers = ClosingBlockers();
        bool canClose = store.storeState == PlayerStore.StoreState.OPEN && canAct && client == null &&
            store.lockShutterButtonFlag == 0 && !HasProtectedQueued() && ClosingSafety.Safe(closingBlockers) && store.CanEndDay();
        bool ready = store.isClientArrived && StoreClientMono.Instance != null && !StoreClientMono.Instance.IsMoving();
        bool patrolClosing = PatrolCanLeaveAtClosing(client, ready, canAct && !suspended, closingBlockers);
        return new ShopFrame(CustomerKey(), ready, protectedVisit, suspended,
            client != null && canAct && store.IsClientDimissable() && (IsOrdinary(client) || patrolClosing),
            canClose, closingBlockers, patrolClosing);
    }

    private static bool HasProtectedQueued()
    {
        var queue = PlayerStore.Instance.storeClientManager.clientStack;
        for (int i = 0; i < queue.Count; i++) if (!IsOrdinary(queue[i])) return true;
        return false;
    }
    private static bool CanAdmit()
    {
        if (!Mod.Clock.AdmissionClosed) return true;
        if (Mod.Clock.Phase == Phase.Closed) return false;
        var queue = PlayerStore.Instance.storeClientManager.clientStack;
        // Ordinary queued customers go home at closing, matching native EndDay; protected visits retain order.
        while (queue.Count > 0 && IsOrdinary(queue[0])) queue.RemoveAt(0);
        return queue.Count > 0;
    }
    private static bool AdmissionPrefix()
    {
        if (!Mod.RealTimeActive || !InShop || !Mod.Clock.Timed) return true;
        bool cutoff = Mod.Clock.AdmissionClosed;
        try { bool allowed = CanAdmit(); TraceAdmission(allowed); return allowed; }
        catch (Exception ex) { Mod.Trace("admission guard failure: " + ex.GetType().Name); return !cutoff; }
    }
    private static void CanNextPostfix(ref bool __result)
    {
        if (!__result || !Mod.RealTimeActive || !InShop || !Mod.Clock.AdmissionClosed) return;
        try
        {
            __result = Mod.Clock.Phase == Phase.Closing && HasProtectedQueued();
        }
        catch (Exception ex) { Mod.Disable(ex); }
    }
    private static bool OpenPrefix() => !Mod.RealTimeActive || !InShop || Mod.Clock.Phase != Phase.Closed;
    private static string lastSleepBlock = "";
    private static string announcedEvening = "";
    private static bool SleepSafe(out string reason)
    {
        var store=PlayerStore.Instance;
        if (store.storeState!=PlayerStore.StoreState.CLOSED || Mod.Clock.Phase!=Phase.Closed)
        {reason="The shop must be closed.";return false;}
        if (store.playerLocation!=PlayerStore.PlayerLocation.IN_STORE)
        {reason="Sleep waits until you return to the shop.";return false;}
        if (!SafeToAct() || TutorialActive() || DialogueVisible() || NativeReportVisible || NativeExitPending ||
            store.currentClientInstance!=null || HasProtectedQueued() ||
            store.isSecurityAlarmActive || store.lockShutterButtonFlag!=0 ||
            !ClosingSafety.Safe(ClosingBlockers()) ||
            (GameOverUIManager.Instance!=null && Visible(GameOverUIManager.Instance.uiPanel)))
        {reason="Sleep waits for the current interaction or transition to finish.";return false;}
        reason="";return true;
    }
    internal static void EveningUpdate(double seconds)
    {
        var sleepUI=EndOfDayUIManager.Instance;
        bool report=sleepUI!=null && Visible(sleepUI.UI);
        var store=PlayerStore.Instance;
        // Outings consume evening time too, but SleepSafe never pulls the player
        // out of an outing or an unfinished transaction. The clock caps at 03:00.
        bool held=Busy || IsBlackScreen() || store.cinematicMode || TutorialActive() || DialogueVisible() || NativeReportVisible ||
            store.isClientBeingArrested || store.isHandlingShootout || NativeExitPending;
        double before=Mod.Clock.EveningMinute ?? Mod.Clock.MinuteOfDay;
        Mod.Clock.AdvanceEvening(seconds, Mod.TimersHeld || held || report);
        if(before<1560 && Mod.Clock.EveningMinute>=1560) Mod.Notify("One hour until bedtime (03:00). Sleeping late delays tomorrow's opening.");
        if (announcedEvening!=Mod.Clock.DayKey)
        {announcedEvening=Mod.Clock.DayKey;Mod.Notify("The shop is closed. Time continues until you sleep; bedtime is 03:00.");}
        if(!Mod.Clock.SleepDue || Mod.Clock.SleepRequested)return;
        if(!SleepSafe(out string reason)) { SleepBlocked(reason); return; }
        if(sleepUI==null){SleepBlocked("The native sleep screen is unavailable.");return;}
        if(!report)
        {
            var bed=UnityEngine.Object.FindObjectOfType<StoreBedroll>();
            if(bed==null){SleepBlocked("The bedroll is unavailable.");return;}
            bed.OnClick();
            return; // Native UI must finish opening before its enabled state is read.
        }
        sleepUI.RefreshUI();
        if(sleepUI.endDayButton==null || !sleepUI.endDayButton.IsInteractable() || !Visible(sleepUI.endDayButton.gameObject))
        {SleepBlocked("Sleep is due, but the native sleep requirements are not met. Resolve the displayed warning.");return;}
        sleepUI.OnEndDayButtonClick();
    }
    private static void SleepBlocked(string reason)
    {
        if(lastSleepBlock==reason)return;
        lastSleepBlock=reason;Mod.Notify(reason);
    }
    private static bool SleepPrefix(out bool __state)
    {
        __state=false;
        if(!Mod.RealTimeActive)return true;
        try
        {
            if(Mod.Clock.SleepRequested)return false;
            if(!SleepSafe(out string reason)){SleepBlocked(reason);return false;}
            var ui=EndOfDayUIManager.Instance;
            if(ui==null || !Visible(ui.UI) || ui.endDayButton==null)return false;
            ui.RefreshUI();
            if(!ui.endDayButton.IsInteractable() || !Visible(ui.endDayButton.gameObject))return false;
            bool outing=PlayerStore.Instance.healthData.wentToAfterhoursLastNight;
            if(!Mod.Clock.RequestSleep(outing))return false;
            __state=true;lastSleepBlock="";
            Mod.LoggerInstance.Msg("SLEEP-REQUEST day="+Mod.Clock.DayKey+"; bedtime="+Mod.Clock.EveningMinute+"; wake="+Mod.Clock.PendingWakeMinute+"; nativeOuting="+outing);
            return true;
        }
        catch(Exception ex){Mod.Disable(ex);return false;}
    }
    private static Exception? SleepFinalizer(bool __state, Exception? __exception)
    {
        if(__state && __exception!=null)Mod.Clock.CancelSleepRequest();
        return __exception;
    }
    internal static void HandleClosing()
    {
        if(!Mod.RealTimeActive) return;
        var store = PlayerStore.Instance;
        if (!SafeToAct() || !ClosingSafety.Safe(ClosingBlockers()) || store.currentClientInstance != null) return;
        if (store.storeState == PlayerStore.StoreState.MORNING)
        {
            // Native customer generation happens at first opening, including rent/story visits.
            // Run that path once even if preparation consumed the whole day; admission is already closed.
            if (!openingForClose && store.lockShutterButtonFlag == 0 && store.CanStartDay())
            {
                openingForClose = true;
                Mod.Notify("Closing time: resolving required visits before shutting the shop.");
                StoreShutterButton.Instance.OpenShutter();
            }
            return;
        }
        if (store.storeState == PlayerStore.StoreState.OPEN && CanAdmit() && store.CanNextClient())
        {
            Mod.Trace("closing-only automatic dispatch; elapsed=" + Mod.Clock.Elapsed);
            store.TryCallNextClient();
        }
    }
}


