using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>
    /// Q 궁극기 (개인 독립 킬 카운트제). 본인이 직접 처치한 적 수가 필요 수에 닿으면 Q로 발동한다.
    /// 발동 중 처치한 적은 세지 않는다. 필요 수는 15, 3번(스킬) 파츠를 돌려받으면 10.
    /// 효과 자체는 UltimateData(나노 강화제 / 스마트 락온)가 정한다.
    /// </summary>
    public class Ultimate : NetworkBehaviour
    {
        [SerializeField] UltimateData data;
        [SerializeField] PlayerEvent enemyKilled;
        [SerializeField] InputActionReference activate;
        [Tooltip("궁극기 사격 궤적에 쓸 재질")]
        [SerializeField] Material tracerMaterial;

        readonly NetworkVariable<int> kills = new();
        readonly NetworkVariable<double> activeUntil = new();
        // 조준 방향: 조준선은 내 클라이언트만 알므로 소유자가 계속 알려 준다
        readonly NetworkVariable<Vector3> aim = new(Vector3.forward,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        Player me;
        CrossHudState hud;
        bool wasActive;   // 서버

        // 효과가 쓰는 발동별 상태 (서버)
        [System.NonSerialized] public float timer;
        [System.NonSerialized] public int counter;
        [System.NonSerialized] public float extendedSeconds;

        double Now => NetworkManager.ServerTime.Time;
        public Player Me => me;
        public UltimateData Data => data;
        public Vector3 Aim => aim.Value.sqrMagnitude > 0.01f ? aim.Value.normalized : transform.forward;
        public int Kills => kills.Value;
        public int KillsRequired => hud != null && hud.IsReturned(HudPart.Skill) ? data.killsRequiredAwakened : data.killsRequired;
        public bool IsReady => kills.Value >= KillsRequired;
        public bool IsActive => IsSpawned && Now < activeUntil.Value;
        public float ActiveLeft => IsSpawned ? Mathf.Max(0f, (float)(activeUntil.Value - Now)) : 0f;

        public float DamageMultiplier => IsActive ? data.DamageMultiplier : 1f;
        public float AttackSpeedMultiplier => IsActive ? data.AttackSpeedMultiplier : 1f;
        public float MoveSpeedMultiplier => IsActive ? data.MoveSpeedMultiplier : 1f;
        /// <summary>유도탄 발사 수 배율 (3번 파츠 반환 각성에서 +50%)</summary>
        public float HomingRateMultiplier => GetComponent<AwakeningSet>().HomingRateMultiplier;

        void Awake() => me = GetComponent<Player>();

        public override void OnNetworkSpawn()
        {
            hud = FindFirstObjectByType<CrossHudState>();
            if (IsServer) enemyKilled.Raised += OnEnemyKilled;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) enemyKilled.Raised -= OnEnemyKilled;
        }

        void Update()
        {
            if (!IsSpawned) return;

            if (IsOwner)
            {
                aim.Value = Camera.main.transform.forward;
                me.Motor.ExternalSpeedMultiplier = MoveSpeedMultiplier;
                if (CursorLock.IsLocked && activate.action.WasPressedThisFrame() && IsReady && !IsActive) ActivateRpc();
            }

            if (!IsServer) return;
            bool active = IsActive;
            if (active) data.Tick(this, Time.deltaTime);
            else if (wasActive) data.End(this);
            wasActive = active;
        }

        void OnEnemyKilled(Player killer)
        {
            // 시너지: 파트너가 내 스킬 파츠를 들고 있으면 발동 중 파트너의 처치마다 지속 시간 연장
            if (killer != null && killer == me.Partner && IsActive && data.extendPerPartnerKill > 0f
                && hud != null && !hud.IsReturned(HudPart.Skill))
                Extend(data.extendPerPartnerKill, data.extendMax);

            if (killer != me || IsActive) return;   // 발동 중 처치는 누적하지 않는다
            kills.Value = Mathf.Min(kills.Value + 1, data.killsRequired);
        }

        /// <summary>서버 전용. 처치 수를 더한다 (3번 파츠 시너지: 파트너 패링 성공 시 +2).</summary>
        public void AddKills(int amount)
        {
            if (IsServer && !IsActive) kills.Value = Mathf.Min(kills.Value + amount, data.killsRequired);
        }

        /// <summary>서버 전용. 발동 중이면 지속 시간을 늘린다. 이번 발동에서 늘린 총량은 maxTotal을 넘지 않는다.</summary>
        public void Extend(float seconds, float maxTotal)
        {
            if (!IsServer || !IsActive) return;
            float add = Mathf.Min(seconds, maxTotal - extendedSeconds);
            if (add <= 0f) return;
            extendedSeconds += add;
            activeUntil.Value += add;
        }

        [Rpc(SendTo.Server)]
        void ActivateRpc()
        {
            if (!IsReady || IsActive || me.Health.IsDead) return;
            kills.Value = 0;
            timer = 0f; counter = 0; extendedSeconds = 0f;
            activeUntil.Value = Now + data.duration;
            wasActive = true;
            data.Begin(this);
        }

        // ---- 효과에서 쓰는 도구 ----
        [Rpc(SendTo.Owner)]
        public void SetDashBonusRpc(int bonus, bool fill) => me.Motor.SetDashBonus(bonus, fill);

        public void ShowShot(Vector3 from, Vector3 to) => ShowShotRpc(from, to);

        [Rpc(SendTo.ClientsAndHost)]
        void ShowShotRpc(Vector3 from, Vector3 to)
        {
            var go = new GameObject("UltimateShot");
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true; line.positionCount = 2; line.widthMultiplier = 0.05f;
            line.sharedMaterial = tracerMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.SetPosition(0, from); line.SetPosition(1, to);
            Destroy(go, 0.06f);
        }
    }
}
