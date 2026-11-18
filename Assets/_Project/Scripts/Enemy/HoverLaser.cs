using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 공중 레이저형. 상공에서 플레이어와 거리를 유지하다가, 사거리 안의 대상을 고정해
    /// 조준선을 보여 주며 차징한 뒤 직선 레이저를 쏜다. 조준은 느리게 따라가므로 대쉬로 피할 수 있다.
    /// </summary>
    public class HoverLaser : EnemyBase
    {
        [SerializeField] HoverLaserData data;
        [SerializeField] Transform eye;
        [SerializeField] LineRenderer laser;

        // 조준선 표시용 (서버가 쓰고 모두가 그린다)
        readonly NetworkVariable<bool> charging = new();
        readonly NetworkVariable<Vector3> aimPoint = new();

        float readyTime;
        bool flashing;   // 발사 순간 굵은 레이저 연출 중
        float baseWidth;

        protected override EnemyData Data => data;

        void Awake() => baseWidth = laser.widthMultiplier;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // 풀에서 재사용: 차징 중에 죽었던 인스턴스의 조준선·굵기·쿨다운을 되돌린다
            flashing = false;
            laser.widthMultiplier = baseWidth;
            laser.enabled = false;
            readyTime = 0f;
            if (IsServer) { Agent.baseOffset = data.hoverHeight; charging.Value = false; }
        }

        void Update()
        {
            laser.enabled = charging.Value || flashing;
            if (laser.enabled) { laser.SetPosition(0, eye.position); laser.SetPosition(1, aimPoint.Value); }

            if (!IsServer || !IsSpawned || charging.Value) return;

            var target = NearestPlayer();
            if (target == null) return;

            // 거리 유지: 멀면 다가가고 가까우면 멈춘다
            Agent.stoppingDistance = data.preferredDistance;
            Agent.SetDestination(target.transform.position);

            if (Time.time >= readyTime && InSight(target)) StartCoroutine(ChargeAndFire(target));
        }

        bool InSight(Health target)
        {
            Vector3 to = Chest(target) - eye.position;
            return to.magnitude <= data.range
                   && Physics.Raycast(eye.position, to.normalized, out var hit, data.range, data.targetMask)
                   && Health.Of(hit.collider) == target;
        }

        IEnumerator ChargeAndFire(Health target)
        {
            Agent.isStopped = true;
            charging.Value = true;
            Vector3 aim = (Chest(target) - eye.position).normalized;

            for (float t = 0; t < data.chargeTime; t += Time.deltaTime)
            {
                if (target != null)
                {
                    Vector3 want = (Chest(target) - eye.position).normalized;
                    aim = Vector3.RotateTowards(aim, want, data.trackingSpeed * Mathf.Deg2Rad * Time.deltaTime, 0f);
                }
                aimPoint.Value = eye.position + aim * data.range;
                yield return null;
            }

            if (Physics.Raycast(eye.position, aim, out var hit, data.range, data.targetMask))
            {
                aimPoint.Value = hit.point;
                if (Health.Of(hit.collider) is { } h) h.Damage(data.damage);
            }
            ShowShotRpc();

            charging.Value = false;
            Agent.isStopped = false;
            readyTime = Time.time + data.cooldown;
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ShowShotRpc() => StartCoroutine(Pulse());

        static readonly WaitForSeconds PulseWait = new(0.15f);

        IEnumerator Pulse()
        {
            laser.widthMultiplier = baseWidth * 5f;
            flashing = true;
            yield return PulseWait;
            flashing = false;
            laser.widthMultiplier = baseWidth;
        }
    }
}
