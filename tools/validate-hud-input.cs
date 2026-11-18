using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using OnYourMark.UI;

public static class HudInputValidation
{
    static Keyboard keyboard;
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static async Task Keys(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
        await Task.Delay(180);
    }
    static async Task Press(Key key){await Keys(key);await Keys();}
    public static async Task<string> Run()
    {
        Check(EditorApplication.isPlaying,"Enter Play mode before validation.");
        Check(Application.isFocused,"This optional live-keyboard check requires the Unity Game view in the foreground. Use validate-hud-interaction.cs for focus-independent UGUI event validation.");
        var hud=UnityEngine.Object.FindFirstObjectByType<OnYourMarkHud>();
        var previousKeyboard=Keyboard.current;
        keyboard=InputSystem.AddDevice<Keyboard>("SenseLinkValidationKeyboard");
        keyboard.MakeCurrent();
        bool motion=hud.reducedMotion;
        int support=-1;
        Action<int> onSupport=channel=>support=channel;
        hud.SupportRequested+=onSupport;
        try
        {
            await Press(Key.F1);await Press(Key.E);
            Check(!hud.returned[0],"E bypassed the objective gate.");
            for(int i=0;i<3;i++)
            {
                await Press(Key.F1);await Press(new[]{Key.Digit1,Key.Digit2,Key.Digit3}[i]);
                Check(hud.pendingChannel==i&&!hud.returned[i],"Request input mismatch: "+i+" / "+hud.pendingChannel);
                await Press(Key.Enter);
                Check(hud.returned[i]&&hud.pendingChannel==-1,"Enter did not confirm matching consent.");
            }
            await Press(Key.F2);Check(hud.state==OnYourMarkHud.PreviewState.Request,"F2 failed.");
            await Press(Key.Escape);Check(hud.pendingChannel==-1,"Escape did not cancel.");
            await Press(Key.F3);Check(hud.state==OnYourMarkHud.PreviewState.Critical,"F3 failed.");
            await Press(Key.F4);Check(hud.state==OnYourMarkHud.PreviewState.Awakened&&hud.returned[0],"F4 failed.");
            await Press(Key.F1);
            await Keys(Key.Q);await Task.Delay(250);
            Check(hud.transform.Find("SupportWheel").GetComponent<CanvasGroup>().alpha>.9f,"Hold Q did not open support wheel.");
            await Keys(Key.Q,Key.Digit2);await Keys();
            Check(support==1&&hud.pendingChannel==-1,"Support selection requested an exchange or failed.");
            await Press(Key.R);Check(hud.reducedMotion!=motion,"R did not toggle reduced motion.");
            hud.SetPartnerTelemetry(.78f,1);Check(hud.state==OnYourMarkHud.PreviewState.Critical,"Low reserves alert failed.");
            hud.SetPartnerTelemetry(.78f,2);Check(hud.state==OnYourMarkHud.PreviewState.Field,"Reserves recovery failed.");
            hud.RequestExchange(0);
            typeof(OnYourMarkHud).GetField("requestAt",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(hud,Time.unscaledTime-11);
            hud.ConfirmExchange(0);Check(!hud.returned[0]&&hud.pendingChannel==-1,"Expired request was accepted.");
            return "PASS: F1-F4, 1/2/3 + Enter mutual consent, Esc, objective-gated E, hold Q + selection, R, supply threshold/recovery, stale consent rejection.";
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);previousKeyboard?.MakeCurrent();
            hud.SupportRequested-=onSupport;hud.reducedMotion=motion;hud.SetPreviewState(OnYourMarkHud.PreviewState.Field);
        }
    }
}
