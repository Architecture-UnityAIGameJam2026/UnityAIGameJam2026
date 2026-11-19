using System.Reflection;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OYM.Signal
{
    /// Play-test shortcut (editor / development builds): F11 calls the CITADEL now, skipping the zones, to check the
    /// boss entrance + music, the red-light weak points, lock-on and the radio without flying the whole level.
    public class SignalDevKeys : MonoBehaviour
    {
        static MethodInfo s_startBoss;

        void Update()
        {
            if (!Application.isEditor && !Debug.isDebugBuild) return;
            bool f11;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            f11 = k != null && k.f11Key.wasPressedThisFrame;
#else
            f11 = Input.GetKeyDown(KeyCode.F11);
#endif
            if (!f11) return;
            var e = HuskEncounter.Current;
            var lv = e ? e.Level : null;
            if (!lv || !e.Gate || lv.InCutaway || lv.beat == HuskLevel.Beat.Boss || lv.beat == HuskLevel.Beat.Victory) return;
            e.Engaged = true;                                        // normally set at first contact: the fight needs it
            if (s_startBoss == null) s_startBoss = typeof(HuskLevel).GetMethod("StartBoss", BindingFlags.NonPublic | BindingFlags.Instance);
            s_startBoss?.Invoke(lv, null);
            Debug.Log("[OYM-SIGNAL] F11: CITADEL called (play-test shortcut)");
        }
    }
}
