using System.Reflection;
using UnityEngine;

namespace OYM
{
    /// A destructible part of the CITADEL (its launcher pods): takes the players' hits through a ZeroGTarget like the
    /// small HUSK, flashes (HitFlash) and pops a blast where it is hit, and at 0 HP swaps to its pre-made damaged twin
    /// (citadel_launcher_*_damaged, posed where the intact pod has swung to) under a big explosion.
    public class HuskBossPart : MonoBehaviour
    {
        public string label = "pod";
        public float maxHp = 300f;
        public GameObject damagedTwin;

        public float Hp { get; private set; }
        public bool Broken { get; private set; }
        public float Hp01 => maxHp > 0f ? Mathf.Clamp01(Hp / maxHp) : 0f;
        public Vector3 Center => _box ? transform.TransformPoint(_box.center) : transform.position;

        /// (part) when it breaks
        public event System.Action<HuskBossPart> OnBroken;

        ZeroGTarget _target;
        HitFlash _flash;
        BoxCollider _box;
        HuskEncounter _e;
        float _hitCool;
        static FieldInfo s_flashField;

        public void Setup(HuskEncounter e, string name, float hp, GameObject twin)
        {
            _e = e; label = name; maxHp = hp; Hp = hp; damagedTwin = twin;
            // collider = the pod's mesh bounds in its own space (follows the swing)
            Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
            foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh || mf.name.Contains("_LOD1") || mf.name.Contains("_LOD2")) continue;
                var b = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var q = transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    mn = Vector3.Min(mn, q); mx = Vector3.Max(mx, q);
                }
            }
            _box = gameObject.AddComponent<BoxCollider>();
            if (mn.x <= mx.x) { _box.center = (mn + mx) * 0.5f; _box.size = mx - mn; }
            var rb = GetComponent<Rigidbody>();
            if (!rb) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            _target = gameObject.AddComponent<ZeroGTarget>();
            var dummy = new GameObject("[hit target dummy]");
            dummy.transform.SetParent(transform, false);
            var dr = dummy.AddComponent<MeshRenderer>(); dr.enabled = false;
            _target.renderers = new Renderer[] { dr };
            _flash = gameObject.AddComponent<HitFlash>();
            var rs = new System.Collections.Generic.List<Renderer>();
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true)) rs.Add(r);
            _flash.renderers = rs.ToArray();
            if (s_flashField == null) s_flashField = typeof(ZeroGTarget).GetField("_flash", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        void Update()
        {
            _hitCool -= Time.deltaTime;
            if (Broken || !_target || s_flashField == null) return;
            if ((float)s_flashField.GetValue(_target) < 0.5f) return;
            s_flashField.SetValue(_target, 0f);
            bool slash = _e && _e.PlayerFlight is KestrelController;
            float dmg = _e ? (slash ? _e.config.slashDamage : _e.config.railDamage) : 75f;
            Vector3 from = _e ? _e.PlayerChest : transform.position - transform.forward * 50f;
            Vector3 p = _box ? _box.ClosestPoint(from) : transform.position;
            if (HuskEncounter.RouteHit(this, dmg, p, (from - p).normalized, 0f)) return;   // co-op guest: the host's pod
            Hit(dmg, p, (from - p).normalized);
        }

        /// (pod, damage, position, normal) on every hit that lands (the co-op host sends it on)
        public static event System.Action<HuskBossPart, float, Vector3, Vector3> Damaged;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetDamaged() { Damaged = null; }

        public void Hit(float dmg, Vector3 p, Vector3 n)
        {
            if (Broken) return;
            Damaged?.Invoke(this, dmg, p, n);
            Hp -= dmg;
            if (_flash) _flash.AddHit(p, new Color(8f, 3.6f, 1.4f), 9f, 0.35f);
            if (_hitCool <= 0f) { HuskFX.PlayHit(HitKind.Explosion, p, n, null, 2.2f, null); _hitCool = 0.15f; }
            if (Hp <= 0f) Break(n);
        }

        void Break(Vector3 n)
        {
            Broken = true;
            Vector3 c = Center;
            if (damagedTwin)
            {
                // the twin is a sibling under the boss model: put it where the intact pod has swung to
                var t = damagedTwin.transform;
                if (t.parent == transform.parent) t.SetLocalPositionAndRotation(transform.localPosition, transform.localRotation);
                else t.SetPositionAndRotation(transform.position, transform.rotation);
                t.localScale = transform.localScale;
                damagedTwin.SetActive(true);
            }
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            if (_box) _box.enabled = false;
            for (int i = 0; i < 4; i++)
                HuskFX.PlayHit(HitKind.Explosion, c + Random.insideUnitSphere * (_box ? _box.size.magnitude * 0.25f : 5f), n, null, i == 0 ? 7f : 4f, null);
            var cam = _e ? _e.FlightCam : null;
            if (cam) cam.Shake(0.6f);
            GameFeel.HitStop(0.12f, 0.15f);
            OnBroken?.Invoke(this);
        }
    }
}
