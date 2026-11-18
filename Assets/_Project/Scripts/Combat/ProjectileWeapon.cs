using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>자원을 소모해 투사체를 쏜다. P1은 R로 원격 검기(검기 1칸 소모).</summary>
    public class ProjectileWeapon : NetworkBehaviour
    {
        [SerializeField] ProjectileData data;
        [SerializeField] InputActionReference fire;
        [SerializeField] AmmoPool resource;

        float readyTime;

        void Update()
        {
            if (!IsOwner || !CursorLock.IsLocked || !fire.action.WasPressedThisFrame() || Time.time < readyTime) return;
            if (resource.Current < data.cost) return;
            readyTime = Time.time + data.cooldown;

            var cam = Camera.main.transform;
            LaunchRpc(cam.forward);
        }

        [Rpc(SendTo.Server)]
        void LaunchRpc(Vector3 direction)
        {
            if (!resource.TrySpend(data.cost)) return;
            Vector3 origin = transform.position + Vector3.up * 1.2f + transform.forward * 0.8f;
            var obj = Instantiate(data.prefab, origin, Quaternion.LookRotation(direction));
            obj.GetComponent<Projectile>().Launch(data, direction);
            obj.Spawn(destroyWithScene: true);
        }
    }
}
