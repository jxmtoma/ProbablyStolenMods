using Il2Cpp;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using Il2CppTMPro;

namespace RealTimeShop;

internal static class NewGameChoiceUI
{
    private static readonly NewGameSetupSelection selection=new();
    private static GameObject? owned;
    private static Toggle? toggle;
    private static UnityAction<bool>? listener;
    private static int finalStartDepth;
    private static MainMenuUIController? setupMenu;
    private static Toggle? nativeVisibilitySource;
    internal static void Clear()
    {
        selection.Clear();
        if(owned!=null) { owned.SetActive(false); UnityEngine.Object.Destroy(owned); }
        owned=null;toggle=null;listener=null;setupMenu=null;nativeVisibilitySource=null;
    }
    internal static void Open(MainMenuUIController menu)
    {
        Clear();
        try
        {
            var data=NewGameData.Instance;
            if(Plugin.Current?.Enabled!=true||data==null||data.isLoadFromMainMenu||menu.newGamePanel==null||!menu.newGamePanel.activeInHierarchy)return;
            var source=menu.endlessToggle;var hard=menu.hardToggle;
            if(source==null||hard==null)throw new InvalidOperationException("Native mode controls unavailable");
            var nativeRect=source.transform.Cast<RectTransform>();var hardRect=hard.transform.Cast<RectTransform>();
            if(nativeRect.parent==null||nativeRect.parent.Pointer!=hardRect.parent.Pointer||nativeRect.anchorMin!=hardRect.anchorMin||nativeRect.anchorMax!=hardRect.anchorMax||nativeRect.pivot!=hardRect.pivot)throw new InvalidOperationException("Native mode row changed");
            Vector2 stride=nativeRect.anchoredPosition-hardRect.anchoredPosition;
            if(stride.x<=0||Math.Abs(stride.y)>1)throw new InvalidOperationException("Native mode spacing changed");
            var parent=nativeRect.parent.Cast<RectTransform>();
            var start=menu.startGameButton;
            if(start==null)throw new InvalidOperationException("Native setup Start button unavailable");
            var startRect=start.transform.Cast<RectTransform>();
            if(startRect.parent==null||startRect.parent.Pointer!=parent.Pointer)throw new InvalidOperationException("Native Start parent changed");
            float footprintHeight=nativeRect.rect.height*Math.Abs(nativeRect.localScale.y);
            if(source.targetGraphic!=null)footprintHeight=Math.Max(footprintHeight,source.targetGraphic.rectTransform.rect.height*Math.Abs(source.targetGraphic.rectTransform.lossyScale.y/parent.lossyScale.y));
            float rowBottom=nativeRect.localPosition.y+nativeRect.rect.yMin*Math.Abs(nativeRect.localScale.y);
            rowBottom=Math.Min(rowBottom,nativeRect.localPosition.y-footprintHeight/2);
            float startTop=startRect.localPosition.y+startRect.rect.yMax*Math.Abs(startRect.localScale.y);
            if(rowBottom-startTop<footprintHeight+8)throw new InvalidOperationException("Native controls leave no vertical gap");
            float centerY=(rowBottom+startTop)/2;
            var position=nativeRect.anchoredPosition+new Vector2(startRect.localPosition.x-nativeRect.localPosition.x,centerY-nativeRect.localPosition.y);
            var staging=new GameObject("RealTimeShopSetupStaging");staging.SetActive(false);
            try
            {
                owned=UnityEngine.Object.Instantiate(source.gameObject,staging.transform,false);owned.name="RealTimeModeToggle";
                toggle=owned.GetComponent<Toggle>();if(toggle==null)throw new InvalidOperationException("Native toggle missing");
                // Replace persistent serialized callbacks as well as runtime listeners.
                toggle.onValueChanged=new Toggle.ToggleEvent();toggle.group=null;
                toggle.SetIsOnWithoutNotify(false);toggle.interactable=true;
                var labels=owned.GetComponentsInChildren<TextMeshProUGUI>(true);
                if(labels.Length!=1)throw new InvalidOperationException("Native toggle label changed");
                foreach(var component in owned.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if(component.TryCast<Toggle>()!=null||component.TryCast<Image>()!=null||component.TryCast<TextMeshProUGUI>()!=null)continue;
                    component.enabled=false;UnityEngine.Object.Destroy(component);
                }
                labels[0].text="Real-time mode";
                long menuIdentity=menu.Pointer.ToInt64(),dataIdentity=data.Pointer.ToInt64();
                long ticket=selection.Begin(menuIdentity,dataIdentity);
                listener=(UnityAction<bool>)(System.Action<bool>)(enabled=>
                {
                    if(ticket!=selection.Generation||selection.Starting)return;
                    try
                    {
                        var current=NewGameData.Instance;
                        if(current==null||Plugin.Current?.Enabled!=true||!menu.newGamePanel.activeInHierarchy||!selection.Select(ticket,menu.Pointer.ToInt64(),current.Pointer.ToInt64(),enabled,current.isLoadFromMainMenu))
                        { selection.Back();if(toggle!=null)toggle.SetIsOnWithoutNotify(false); return; }
                        Plugin.Current.LogChoice("New-game setup Real-time mode="+enabled);
                    }
                    catch(Exception ex){Clear();Plugin.Current?.LogChoice("Setup choice failed OFF: "+ex.GetType().Name);}
                });
                toggle.onValueChanged.AddListener(listener);
                owned.SetActive(false);owned.transform.SetParent(parent,false);
                var rect=owned.transform.Cast<RectTransform>();
                // Reparenting through a non-RectTransform staging object must not define
                // the final native row geometry. Restore the complete source basis.
                rect.anchorMin=nativeRect.anchorMin;rect.anchorMax=nativeRect.anchorMax;
                rect.pivot=nativeRect.pivot;rect.sizeDelta=nativeRect.sizeDelta;
                rect.localRotation=nativeRect.localRotation;rect.localScale=nativeRect.localScale;
                var nativePosition=nativeRect.anchoredPosition3D;
                rect.anchoredPosition3D=new Vector3(position.x,position.y,nativePosition.z);
                setupMenu=menu;nativeVisibilitySource=source;
                Refresh();
                Plugin.Current.LogChoice("New-game setup toggle installed; default OFF; centered between native mode row and Start Game.");
            }
            finally { UnityEngine.Object.Destroy(staging); }
        }
        catch(Exception ex) { Clear(); Plugin.Current?.LogChoice("New-game setup toggle unavailable; vanilla remains active: "+ex.GetType().Name+" "+ex.Message); }
    }
    internal static void Refresh()
    {
        if(owned==null)return;
        try
        {
            if(setupMenu==null||nativeVisibilitySource==null||setupMenu.newGamePanel==null){Clear();return;}
            bool visible=setupMenu.newGamePanel.activeInHierarchy&&nativeVisibilitySource.gameObject.activeSelf;
            if(setupMenu.newGamePanel.activeInHierarchy&&!nativeVisibilitySource.gameObject.activeSelf)
            { selection.Back();if(toggle!=null)toggle.SetIsOnWithoutNotify(false); }
            if(owned.activeSelf!=visible)owned.SetActive(visible);
        }
        catch(Exception ex){Clear();Plugin.Current?.LogChoice("Setup visibility failed OFF: "+ex.GetType().Name);}
    }
    internal static void Back()
    { if(finalStartDepth>0)return;selection.Back();if(toggle!=null)toggle.SetIsOnWithoutNotify(false);Plugin.Current?.LogChoice("New-game setup returned from perks; mode reset OFF."); }
    internal static void EnterFinalStart() => finalStartDepth++;
    internal static Exception? LeaveFinalStart(Exception? error)
    { finalStartDepth=Math.Max(0,finalStartDepth-1);if(error!=null)Clear();return error; }
    internal static bool BeforeStart(MainMenuUIController menu)
    {
        var data=NewGameData.Instance;
        selection.Start(menu.Pointer.ToInt64(),data?.Pointer.ToInt64()??0,data==null||data.isLoadFromMainMenu);
        Plugin.Current?.LogChoice("Native Start; captured setup mode="+selection.Enabled);return true;
    }
    internal static bool Consume()
    {
        var data=NewGameData.Instance;
        bool enabled=selection.Consume(data?.Pointer.ToInt64()??0,data==null||data.isLoadFromMainMenu);
        Clear();return enabled;
    }
}
