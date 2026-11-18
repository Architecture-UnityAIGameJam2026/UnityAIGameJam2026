using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>
    /// P2 레일건. 화면 중앙 방향으로 서버가 레이캐스트한다.
    /// 재장전은 ResourceReload가 맡는다 (파트너의 R, 2번 파츠 반환 후에는 내 R과 자동 재생).
    /// 탄이 없으면 내 화면에서만 빈 총 격발음을 낸다.
    /// </summary>
    public class HitscanWeapon : NetworkBehaviour
    {
        [SerializeField] HitscanData data;
        [SerializeField] InputActionReference fire;
        [SerializeField] AmmoPool magazine;
        [SerializeField] Transform muzzle;
        [SerializeField] LineRenderer tracer;
        [SerializeField] AudioSource audioSource;
        [Tooltip("팀원 지원으로 쓰인 클릭이면 쏘지 않는다")]
        [SerializeField] SupportGiver support;

        float readyTime;
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

            if (!fire.action.WasPressedThisFrame() || support.ConsumedTrigger || Time.time < readyTime) return;
            readyTime = Time.time + data.cooldown;

            if (magazine.Current == 0) { audioSource.PlayOneShot(data.dryFireSound); return; }

            var cam = Camera.main.transform;
            FireRpc(cam.position, cam.forward);
        }

        [Rpc(SendTo.Server)]
        void FireRpc(Vector3 aimOrigin, Vector3 aimDirection)
        {
            if (!magazine.TrySpend(1)) return;

            // 카메라는 캐릭터 뒤에 있으므로, 캐릭터 위치에 해당하는 지점부터 사거리를 잰다
            Vector3 origin = aimOrigin + aimDirection * Vector3.Dot(transform.position - aimOrigin, aimDirection);
            Vector3 end = origin + aimDirection * data.range;
            if (Physics.Raycast(origin, aimDirection, out var hit, data.range, data.hitMask))
            {
                end = hit.point;
                Health.Of(hit.collider)?.Damage(data.damage, me);
                awakening.NotifyHit(hit.point);
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
