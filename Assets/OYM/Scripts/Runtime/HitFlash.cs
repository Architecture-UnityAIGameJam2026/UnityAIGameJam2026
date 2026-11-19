using System.Collections.Generic;
using UnityEngine;

namespace OYM
{
    /// Put on a character / enemy root. On a hit it appends the OYM/FX/HitOverlay material (one instance per target)
    /// to its renderers and drives it: whole-body HDR flash that snaps off fast, plus up to 4 hit points that glow
    /// white-hot, cool down and send a hex/plasma wave over the hull. The overlay is removed again when everything
    /// has faded, so an idle target costs nothing.
    /// Note: an appended material draws the renderer's LAST sub-mesh; Meshy single-material meshes are fully covered.
    public class HitFlash : MonoBehaviour
    {
        public Renderer[] renderers;
        public Material overlayTemplate;
        [Tooltip("Overlay life of one hit (s) - keep in sync with the material's _Life")] public float life = 0.6f;
        public float flashDecay = 0.07f;
        [ColorUsage(false, true)] public Color flashColor = new Color(1.6f, 1.6f, 1.7f);

        /// Comfort cap on the whole-body flash (11-19 "섬광이 그대로야 눈이 너무 아파"): hits on the player asked for 0.55
        /// (damage) to 0.9 (dart blast), which turned the character in the middle of the screen white. The hot spots and
        /// waves at the hit points are not affected.
        public static float MaxBodyFlash = 0.3f;

        Material _mat;
        bool _attached;
        readonly Vector4[] _hits = new Vector4[4];
        readonly Vector4[] _cols = new Vector4[4];
        float _flash, _holdT;
        bool _holding, _heldThisFrame, _inited;
        const int HoldSlot = 3;

        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int FlashColId = Shader.PropertyToID("_FlashColor");
        static readonly int LifeId = Shader.PropertyToID("_Life");
        static readonly int[] HitIds = { Shader.PropertyToID("_Hit0"), Shader.PropertyToID("_Hit1"), Shader.PropertyToID("_Hit2"), Shader.PropertyToID("_Hit3") };
        static readonly int[] ColIds = { Shader.PropertyToID("_HitCol0"), Shader.PropertyToID("_HitCol1"), Shader.PropertyToID("_HitCol2"), Shader.PropertyToID("_HitCol3") };

        void Awake() { Init(); }

        void Init()
        {
            if (_inited) return;
            _inited = true;
            for (int i = 0; i < 4; i++) _hits[i] = new Vector4(0, 0, 0, -1f);
            if (renderers == null || renderers.Length == 0)
            {
                var list = new List<Renderer>();
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                    if (r is MeshRenderer || r is SkinnedMeshRenderer) list.Add(r);
                renderers = list.ToArray();
            }
        }

        Material Mat()
        {
            if (_mat) return _mat;
            var tpl = overlayTemplate ? overlayTemplate : (HitFX.Library ? HitFX.Library.overlayMaterial : null);
            if (!tpl) return null;
            _mat = new Material(tpl) { name = tpl.name + " (" + name + ")", hideFlags = HideFlags.DontSave };
            return _mat;
        }

        /// One impact at `worldPos`. `hdrColor` is the overlay colour (HDR), `size` in metres, `flash` 0..1.
        public void AddHit(Vector3 worldPos, Color hdrColor, float size, float flash)
        {
            Init();
            int slot = 0; float oldest = -2f;
            for (int i = 0; i < HoldSlot; i++)
            {
                float age = _hits[i].w;
                if (age < 0f || age > life) { slot = i; break; }
                if (age > oldest) { oldest = age; slot = i; }
            }
            _hits[slot] = new Vector4(worldPos.x, worldPos.y, worldPos.z, 0f);
            _cols[slot] = new Vector4(hdrColor.r, hdrColor.g, hdrColor.b, size);
            _flash = Mathf.Max(_flash, Mathf.Min(flash, MaxBodyFlash));
            Attach();
            Push();
        }

        /// Sustained hit (a beam held on the target): call every frame while it burns. The hot spot stays lit and
        /// re-fires a wave every `period` seconds; stops refreshing when the calls stop and fades out normally.
        public void HoldHit(Vector3 worldPos, Color hdrColor, float size, float period = 0.28f)
        {
            Init();
            if (!_holding) _holdT = 0f;
            _holding = true; _heldThisFrame = true;
            _hits[HoldSlot] = new Vector4(worldPos.x, worldPos.y, worldPos.z, Mathf.Repeat(_holdT, period));
            _cols[HoldSlot] = new Vector4(hdrColor.r, hdrColor.g, hdrColor.b, size);
            Attach();
        }

        /// Clear every hit + flash and take the overlay off (edit-mode captures, respawn).
        public void ResetAll()
        {
            Init();
            for (int i = 0; i < 4; i++) _hits[i] = new Vector4(0, 0, 0, -1f);
            _flash = 0f; _holding = false; _heldThisFrame = false;
            Push();
            Detach();
        }

        void LateUpdate() { Tick(Time.deltaTime); }

        /// Advance ages + flash (edit-mode captures call this by hand).
        public void Tick(float dt)
        {
            if (!_attached) return;
            bool alive = false;
            for (int i = 0; i < 4; i++)
            {
                if (_hits[i].w < 0f) continue;
                if (i == HoldSlot && _holding)
                {
                    if (_heldThisFrame) { _holdT += dt; alive = true; continue; }
                    _holding = false;   // beam left: let the last state age out
                }
                _hits[i].w += dt;
                if (_hits[i].w > life) _hits[i].w = -1f; else alive = true;
            }
            _heldThisFrame = false;
            _flash *= Mathf.Exp(-dt / Mathf.Max(1e-3f, flashDecay));
            if (_flash > 0.003f) alive = true; else _flash = 0f;
            Push();
            if (!alive) Detach();
        }

        void Push()
        {
            var m = Mat(); if (!m) return;
            m.SetFloat(FlashId, _flash);
            m.SetColor(FlashColId, flashColor);
            m.SetFloat(LifeId, life);
            for (int i = 0; i < 4; i++) { m.SetVector(HitIds[i], _hits[i]); m.SetVector(ColIds[i], _cols[i]); }
        }

        void Attach()
        {
            if (_attached) return;
            var m = Mat(); if (!m) return;
            foreach (var r in renderers)
            {
                if (!r) continue;
                var mats = r.sharedMaterials;
                var add = new Material[mats.Length + 1];
                mats.CopyTo(add, 0); add[mats.Length] = m;
                r.sharedMaterials = add;
            }
            _attached = true;
        }

        void Detach()
        {
            if (!_attached) return;
            foreach (var r in renderers)
            {
                if (!r) continue;
                var mats = r.sharedMaterials;
                var keep = new List<Material>(mats.Length);
                foreach (var x in mats) if (x != _mat) keep.Add(x);
                r.sharedMaterials = keep.ToArray();
            }
            _attached = false;
        }

        void OnDisable() { Detach(); }

        void OnDestroy()
        {
            Detach();
            if (_mat) { if (Application.isPlaying) Destroy(_mat); else DestroyImmediate(_mat); }
        }
    }
}
