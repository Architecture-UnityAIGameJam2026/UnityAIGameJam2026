using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OYM
{
    /// The CITADEL's weak points = its red sensor lights (head eye cluster, the four claw sensors), installed on the boss
    /// when the fight starts (HuskLevel). Each one is broken on its own: it flares and dies, blasts walk across its glow
    /// spots one after another, a claw tears off and tumbles away burning, the head keeps burning. The last one sets off
    /// the end: a chain of blasts across the hull, every light on the hull goes dark, then AllBroken (HuskLevel: victory).
    /// Positions come from the baked Resources/OYM_CitadelWeakPoints (glow of the emissive maps); without it, a rough
    /// guess from the parts' bounds.
    public class CitadelWeakPoints : MonoBehaviour
    {
        public static CitadelWeakPoints Current { get; private set; }
        /// (point, weak points left) whenever one breaks
        public static event System.Action<CitadelWeakPoint, int> Broke;
        public event System.Action AllBroken;

        public float pointHP = 200f;
        [Tooltip("hit sphere round each light, metres: big enough to hit a small light on a 300 m hull, small enough not to wall off the pilot")]
        public float minHitRadius = 5f, maxHitRadius = 8f;

        public readonly List<CitadelWeakPoint> Points = new List<CitadelWeakPoint>();
        public readonly List<HuskBossPart> Pods = new List<HuskBossPart>();
        public HuskEncounter Encounter { get; private set; }
        public Transform Boss { get; private set; }
        public bool Finished { get; private set; }
        public int Total => Points.Count;
        public int Left { get { int n = 0; foreach (var p in Points) if (p && !p.Broken) n++; return n; } }

        static readonly string[] Parts = { "citadel_head", "citadel_claw_L1", "citadel_claw_R1", "citadel_claw_L2", "citadel_claw_R2" };
        static readonly int EmissiveId = Shader.PropertyToID("_EmissiveColor");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Current = null; Broke = null; }

        public static CitadelWeakPoints Install(HuskEncounter e, Transform boss)
        {
            if (!boss) return null;
            var w = boss.GetComponent<CitadelWeakPoints>();
            if (!w) { w = boss.gameObject.AddComponent<CitadelWeakPoints>(); w.Setup(e, boss); }
            return w;
        }

        void OnDestroy() { if (Current == this) Current = null; }

        void Setup(HuskEncounter e, Transform boss)
        {
            Current = this;
            Encounter = e; Boss = boss;
            var byName = new Dictionary<string, Transform>();
            foreach (var t in boss.GetComponentsInChildren<Transform>(true))
                if (!t.name.Contains("_LOD") && !byName.ContainsKey(t.name)) byName[t.name] = t;
            var map = Resources.Load<CitadelWeakPointMap>("OYM_CitadelWeakPoints");
            var entries = new List<CitadelWeakPointMap.Entry>();
            if (map && map.entries.Count > 0) entries.AddRange(map.entries);
            else foreach (var n in Parts) if (byName.TryGetValue(n, out var pt)) entries.Add(Guess(pt));
            var eyes = new List<Light>();
            foreach (var l in boss.GetComponentsInChildren<Light>(true)) if (l.name.StartsWith("EyeLight")) eyes.Add(l);
            foreach (var en in entries)
            {
                if (!byName.TryGetValue(en.part, out var pt) || !pt.gameObject.activeInHierarchy) continue;
                var go = new GameObject("WeakPoint " + en.part);
                var wp = go.AddComponent<CitadelWeakPoint>();
                wp.Setup(this, pt, en, pointHP, minHitRadius, maxHitRadius, en.part.Contains("head") ? eyes.ToArray() : null);
                Points.Add(wp);
            }
            Pods.AddRange(boss.GetComponentsInChildren<HuskBossPart>(true));
            Debug.Log($"[OYM-HUSK] CITADEL weak points: {Points.Count} red lights ({(map ? "baked map" : "bounds guess")}), {Pods.Count} launcher pods");
        }

        /// no baked map: the head's lights sit low at its front, a claw's near its root (rough)
        static CitadelWeakPointMap.Entry Guess(Transform part)
        {
            Bounds b = default; bool any = false;
            foreach (var mf in part.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh || mf.name.Contains("_LOD1") || mf.name.Contains("_LOD2")) continue;
                var mb = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var q = part.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { b = new Bounds(q, Vector3.zero); any = true; } else b.Encapsulate(q);
                }
            }
            var e = new CitadelWeakPointMap.Entry { part = part.name, radius = any ? b.extents.magnitude * 0.18f : 4f };
            e.center = !any ? Vector3.zero : part.name.Contains("head")
                ? new Vector3(b.center.x, b.min.y + b.size.y * 0.3f, b.max.z - b.size.z * 0.08f)
                : b.center;
            e.normal = Vector3.forward;
            e.spots = new[] { e.center };
            return e;
        }

        // ------------------------------------------------------------------ breaking
        public void OnPointBroken(CitadelWeakPoint p, Vector3 n)
        {
            int left = Left;
            Debug.Log($"[OYM-HUSK] weak point {p.part} broken, {left} left");
            StartCoroutine(BreakSequence(p, n));
            Broke?.Invoke(p, left);
            if (left == 0 && !Finished)
            {
                Finished = true;
                StartCoroutine(Finale());
                AllBroken?.Invoke();
            }
        }

        IEnumerator BreakSequence(CitadelWeakPoint p, Vector3 n)
        {
            Vector3 main = p.AimPoint;
            HuskFX.PlayHit(HitKind.Explosion, main, n, null, 7f, null);
            HitScreenFX.Impulse(main, 0.85f, 450f, 0f, 0.3f);              // distance-scaled shake + a little flash
            GameFeel.HitStop(0.08f, 0.1f);                                   // the "쪼" beat of the break
            // the blasts walk across the light's glow spots, then round them
            int spots = Mathf.Max(1, p.SpotCount);
            float r = Mathf.Max(3f, p.Radius * 0.6f);
            for (int i = 0; i < 5; i++)
            {
                yield return new WaitForSeconds(0.12f + 0.07f * i);
                if (!p) yield break;
                Vector3 at = p.SpotWorld(i % spots) + Random.insideUnitSphere * r * (i < spots ? 0.2f : 0.8f);
                HuskFX.PlayHit(HitKind.Explosion, at, p.NormalWorld, null, i == 0 ? 5f : 3.8f, null);
            }
            if (p.IsClaw) StartCoroutine(TearOff(p));
            else StartCoroutine(Burn(p, 3.2f));
        }

        /// a claw tears off at its root and tumbles away, heavy and slow, burning at the joint
        IEnumerator TearOff(CitadelWeakPoint p)
        {
            var claw = p.Part;
            if (!claw) yield break;
            Vector3 c = Boss ? HullBounds().center : claw.position;
            Vector3 out_ = claw.position - c; out_.y = 0f;
            out_ = out_.sqrMagnitude > 1f ? out_.normalized : -Boss.right;
            Vector3 v = out_ * Random.Range(4f, 6.5f) + Vector3.down * Random.Range(2.5f, 4f);
            Vector3 axis = Vector3.Cross(Vector3.up, out_).normalized;
            float spin = Random.Range(5f, 9f);
            claw.SetParent(null, true);
            Vector3 joint = p.SpotWorld(0);
            HuskFX.PlayHit(HitKind.Explosion, joint, out_, null, 6f, null);
            float t = 0f, nextBurn = 0.5f;
            while (t < 26f && claw)
            {
                float dt = Time.deltaTime;
                t += dt;
                float k = Mathf.Clamp01(t / 1.2f);                      // heavy: it takes a moment to get going
                claw.position += v * (k * dt);
                claw.Rotate(axis, spin * k * dt, Space.World);
                if (t >= nextBurn && t < 5f) { nextBurn += Random.Range(0.45f, 0.8f); HuskFX.PlayHit(HitKind.Explosion, p.SpotWorld(0), out_, null, 2.4f, null); }
                yield return null;
            }
            if (claw) foreach (var rr in claw.GetComponentsInChildren<Renderer>(true)) rr.enabled = false;
        }

        IEnumerator Burn(CitadelWeakPoint p, float seconds)
        {
            float t = 0f;
            while (t < seconds && p)
            {
                float w = Random.Range(0.35f, 0.7f);
                yield return new WaitForSeconds(w);
                t += w;
                if (p) HuskFX.PlayHit(HitKind.Explosion, p.SpotWorld(Random.Range(0, p.SpotCount)), p.NormalWorld, null, 2.2f, null);
            }
        }

        /// the last light is gone: blasts chain across the hull, every light on it goes dark
        IEnumerator Finale()
        {
            yield return new WaitForSeconds(0.5f);
            var rs = new List<Renderer>();
            foreach (var r in Boss.GetComponentsInChildren<Renderer>())
                if (r.enabled && !r.name.Contains("_LOD1") && !r.name.Contains("_LOD2")) rs.Add(r);
            foreach (var pod in Pods) if (pod && !pod.Broken) pod.Hit(99999f, pod.Center, Vector3.up);
            const int n = 16;
            for (int i = 0; i < n && rs.Count > 0; i++)
            {
                var r = rs[Random.Range(0, rs.Count)];
                if (!r) continue;
                var b = r.bounds;
                Vector3 dir = Random.onUnitSphere; dir.y = Mathf.Abs(dir.y) * 0.6f + 0.2f;
                Vector3 at = b.center + Vector3.Scale(b.extents, dir) * 0.85f;
                bool big = i == n - 1 || i % 5 == 4;
                HuskFX.PlayHit(HitKind.Explosion, at, dir, null, big ? 14f : Random.Range(6f, 10f), null);
                if (big) HitScreenFX.Impulse(at, 0.9f, 900f, 0.06f, 0.4f);
                yield return new WaitForSeconds(Random.Range(0.12f, 0.28f));
            }
            // lights out: whatever still glows on the hull (engine, tubes) dies with it
            var mpb = new MaterialPropertyBlock();
            foreach (var r in Boss.GetComponentsInChildren<Renderer>(true))
            {
                if (!r) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetVector(EmissiveId, Vector4.zero);
                r.SetPropertyBlock(mpb);
            }
            foreach (var l in Boss.GetComponentsInChildren<Light>(true)) if (l && l.type != LightType.Directional) l.enabled = false;
        }

        Bounds HullBounds()
        {
            Bounds b = new Bounds(Boss.position, Vector3.one);
            bool any = false;
            foreach (var r in Boss.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r.name.Contains("_LOD1") || r.name.Contains("_LOD2")) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }
    }
}
