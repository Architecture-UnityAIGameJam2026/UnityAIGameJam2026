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
        /// <summary>장탄수를 넘게 들어 있는지 (오버차지: 초과 탄환은 대미지 보너스)</summary>
        public bool Overcharged => current.Value > Capacity;
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

        public void Add(int amount) { if (current.Value < Capacity) current.Value = Mathf.Min(Capacity, current.Value + amount); }
        /// <summary>장탄수까지 채운다 (오버차지로 이미 더 많으면 그대로 둔다).</summary>
        public void Fill() => current.Value = Mathf.Max(current.Value, Capacity);
        /// <summary>amount발이 되도록 채운다 (장탄수를 넘을 수 있다: 오버차지).</summary>
        public void FillTo(int amount) => current.Value = Mathf.Max(current.Value, amount);

        /// <summary>장탄수와 남은 탄을 함께 바꾼다 (무기 모드 전환).</summary>
        public void SetMagazine(int newCapacity, int remaining)
        {
            capacity.Value = Mathf.Max(1, newCapacity);
            current.Value = Mathf.Max(0, remaining);   // 오버차지된 탄은 그대로 보관
        }
    }
}
