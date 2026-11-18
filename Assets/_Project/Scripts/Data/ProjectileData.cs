using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>P1 원격 검기 같은 투사체 공격.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Weapon/Projectile")]
    public class ProjectileData : ScriptableObject
    {
        public NetworkObject prefab;
        public float damage = 70f;
        public float speed = 30f;
        public float range = 30f;
        public float radius = 0.6f;
        [Tooltip("발사할 때 쓰는 자원(검기 칸) 수")]
        public int cost = 1;
        public float cooldown = 0.4f;
        public LayerMask hitMask;
    }
}
