using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>
    /// P2 히트스캔 무기. 화면 중앙 방향으로 서버가 레이캐스트한다.
    /// 수치는 WeaponModes의 현재 모드(따발총 / 레일건)에서 읽는다. 따발총은 누르고 있으면 연사, 레일건은 관통.
    /// 무기 변형 중에는 쏘지 않는다.
    /// 재장전은 ResourceReload가 맡는다 (파트너의 R, 2번 파츠 반환 후에는 내 R과 자동 재생).
    /// 탄이 없으면 내 화면에서만 빈 총 격발음을 낸다.
    /// </summary>
    public class HitscanWeapon : NetworkBehaviour
    {
        [Tooltip("WeaponModes가 없을 때 쓰는 기본 무기")]
        [SerializeField] HitscanData data;
        [SerializeField] WeaponModes modes;
        [SerializeField] InputActionReference fire;
        [SerializeField] AmmoPool magazine;
        [SerializeField] Transform muzzle;
        [SerializeField] LineRenderer tracer;
        [SerializeField] AudioSource audioSource;
        [Tooltip("팀원 지원으로 쓰인 클릭이면 쏘지 않는다")]
        [SerializeField] SupportGiver support;

        float readyTime;
        bool triggerUsedForSupport;   // 지원으로 쓴 클릭은 뗄 때까지 연사하지 않는다
        Player me;
        AwakeningSet awakening;

        void Awake()
        {
            me = GetComponent<Player>();
            awakening = GetComponent<AwakeningSet>();
        }

        void Update()
        {
            if (!IsOwner || !CursorLock.IsLocked) return;

            var weapon = Weapon;
            if (support.ConsumedTrigger) triggerUsedForSupport = true;
            if (!fire.action.IsPressed()) triggerUsedForSupport = false;

            bool pulled = weapon.automatic ? fire.action.IsPressed() : fire.action.WasPressedThisFrame();
            if (!pulled || triggerUsedForSupport || Time.time < readyTime || (modes && modes.IsSwitching)) return;
            readyTime = Time.time + weapon.cooldown;

            if (magazine.Current == 0) { if (fire.action.WasPressedThisFrame()) audioSource.PlayOneShot(weapon.dryFireSound); return; }

            var cam = Camera.main.transform;
            FireRpc(cam.position, cam.forward);
        }

        HitscanData Weapon => modes ? modes.Current : data;

        [Rpc(SendTo.Server)]
        void FireRpc(Vector3 aimOrigin, Vector3 aimDirection)
        {
            if ((modes && modes.IsSwitching) || !magazine.TrySpend(1)) return;
            var weapon = Weapon;

            // 카메라 위치가 아니라 캐릭터 위치에 해당하는 지점부터 사거리를 잰다
            Vector3 origin = aimOrigin + aimDirection * Vector3.Dot(transform.position - aimOrigin, aimDirection);
            Vector3 end = origin + aimDirection * weapon.range;

            if (!weapon.pierce)
            {
                if (Physics.Raycast(origin, aimDirection, out var hit, weapon.range, weapon.hitMask))
                {
                    end = hit.point;
                    Health.Of(hit.collider)?.Damage(weapon.damage, me);
                    awakening.NotifyHit(hit.point);
                }
            }
            else
            {
                // 관통: 가까운 순서로 맞은 적을 모두 때리고, 적이 아닌 것(지형)에 닿으면 멈춘다
                var hits = Physics.RaycastAll(origin, aimDirection, weapon.range, weapon.hitMask);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                var damaged = new System.Collections.Generic.HashSet<Health>();
                foreach (var hit in hits)
                {
                    var h = Health.Of(hit.collider);
                    if (h == null) { end = hit.point; break; }
                    if (!damaged.Add(h)) continue;
                    h.Damage(weapon.damage, me);
                    awakening.NotifyHit(hit.point);
                }
            }
            ShowTracerRpc(end);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ShowTracerRpc(Vector3 end)
        {
            tracer.SetPosition(0, muzzle.position);
            tracer.SetPosition(1, end);
            StartCoroutine(MeleeWeapon.Flash(tracer.gameObject, 0.08f));
        }
    }
}
