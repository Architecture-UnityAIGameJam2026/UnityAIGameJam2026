using UnityEngine;

namespace OYM.Flow
{
    /// Dodge gimmick: a derelict solar concentrator on the array that still focuses the sun into a beam and sweeps it
    /// across the gate. Every `period`: the emitter warms up amber for `charge` s with a faint sighting line where the
    /// sweep will start (the telegraph), then the beam burns from `fromDir` round `axis` over `arc` degrees in `sweep`
    /// s (back the other way on the next cycle) and lingers for `linger` s. A pilot within `radius` of the burning beam
    /// takes `damage` (once per `cooldown`) and is shoved out of it. Orange, never white (FX taste: no white flashes in
    /// play); near the lens the beam dims. Same timing on both co-op PCs (FlowClock); each PC checks its own pilot.
    public class SweepBeam : MonoBehaviour
    {
        [Tooltip("normal of the plane the beam sweeps in (world)")] public Vector3 axis = Vector3.forward;
        [Tooltip("beam direction where a sweep starts (world, in that plane)")] public Vector3 fromDir = Vector3.down;
        [Tooltip("degrees swept; every other cycle sweeps back")] public float arc = 50f;
        [Tooltip("beam length when it hits nothing (m)")] public float range = 160f;
        [Tooltip("s: whole cycle / warm-up (telegraph) / sweep / beam lingering at the end")] public float period = 7.5f, charge = 1.6f, sweep = 2.4f, linger = 0.3f;
        [Tooltip("s added to the clock (two beams out of step)")] public float phase;
        public float radius = 1.6f, damage = 20f, knock = 9f, cooldown = 1f;
        [ColorUsage(false, true)] public Color color = new Color(1f, 0.42f, 0.1f);
        [Tooltip("light at the burn point at full beam (lumens at the reference exposure)")] public float lumen = 6000f;

        HuskFXCanvas _fx;
        Light _emitL, _burnL;
        AudioClip _chargeClip, _burnClip;
        float _hitAt = -9f;
        int _cueCycle = -1, _burnCycle = -1;
        Vector3 _end, _startEnd;
        float _beam, _warm;
        static readonly RaycastHit[] s_hits = new RaycastHit[16];

        public bool Burning => _beam > 0.5f;
        public Vector3 BeamEnd => _end;

        void Start()
        {
            var m = Hazard.Materials();
            if (m != null) _fx = new HuskFXCanvas(transform, m);
            _emitL = Hazard.MakeLight(transform, "concentrator light", 30f);
            _burnL = Hazard.MakeLight(transform, "burn light", 22f);
            _chargeClip = Hazard.Clip("rail_charge");
            _burnClip = Hazard.Clip("prism_lance");
        }

        void OnDestroy() { if (_fx != null) { _fx.Destroy(); _fx = null; } }

        Vector3 Dir(float deg) => Quaternion.AngleAxis(deg, axis.normalized) * fromDir.normalized;

        /// how far the beam reaches along d before something solid (pilots and HUSK do not stop it)
        float Reach(Vector3 o, Vector3 d)
        {
            int n = Physics.RaycastNonAlloc(o, d, s_hits, range, ~0, QueryTriggerInteraction.Ignore);
            float best = range;
            for (int i = 0; i < n; i++)
            {
                var c = s_hits[i].collider;
                if (!c || s_hits[i].distance < 0.5f || s_hits[i].distance >= best) continue;
                if (c.GetComponentInParent<HuskEnemy>() || c.GetComponentInParent<ZeroGFlight>()) continue;
                best = s_hits[i].distance;
            }
            return best;
        }

        void Update()
        {
            float T = FlowClock.Now + phase;
            int cycle = Mathf.FloorToInt(T / period);
            float c = T - cycle * period;
            bool back = (cycle & 1) == 1;
            float startDeg = back ? arc : 0f, endDeg = back ? 0f : arc;
            Vector3 o = transform.position;
            float s;
            if (c < charge) { s = 0f; _warm = c / charge; _beam = 0f; }
            else if (c < charge + sweep) { s = HuskFX.Smooth(0f, 1f, (c - charge) / sweep); _warm = 1f; _beam = 1f; }
            else if (c < charge + sweep + linger) { s = 1f; _warm = 1f; _beam = 1f - (c - charge - sweep) / linger; }
            else { s = 1f; _warm = Mathf.Max(0f, 1f - (c - charge - sweep - linger) / 0.6f); _beam = 0f; }
            Vector3 d = Dir(Mathf.Lerp(startDeg, endDeg, s));
            _end = o + d * Reach(o, d);
            Vector3 d0 = Dir(startDeg);
            _startEnd = o + d0 * Reach(o, d0);

            // sound cues: the warm-up hum at the emitter, the burn at where it starts cutting
            if (c < charge && _cueCycle != cycle) { _cueCycle = cycle; Hazard.PlayAt(_chargeClip, o, 0.75f, 0.55f, 18f); }
            if (c >= charge && c < charge + sweep && _burnCycle != cycle) { _burnCycle = cycle; Hazard.PlayAt(_burnClip, _end, 0.8f, 0.62f, 22f); }

            if (_beam > 0.5f && Time.time - _hitAt >= cooldown && Hazard.Pilot(out var e, out var chest))
            {
                Vector3 q = Hazard.OnSegment(o, _end, chest);
                if ((chest - q).sqrMagnitude < radius * radius)
                {
                    _hitAt = Time.time;
                    e.DamagePlayer(damage, q, knock);                  // shoved out of the beam, sideways
                    Debug.Log($"[OYM-FLOW] {name}: the beam caught the pilot (-{damage:F0} HP)");
                }
            }
        }

        void LateUpdate()
        {
            Vector3 o = transform.position;
            float emit = Mathf.Max(_warm * 0.8f, _beam);
            HuskFX.SetLight(_emitL, 2500f * emit * emit, color);
            if (_burnL)
            {
                _burnL.transform.position = _end - (_end - o).normalized * 0.8f;
                HuskFX.SetLight(_burnL, lumen * _beam, color);
            }
            if (_fx == null || !_fx.Valid) return;
            var cam = HuskFX.ViewCam;
            Vector3 cp = cam ? cam.transform.position : o;
            _fx.Begin();
            if (emit > 0.01f)
            {
                float flick = 1f + 0.12f * HuskFX.N(Time.time * 11f, 3);
                float size = 3.2f + 2.4f * emit;
                var halo = HuskFX.Safe(o, ref size, color * (0.18f + 0.55f * emit) * flick, 0.22f, 1.3f);
                _fx.glow.Sprite(o, size, halo);
                float core = 0.9f + 0.6f * emit;
                var hot = HuskFX.Safe(o, ref core, color * (0.5f + 1.1f * emit), 0.06f, 1.6f);
                _fx.dot.Sprite(HuskFX.TowardCam(o, 0.5f), core, hot);
            }
            // the telegraph: a faint line along where the sweep will start
            if (_beam <= 0.01f && _warm > 0.05f)
                _fx.streak.Streak(o, _startEnd, 0.14f, color * (0.05f * _warm), color * (0.22f * _warm));
            if (_beam > 0.01f)
            {
                // dim the beam where it passes the lens (a thin quad close up covers the view)
                float near = Vector3.Distance(cp, Hazard.OnSegment(o, _end, cp));
                float dim = Mathf.Lerp(0.25f, 1f, HuskFX.Smooth(1f, 7f, near)) * _beam;
                _fx.streak.Streak(o, _end, 2.4f, color * (0.16f * dim), color * (0.2f * dim));
                _fx.streak.Streak(o, _end, 0.5f, color * (0.9f * dim), color * (1.25f * dim));
                float burn = 3.2f;
                var glowCol = HuskFX.Safe(_end, ref burn, color * (0.7f * _beam), 0.25f, 1.2f);
                _fx.glow.Sprite(_end, burn, glowCol);
                float spot = 0.9f;
                var spotCol = HuskFX.Safe(_end, ref spot, color * (1.3f * _beam), 0.08f, 1.5f);
                _fx.dot.Sprite(HuskFX.TowardCam(_end, 0.4f), spot, spotCol);
            }
            _fx.End();
        }
    }
}
