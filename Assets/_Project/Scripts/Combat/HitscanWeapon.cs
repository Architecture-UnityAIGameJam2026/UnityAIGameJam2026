using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>
    /// P2 레일건. 화면 중앙 방향으로 서버가 레이캐스트한다. R로 재장전.
    /// 탄이 없으면 내 화면에서만 빈 총 격발음을 낸다.
    /// </summary>
    public class HitscanWeapon : NetworkBehaviour
    {
        [SerializeField] HitscanData data;
        [SerializeField] InputActionReference fire, reload;
        [SerializeField] AmmoPool magazine;
        [SerializeField] Transform muzzle;
        [SerializeField] LineRenderer tracer;
        [SerializeField] AudioSource audioSource;
        [Tooltip("팀원 지원으로 쓰인 클릭이면 쏘지 않는다")]
        [SerializeField] SupportGiver support;

        public static event System.Action<Player> DryFired;
        float readyTime, nextDryReport;
        Player me;
        AwakeningSet awakening;

        void Awake()
        {
            me = GetComponent<Player>();
            awakening = GetComponent<AwakeningSet>();
        }
        bool reloading;   // 서버 상태

        void Update()
        {
            if (!IsOwner || !NetworkObject.IsPlayerObject || !CursorLock.IsLocked) return;

            if (reload.action.WasPressedThisFrame() && !magazine.IsFull) ReloadRpc();

            if (!fire.action.WasPressedThisFrame() || support.ConsumedTrigger || Time.time < readyTime) return;
            readyTime = Time.time + data.cooldown;

            if (magazine.Current == 0)
            {
                audioSource.PlayOneShot(data.dryFireSound);
                ReportDryFireRpc();
                return;
            }

            var cam = Camera.main.transform;
            FireRpc(cam.position, cam.forward);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void ReportDryFireRpc()
        {
            if (magazine.Current != 0 || me.Health.IsDead || reloading || Time.time < nextDryReport) return;
            nextDryReport = Time.time + data.cooldown;
            DryFired?.Invoke(me);
        }

        public bool Reloading => reloading;
        public float Range => data.range;
        public bool CanBotFire => me.IsBot && IsServer && !me.Health.IsDead && !reloading && magazine.Current > 0 && Time.time >= readyTime;
        public bool BotShoot(Vector3 direction)
        {
            if (!CanBotFire) return false;
            readyTime = Time.time + data.cooldown;
            Fire(transform.position + Vector3.up * 1.1f, direction);
            return true;
        }
        public bool BotReload()
        {
            if (!IsServer || !me.IsBot || me.Health.IsDead || reloading || magazine.IsFull) return false;
            StartCoroutine(Reload());
            return true;
        }

        [Rpc(SendTo.Server)]
        void FireRpc(Vector3 aimOrigin, Vector3 aimDirection)
        {
            // 카메라는 캐릭터 뒤에 있으므로, 캐릭터 위치에 해당하는 지점부터 사거리를 잰다
            Vector3 origin = aimOrigin + aimDirection * Vector3.Dot(transform.position - aimOrigin, aimDirection);
            Fire(origin, aimDirection);
        }

        void Fire(Vector3 origin, Vector3 aimDirection)
        {
            if (reloading || !magazine.TrySpend(1)) return;
            Vector3 end = origin + aimDirection * data.range;
            if (Physics.Raycast(origin, aimDirection, out var hit, data.range, data.hitMask))
            {
                end = hit.point;
                Health.Of(hit.collider)?.Damage(data.damage, me);
                awakening.NotifyHit(hit.point);
            }
            ShowTracerRpc(end);
        }

        [Rpc(SendTo.Server)]
        void ReloadRpc()
        {
            if (!reloading) StartCoroutine(Reload());
        }

        IEnumerator Reload()
        {
            reloading = true;
            yield return new WaitForSeconds(data.reloadTime * awakening.ReloadMultiplier);
            magazine.Fill();
            reloading = false;
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
