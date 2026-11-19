using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    public enum MissionStage : byte { Lobby, FirstCrossing, Deflect, ApproachLaser, Split, Land, Reunion, Climb, Strike, Ending }

    public partial class WaveManager
    {
        [Header("Cooperative mission")]
        [SerializeField, Min(.1f)] float ringWindow = .32f;
        [SerializeField, Min(1f)] float ringLead = 1.65f;
        [SerializeField, Range(.1f, 1f)] float ringTimeScale = .45f;
        [SerializeField, Min(0)] float missedFragmentDamage = 35f;
        [SerializeField, Min(1f)] float arrivalRadius = 5f;
        readonly NetworkVariable<MissionStage> stage = new();
        readonly NetworkVariable<double> beat = new();
        readonly NetworkVariable<byte> tried = new(), hits = new(), firstRole = new(), skillRole = new(255);
        readonly NetworkVariable<int> leftStep = new(), rightStep = new();
        public MissionStage Stage => stage.Value;
        public MissionRoute Route { get; private set; }
        public bool HasMission => Route;
        public bool InQte => Stage is MissionStage.Deflect or MissionStage.Land or MissionStage.Strike;
        public bool MissionSilent => Stage == MissionStage.Ending;
        public static WaveManager Current { get; private set; }
        public int Phase => Stage switch
        {
            MissionStage.FirstCrossing or MissionStage.Deflect or MissionStage.ApproachLaser => 1,
            MissionStage.Split => 2,
            MissionStage.Land or MissionStage.Reunion => 3,
            MissionStage.Climb or MissionStage.Strike or MissionStage.Ending => 4, _ => 0
        };
        CrossHudState crossHud;
        CalloutDirector radio;
        bool encounterPending, swordAcross, gunJumped;
        int checkpoint = 1;
        byte checkpointParts;
        double retryAt;
        bool retrying;
        bool awaitingPlacement;
        double placementsUntil;
        public static bool TakesCombatInput => Current && Current.InQte;
        public static bool Sword(Player p) => p.Stats.showDashOnHud;
        static Player Pilot(bool sword) => Player.All.FirstOrDefault(p => p && Sword(p) == sword);
        double Clock => NetworkManager.ServerTime.Time;
        public double RingTime(bool sword) => beat.Value + ((Stage == MissionStage.Deflect || firstRole.Value == (sword ? 0 : 1)) ? 0 : .95f);
        public bool RingIsSkill(bool sword) => skillRole.Value == (sword ? 0 : 1);
        bool Near(Player p, Vector3 target) => p && Vector3.Distance(p.transform.position, target) <= arrivalRadius;
        bool Arrived(Player p, Vector3 target) => p && (p.Health.IsDead || Near(p, target));

        void MissionSpawn()
        {
            Current = this;
            Route = FindFirstObjectByType<MissionRoute>();
            crossHud = FindFirstObjectByType<CrossHudState>();
            radio = FindFirstObjectByType<CalloutDirector>();
            stage.OnValueChanged += StageChanged;
            StageChanged(MissionStage.Lobby, Stage);
        }
        void MissionDespawn()
        {
            stage.OnValueChanged -= StageChanged;
            ReleaseSlowMotion();
            ClearMissionView();
            if (Current == this) Current = null;
        }
        void BeginMission()
        {
            if (!Route) { StartCoroutine(Run()); return; }
            checkpoint = 1;
            crossHud.SetOwnership(7);
            EnterPhase(1, false);
        }
        void EnterPhase(int next, bool restore)
        {
            encounterPending = false;
            retryAt = 0;
            retrying = false;
            tried.Value = hits.Value = 0;
            if (next == 2 && !restore) crossHud.SetOwnership(0);
            if (restore) crossHud.SetOwnership(checkpointParts);
            checkpoint = next; checkpointParts = crossHud.Ownership;
            leftStep.Value = rightStep.Value = 0;
            swordAcross = gunJumped = false;
            awaitingPlacement = restore;
            placementsUntil = Clock + 1.5;
            foreach (var p in Player.All)
            {
                if (restore) p.Health.Restore();
                else if (p.Health.IsDead) p.Health.Revive(.5f);
                else continue;
                p.MoveToCheckpoint(Route.Checkpoint(next, Sword(p)));
            }
            stage.Value = next switch { 1 => MissionStage.FirstCrossing, 2 => MissionStage.Split,
                3 => MissionStage.Land, _ => MissionStage.Climb };
            if (next == 3) BeginRing(MissionStage.Land);
            if (next == 1) { radio?.MissionCue("control_first_wave_inbound"); StartEncounter(0); }
            if (next == 2) radio?.MissionCue("control_beam_release");
            if (next == 4) { radio?.MissionCue("control_third_wave"); StartEncounter(2); }
        }
        public void RestoreCheckpoint()
        {
            if (!IsServer || !Route || Stage is MissionStage.Lobby or MissionStage.Ending) return;
            Stop();
            EnterPhase(checkpoint, true);
        }
        void StartEncounter(int index)
        {
            betweenWaves.Value = false;
            waveIndex.Value = index;
            encounterPending = true;
            StartCoroutine(Encounter(index));
        }
        IEnumerator Encounter(int index)
        {
            yield return new WaitForSeconds(firstWaveDelay);
            yield return Spawn(waves[index]);
            yield return new WaitUntil(() => alive.Value == 0);
            encounterPending = false;
            betweenWaves.Value = true;
            waveCleared.Raise();
        }
        Vector3 EncounterSpawn(int i, Vector3 original)
        {
            if (!Route) return original;
            int deck = WaveNumber switch { 2 => 10 + i % 2, 3 => 14 + i % 2, _ => 1 + i % 2 };
            var center = Route.deck[deck].position;
            var wanted = center + Vector3.right * ((i % 3) - 1) * 1.35f;
            return NavMesh.SamplePosition(wanted, out var hit, 4, NavMesh.AllAreas) ? hit.position : center;
        }
        public Vector3 Objective(Player p)
        {
            bool sword = Sword(p);
            return Stage switch
            {
                MissionStage.FirstCrossing => Route.deck[sword || swordAcross ? 1 : 0].position,
                MissionStage.Deflect => Route.deck[1].position,
                MissionStage.ApproachLaser => Route.deck[3].position,
                MissionStage.Split => Route.Lane(sword, Mathf.Min((sword ? leftStep : rightStep).Value, Route.upperLane.Length - 1)),
                MissionStage.Land or MissionStage.Reunion => Route.deck[Stage == MissionStage.Land ? 9 : 12].position,
                MissionStage.Climb or MissionStage.Strike => Route.deck[16].position,
                _ => p.transform.position
            };
        }
        void BeginRing(MissionStage which)
        {
            stage.Value = which;
            retrying = false; tried.Value = hits.Value = 0;
            var sword = Pilot(true); var gun = Pilot(false);
            firstRole.Value = (byte)(which == MissionStage.Strike && gun && gun.Health.IsDead ? 1 : 0);
            skillRole.Value = (byte)(which != MissionStage.Strike ? 255 :
                sword && sword.Health.IsDead ? 0 : gun && gun.Health.IsDead ? 1 : 255);
            beat.Value = Clock + ringLead;
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void InputRpc(MissionStage atStage, double atBeat, double stamp, RpcParams args = default)
        {
            var pilot = Player.All.FirstOrDefault(p => !p.IsBot && p.OwnerClientId == args.Receive.SenderClientId);
            if (!pilot || atStage != Stage) return;
            if (Stage == MissionStage.FirstCrossing && !Sword(pilot)) { if (swordAcross && Route.LeavingFirstHull(pilot.transform.position)) gunJumped = true; return; }
            if (InQte && atBeat == beat.Value && stamp >= Clock - .65 && stamp <= Clock + .15) Judge(pilot, stamp);
        }
        void Judge(Player p, double stamp)
        {
            int role = Sword(p) ? 0 : 1;
            byte bit = (byte)(1 << role);
            if (retrying || (tried.Value & bit) != 0) return;
            tried.Value |= bit;
            if (System.Math.Abs(stamp - RingTime(role == 0)) <= ringWindow)
            {
                hits.Value |= bit;
                if (Stage == MissionStage.Strike && RingIsSkill(Sword(p)) && p.Partner)
                    p.Partner.Ultimate.ActivateFree(p.Partner.Ultimate.Data.duration);
                else if (Stage is MissionStage.Strike or MissionStage.Deflect)
                    PresentStrikeRpc(role == 0);
            }
        }
        [Rpc(SendTo.Everyone)]
        void PresentStrikeRpc(bool sword)
        {
            var pilot = Pilot(sword);
            if (!pilot || !pilot.TryGetComponent<PilotVisuals>(out var visuals)) return;
            if (sword) visuals.Slash();
            else visuals.Recoil(false);
        }
        void ReadMissionInput(Player p)
        {
            if (!p || !CursorLock.IsLocked) return;
            var keyboard = Keyboard.current; var mouse = Mouse.current;
            bool jump = keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
            if (Stage == MissionStage.FirstCrossing && !Sword(p) && jump) InputRpc(Stage, 0, Clock);
            if (!InQte) return;
            bool pressed = Stage == MissionStage.Land ? jump : RingIsSkill(Sword(p)) ?
                keyboard != null && keyboard.qKey.wasPressedThisFrame : mouse != null && mouse.leftButton.wasPressedThisFrame;
            if (pressed) InputRpc(Stage, beat.Value, Clock);
        }
        void TickRing(Player sword, Player gun)
        {
            if (retrying) { if (Clock >= retryAt) BeginRing(Stage); return; }
            foreach (var p in Player.All)
                if (p.IsBot && Clock >= RingTime(Sword(p))) Judge(p, RingTime(Sword(p)));
            if (tried.Value != 3 && Clock <= System.Math.Max(RingTime(true), RingTime(false)) + ringWindow + .65) return;
            if (Stage == MissionStage.Deflect)
            {
                if ((hits.Value & 1) == 0) gun.Health.Damage(missedFragmentDamage);
                if ((hits.Value & 2) == 0) sword.Health.Damage(missedFragmentDamage);
                stage.Value = MissionStage.ApproachLaser;
            }
            else if (hits.Value != 3)
            {
                retrying = true; retryAt = Clock + 1.2; tried.Value = 3;
            }
            else if (Stage == MissionStage.Land)
            {
                foreach (var p in Player.All) p.MoveToCheckpoint(Route.Checkpoint(3, Sword(p)));
                stage.Value = MissionStage.Reunion;
                radio?.MissionCue("control_split_wave");
                StartEncounter(1);
            }
            else
            {
                stage.Value = MissionStage.Ending;
                StartCoroutine(FinishMission());
            }
        }
        IEnumerator FinishMission()
        {
            yield return new WaitForSecondsRealtime(3);
            allWavesCleared.Raise();
        }
        void Update()
        {
            if (!IsSpawned || !Route || Stage is MissionStage.Lobby or MissionStage.Ending) return;
            ReadMissionInput(Player.Local);
            if (!IsServer) return;
            var sword = Pilot(true); var gun = Pilot(false);
            if (!sword || !gun || (sword.Health.IsDead && gun.Health.IsDead)) return;
            if (awaitingPlacement && Clock < placementsUntil &&
                (!Near(sword,Route.Checkpoint(checkpoint,true)) || !Near(gun,Route.Checkpoint(checkpoint,false)))) return;
            awaitingPlacement = false;
            if (InQte) { TickRing(sword, gun); return; }
            switch (Stage)
            {
                case MissionStage.FirstCrossing:
                    if (Near(sword, Route.deck[1].position) || sword.Health.IsDead) swordAcross = true;
                    if (swordAcross && (gunJumped || (gun.IsBot && !gun.Motor.Grounded && Route.LeavingFirstHull(gun.transform.position)) || gun.Health.IsDead || Near(gun,Route.deck[1].position))) BeginRing(MissionStage.Deflect);
                    break;
                case MissionStage.ApproachLaser:
                    if (!encounterPending && Arrived(sword,Objective(sword)) && Arrived(gun,Objective(gun))) EnterPhase(2,false);
                    break;
                case MissionStage.Split:
                    foreach (var p in Player.All)
                    {
                        var step = Sword(p) ? leftStep : rightStep;
                        if (step.Value < Route.upperLane.Length && Arrived(p,Objective(p))) step.Value++;
                    }
                    if (leftStep.Value == Route.upperLane.Length && rightStep.Value == Route.lowerLane.Length) EnterPhase(3,false);
                    break;
                case MissionStage.Reunion:
                    if (!encounterPending && Arrived(sword,Objective(sword)) && Arrived(gun,Objective(gun))) EnterPhase(4,false);
                    break;
                case MissionStage.Climb:
                    if (!encounterPending && Arrived(sword,Objective(sword)) && Arrived(gun,Objective(gun))) BeginRing(MissionStage.Strike);
                    break;
            }
        }
    }
}
