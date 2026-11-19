using System;
using System.Collections.Generic;
using UnityEngine;

namespace OYM.Flow
{
    /// The 10-15 minute route of the game scene (Showcase_DebrisField), baked by OYM.EditorTools.OYMLevelFlow
    /// (menu OYM/Level/Flow): sectors in route order, each a list of waves (a HuskZones zone that unlocks, the start-up
    /// pack HuskLevel wakes, or spawn points of its own), the breathers between them, the extra heal pack spots and
    /// the cruise lane. OYM.Flow.LevelFlow plays it on the host (single player or co-op host). Numbers are meant to be
    /// tuned here in the inspector; the builder writes its defaults again only when it is run again.
    public class LevelFlowPlan : MonoBehaviour
    {
        public const string RootName = "Level Flow (OYMLevelFlow)";

        public List<FlowSector> sectors = new List<FlowSector>();

        [Header("Finale")]
        [Tooltip("seconds of quiet after the last sector is clear before the CITADEL may come (HuskLevel then adds its own quiet beat and the entrance)")]
        public float bossBreather = 14f;
        [Tooltip("objective line while the breather before the boss runs")] public string breatherObjective = "숨 고르기 — 힐팩 챙기기";
        [Tooltip("objective line once the CITADEL is on its way")] public string bossObjective = "CITADEL — 붉은 약점 파괴";

        [Header("Heal packs of the flow (the field kit's own sit on the stepping stones)")]
        public List<Vector3> healSpots = new List<Vector3>();

        [Header("Cruise lane (hint only)")]
        public Vector3 cruiseFrom;
        public Vector3 cruiseTo;
        [Tooltip("shown once when a sector's target is far and the pilot is out of combat")] public string cruiseHint = "Shift 길게 — 순항";
        [Tooltip("the hint shows when the pilot is farther than this from the sector's anchor (m)")] public float cruiseHintRange = 160f;

        [Header("Design estimate (s) for the log / report")]
        public float estimateArrival = 45f;
        public float estimateBoss = 195f;
    }

    [Serializable]
    public class FlowSector
    {
        public string name;
        [Tooltip("objective line at the top of the screen while this sector is the target")] public string objective;
        [Tooltip("short name on the waypoint mark (empty = the objective line)")] public string marker;
        [Tooltip("the distance readout points here")] public Vector3 anchor;
        [Tooltip("seconds after the sector before it is clear until this one unlocks (the breather)")] public float unlockAfter = 6f;
        [Tooltip("radio as the sector unlocks")] public string startBark;
        public string startBy = "ORBIT FRAME";
        public List<FlowWave> waves = new List<FlowWave>();
        [Tooltip("seconds after its last wave started that the sector counts as clear even with stragglers left")] public float clearTimeout = 150f;
        public string clearBark;
        public string clearBy = "ORBIT FRAME";
        [Tooltip("show the cruise hint while this sector is the target")] public bool cruiseHint;
        [Tooltip("design estimate in seconds (log / report only)")] public float estimate;
    }

    [Serializable]
    public class FlowWave
    {
        public string name;
        [Tooltip("HuskZones zone this wave unlocks (it wakes when a pilot comes within its radius); '@contact' = the start-up pack HuskLevel wakes; empty = the spawn points below")]
        public string zone;
        public List<FlowSpawn> spawns = new List<FlowSpawn>();
        [Tooltip("starts when the HUSK of the waves before it in this sector are down to this many ...")] public int whenAliveAtMost = 1;
        [Tooltip("... or this many seconds after the wave before it started")] public float orAfter = 60f;
        [Tooltip("seconds between the trigger and the wave")] public float delay = 2f;
        [Tooltip("zone waves: wake it anyway this many seconds after it unlocked when no pilot came near (0 = wait for the pilots)")] public float forceWakeAfter;
        [Tooltip("spawn waves: only once a pilot is within this many metres of the sector anchor (0 = anywhere)")] public float nearAnchor;
        public string bark;
        public string barkBy = "KESTREL";
    }

    [Serializable]
    public struct FlowSpawn
    {
        public bool prism;
        public Vector3 position;
        public Quaternion rotation;
        [Tooltip("initial velocity as it comes in (m/s)")] public Vector3 inbound;
    }
}
