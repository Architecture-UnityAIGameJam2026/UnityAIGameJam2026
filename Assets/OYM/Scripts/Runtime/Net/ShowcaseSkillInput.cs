using OnYourMark.UI;
using OYM.Signal;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OYM.Net
{
    [DefaultExecutionOrder(80)]
    public sealed class ShowcaseSkillInput : MonoBehaviour
    {
        internal enum Edge : byte { Press, Release, Cancel }
        internal static ShowcaseSkillInput Current { get; private set; }

        CoopHud _hud;
        ZeroGFlight _held, _fromPartner;
        bool _sentToPartner;

        void Awake() { _hud = GetComponent<CoopHud>(); Current = this; }
        void OnDisable() { EndPress(Edge.Cancel); EndPartnerPress(Edge.Cancel); }
        void OnDestroy() { if (Current == this) Current = null; }
        void OnApplicationFocus(bool focus) { if (!focus) EndPress(Edge.Cancel); }

        static bool InPlay => Time.timeScale > 0f && SignalInstall.PlayerView(HuskEncounter.Current);
        static bool CanCast(ZeroGFlight pilot) => pilot && pilot.isActiveAndEnabled && InPlay &&
            (!HuskEncounter.Current || HuskEncounter.Current.PlayerAlive);

        void LateUpdate()
        {
            var game = CoopGame.Current;
            var camera = FlightCamera.Main;
            var pilot = _hud.SkillPilot;
            bool remote = game && pilot && pilot == game.RemoteFlight;
            bool canInput = InPlay && camera && camera.InputLive && camera.target &&
                camera.target.TryGetComponent<ZeroGFlight>(out var me) && !me.scripted &&
                (remote ? game.PartnerInGame && !game.PartnerDown : CanCast(pilot));

            // A release belongs to the same pilot as its press. A relay, Tab, or cutscene
            // in between cancels the hold instead of spending either pilot's cooldown.
            if (_held && (!canInput || _held != pilot)) EndPress(Edge.Cancel);
            if (_fromPartner && (_hud.OwnSkill || !game || !game.PartnerInGame || !CanCast(_fromPartner)))
                EndPartnerPress(Edge.Cancel);
            if (!canInput) return;
#if ENABLE_INPUT_SYSTEM
            var key = Keyboard.current?.eKey;
            bool down = key != null && key.wasPressedThisFrame;
            bool up = key != null && key.wasReleasedThisFrame;
#else
            bool down = Input.GetKeyDown(KeyCode.E);
            bool up = Input.GetKeyUp(KeyCode.E);
#endif
            if (down && !_held)
            {
                _held = pilot;
                _sentToPartner = remote;
                Deliver(Edge.Press);
            }
            if (up) EndPress(Edge.Release);
        }

        void EndPress(Edge edge)
        {
            if (_held) Deliver(edge);
            _held = null;
        }

        void Deliver(Edge edge)
        {
            if (!_sentToPartner) Apply(_held, edge);
            else if (CoopSession.InGame && CoopSession.Active && CoopSession.Active.Connected)
                CoopSession.Active.Send(new CoopMsg.W(CoopMsg.SkillInput).U8((byte)edge).Done());
        }

        // Each machine runs its own pilot's ability, including aim, hits and cooldown.
        internal void FromPartner(ZeroGFlight localPilot, Edge edge)
        {
            if (_hud.OwnSkill || !CanCast(localPilot) || edge == Edge.Cancel)
            {
                EndPartnerPress(Edge.Cancel);
                return;
            }
            if (edge == Edge.Press && !_fromPartner)
            {
                _fromPartner = localPilot;
                Apply(localPilot, edge);
            }
            else if (edge == Edge.Release) EndPartnerPress(edge);
        }

        void EndPartnerPress(Edge edge)
        {
            if (_fromPartner) Apply(_fromPartner, edge);
            _fromPartner = null;
        }

        static void Apply(ZeroGFlight pilot, Edge edge)
        {
            if (pilot is KestrelController kestrel)
            {
                if (edge == Edge.Press) kestrel.CommandStrike();
            }
            else if (pilot.TryGetComponent<OrbitSalvo>(out var salvo)) salvo.Command(edge);
        }

        internal static HudSkillPanel.Telemetry Telemetry(ZeroGFlight pilot)
        {
            if (pilot is KestrelController kestrel)
                return new HudSkillPanel.Telemetry { valid = kestrel.swiftStrike, eSkill = HudSkillPanel.ESkill.SwiftStrike,
                    eActive = kestrel.Striking, eCooldownLeft = kestrel.StrikeCooldownLeft, eCooldown = kestrel.strikeCooldown };
            if (pilot && pilot.TryGetComponent<OrbitSalvo>(out var salvo))
                return new HudSkillPanel.Telemetry { valid = true, eSkill = HudSkillPanel.ESkill.MissileSalvo,
                    eActive = salvo.Locking, eCooldownLeft = salvo.CooldownLeft, eCooldown = salvo.cooldown };
            return default;
        }
    }
}
