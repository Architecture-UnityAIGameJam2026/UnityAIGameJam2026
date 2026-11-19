using UnityEngine;

namespace OYM.Flow
{
    /// Dodge gimmick: rocks and hull plates streaming through the level. Band = they slide along `flow` across the
    /// route and wrap round (a river of debris between two stepping stones); Ring = they circle round `flow` as an axis
    /// (debris caught spinning in the throat of the asteroid corridor). Touching one hurts and shoves the pilot off it.
    /// The pieces are this object's children, placed by the builder (OYMLevelFlow): an authored position gives a
    /// piece its lane and phase; their colliders are removed (they hit by distance, nothing to snag the flight on).
    /// Same motion on both co-op PCs (FlowClock); each PC only checks its own pilot.
    [DisallowMultipleComponent]
    public class DebrisStream : MonoBehaviour
    {
        public enum Shape { Band, Ring }
        public Shape shape = Shape.Band;
        [Tooltip("Band: the flow direction (world). Ring: the axis the pieces circle round (world)")] public Vector3 flow = Vector3.right;
        [Tooltip("Band: length along the flow (m), centred on this object; the pieces wrap round")] public float length = 200f;
        [Tooltip("m/s along the band / round the ring, per piece between these")] public Vector2 speed = new Vector2(7f, 12f);
        public float damage = 12f, knock = 7f;
        [Tooltip("added to a piece's radius for the touch test (the pilot's body)")] public float pad = 0.5f;
        [Tooltip("seconds before the pilot can be hit again")] public float cooldown = 0.9f;
        [Tooltip("Band: the pieces grow in / shrink out over this share of the length at each end")] public float fadeEnds = 0.08f;
        [Tooltip("tumble, deg/s at most")] public float maxSpin = 26f;

        Transform[] _p;
        Vector3[] _lane, _e1, _e2, _scale, _spinAxis;
        float[] _u0, _v, _r, _spin, _R, _axial;
        Quaternion[] _rot;
        Vector3 _c, _f;
        float _hitAt = -9f;
        AudioClip _crack;

        public int Pieces => _p != null ? _p.Length : transform.childCount;

        void Start()
        {
            _c = transform.position;
            _f = flow.sqrMagnitude > 1e-6f ? flow.normalized : Vector3.right;
            int n = transform.childCount;
            _p = new Transform[n]; _lane = new Vector3[n]; _e1 = new Vector3[n]; _e2 = new Vector3[n]; _scale = new Vector3[n]; _spinAxis = new Vector3[n];
            _u0 = new float[n]; _v = new float[n]; _r = new float[n]; _spin = new float[n]; _R = new float[n]; _axial = new float[n];
            _rot = new Quaternion[n];
            for (int i = 0; i < n; i++)
            {
                var t = transform.GetChild(i);
                _p[i] = t;
                foreach (var col in t.GetComponentsInChildren<Collider>(true)) Destroy(col);
                Vector3 d = t.position - _c;
                float h = HuskFX.H(i, 17);
                _v[i] = Mathf.Lerp(speed.x, speed.y, h);
                if (shape == Shape.Band)
                {
                    float along = Vector3.Dot(d, _f);
                    _lane[i] = d - _f * along;
                    _u0[i] = along + length * 0.5f;
                }
                else
                {
                    _axial[i] = Vector3.Dot(d, _f);
                    Vector3 rad = d - _f * _axial[i];
                    _R[i] = Mathf.Max(0.5f, rad.magnitude);
                    _e1[i] = rad.sqrMagnitude > 1e-6f ? rad.normalized : Vector3.Cross(_f, Vector3.up).normalized;
                    _e2[i] = Vector3.Cross(_f, _e1[i]);
                }
                Bounds b = new Bounds(t.position, Vector3.one);
                bool any = false;
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                _r[i] = any ? (b.extents.x + b.extents.y + b.extents.z) / 3f : 1f;
                _rot[i] = t.rotation;
                _scale[i] = t.localScale;
                _spinAxis[i] = HuskFX.Dir(i, 5);
                _spin[i] = (HuskFX.H(i, 9) * 2f - 1f) * maxSpin;
            }
            _crack = Hazard.Clip("rock_hit");
        }

        void Update()
        {
            if (_p == null) return;
            float t = FlowClock.Now;
            bool can = Time.time - _hitAt >= cooldown & Hazard.Pilot(out var e, out var chest);
            int hit = -1; Vector3 hitP = default;
            for (int i = 0; i < _p.Length; i++)
            {
                var tr = _p[i];
                if (!tr) continue;
                Vector3 pos; float k = 1f;
                if (shape == Shape.Band)
                {
                    float u = Mathf.Repeat(_u0[i] + _v[i] * t, length);
                    float x = u / length;
                    k = HuskFX.Smooth(0f, fadeEnds, x) * HuskFX.Smooth(0f, fadeEnds, 1f - x);
                    pos = _c + _f * (u - length * 0.5f) + _lane[i];
                }
                else
                {
                    float a = _v[i] * t / _R[i];
                    pos = _c + _f * _axial[i] + (_e1[i] * Mathf.Cos(a) + _e2[i] * Mathf.Sin(a)) * _R[i];
                }
                tr.SetPositionAndRotation(pos, _rot[i] * Quaternion.AngleAxis(_spin[i] * t, _spinAxis[i]));
                if (shape == Shape.Band) tr.localScale = _scale[i] * Mathf.Max(0.001f, k);
                if (can && hit < 0 && k > 0.5f)
                {
                    float rr = _r[i] * k + pad;
                    if ((chest - pos).sqrMagnitude < rr * rr) { hit = i; hitP = pos; }
                }
            }
            if (hit >= 0)
            {
                _hitAt = Time.time;
                e.DamagePlayer(damage, hitP, knock);                   // shoves the pilot away from the piece's centre
                Hazard.PlayAt(_crack, Vector3.Lerp(hitP, chest, 0.6f), 0.9f, Random.Range(0.85f, 1.0f), 4f);
                Debug.Log($"[OYM-FLOW] {name}: debris hit the pilot (-{damage:F0} HP)");
            }
        }
    }
}
