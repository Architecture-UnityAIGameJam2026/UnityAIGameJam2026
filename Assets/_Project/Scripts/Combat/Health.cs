using System;
using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>체력. 값은 서버만 바꾸고 NetworkVariable로 모두에게 동기화된다.</summary>
    public class Health : NetworkBehaviour
    {
        [SerializeField] HealthData data;
        [Tooltip("죽었을 때 마지막으로 때린 플레이어를 실어 보낼 채널 (적만 지정)")]
        [SerializeField] PlayerEvent killedBy;

        readonly NetworkVariable<float> current = new();
        readonly NetworkVariable<float> shield = new();
        readonly NetworkVariable<double> shieldUntil = new();

        /// <summary>서버에서만 호출된다.</summary>
        public event Action<Health> Died;

        public float Current => current.Value;
        public float Max => data.maxHealth;
        public float Ratio => current.Value / data.maxHealth;
        public bool IsDead => current.Value <= 0f;
        public float Shield => IsSpawned && NetworkManager.ServerTime.Time < shieldUntil.Value ? shield.Value : 0f;

        public override void OnNetworkSpawn()
        {
            if (IsServer) current.Value = data.maxHealth;
        }

        /// <param name="attacker">때린 플레이어. 적의 공격이면 null</param>
        public void Damage(float amount, Player attacker = null)
        {
            if (!IsServer || IsDead) return;

            float absorbed = Mathf.Min(Shield, amount);   // 실드가 먼저 깎인다
            shield.Value -= absorbed;
            current.Value = Mathf.Max(0f, current.Value - (amount - absorbed));
            if (!IsDead) return;
            if (killedBy != null && attacker != null) killedBy.Raise(attacker);
            Died?.Invoke(this);
        }

        /// <summary>살아 있을 때만 회복된다. 쓰러진 플레이어는 소생(Revive)으로만 일어난다.</summary>
        public void Heal(float amount)
        {
            if (!IsServer || IsDead) return;
            current.Value = Mathf.Min(data.maxHealth, current.Value + amount);
        }

        /// <summary>죽었든 살았든 가득 채운다 (테스트 더미 부활 등).</summary>
        public void Restore()
        {
            if (IsServer) current.Value = data.maxHealth;
        }

        /// <summary>쓰러진 상태에서 최대 체력의 ratio만큼으로 일어난다.</summary>
        public void Revive(float ratio)
        {
            if (IsServer && IsDead) current.Value = data.maxHealth * ratio;
        }

        public void AddShield(float amount, float duration)
        {
            if (!IsServer) return;
            shield.Value = amount;
            shieldUntil.Value = NetworkManager.ServerTime.Time + duration;
        }

        /// <summary>충돌한 콜라이더에서 체력을 찾는다 (자식 콜라이더 포함).</summary>
        public static Health Of(Collider c) => c.GetComponentInParent<Health>();
    }
}
