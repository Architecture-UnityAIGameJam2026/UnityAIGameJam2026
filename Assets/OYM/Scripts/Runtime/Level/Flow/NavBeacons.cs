using System.Collections.Generic;
using UnityEngine;

namespace OYM.Flow
{
    /// Blinking amber nav lights along the cruise lane: diegetic guides to the crashed bow (no markers). Each child
    /// with a Light blinks; one FX batch draws their glints.
    public class NavBeacons : MonoBehaviour
    {
        public float lumen = 1400f, period = 1.8f, blink = 0.22f;
        public Color color = new Color(1f, 0.62f, 0.2f);
        readonly List<Light> _l = new List<Light>();
        HuskFXCanvas _fx;

        void Start()
        {
            GetComponentsInChildren(true, _l);
            var m = Hazard.Materials();
            if (m != null) _fx = new HuskFXCanvas(transform, m);
        }

        void OnDestroy() { if (_fx != null) { _fx.Destroy(); _fx = null; } }

        void LateUpdate()
        {
            bool fx = _fx != null && _fx.Valid;
            if (fx) _fx.Begin();
            for (int i = 0; i < _l.Count; i++)
            {
                var l = _l[i];
                if (!l) continue;
                float c = Mathf.Repeat(FlowClock.Now + i * 0.37f, period);
                float k = c < blink ? Mathf.Sin(c / blink * Mathf.PI) : 0f;
                HuskFX.SetLight(l, lumen * (0.05f + 0.95f * k), color);
                if (fx)
                {
                    Vector3 p = l.transform.position;
                    float s = 0.5f + 1.2f * k;
                    var dotCol = HuskFX.Safe(p, ref s, color * (0.35f + 1.2f * k), 0.04f, 1.4f);
                    _fx.dot.Sprite(HuskFX.TowardCam(p, 0.3f), s, dotCol);
                    float g = 4f;
                    if (k > 0.02f) { var glowCol = HuskFX.Safe(p, ref g, color * (0.25f * k), 0.12f, 0.8f); _fx.glow.Sprite(p, g, glowCol); }
                }
            }
            if (fx) _fx.End();
        }
    }
}
