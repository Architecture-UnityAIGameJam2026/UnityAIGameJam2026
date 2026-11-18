using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>
    /// Q 궁극기. 효과 자체는 UltimateData(나노 강화제 / 스마트 락온)가 정한다.
    /// 3번(스킬) 파츠 반환 전 — 역전 킬 카운트 &amp; 원격 대리 발동:
    ///   파트너가 처치할 때마다 내 킬 게이지가 차고, 내 게이지가 다 차면 파트너가 Q를 눌러야 내 궁극기가 발동한다.
    ///   (반대로 내가 Q를 누르면 파트너의 궁극기를 발동시킨다)
    /// 반환 후 — 독자 각성: 내 처치가 내 게이지에 쌓이고(각성에 따라 처치당 2킬), 내 Q로 내 궁극기를 발동한다.
    /// 발동 중 처치는 게이지에 쌓이지 않는다.
    /// </summary>
    public class Ultimate : NetworkBehaviour
    {
        [SerializeField] UltimateData data;
        [SerializeField] PlayerEvent enemyKilled;
        [SerializeField] InputActionReference activate;
        [Tooltip("궁극기 사격 궤적에 쓸 재질")]
        [SerializeField] Material tracerMaterial;
        [Tooltip("광역 폭발 연출 (P2 각성 레이저)")]
        [SerializeField] GameObject blastEffect;

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
        /// <summary>3번 파츠를 서로 들고 있는지 (반환 전)</summary>
        public bool Crossed => hud != null && !hud.IsReturned(HudPart.Skill);
        public AwakeningSet Awakening => GetComponent<AwakeningSet>();

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

            if (IsOwner && NetworkObject.IsPlayerObject)
            {
                aim.Value = Camera.main.transform.forward;
                me.Motor.ExternalSpeedMultiplier = MoveSpeedMultiplier;
                // 반환 전에는 Q가 파트너의 궁극기를 발동시킨다 (판정은 서버)
                if (CursorLock.IsLocked && activate.action.WasPressedThisFrame()) ActivateRpc();
            }

            if (!IsServer) return;
            bool active = IsActive;
            if (active) data.Tick(this, Time.deltaTime);
            else if (wasActive) data.End(this);
            wasActive = active;
        }

        void OnEnemyKilled(Player killer)
        {
            if (killer == null || IsActive) return;   // 발동 중 처치는 누적하지 않는다
            if (Crossed)
            {
                // 역전 킬 카운트: 파트너의 처치가 내 게이지를 채운다
                if (killer == me.Partner) AddKills(1);
            }
            else if (killer == me)
            {
                AddKills(Awakening.KillValue);   // P2 각성: 따발총 처치는 2킬
            }
        }

        /// <summary>서버 전용. 킬 게이지를 더한다.</summary>
        public void AddKills(int amount)
        {
            if (IsServer && !IsActive) kills.Value = Mathf.Min(kills.Value + amount, KillsRequired);
        }

        // 반환 전: 내 Q는 파트너의 궁극기를, 반환 후: 내 궁극기를 발동한다
        [Rpc(SendTo.Server)]
        void ActivateRpc()
        {
            if (me.Health.IsDead) return;
            var target = Crossed ? (me.Partner ? me.Partner.Ultimate : null) : this;
            if (target != null) target.TryActivate();
        }

        /// <summary>서버 전용. 게이지가 다 찼으면 비우고 발동한다.</summary>
        public void TryActivate()
        {
            if (!IsServer || !IsReady || IsActive || me.Health.IsDead) return;
            kills.Value = 0;
            Begin(data.duration);
        }

        /// <summary>서버 전용. 게이지를 쓰지 않고 seconds 동안 발동한다 (P1 패링 반사 각성의 미니 발동).</summary>
        public void ActivateFree(float seconds)
        {
            if (!IsServer || IsActive || me.Health.IsDead) return;
            Begin(seconds);
        }

        void Begin(float seconds)
        {
            timer = 0f; counter = 0;
            activeUntil.Value = Now + seconds;
            wasActive = true;
            data.Begin(this);
        }

        // ---- 효과에서 쓰는 도구 ----
        [Rpc(SendTo.Owner)]
        public void SetDashBonusRpc(int bonus, bool fill) => me.Motor.SetDashBonus(bonus, fill);

        public void ShowShot(Vector3 from, Vector3 to) => ShowShotRpc(from, to);
        public void ShowBlast(Vector3 at, float radius) => ShowBlastRpc(at, radius);

        [Rpc(SendTo.ClientsAndHost)]
        void ShowBlastRpc(Vector3 at, float radius)
        {
            if (blastEffect == null) return;
            FxPool.Play(blastEffect, at, Quaternion.identity, Vector3.one * radius * 2f, 0.35f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ShowShotRpc(Vector3 from, Vector3 to)
        {
            // 발사마다 GameObject + LineRenderer를 만들고 지우던 것을 템플릿 하나 + FxPool 재사용으로
            if (!shotTemplate)
            {
                shotTemplate = new GameObject("UltimateShot");
                shotTemplate.SetActive(false);
                shotTemplate.transform.SetParent(transform, false);
                var l = shotTemplate.AddComponent<LineRenderer>();
                l.useWorldSpace = true; l.positionCount = 2; l.widthMultiplier = 0.05f;
                l.sharedMaterial = tracerMaterial;
                l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var go = FxPool.Play(shotTemplate, Vector3.zero, Quaternion.identity, Vector3.one, 0.06f);
            var line = go.GetComponent<LineRenderer>();
            line.SetPosition(0, from); line.SetPosition(1, to);
        }

        GameObject shotTemplate;
    }
}
