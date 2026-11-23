using System.Reflection;
using UnityEngine;

namespace OYM
{
    /// One red sensor light of the CITADEL (the head's eye cluster, each claw's sensor): a hit target sitting on the
    /// light (ZeroGTarget + sphere collider, like HuskBossPart). Hits blast sparks at the light and make it flare; low on
    /// HP it stutters; at 0 it breaks: the light flares and dies with a couple of flickers, blasts go off across its
    /// glow spots one after another, a claw tears off and tumbles away, the head keeps burning for a while.
    public class CitadelWeakPoint : MonoBehaviour
    {
        public string part;
        public float maxHp = 200f;
        public float Hp { get; private set; }
        public bool Broken { get; private set; }
        public float Hp01 => maxHp > 0f ? Mathf.Clamp01(Hp / maxHp) : 0f;
        public Transform Part { get; private set; }
        public Vector3 AimPoint => transform.position;
        public float Radius => _col ? _col.radius * MaxScale(transform) : 4f;
        public bool IsClaw => part != null && part.Contains("claw");

        CitadelWeakPoints _owner;
        Vector3[] _spots = new Vector3[0];                      // part space
        Vector3 _normal = Vector3.forward;                      // part space
        Renderer[] _lights = new Renderer[0];
        Light[] _lamps = new Light[0];
        ZeroGTarget _target;
        SphereCollider _col;
        MaterialPropertyBlock _mpb;
        float _flare, _hitCool, _breakT = -1f;
        // how far round the back of the light it still counts (cos of the angle from the light's facing):
        // the claw sensors face up / forward but the pilot fights from below and in front, so they take almost any
        // angle except straight through the claw from underneath; the head's eyes face forward
        float _minFacing = -0.35f;
        static FieldInfo s_flashField;
        static readonly int EmissiveId = OYM.Web.Emissive.Id;

        static float MaxScale(Transform t) { var s = t.lossyScale; return Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))); }

        public void Setup(CitadelWeakPoints owner, Transform partT, CitadelWeakPointMap.Entry e, float hp, float minWorldRadius, float maxWorldRadius, Light[] lamps)
        {
            _owner = owner; Part = partT; part = e.part; maxHp = hp; Hp = hp;
            _minFacing = IsClaw ? -0.8f : -0.35f;
            _spots = e.spots != null && e.spots.Length > 0 ? e.spots : new[] { e.center };
            _normal = e.normal.sqrMagnitude > 1e-6f ? e.normal.normalized : Vector3.forward;
            transform.SetParent(partT, false);
            transform.localPosition = _spots[0];                  // on the brightest light: the lock-on point
            transform.localRotation = Quaternion.identity;
            // part units are tens of metres here (claw ~46 m/unit): size the hit sphere in metres, centred on the light,
            // small enough not to be an invisible wall for the pilot / camera / lances (the hull itself has no collider)
            float s = Mathf.Max(1e-4f, MaxScale(partT));
            _col = gameObject.AddComponent<SphereCollider>();
            _col.center = Vector3.zero;
            _col.radius = Mathf.Clamp(e.radius * 1.15f * s, minWorldRadius, maxWorldRadius) / s;
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            _target = gameObject.AddComponent<ZeroGTarget>();
            var dummy = new GameObject("[hit target dummy]");
            dummy.transform.SetParent(transform, false);
            var dr = dummy.AddComponent<MeshRenderer>(); dr.enabled = false;
            _target.renderers = new Renderer[] { dr };            // its flash must not touch the hull emission
            if (s_flashField == null) s_flashField = typeof(ZeroGTarget).GetField("_flash", BindingFlags.NonPublic | BindingFlags.Instance);
            _lights = partT.GetComponentsInChildren<Renderer>(true);
            _lamps = lamps ?? new Light[0];
            _mpb = new MaterialPropertyBlock();
        }

        public Vector3 SpotWorld(int i) => Part ? Part.TransformPoint(_spots[Mathf.Clamp(i, 0, _spots.Length - 1)]) : transform.position;
        public int SpotCount => _spots.Length;
        /// TransformVector, not TransformDirection: the right claws are mirrored (negative scale) and their lights face out
        public Vector3 NormalWorld => Part ? Part.TransformVector(_normal).normalized : transform.forward;
        /// the light can be seen from `p` (no locking / hitting it from right behind, through the hull; wide for claws)
        public bool FacesPoint(Vector3 p)
        {
            Vector3 d = p - AimPoint;
            float m = d.magnitude;
            return m < 1e-3f || Vector3.Dot(NormalWorld, d / m) > _minFacing;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _hitCool -= dt;
            // a hit sets the target's flash to 1 and it decays from there: 0.25 still catches it at ~6 fps
            if (!Broken && _target && s_flashField != null && (float)s_flashField.GetValue(_target) >= 0.25f)
            {
                s_flashField.SetValue(_target, 0f);
                var e = _owner ? _owner.Encounter : null;
                bool slash = e && e.PlayerFlight is KestrelController;
                float dmg = e && e.config ? (slash ? e.config.slashDamage : e.config.railDamage) : 75f;
                Vector3 from = e ? e.PlayerChest : transform.position + NormalWorld * 50f;
                if (FacesPoint(from))                                        // from behind it's inside the hull
                {
                    // co-op guest: the host breaks the lights (OYM.Net), this copy shows what it sends back
                    if (!HuskEncounter.RouteHit(this, dmg, AimPoint, (from - AimPoint).normalized, 0f))
                        Hit(dmg, AimPoint, (from - AimPoint).normalized);
                }
            }
            Glow(dt);
        }

        /// (point, damage, position, normal) on every hit that lands (the co-op host sends it on)
        public static event System.Action<CitadelWeakPoint, float, Vector3, Vector3> Damaged;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetDamaged() { Damaged = null; }

        public void Hit(float dmg, Vector3 p, Vector3 n)
        {
            if (Broken) return;
            Damaged?.Invoke(this, dmg, p, n);
            Hp -= dmg;
            _flare = 1f;
            if (_hitCool <= 0f) { HuskFX.PlayHit(HitKind.Explosion, p, n, null, 2.6f, null); _hitCool = 0.12f; }
            if (Hp <= 0f) Break(n);
        }

        void Break(Vector3 n)
        {
            Broken = true;
            _breakT = 0f;
            if (_col) _col.enabled = false;
            if (_target) _target.enabled = false;
            foreach (var l in _lamps) if (l) l.enabled = false;
            if (_owner) _owner.OnPointBroken(this, n);
        }

        /// the part's emission through a property block: flare on hits, stutter when weak, flare-and-die when broken.
        /// HDR emission goes through SetVector (SetColor would gamma-convert it into a full-screen white-out).
        void Glow(float dt)
        {
            _flare = Mathf.Max(0f, _flare - dt * 5f);
            float k;
            if (Broken)
            {
                _breakT += dt;
                float t = _breakT;
                // "쪼": a hard flare for two frames, dead, two dying flickers, then dark for good
                k = t < 0.06f ? 4f : t < 0.16f ? 0f : t < 0.21f ? 0.8f : t < 0.33f ? 0f : t < 0.37f ? 0.3f : 0f;
                if (t > 0.6f && _breakT - dt <= 0.6f) { Apply(0f); return; }
                if (t > 0.6f) return;
            }
            else
            {
                float weak = Hp01 < 0.35f ? (Mathf.PerlinNoise(Time.time * 13f, part.Length) > 0.42f ? 1f : 0.35f) : 1f;
                k = weak * (1f + 3f * _flare);
                if (Mathf.Abs(k - 1f) < 0.01f) { Clear(); return; }
            }
            Apply(k);
        }

        void Apply(float k)
        {
            foreach (var r in _lights)
            {
                if (!r) continue;
                var m = r.sharedMaterial;
                Vector4 baseE = m && m.HasProperty(EmissiveId) ? m.GetVector(EmissiveId) : Vector4.zero;
                r.GetPropertyBlock(_mpb);
                _mpb.SetVector(EmissiveId, new Vector4(baseE.x * k, baseE.y * k, baseE.z * k, baseE.w));
                r.SetPropertyBlock(_mpb);
            }
        }

        bool _cleared = true;
        void Clear()
        {
            if (_cleared) return;
            _cleared = true;
            foreach (var r in _lights) if (r) r.SetPropertyBlock(null);
        }

        void LateUpdate() { if (_flare > 0f || Broken || Hp01 < 0.35f) _cleared = false; }
    }
}
