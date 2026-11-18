using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>자원을 소모해 투사체를 쏜다. (P13에서 P1 검기 발사는 제거됨. 투사체 무기 틀로 남겨 둔다) 투사체는 NetworkObjectPool에서 꺼낸다.</summary>
    public class ProjectileWeapon : NetworkBehaviour
    {
        [SerializeField] ProjectileData data;
        [SerializeField] InputActionReference fire;
        [SerializeField] AmmoPool resource;

        float readyTime;

        const int PrewarmProjectiles = 6;

        // 모든 피어에서 등록해야 클라이언트도 풀에서 꺼내고 돌려놓는다 (여러 번 불러도 한 번만 등록)
        public override void OnNetworkSpawn() => NetworkObjectPool.Register(data.prefab, PrewarmProjectiles);

        void Update()
        {
            if (!IsOwner || !NetworkObject.IsPlayerObject || !CursorLock.IsLocked || !fire.action.WasPressedThisFrame() || Time.time < readyTime) return;
            if (resource.Current < data.cost) return;
            readyTime = Time.time + data.cooldown;

            var cam = Camera.main.transform;
            LaunchRpc(cam.forward);
        }

        public float Range => data.range;
        public bool BotLaunch(Vector3 direction)
        {
            var p = GetComponent<Player>();
            if (!IsServer || !p.IsBot || p.Health.IsDead || resource.Current < data.cost || Time.time < readyTime) return false;
            readyTime = Time.time + data.cooldown;
            LaunchRpc(direction);
            return true;
        }
        [Rpc(SendTo.Server)]
        void LaunchRpc(Vector3 direction)
        {
            if (!resource.TrySpend(data.cost)) return;
            Vector3 origin = transform.position + Vector3.up * 1.2f + transform.forward * 0.8f;
            var obj = NetworkObjectPool.Get(data.prefab, origin, Quaternion.LookRotation(direction));
            obj.GetComponent<Projectile>().Launch(data, direction, GetComponent<Player>());
            obj.Spawn(destroyWithScene: true);
        }
    }
}
