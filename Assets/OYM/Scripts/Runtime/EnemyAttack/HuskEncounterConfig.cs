using UnityEngine;

namespace OYM
{
    /// Tuning + rigs for the live HUSK in the game scene (Resources/OYM_HuskEncounter, built by
    /// OYM/FX/Enemy Attack: Build). Edit the numbers in the inspector; HuskEncounter reads them at runtime.
    [CreateAssetMenu(menuName = "OYM/HUSK Encounter", fileName = "OYM_HuskEncounter")]
    public class HuskEncounterConfig : ScriptableObject
    {
        [Header("Attack rigs")]
        public PrismAttackFX prismPrefab;
        public WickAttackFX wickPrefab;
        public HomingDartFX dartPrefab;

        [Header("Flow")]
        [Tooltip("seconds after the player gets control (cutscene over) before the pack wakes")] public float engageDelay = 2f;
        [Tooltip("seconds between the sleeping WICKs waking one after another")] public float wakeStagger = 2.2f;
        public int maxWicks = 3, maxPrisms = 2;
        public float wickRespawn = 5f, prismRespawn = 12f;
        [Tooltip("new HUSK come in from this far (m)")] public float spawnDistance = 65f;
        public float salvoFirst = 35f, salvoEvery = 22f;
        public int salvoCount = 5;
        public float salvoDamage = 14f, salvoRadius = 3f;

        [Header("Player")]
        public float playerHP = 100f;
        public float regenDelay = 4f, regenRate = 15f;
        [Tooltip("damage of one KESTREL slash / one RG-77 shot on a HUSK")] public float slashDamage = 40f, railDamage = 75f;

        [Header("WICK (rush, latch, self-destruct)")]
        public float wickHP = 30f;
        public float wickSpeed = 13f, wickAccel = 20f;
        [Tooltip("arms this close to the player's chest (m)")] public float wickTrigger = 2.6f;
        public float wickFuse = 1.2f;
        [Tooltip("max speed while latched: a dash (16 m/s) shakes it off")] public float wickLatchSpeed = 7.5f;
        public float wickBlastRadius = 4.2f, wickDamage = 32f;

        [Header("PRISM (keep distance, charge, lance)")]
        public float prismHP = 90f;
        public float prismSpeed = 7f, prismDistance = 21f, prismRange = 45f;
        public float prismCharge = 2.3f, prismCooldown = 3.2f;
        [Tooltip("deg/s the aim follows the player while charging: lower = easier to dash out of")] public float prismTracking = 32f;
        public float prismDamage = 24f;
        [Tooltip("the lance hits when it passes this close to the chest (m)")] public float prismHitRadius = 0.75f;
        [Tooltip("damage multiplier on a PRISM hit while it charges (lens open = weak point); the hit also breaks the charge")]
        public float prismWeakMul = 2f;

        [Header("Level flow (design/LEVEL_ENCOUNTER.md); progress = metres along the debris path toward the wreck")]
        public bool cutaways = true;
        [Tooltip("first contact: seconds after the player gets control, or this much progress")] public float contactDelay = 3f, contactProgress = 8f;
        public float snipersProgress = 40f, snipersTimeout = 45f;
        public float ambushProgress = 120f, ambushTimeout = 60f;
        public float bossProgress = 200f, bossTimeout = 70f;
        public float bossSalvoEvery = 9f, bossHatchEvery = 12f;
        public int bossSalvoDarts = 8, bossHatchWicks = 3;

        [Header("Radio (subtitle bar)")]
        public string barkContactBy = "ORBIT FRAME";
        public string barkContact = "접촉. WICK, 정면!";
        public string barkSnipersBy = "KESTREL";
        public string barkSnipers = "저격수야. 충전할 때 렌즈를 노려.";
        public string barkAmbushBy = "ORBIT FRAME";
        public string barkAmbush = "발밑이다!";
        public string barkBossBy = "KESTREL";
        public string barkBoss = "저게… CITADEL이야.";
        public string barkSalvoBy = "ORBIT FRAME";
        public string barkSalvo = "미사일! 흩어져!";

        [Header("Zones of the level layout (HuskZones): radio as each one wakes, in route order after first contact")]
        public string[] zoneBarkBy = { "KESTREL", "ORBIT FRAME", "KESTREL", "ORBIT FRAME" };
        public string[] zoneBarks = { "기지 위에 저격수. 충전할 때 렌즈를 노려.", "거주 모듈에서 쏟아져 나온다!", "게이트 위아래, 양쪽 다 봐.", "둥지다… 전부 깨어난다!" };
        [Tooltip("first contact: the start-up pack wakes this long after control, or when the player is this close to it")]
        public float contactAfter = 4f, contactRange = 60f;
        [Tooltip("the CITADEL comes this long after the last zone (the nest) is cleared, or this long after it woke")]
        public float bossAfterNest = 3.5f, bossNestTimeout = 80f;
        public string barkQuietBy = "KESTREL";
        public string barkQuiet = "…조용해. 너무 조용해.";

        [Header("CITADEL launcher pods (objective: break both)")]
        public float podHP = 300f;
        public string barkObjectiveBy = "ORBIT FRAME";
        public string barkObjective = "런처 포드를 노려. 레일건으로!";
        public string barkPodBy = "ORBIT FRAME";
        public string barkPodFirst = "포드 하나 날렸다!";
        public string barkPodSecond = "두 번째도 끝났어!";
        public string barkVictoryBy = "KESTREL";
        public string barkVictory = "물러난다…!";
        public string barkVictory2By = "ORBIT FRAME";
        public string barkVictory2 = "살았다. 숨 좀 돌리자.";
    }
}
