using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace OnYourMark
{
    public enum MissionStage : byte { Lobby = 0, FirstCrossing = 1, ApproachLaser = 3, Split = 4, Reunion = 6, Climb = 7, Ending = 9 }

    public partial class WaveManager
    {
        [Header("Cooperative mission")]
        [SerializeField, Min(1f)] float arrivalRadius = 5f;
        readonly NetworkVariable<MissionStage> stage = new();
        readonly NetworkVariable<int> leftStep = new(), rightStep = new();
        public MissionStage Stage => stage.Value;
        public MissionRoute Route { get; private set; }
        public bool HasMission => Route;
        public bool MissionSilent => Stage == MissionStage.Ending;
        public static WaveManager Current { get; private set; }
        public int Phase => Stage switch
        {
            MissionStage.FirstCrossing or MissionStage.ApproachLaser => 1,
            MissionStage.Split => 2,
            MissionStage.Reunion => 3,
            MissionStage.Climb or MissionStage.Ending => 4, _ => 0
        };
        CrossHudState crossHud;
        CalloutDirector radio;
        bool encounterPending, swordAcross;
        int checkpoint = 1;
        byte checkpointParts;
        bool awaitingPlacement;
        double placementsUntil;
        public static bool Sword(Player p) => p.Stats.showDashOnHud;
        static Player Pilot(bool sword) => Player.All.FirstOrDefault(p => p && Sword(p) == sword);
        double Clock => NetworkManager.ServerTime.Time;
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
            if (next == 2 && !restore) crossHud.SetOwnership(0);
            if (restore) crossHud.SetOwnership(checkpointParts);
            checkpoint = next; checkpointParts = crossHud.Ownership;
            leftStep.Value = rightStep.Value = 0;
            swordAcross = false;
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
                3 => MissionStage.Reunion, _ => MissionStage.Climb };
            if (next == 3)
            {
                radio?.MissionCue("control_split_wave");
                StartEncounter(1);
            }
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
                MissionStage.ApproachLaser => Route.deck[3].position,
                MissionStage.Split => Route.Lane(sword, Mathf.Min((sword ? leftStep : rightStep).Value, Route.upperLane.Length - 1)),
                MissionStage.Reunion => Route.deck[12].position,
                MissionStage.Climb => Route.deck[16].position,
                _ => p.transform.position
            };
        }
        IEnumerator FinishMission()
        {
            yield return new WaitForSecondsRealtime(3);
            allWavesCleared.Raise();
        }
        void Update()
        {
            if (!IsSpawned || !Route || Stage is MissionStage.Lobby or MissionStage.Ending) return;
            if (!IsServer) return;
            var sword = Pilot(true); var gun = Pilot(false);
            if (!sword || !gun || (sword.Health.IsDead && gun.Health.IsDead)) return;
            if (awaitingPlacement && Clock < placementsUntil &&
                (!Near(sword,Route.Checkpoint(checkpoint,true)) || !Near(gun,Route.Checkpoint(checkpoint,false)))) return;
            awaitingPlacement = false;
            switch (Stage)
            {
                case MissionStage.FirstCrossing:
                    if (Near(sword, Route.deck[1].position) || sword.Health.IsDead) swordAcross = true;
                    if (swordAcross && ((gun.IsBot && !gun.Motor.Grounded && Route.LeavingFirstHull(gun.transform.position)) || gun.Health.IsDead || Near(gun,Route.deck[1].position)))
                        stage.Value = MissionStage.ApproachLaser;
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
                    if (!encounterPending && Arrived(sword,Objective(sword)) && Arrived(gun,Objective(gun)))
                    {
                        stage.Value = MissionStage.Ending;
                        StartCoroutine(FinishMission());
                    }
                    break;
            }
        }
    }
}
