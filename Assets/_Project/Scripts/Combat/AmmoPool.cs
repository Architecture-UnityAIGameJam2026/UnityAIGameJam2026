using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>소모품 자원: P1 검기 칸 / P2 탄창. 용량과 시작값은 PlayerStats에서 읽는다.</summary>
    public class AmmoPool : NetworkBehaviour
    {
        [SerializeField] PlayerStats stats;

        readonly NetworkVariable<int> current = new();

        public int Current => current.Value;
        public int Capacity => stats.resourceCapacity;
        public float Ratio => (float)current.Value / stats.resourceCapacity;
        public bool IsFull => current.Value >= stats.resourceCapacity;

        public override void OnNetworkSpawn()
        {
            if (IsServer) current.Value = stats.resourceStartsFull ? stats.resourceCapacity : 0;
        }

        // 아래는 모두 서버에서만 호출
        public bool TrySpend(int amount)
        {
            if (current.Value < amount) return false;
            current.Value -= amount;
            return true;
        }

        public void Add(int amount) => current.Value = Mathf.Min(stats.resourceCapacity, current.Value + amount);
        public void Fill() => current.Value = stats.resourceCapacity;
    }
}
