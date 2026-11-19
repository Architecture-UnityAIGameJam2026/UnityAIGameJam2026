using System.Collections.Generic;
using UnityEngine;

namespace OYM
{
    /// Ripple salvo of homing darts out of a launcher's tubes (CITADEL pod, or any emitter). Fire() schedules one
    /// dart per tube (cycling) every `stagger` seconds; darts come from a small local pool.
    public class DartSalvoFX : MonoBehaviour
    {
        public HomingDartFX dartPrefab;
        [Tooltip("tube mouths; +Z = launch direction")] public Transform[] tubes;
        public int count = 6;
        public float stagger = 0.09f;
        public Vector2 flightTime = new Vector2(1.5f, 2.0f);
        [Tooltip("random scatter round the aim point (m) - some darts miss and hit the hull around the target")]
        public float scatter = 2.5f;

        struct Pending { public float at; public int tube; public Vector3 target; public Collider col; public Vector3 n; public int seed; public float flight; }
        readonly List<Pending> _pending = new List<Pending>();
        readonly List<HomingDartFX> _pool = new List<HomingDartFX>();
        float _clock;
        int _salvo;

        public IReadOnlyList<HomingDartFX> Darts => _pool;

        /// Launch `count` darts at `target` (+ scatter). targetCol / normal feed the impact FX.
        public void Fire(Vector3 target, Collider targetCol = null, Vector3 normal = default)
        {
            _salvo++;
            int n = Mathf.Max(1, count);
            for (int i = 0; i < n; i++)
            {
                int seed = _salvo * 101 + i * 7;
                Vector3 jitter = HuskFX.Dir(i, seed) * (scatter * HuskFX.H(i, seed + 1));
                _pending.Add(new Pending
                {
                    at = _clock + i * stagger, tube = tubes != null && tubes.Length > 0 ? i % tubes.Length : -1,
                    target = target + (i == 0 ? Vector3.zero : jitter), col = i == 0 ? targetCol : null, n = normal, seed = seed,
                    flight = Mathf.Lerp(flightTime.x, flightTime.y, HuskFX.H(i, seed + 2)),
                });
            }
        }

        /// Launch one dart now from an explicit tube pose (captures / scripted shots).
        public HomingDartFX LaunchOne(Vector3 tubePos, Vector3 tubeDir, Vector3 target, float flight, int seed, Collider col = null, Vector3 normal = default)
        {
            var d = Get();
            d.Launch(tubePos, tubeDir, target, flight, seed, col, normal);
            return d;
        }

        HomingDartFX Get()
        {
            foreach (var d in _pool) if (d && !d.Busy) { d.gameObject.SetActive(true); return d; }
            var nd = Instantiate(dartPrefab, transform);
            nd.name = dartPrefab.name + " " + _pool.Count;
            if (!Application.isPlaying) nd.gameObject.hideFlags = HideFlags.DontSave;
            _pool.Add(nd);
            return nd;
        }

        void Update() { if (Application.isPlaying) Tick(Time.deltaTime); }

        public void Tick(float dt)
        {
            _clock += dt;
            if (!Application.isPlaying)                                   // edit-mode captures: darts do not Update
                foreach (var d in _pool) if (d && d.Busy) d.Tick(dt);
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                if (_clock < p.at) continue;
                _pending.RemoveAt(i);
                Vector3 pos = transform.position, dir = transform.forward;
                if (p.tube >= 0 && tubes[p.tube]) { pos = tubes[p.tube].position; dir = tubes[p.tube].forward; }
                var d = Get();
                d.Launch(pos, dir, p.target, p.flight, p.seed, p.col, p.n);
                d.Tick(_clock - p.at);                                     // catch up inside this frame
            }
        }

        public void ResetAll()
        {
            _pending.Clear();
            foreach (var d in _pool) if (d) { if (Application.isPlaying) Destroy(d.gameObject); else DestroyImmediate(d.gameObject); }
            _pool.Clear();
            _clock = 0f;
        }
    }
}
