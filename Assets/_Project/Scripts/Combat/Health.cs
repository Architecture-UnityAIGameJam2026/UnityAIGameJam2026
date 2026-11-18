using System;
using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>체력. 값은 서버만 바꾸고 NetworkVariable로 모두에게 동기화된다.</summary>
    public class Health : NetworkBehaviour
    {
        [SerializeField] HealthData data;

        readonly NetworkVariable<float> current = new();

        /// <summary>서버에서만 호출된다.</summary>
        public event Action<Health> Died;

        public float Current => current.Value;
        public float Max => data.maxHealth;
        public float Ratio => current.Value / data.maxHealth;
        public bool IsDead => current.Value <= 0f;

        public override void OnNetworkSpawn()
        {
            if (IsServer) current.Value = data.maxHealth;
        }

        public void Damage(float amount)
        {
            if (!IsServer || IsDead) return;
            current.Value = Mathf.Max(0f, current.Value - amount);
            if (IsDead) Died?.Invoke(this);
        }

        public void Heal(float amount)
        {
            if (!IsServer) return;
            current.Value = Mathf.Min(data.maxHealth, current.Value + amount);
        }

        public void Restore() => Heal(data.maxHealth);

        /// <summary>충돌한 콜라이더에서 체력을 찾는다 (자식 콜라이더 포함).</summary>
        public static Health Of(Collider c) => c.GetComponentInParent<Health>();
    }
}
