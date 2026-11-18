using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using OnYourMark.UI;

public static class HudPreviewCapture
{
    public static async Task<string> Run()
    {
        if(!EditorApplication.isPlaying)throw new InvalidOperationException("Enter Play mode first.");
        if(Screen.width!=1920||Screen.height!=1080)throw new InvalidOperationException("Set the Game View to 1920x1080.");
        var hud=UnityEngine.Object.FindFirstObjectByType<OnYourMarkHud>();
        bool motion=hud.reducedMotion;
        Directory.CreateDirectory(".gameharness/artifacts");
        try
        {
            hud.reducedMotion=true;
            foreach(var name in new[]{"field","channels","support","request","critical","awakened"})
            {
                hud.SetPreviewState(name=="request"?OnYourMarkHud.PreviewState.Request:name=="critical"?OnYourMarkHud.PreviewState.Critical:name=="awakened"?OnYourMarkHud.PreviewState.Awakened:OnYourMarkHud.PreviewState.Field);
                if(name=="channels")hud.ActivateAction("channels");
                if(name=="support")hud.ActivateAction("support");
                await Task.Delay(400);
                ScreenCapture.CaptureScreenshot(Path.GetFullPath(".gameharness/artifacts/unity-"+name+".png"));
                await Task.Delay(500);
            }
            return "Captured six 1920x1080 Game View previews.";
        }
        finally {hud.reducedMotion=motion;hud.SetPreviewState(OnYourMarkHud.PreviewState.Field);}
    }
}
