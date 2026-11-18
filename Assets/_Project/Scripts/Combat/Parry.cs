using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>
    /// P1 E 스킬 — 튕겨내기(패링). E로 켜면 duration 동안 정면에서 오는 공격을 막는다.
    /// - 투사체 · 레이저: 대미지 무효 + 지금 조준선 방향으로 즉시 반사 (reflectDamage)
    /// - 근접 공격: 대미지 무효 + 공격한 적 경직
    /// - 성공하면 대쉬 1칸을 돌려받는다 (시전당 1회)
    /// E를 다시 누르거나 좌클릭 · Shift를 누르면 바로 끝난다. 쿨타임은 시전할 때 시작한다.
    /// 막을지 판정은 서버가 하고, 적은 대미지를 주기 전에 Parry.Blocks로 물어본다.
    /// </summary>
    public class Parry : NetworkBehaviour
    {
        [SerializeField] ParryData data;
        [SerializeField] InputActionReference skill, fire, dash;
        [SerializeField] GameObject shieldEffect;
        [SerializeField] LineRenderer reflectTracer;

        readonly NetworkVariable<double> activeUntil = new();
        readonly NetworkVariable<double> readyAt = new();
        // 반사 방향: 조준선은 내 클라이언트만 알므로 소유자가 계속 알려 준다
        readonly NetworkVariable<Vector3> aim = new(Vector3.forward,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        Player me;
        AwakeningSet awakening;
        bool rewarded;   // 서버: 이번 시전에서 대쉬 보상을 줬는지

        double Now => NetworkManager.ServerTime.Time;
        public bool IsActive => IsSpawned && Now < activeUntil.Value;
        public float CooldownLeft => IsSpawned ? Mathf.Max(0f, (float)(readyAt.Value - Now)) : 0f;
        public float ActiveLeft => IsSpawned ? Mathf.Max(0f, (float)(activeUntil.Value - Now)) : 0f;

        void Awake()
        {
            me = GetComponent<Player>();
            awakening = GetComponent<AwakeningSet>();
        }

        public float Cooldown => data.cooldown;

        void Update()
        {
            if (shieldEffect) shieldEffect.SetActive(IsActive);
            if (!IsOwner || !IsSpawned) return;

            if (IsActive) aim.Value = Camera.main.transform.forward;
            if (!CursorLock.IsLocked) return;

            if (skill.action.WasPressedThisFrame())
            {
                if (IsActive) CancelRpc();
                else if (CooldownLeft <= 0f) StartRpc(Camera.main.transform.forward);
            }
            else if (IsActive && (fire.action.WasPressedThisFrame() || dash.action.WasPressedThisFrame()))
            {
                CancelRpc();
            }
        }

        [Rpc(SendTo.Server)]
        void StartRpc(Vector3 aimDirection)
        {
            if (CooldownLeft > 0f || me.Health.IsDead) return;
            activeUntil.Value = Now + data.duration;
            readyAt.Value = Now + Cooldown;
            rewarded = false;
        }

        [Rpc(SendTo.Server)]
        void CancelRpc() => activeUntil.Value = Now;

        /// <summary>
        /// 서버 전용. target이 패링 중이고 공격이 정면에서 왔으면 막고 true를 돌려준다.
        /// 호출한 쪽은 true면 대미지를 주지 않는다.
        /// </summary>
        public static bool Blocks(Health target, Vector3 from, bool melee, EnemyBase attacker)
        {
            if (target == null || !target.TryGetComponent<Parry>(out var parry)) return false;
            return parry.TryBlock(from, melee, attacker);
        }

        bool TryBlock(Vector3 from, bool melee, EnemyBase attacker)
        {
            if (!IsServer || !IsActive || me.Health.IsDead) return false;

            Vector3 to = from - transform.position; to.y = 0f;
            Vector3 forward = transform.forward; forward.y = 0f;
            if (to.sqrMagnitude > 0.0001f && Vector3.Angle(forward, to) > data.frontHalfAngle) return false;

            if (melee) { if (attacker) attacker.Stun(data.stunSeconds); }
            else
            {
                Reflect();
                awakening.NotifyReflect();   // P1 3번 파츠 각성: 반사 성공 시 Q 미니 발동
            }

            if (!rewarded && data.dashPayback > 0)
            {
                rewarded = true;
                AddDashRpc(data.dashPayback);
            }
            return true;
        }

        // 조준선 방향으로 즉시 반사: 첫 번째로 맞는 대상에게 reflectDamage
        void Reflect()
        {
            Vector3 origin = transform.position + Vector3.up * 1.4f;
            Vector3 dir = aim.Value.sqrMagnitude > 0.01f ? aim.Value.normalized : transform.forward;
            Vector3 end = origin + dir * data.reflectRange;
            if (Physics.Raycast(origin + dir * 0.6f, dir, out var hit, data.reflectRange, data.reflectMask))
            {
                end = hit.point;
                if (Health.Of(hit.collider) is { } h && h != me.Health) h.Damage(data.reflectDamage, me);
            }
            ShowReflectRpc(origin, end);
        }

        [Rpc(SendTo.Owner)]
        void AddDashRpc(int amount) => me.Motor.AddDashStacks(amount);

        [Rpc(SendTo.ClientsAndHost)]
        void ShowReflectRpc(Vector3 from, Vector3 to)
        {
            if (!reflectTracer) return;
            reflectTracer.SetPosition(0, from);
            reflectTracer.SetPosition(1, to);
            StartCoroutine(MeleeWeapon.Flash(reflectTracer.gameObject, 0.12f));
        }
    }
}
