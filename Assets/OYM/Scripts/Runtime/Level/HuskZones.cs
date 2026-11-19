using System.Collections.Generic;
using UnityEngine;

namespace OYM
{
    /// Level encounters for the live HUSK (HuskEncounter). The level builder (OYMLevel) leaves static HUSK models asleep
    /// on the prop sets, grouped under one child of "HUSK Enemies (test)" per zone (nested, so HuskEncounter's own
    /// start-up pack, the direct WICK_* / PRISM_* children, stays separate). When the player comes within `radius` of a
    /// zone's centre, its sleepers are swapped for live rigs through HuskEncounter.Spawn and wake one after another,
    /// nearest first. Only HuskEncounter's public API is used; with -oymNoHusk (no encounter) the sleepers stay decor.
    public class HuskZones : MonoBehaviour
    {
        [System.Serializable]
        public class Zone
        {
            public string name;
            [Tooltip("parent of the sleeping WICK_* / PRISM_* models of this zone")] public Transform sleepers;
            public Vector3 center;
            [Tooltip("the zone wakes when the player is this close to its centre (m)")] public float radius = 60f;
        }

        public List<Zone> zones = new List<Zone>();
        [Tooltip("seconds between the WICKs of a zone waking (nearest first)")] public float wickStagger = 0.7f;
        [Tooltip("PRISMs wake after this many seconds, then one every prismStagger")] public float prismDelay = 1.6f;
        public float prismStagger = 3.5f;

        readonly HashSet<Zone> _woken = new HashSet<Zone>();

        void Update()
        {
            var enc = HuskEncounter.Current;
            if (!enc || enc.Mirror || !enc.Engaged || !enc.Gate || !enc.hostileEnabled) return;   // co-op guest: the host wakes them
            if (!enc.Player) return;
            foreach (var z in zones)
            {
                if (z == null || !z.sleepers || _woken.Contains(z)) continue;
                // either pilot (co-op) close enough wakes the zone
                if (!enc.AnyPilotWithin(z.center, z.radius, out Vector3 p)) continue;
                _woken.Add(z);
                Wake(enc, z, p);
            }
        }

        void Wake(HuskEncounter enc, Zone z, Vector3 from)
        {
            var list = new List<Transform>();
            foreach (Transform ch in z.sleepers) if (ch.gameObject.activeSelf) list.Add(ch);
            list.Sort((a, b) => (a.position - from).sqrMagnitude.CompareTo((b.position - from).sqrMagnitude));
            int w = 0, pr = 0;
            foreach (var ch in list)
            {
                bool wick = ch.name.StartsWith("WICK");
                if (!wick && !ch.name.StartsWith("PRISM")) continue;
                float delay = wick ? w++ * wickStagger : prismDelay + pr++ * prismStagger;
                enc.Spawn(wick ? HuskEnemy.Kind.Wick : HuskEnemy.Kind.Prism, ch.position, ch.rotation, delay);
                ch.gameObject.SetActive(false);
            }
            Debug.Log($"[OYM-LEVEL] zone '{z.name}' wakes: {w} WICK, {pr} PRISM");
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.6f, 0.6f);
            foreach (var z in zones) if (z != null) Gizmos.DrawWireSphere(z.center, z.radius);
        }
    }
}
