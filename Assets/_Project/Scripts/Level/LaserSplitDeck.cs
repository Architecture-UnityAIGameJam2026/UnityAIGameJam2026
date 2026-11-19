using OYM;
using OYM.Net;
using UnityEngine;

namespace OnYourMark
{
    // The laser follows the authored seam. Kestrel rides the upper piece while Orbit stays on the lower truss.
    [DefaultExecutionOrder(-200)]
    public sealed class LaserSplitDeck : MonoBehaviour
    {
        const float RallyRadius = 2.4f;
        const float SweepDuration = 1.9f;
        const float OpenAt = 1.45f;
        const float FullyOpenAt = 2.75f;

        [SerializeField] Mesh lowerMesh;
        [SerializeField] MeshFilter intactHull;
        [SerializeField] MeshCollider upperCollider;
        [SerializeField] Transform approachSleepers;
        [SerializeField] Transform[] takeoffGuides;
        [SerializeField] Vector3 cutStart, cutEnd, upperFooting, lowerFooting, travel;
        [SerializeField] Material beamMaterial;
        CoopSession session;
        HuskZones.Zone approach;
        LineRenderer rallyMarker, beam;
        Vector3 origin, displacement, start, end, incoming;
        Vector3[] guideOrigins;
        double cutAt;
        bool cut, finished;

        static double Clock => CoopGame.Current ? CoopGame.Current.LevelTime : Time.unscaledTimeAsDouble;

        void Start()
        {
            var zones = FindFirstObjectByType<HuskZones>();
            if (zones) approach = zones.zones.Find(z => z.sleepers == approachSleepers);
        }

        void OnEnable()
        {
            session = CoopSession.Active;
            if (session) session.Message += OnMessage;
        }

        void OnDisable()
        {
            if (session) session.Message -= OnMessage;
        }

        Vector3 Footing(Transform pilot) =>
            intactHull.transform.TransformPoint(pilot.TryGetComponent<KestrelController>(out _) ? upperFooting : lowerFooting);

        bool AtRally(Transform pilot)
        {
            var offset = pilot.position - Footing(pilot);
            return Mathf.Abs(offset.y) <= .55f && offset.sqrMagnitude <= RallyRadius * RallyRadius;
        }

        void Update()
        {
            if (finished) return;
            if (cut) { AnimateCut(); return; }

            var encounter = HuskEncounter.Current;
            if (!encounter || !encounter.Player) return;
            ShowRally(encounter);
            if ((session && !session.IsHost) || !encounter.Hostile || approach == null || !approach.Cleared) return;
            foreach (var pilot in encounter.Pilots())
                if (encounter.AliveOf(pilot) && !AtRally(pilot)) return;

            var now = Clock;
            if (session && session.Connected) session.Send(new CoopMsg.W(CoopMsg.ArenaDeckCut).F((float)now).Done());
            BeginCut(now);
        }

        void ShowRally(HuskEncounter encounter)
        {
            var target = Footing(encounter.Player);
            bool visible = encounter.Gate && !encounter.hold && encounter.PlayerAlive &&
                (encounter.Player.position - target).sqrMagnitude < 35f * 35f;
            if (!visible) { if (rallyMarker) rallyMarker.enabled = false; return; }
            if (!rallyMarker)
            {
                // The original mission HUD points each pilot to their own footing.
                // Mark that spot directly on the deck in the shared field.
                rallyMarker = new GameObject("Laser rally").AddComponent<LineRenderer>();
                rallyMarker.transform.SetParent(intactHull.transform, true);
                rallyMarker.sharedMaterial = beamMaterial;
                rallyMarker.loop = true;
                rallyMarker.useWorldSpace = false;
                rallyMarker.startWidth = rallyMarker.endWidth = .08f;
                rallyMarker.positionCount = 32;
                for (int i = 0; i < rallyMarker.positionCount; i++)
                {
                    float angle = i * (2f * Mathf.PI / rallyMarker.positionCount);
                    rallyMarker.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
                }
            }
            rallyMarker.transform.position = target + Vector3.up * .12f;
            rallyMarker.enabled = true;
        }

        void OnMessage(CoopMsg.R message)
        {
            if (message.Type == CoopMsg.ArenaDeckCut && !session.IsHost && !cut)
                BeginCut(message.Remaining >= sizeof(float) ? message.F() : Clock);
        }

        void BeginCut(double startedAt)
        {
            cut = true;
            cutAt = startedAt;
            if (rallyMarker) Destroy(rallyMarker.gameObject);
            intactHull.sharedMesh = lowerMesh;
            intactHull.GetComponent<MeshCollider>().sharedMesh = lowerMesh;
            upperCollider.enabled = true;
            upperCollider.GetComponent<MeshRenderer>().enabled = true;

            beam = new GameObject("Deck cutting beam").AddComponent<LineRenderer>();
            beam.transform.SetParent(transform, false);
            beam.sharedMaterial = beamMaterial;
            beam.startWidth = beam.endWidth = .34f;
            beam.positionCount = 2;
            start = intactHull.transform.TransformPoint(cutStart);
            end = intactHull.transform.TransformPoint(cutEnd);
            incoming = Vector3.up * 16f - Vector3.ProjectOnPlane(end - start, Vector3.up).normalized * 7f;
            origin = transform.position;
            displacement = transform.parent.TransformVector(travel);
            guideOrigins = new Vector3[takeoffGuides.Length];
            for (int i = 0; i < guideOrigins.Length; i++) guideOrigins[i] = takeoffGuides[i].position;
            AnimateCut();
        }

        void AnimateCut()
        {
            float elapsed = Mathf.Max(0f, (float)(Clock - cutAt));
            var contact = Vector3.Lerp(start, end, elapsed / SweepDuration);
            beam.enabled = elapsed < SweepDuration;
            beam.SetPosition(0, contact + incoming);
            beam.SetPosition(1, contact - incoming * .35f);

            var offset = displacement * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(OpenAt, FullyOpenAt, elapsed));
            var next = origin + offset;
            var delta = next - transform.position;
            if (delta != Vector3.zero)
            {
                var rider = MovingPlatform.Rider(upperCollider);
                if (rider) rider.Move(delta);
                transform.position = next;
                for (int i = 0; i < takeoffGuides.Length; i++) takeoffGuides[i].position = guideOrigins[i] + offset;
            }
            if (elapsed < FullyOpenAt) return;
            finished = true;
            Destroy(beam.gameObject);
        }
    }
}
