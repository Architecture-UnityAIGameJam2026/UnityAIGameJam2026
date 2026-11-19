using System.Collections;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 짚라인: 출발 기둥의 고리(트리거)에 달려들면 줄에 매달려 반대편 갑판까지 미끄러져 간다.
    /// 움직임은 각자 자기 캐릭터(소유자)에서 한다. 줄은 기둥 꼭대기 사이에 걸린 LineRenderer.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Zipline : ArenaTransit
    {
        public Transform end;
        [Min(4f)] public float speed = 17f;
        [Tooltip("손잡이를 잡으러 뛰어오르는 높이: 출발 갑판 가장자리의 턱 위로 줄을 타기 시작한다")]
        public float grabLift = 1.2f;
        protected override void Ride(OYM.ZeroGFlight pilot)
        {
            if (end) StartCoroutine(GrabCable(pilot));
        }

        IEnumerator GrabCable(OYM.ZeroGFlight pilot)
        {
            var forward = Vector3.ProjectOnPlane(end.position - transform.position, Vector3.up).normalized;
            var trigger = GetComponent<Collider>().bounds;
            // Board between the posts, below the crossbar, even when a pilot approaches from above.
            var mount = trigger.center + forward * (pilot.bodyRadius + .6f);
            mount.y = Mathf.Min(pilot.transform.position.y + grabLift, trigger.center.y - .15f);
            float grabTime = Mathf.Max(.14f, Vector3.Distance(pilot.transform.position, mount) / speed);
            pilot.RideArenaDevice(mount, grabTime, false);
            while (pilot && pilot.isActiveAndEnabled && pilot.RidingArenaDevice) yield return null;
            if (pilot && pilot.isActiveAndEnabled && !pilot.scripted && end)
                pilot.RideArenaDevice(end.position, Vector3.Distance(pilot.transform.position, end.position) / speed, false);
        }
    }
}
