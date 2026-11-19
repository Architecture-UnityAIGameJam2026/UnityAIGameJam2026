using UnityEngine;
using Unity.AI.Navigation;

namespace OnYourMark
{
    // Markers sit on the actual walkable surfaces. The same route drives HUD guidance, checkpoints and AI.
    public class MissionRoute : MonoBehaviour
    {
        public Transform[] deck, upperLane, lowerLane;
        public Transform finalPlate, landingHull;
        Vector3 docked;
        Collider launchHull;
        NavMeshLink[] links;
        public Quaternion ClosedPlate { get; private set; }
        bool[] stepped;
        void Awake()
        {
            links = GetComponentsInChildren<NavMeshLink>();
            // 디딤판이 놓인 틈과 바닥이 이어진 통로는 걸어서 · 밟고 건너는 자리다: 점프 어시스트로 넘겨 버리지 않는다
            stepped = new bool[links.Length];
            for (int i = 0; i < links.Length; i++)
            {
                var kind = Classify(links[i].transform.TransformPoint(links[i].startPoint), links[i].transform.TransformPoint(links[i].endPoint));
                stepped[i] = kind != GapKind.Open;
            }
            if (landingHull) docked = landingHull.position;
            if (finalPlate) ClosedPlate = finalPlate.localRotation;
            if (deck != null && deck.Length > 0 && Physics.Raycast(deck[0].position + Vector3.up * 2, Vector3.down, out var hit, 6, LayerMask.GetMask("Environment")))
                launchHull = hit.collider;
        }
        public enum GapKind { Open, Foothold, Walkway }

        /// <summary>
        /// 링크(a→b) 아래가 어떤 자리인지: 디딤판(MissionLevelBuilder의 "Foothold": 징검다리 · 떠 있는 화물)이 놓였거나,
        /// 처음부터 끝까지 바닥이 이어진 통로(하부 트러스 다리)거나, 진짜 빈 틈이거나. 움직이는 발판은 바닥으로 치지 않는다
        /// </summary>
        public static GapKind Classify(Vector3 a, Vector3 b)
        {
            int ground = 0;
            for (int k = 1; k <= 19; k++)
            {
                if (!Physics.Raycast(Vector3.Lerp(a, b, k / 20f) + Vector3.up * 1.5f, Vector3.down, out var h, 3.5f, LayerMask.GetMask("Default", "Environment"), QueryTriggerInteraction.Ignore)) continue;
                if (h.collider.name.StartsWith("Foothold")) return GapKind.Foothold;
                if (!h.collider.GetComponentInParent<MovingPlatform>()) ground++;
            }
            return ground >= 15 ? GapKind.Walkway : GapKind.Open;
        }

        // Navigation supplies the link corners, while the bot crosses with its normal jump and hover fuel.
        public bool TryFindJump(Vector3 position, Vector3[] corners, out Vector3 landing)
        {
            landing = default;
            foreach (var link in links)
            {
                var start = link.transform.TransformPoint(link.startPoint);
                var end = link.transform.TransformPoint(link.endPoint);
                if ((position - end).sqrMagnitude < (position - start).sqrMagnitude) (start, end) = (end, start);
                if ((position - start).sqrMagnitude > 9f) continue;
                for (int i = 1; i < corners.Length; i++)
                {
                    if ((corners[i - 1] - start).sqrMagnitude > 4f || (corners[i] - end).sqrMagnitude > 4f) continue;
                    landing = end + Vector3.ProjectOnPlane(end - start, Vector3.up).normalized * .7f;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 점프 어시스트: 틈(링크)의 한쪽 끝 가까이에서 건너편 쪽으로 뛰면 건너편 착지점을 돌려준다.
        /// 가까운 끝 3.5m 안, 진행 방향이 건너편과 40도 안, 건너편이 4m보다 멀 때만.
        /// </summary>
        public Vector3? AssistJump(Vector3 position, Vector3 heading)
        {
            heading = Vector3.ProjectOnPlane(heading, Vector3.up);
            if (heading.sqrMagnitude < .01f || links == null) return null;
            Vector3? best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < links.Length; i++)
            {
                var link = links[i];
                if (!link || !link.isActiveAndEnabled || stepped[i]) continue;
                var a = link.transform.TransformPoint(link.startPoint);
                var b = link.transform.TransformPoint(link.endPoint);
                for (int k = 0; k < 2; k++)
                {
                    var near = k == 0 ? a : b; var far = k == 0 ? b : a;
                    float d = Vector3.ProjectOnPlane(near - position, Vector3.up).magnitude;
                    if (d > 3.5f || Mathf.Abs(near.y - position.y) > 2.5f) continue;
                    Vector3 across = Vector3.ProjectOnPlane(far - near, Vector3.up);
                    // 15m보다 긴 틈(합류 QTE 지점 등)은 연출 구간이라 보정하지 않는다
                    if (across.magnitude < 4f || across.magnitude > 15f || Vector3.Angle(heading, far - position) > 40f) continue;
                    float score = d + Vector3.Angle(heading, across) * .05f;
                    if (score >= bestScore) continue;
                    bestScore = score;
                    best = far + across.normalized * .9f + Vector3.up * .15f;
                }
            }
            return best;
        }

        public bool LeavingFirstHull(Vector3 position)
        {
            if (!launchHull) return Vector3.Distance(position, deck[0].position) < 8;
            var bounds = launchHull.bounds;
            bounds.Expand(new Vector3(3, 7, 3));
            return bounds.Contains(position);
        }
        public void MoveLanding(float offset)
        {
            if (landingHull) landingHull.position = docked + Vector3.right * offset;
        }
        public GameObject fragmentArt;
        public Material signalMaterial;

        public Vector3 Lane(bool sword, int step) => (sword ? upperLane : lowerLane)[step].position;

        public Vector3 Checkpoint(int phase, bool sword)
        {
            int index = phase switch { 2 => 3, 3 => 9, 4 => 12, _ => 0 };
            return deck[index].position + (sword ? Vector3.left : Vector3.right) * 1.1f + Vector3.up * .22f;
        }
    }
}
