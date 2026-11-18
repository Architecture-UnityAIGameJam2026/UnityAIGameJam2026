using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using OnYourMark.UI;

// Validate UGUI hit-testing and its pointer event route without depending on OS window focus.
public static class HudInteractionValidation
{
 static OnYourMarkHud hud;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static PointerEventData Pointer(string target)
 {
  var rt=hud.transform.Find(target).GetComponent<RectTransform>();
  return new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(hud.GetComponent<Canvas>().worldCamera,rt.TransformPoint(rt.rect.center)),button=PointerEventData.InputButton.Left};
 }
 static async Task Click(string target)
 {
  var p=Pointer(target);var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(p,hits);
  Check(hits.Count>0&&hits[0].gameObject.name==target,"Hit-test failed for "+target);
  var go=hits[0].gameObject;
  ExecuteEvents.Execute(go,p,ExecuteEvents.pointerEnterHandler);
  ExecuteEvents.Execute(go,p,ExecuteEvents.pointerDownHandler);
  ExecuteEvents.Execute(go,p,ExecuteEvents.pointerUpHandler);
  ExecuteEvents.Execute(go,p,ExecuteEvents.pointerClickHandler);
  ExecuteEvents.Execute(go,p,ExecuteEvents.pointerExitHandler);
  await Task.Delay(250);
 }
 public static async Task<string> Run()
 {
  Check(EditorApplication.isPlaying,"Enter Play mode.");
  hud=UnityEngine.Object.FindFirstObjectByType<OnYourMarkHud>();
  int support=-1;Action<int> listener=c=>support=c;hud.SupportRequested+=listener;
  bool previousMotion=hud.reducedMotion;
  try
  {
   hud.SetPreviewState(OnYourMarkHud.PreviewState.Field);await Task.Delay(250);
   var map=hud.transform.Find("Spatial").GetComponent<RectTransform>();
   Check(map.anchoredPosition.x>1500&&map.anchoredPosition.y>-100&&map.sizeDelta==new Vector2(240,250),"Reference layout requires a rectangular top-right map.");
   var vitals=hud.transform.Find("Vitals").GetComponent<RectTransform>();
   Check(vitals.anchoredPosition.x<100&&vitals.anchoredPosition.y>-100&&vitals.sizeDelta.x>400,"Health must be a wide top-left bar.");
   foreach(var name in new[]{"ToggleChannels","ToggleSupport","QuickShield","QuickScan"}){
    var slot=hud.transform.Find(name).GetComponent<RectTransform>();
    Check(slot.anchoredPosition.x<400&&slot.anchoredPosition.y<-900,"Quick action left the bottom-left corner.");
   }
   hud.reducedMotion=true;hud.SetPartnerTelemetry(.25f,6);await Task.Delay(120);
   var healthBar=hud.transform.Find("Vitals/Health bar").GetComponent<RectTransform>();
   Check(Mathf.Abs(healthBar.sizeDelta.x-70)<.1f,"Horizontal health fill does not match telemetry.");
   hud.SetPreviewState(OnYourMarkHud.PreviewState.Awakened);await Task.Delay(120);
   Check(Mathf.Abs(healthBar.sizeDelta.x-257.6f)<.1f,"Restored health bar did not switch to local telemetry.");
   hud.reducedMotion=previousMotion;hud.SetPreviewState(OnYourMarkHud.PreviewState.Field);
   var labels=hud.GetComponentsInChildren<Text>(true);
   Check(labels.Length==3&&labels.All(t=>t.text.All(char.IsDigit)),"HUD should contain only health, ammo and distance numbers.");
   var rifle=hud.transform.Find("Reserves/Primary weapon").GetComponent<HudGeometry>();
   var sword=hud.transform.Find("Reserves/Melee weapon").GetComponent<HudGeometry>();
   Check(rifle.rectTransform.sizeDelta.x/rifle.rectTransform.sizeDelta.y>3&&sword.rectTransform.sizeDelta.x/sword.rectTransform.sizeDelta.y>4,"Weapon silhouettes lost their long aspect ratios.");
   foreach(var button in hud.GetComponentsInChildren<HudActionButton>(true)){var area=button.GetComponent<RectTransform>();Check(area.sizeDelta.x>=44&&area.sizeDelta.y>=44,"Icon hit target too small.");}
   Check(hud.transform.Find("Channel0").GetComponent<CanvasGroup>().alpha==0,"Channel details visible at rest.");
   Check(!hud.transform.Find("Channel0").GetComponent<CanvasGroup>().blocksRaycasts,"Hidden tray intercepts input.");
   await Click("QuickShield");Check(support==0,"Quick shield slot failed.");
   await Click("QuickScan");Check(support==1,"Quick scan slot failed.");
   var hover=hud.transform.Find("ToggleChannels").gameObject;
   ExecuteEvents.Execute(hover,Pointer("ToggleChannels"),ExecuteEvents.pointerEnterHandler);await Task.Delay(250);
   Check(hover.GetComponent<Image>().color.a>.1f,"Hover highlight did not animate.");
   ExecuteEvents.Execute(hover,Pointer("ToggleChannels"),ExecuteEvents.pointerExitHandler);
   await Click("ToggleChannels");Check(hud.openPanel==OnYourMarkHud.Panel.Channels,"LINK click failed.");
   await Click("Channel0");Check(hud.pendingChannel==0&&!hud.returned[0],"Channel click bypassed or missed consent.");
   await Click("CancelExchange");Check(hud.pendingChannel==-1,"Cancel failed.");
   await Click("ToggleChannels");await Click("Channel1");
   Check(hud.transform.Find("Exchange/Channel icon").GetComponent<HudGeometry>().iconName=="map","Requested map icon mismatch.");
   hud.ConfirmExchange(1);
   Check(hud.transform.Find("Spatial/Ownership icon").GetComponent<HudGeometry>().iconName=="self","Local ownership icon missing.");
   await Click("ToggleChannels");await Click("Channel2");
   Check(hud.transform.Find("Exchange/Channel icon").GetComponent<HudGeometry>().iconName=="ammo","Requested reserves icon mismatch.");
   await Click("CancelExchange");
   await Click("ToggleSupport");Check(hud.openPanel==OnYourMarkHud.Panel.Support,"SUPPORT click failed.");
   await Click("Support1");Check(support==1&&hud.openPanel==OnYourMarkHud.Panel.Closed,"Support selection failed.");
   Check(hud.transform.Find("Toast").GetComponent<CanvasGroup>().alpha>.95f,"Request feedback missing.");
   await Click("ToggleChannels");
   Check(hud.transform.Find("Toast").GetComponent<CanvasGroup>().alpha==0,"Feedback overlaps the icon tray.");
   await Click("CloseTray");Check(hud.openPanel==OnYourMarkHud.Panel.Closed,"Close failed.");
   await Click("ToggleSupport");await Click("ToggleChannels");
   Check(hud.openPanel==OnYourMarkHud.Panel.Channels&&hud.transform.Find("SupportWheel").GetComponent<CanvasGroup>().alpha==0,"Two trays visible at once.");
   return "PASS: four-corner layout, horizontal health telemetry and ownership, quick slots, UGUI raycast + pointer open/select/cancel/close/support, hover, toast feedback, one tray at a time, hidden tray raycast blocking. "+OnYourMark.UI.Editor.HudShowcaseBuilder.Validate();
  }
  finally{hud.SupportRequested-=listener;hud.reducedMotion=previousMotion;hud.SetPreviewState(OnYourMarkHud.PreviewState.Field);}
 }
}
