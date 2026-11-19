using System.Collections;
using System.Collections.Generic;
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
        bool swordAcross, gunJumped;
        EnemyData rusher, hover;
        readonly NetworkVariable<int> climbDeck = new(13);
        double firstContactDeadline;
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
        bool Near(Player p, Vector3 target, float radius = -1f) => p && Vector3.Distance(p.transform.position, target) <= (radius > 0 ? radius : arrivalRadius);
        bool Arrived(Player p, Vector3 target) => p && (p.Health.IsDead || Near(p, target));

        public bool WaitingAtSplitRally(Player p) => Stage == MissionStage.Split &&
            (Sword(p) ? leftStep.Value >= Route.upperLane.Length : rightStep.Value >= Route.lowerLane.Length);

        void MissionSpawn()
        {
            Current = this;
            Route = FindFirstObjectByType<MissionRoute>();
            crossHud = FindFirstObjectByType<CrossHudState>();
            radio = FindFirstObjectByType<CalloutDirector>();
            foreach (var wave in waves ?? System.Array.Empty<WaveData>())
                if (wave && wave.groups != null)
                    foreach (var g in wave.groups)
                    {
                        if (g.enemy is RusherData && !rusher) rusher = g.enemy;
                        else if (g.enemy is HoverLaserData && !hover) hover = g.enemy;
                    }
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
            if (next == 1)
            {
                radio?.MissionCue("control_first_wave_inbound");
                waveIndex.Value = 0;
                firstContactDeadline = Clock + 25;
            }
            if (next == 2) radio?.MissionCue("control_beam_release");
            if (next == 4)
            {
                waveIndex.Value = 2;
                climbDeck.Value = 13;
                radio?.MissionCue("control_third_wave");
                StartEncounter("tier1", 4, 1, Route.deck[13].position);
            }
        }
        public void RestoreCheckpoint()
        {
            if (!IsServer || !Route || Stage is MissionStage.Lobby or MissionStage.Ending) return;
            Stop();
            EnterPhase(checkpoint, true);
        }
        class Encounter
        {
            public readonly List<NetworkObject> live = new();
            public bool spawning;
            public Vector3 anchor;
        }
        readonly Dictionary<string, Encounter> encounters = new();
        readonly Dictionary<NetworkObject, float> stuckSince = new();

        bool Started(string id) => encounters.ContainsKey(id);
        bool Cleared(string id) => encounters.TryGetValue(id, out var e) && !e.spawning && e.live.Count == 0;

        /// <summary>서버: 조우 하나를 연다. 같은 이름은 한 번만 (체크포인트 복원 전까지).</summary>
        void StartEncounter(string id, int rushers, int hovers, params Vector3[] anchors)
        {
            if (!IsServer || Started(id) || anchors.Length == 0) return;
            var e = new Encounter { spawning = true, anchor = anchors[0] };
            encounters[id] = e;
            betweenWaves.Value = false;
            StartCoroutine(SpawnEncounter(e, rushers, hovers, anchors));
        }

        IEnumerator SpawnEncounter(Encounter e, int rushers, int hovers, Vector3[] anchors)
        {
            yield return new WaitForSeconds(.25f);
            for (int i = 0; i < rushers + hovers; i++)
            {
                var data = i < rushers ? rusher : hover;
                if (!data) continue;
                var anchor = anchors[i % anchors.Length];
                var obj = NetworkObjectPool.Get(data.prefab, SpawnPoint(anchor, i), Quaternion.LookRotation(Vector3.back));
                stuckSince.Remove(obj);
                foreach (var other in encounters.Values) other.live.Remove(obj);   // 재사용된 인스턴스가 이전 조우에 남지 않게
                obj.Spawn(destroyWithScene: true);
                e.live.Add(obj);
                yield return new WaitForSeconds(.32f);
            }
            e.spawning = false;
        }

        // 앵커 둘레의 NavMesh 지점 중 플레이어와 6m 넘게 떨어진 곳. 그런 곳이 없으면 플레이어에게서 가장 먼 후보
        // (착지한 바로 옆에 적이 튀어나와 터지지 않게). 앵커와 걸어서 이어진 지점만 쓴다 (갑판 아래 끊긴 면에 스폰되면 아무도 닿지 못한다)
        NavMeshPath spawnPath;   // Unity는 필드 초기화에서 NavMeshPath 생성을 막는다
        Vector3 SpawnPoint(Vector3 anchor, int i)
        {
            if (!NavMesh.SamplePosition(anchor, out var center, 6, NavMesh.AllAreas)) return anchor;
            spawnPath ??= new NavMeshPath();
            Vector3 best = center.position; float bestDist = -1f;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                float a = (i * 137.5f + attempt * 61f) * Mathf.Deg2Rad;
                var wanted = center.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (2f + (attempt % 4) * 1.6f);
                if (!NavMesh.SamplePosition(wanted, out var hit, 2f, NavMesh.AllAreas)) continue;
                if (!NavMesh.CalculatePath(center.position, hit.position, NavMesh.AllAreas, spawnPath) || spawnPath.status != NavMeshPathStatus.PathComplete) continue;
                float nearest = float.MaxValue;
                foreach (var p in Player.All) if (p) nearest = Mathf.Min(nearest, (p.transform.position - hit.position).sqrMagnitude);
                if (nearest >= 36f) return hit.position;
                if (nearest > bestDist) { bestDist = nearest; best = hit.position; }
            }
            return best;
        }

        void PruneEncounters()
        {
            int total = 0;
            foreach (var e in encounters.Values)
            {
                for (int i = e.live.Count - 1; i >= 0; i--)
                {
                    var obj = e.live[i];
                    if (!obj || !obj.IsSpawned || !obj.gameObject.activeInHierarchy) { stuckSince.Remove(obj); e.live.RemoveAt(i); continue; }
                    // 떨어졌거나 길을 잃은 적은 치운다 (진행이 멈추지 않게)
                    if (obj.transform.position.y < e.anchor.y - 25f || (obj.transform.position - e.anchor).sqrMagnitude > 140f * 140f)
                    {
                        stuckSince.Remove(obj); obj.Despawn(); e.live.RemoveAt(i); continue;
                    }
                    // 어디로도 못 가는 채 12초 넘게 서 있는 적은 자폭한다 (닿을 수 없는 자리에 갇혀 진행이 멈추지 않게)
                    if (obj.TryGetComponent<NavMeshAgent>(out var agent) && agent.enabled && agent.isOnNavMesh &&
                        agent.pathStatus != NavMeshPathStatus.PathComplete && agent.velocity.sqrMagnitude < .05f)
                    {
                        if (!stuckSince.TryGetValue(obj, out var since)) stuckSince[obj] = Time.time;
                        else if (Time.time - since > 12f && obj.TryGetComponent<Health>(out var h) && !h.IsDead)
                        {
                            stuckSince.Remove(obj);
                            h.Damage(1e6f);
                        }
                    }
                    else stuckSince.Remove(obj);
                }
                total += e.live.Count;
            }
            if (alive.Value != total) alive.Value = total;
            if (total == 0 && !betweenWaves.Value && encounters.Count > 0 && encounters.Values.All(e => !e.spawning)) { betweenWaves.Value = true; waveCleared.Raise(); }
        }

        public Vector3 Objective(Player p)
        {
            bool sword = Sword(p);
            return Stage switch
            {
                MissionStage.FirstCrossing => Route.deck[sword || swordAcross ? 1 : 0].position,
                MissionStage.Deflect => Route.deck[1].position,
                MissionStage.ApproachLaser => Route.deck[3].position,
                MissionStage.Split => Route.Lane(sword, Mathf.Min((sword ? leftStep : rightStep).Value, (sword ? Route.upperLane : Route.lowerLane).Length - 1)),
                MissionStage.Land or MissionStage.Reunion => Route.deck[Stage == MissionStage.Land ? 9 : 12].position,
                MissionStage.Climb => Route.deck[climbDeck.Value].position,
                MissionStage.Strike => Route.deck[16].position,
                _ => p.transform.position
            };
        }
        void LaneEncounters(bool sword, int step)
        {
            var lane = sword ? Route.upperLane : Route.lowerLane;
            if (lane.Length < 5) return;
            if (sword)
            {
                if (step >= 1) StartEncounter("upperA", 3, 0, lane[2].position);
                if (step >= 3) StartEncounter("upperB", 2, 1, lane[4].position);
            }
            else
            {
                if (step >= 1) StartEncounter("lowerA", 1, 2, lane[2].position);
                if (step >= 3) StartEncounter("lowerB", 3, 1, lane[4].position);
            }
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
                StartEncounter("approach", 4, 1, Route.deck[2].position, Route.deck[3].position);
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
                waveIndex.Value = 1;
                StartEncounter("landing", 5, 0, Route.deck[10].position);
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
            PruneEncounters();
            var sword = Pilot(true); var gun = Pilot(false);
            if (!sword || !gun || (sword.Health.IsDead && gun.Health.IsDead)) return;
            if (awaitingPlacement && Clock < placementsUntil &&
                (!Near(sword,Route.Checkpoint(checkpoint,true)) || !Near(gun,Route.Checkpoint(checkpoint,false)))) return;
            awaitingPlacement = false;
            if (InQte) { TickRing(sword, gun); return; }
            switch (Stage)
            {
                case MissionStage.FirstCrossing:
                    if (!Started("contact") && (!Route.LeavingFirstHull(sword.transform.position) || Clock >= firstContactDeadline))
                        StartEncounter("contact", 4, 1, Route.deck[1].position);
                    if (Near(sword, Route.deck[1].position) || sword.Health.IsDead) swordAcross = true;
                    if (swordAcross && Cleared("contact") && (gunJumped || (gun.IsBot && !gun.Motor.Grounded && Route.LeavingFirstHull(gun.transform.position)) || gun.Health.IsDead || Near(gun,Route.deck[1].position))) BeginRing(MissionStage.Deflect);
                    break;
                case MissionStage.ApproachLaser:
                    if (Cleared("approach") && Arrived(sword,Objective(sword)) && Arrived(gun,Objective(gun))) EnterPhase(2,false);
                    break;
                case MissionStage.Split:
                    foreach (var p in Player.All)
                    {
                        bool s = Sword(p);
                        var step = s ? leftStep : rightStep;
                        var lane = s ? Route.upperLane : Route.lowerLane;
                        // 앞쪽 집결점을 먼저 밟았으면 그 자리까지 건너뛴다 (뒤로 돌아가지 않아도 된다)
                        for (int i = lane.Length - 1; i >= step.Value; i--)
                            if (p.Health.IsDead && i == step.Value || Near(p, lane[i].position)) { step.Value = i + 1; break; }
                        LaneEncounters(s, step.Value);
                    }
                    if (leftStep.Value >= Route.upperLane.Length && rightStep.Value >= Route.lowerLane.Length) EnterPhase(3, false);
                    break;
                case MissionStage.Reunion:
                    if (!Started("overwatch") && (Cleared("landing") || Near(sword, Route.deck[10].position, 8f) || Near(gun, Route.deck[10].position, 8f)))
                        StartEncounter("overwatch", 0, 2, Route.deck[11].position);
                    if (Cleared("landing") && Cleared("overwatch") && Arrived(sword,Objective(sword)) && Arrived(gun,Objective(gun))) EnterPhase(4,false);
                    break;
                case MissionStage.Climb:
                    if (climbDeck.Value == 13 && Cleared("tier1") && (Near(sword, Route.deck[13].position, 9f) || Near(gun, Route.deck[13].position, 9f)))
                    {
                        climbDeck.Value = 14;
                        StartEncounter("tier2", 4, 1, Route.deck[14].position);
                    }
                    if (climbDeck.Value == 14 && Cleared("tier2") && (Near(sword, Route.deck[14].position, 9f) || Near(gun, Route.deck[14].position, 9f)))
                    {
                        climbDeck.Value = 15;
                        StartEncounter("tier3", 3, 2, Route.deck[15].position, Route.deck[16].position);
                    }
                    if (Cleared("tier3"))
                    {
                        climbDeck.Value = 16;
                        if (Arrived(sword,Objective(sword)) && Arrived(gun,Objective(gun))) BeginRing(MissionStage.Strike);
                    }
                    break;
            }
        }
    }
}
