using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 소모품 자원 (P2 탄창). 용량과 시작값은 PlayerStats에서 읽고,
    /// 무기 모드가 바뀌면 WeaponModes가 SetMagazine으로 용량과 남은 탄을 바꾼다.
    /// </summary>
    public class AmmoPool : NetworkBehaviour
    {
        [SerializeField] PlayerStats stats;

        readonly NetworkVariable<int> current = new();
        readonly NetworkVariable<int> capacity = new();

        public int Current => current.Value;
        public int Capacity => capacity.Value > 0 ? capacity.Value : stats.resourceCapacity;
        public float Ratio => (float)current.Value / Capacity;
        public bool IsFull => current.Value >= Capacity;
        /// <summary>마지막으로 탄을 쓴 시각 (서버, Time.time)</summary>
        public float LastSpendTime { get; private set; } = -999f;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            if (capacity.Value <= 0) capacity.Value = stats.resourceCapacity;
            current.Value = stats.resourceStartsFull ? Capacity : 0;
        }

        // 아래는 모두 서버에서만 호출
        public bool TrySpend(int amount)
        {
            if (current.Value < amount) return false;
            current.Value -= amount;
            LastSpendTime = Time.time;
            return true;
        }

        public void Add(int amount) => current.Value = Mathf.Min(Capacity, current.Value + amount);
        public void Fill() => current.Value = Capacity;

        /// <summary>장탄수와 남은 탄을 함께 바꾼다 (무기 모드 전환).</summary>
        public void SetMagazine(int newCapacity, int remaining)
        {
            capacity.Value = Mathf.Max(1, newCapacity);
            current.Value = Mathf.Clamp(remaining, 0, capacity.Value);
        }
    }
}
