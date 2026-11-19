using System.Collections;
using OYM;
using OYM.Net;
using UnityEngine;

namespace OnYourMark
{
    // The lower half stays on the truss; the cut carries the upper half and its takeoff arrows toward the upper lane.
    public sealed class LaserSplitDeck : MonoBehaviour
    {
        [SerializeField] Mesh lowerMesh;
        [SerializeField] MeshFilter intactHull;
        [SerializeField] MeshCollider upperCollider;
        [SerializeField] Transform[] takeoffGuides;
        [SerializeField] Vector3 cutStart, cutEnd, travel;
        [SerializeField] Material beamMaterial;
        CoopSession session;
        bool cut;

        void OnEnable()
        {
            session = CoopSession.Active;
            if (session) session.Message += OnMessage;
        }

        void OnDisable()
        {
            if (session) session.Message -= OnMessage;
        }

        void Update()
        {
            if (cut || (session && !session.IsHost)) return;
            var encounter = HuskEncounter.Current;
            var seam = intactHull.transform.TransformPoint((cutStart + cutEnd) * .5f);
            if (!encounter || !encounter.Hostile || !encounter.AnyPilotWithin(seam, 14f, out _)) return;
            if (session && session.Connected) session.Send(new[] { CoopMsg.ArenaDeckCut });
            StartCoroutine(Split());
        }

        void OnMessage(CoopMsg.R message)
        {
            if (message.Type == CoopMsg.ArenaDeckCut && !session.IsHost && !cut) StartCoroutine(Split());
        }

        IEnumerator Split()
        {
            cut = true;
            intactHull.sharedMesh = lowerMesh;
            intactHull.GetComponent<MeshCollider>().sharedMesh = lowerMesh;
            upperCollider.enabled = true;
            upperCollider.GetComponent<MeshRenderer>().enabled = true;

            var beam = new GameObject("Deck cutting beam").AddComponent<LineRenderer>();
            beam.transform.SetParent(transform, false);
            beam.sharedMaterial = beamMaterial;
            beam.startWidth = beam.endWidth = .34f;
            beam.positionCount = 2;
            var start = intactHull.transform.TransformPoint(cutStart);
            var end = intactHull.transform.TransformPoint(cutEnd);
            var incoming = Vector3.up * 16f - Vector3.ProjectOnPlane(end - start, Vector3.up).normalized * 7f;
            var origin = transform.position;
            var displacement = transform.parent.TransformVector(travel);
            float elapsed = 0f;
            do
            {
                var contact = Vector3.Lerp(start, end, elapsed / 1.9f);
                beam.enabled = elapsed < 1.9f;
                beam.SetPosition(0, contact + incoming);
                beam.SetPosition(1, contact - incoming * .35f);

                var next = origin + displacement * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.45f, 2.75f, elapsed));
                var delta = next - transform.position;
                var rider = MovingPlatform.Rider(upperCollider);
                transform.position = next;
                foreach (var guide in takeoffGuides) guide.position += delta;
                if (rider) rider.Move(delta);
                if (elapsed >= 2.75f) break;
                yield return null;
                elapsed += Time.deltaTime;
            } while (true);
            Destroy(beam.gameObject);
        }
    }
}
