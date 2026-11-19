using System.Collections;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 짚라인: 출발 기둥의 고리(트리거)에 달려들면 줄에 매달려 반대편 갑판까지 미끄러져 간다.
    /// 움직임은 각자 자기 캐릭터(소유자)에서 한다. 줄은 기둥 꼭대기 사이에 걸린 LineRenderer.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Zipline : MonoBehaviour
    {
        public Transform end;
        [Min(4f)] public float speed = 17f;
        [Tooltip("손잡이를 잡으러 뛰어오르는 높이: 출발 갑판 가장자리의 턱 위로 줄을 타기 시작한다")]
        public float grabLift = 1.2f;
        float cooldownUntil;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            if (!end || Time.time < cooldownUntil) return;
            var player = other.GetComponentInParent<Player>();
            if (!player || !player.IsSpawned || !player.IsOwner || player.Health.IsDead) return;
            var motor = player.Motor;
            if (!motor || !motor.enabled || motor.IsLeaping) return;
            cooldownUntil = Time.time + .4f;
            StartCoroutine(Ride(motor));
            motor.RefillHover();
        }

        IEnumerator Ride(PlayerMotor motor)
        {
            const float grabTime = .14f;
            motor.Zip(motor.transform.position + Vector3.up * grabLift, grabTime);
            yield return new WaitForSeconds(grabTime);
            if (motor && motor.enabled) motor.Zip(end.position, Vector3.Distance(motor.transform.position, end.position) / speed);
        }
    }
}
