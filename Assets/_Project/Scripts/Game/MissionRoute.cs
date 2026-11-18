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
        void Awake()
        {
            links = GetComponentsInChildren<NavMeshLink>();
            if (landingHull) docked = landingHull.position;
            if (finalPlate) ClosedPlate = finalPlate.localRotation;
            if (deck != null && deck.Length > 0 && Physics.Raycast(deck[0].position + Vector3.up * 2, Vector3.down, out var hit, 6, LayerMask.GetMask("Environment")))
                launchHull = hit.collider;
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
