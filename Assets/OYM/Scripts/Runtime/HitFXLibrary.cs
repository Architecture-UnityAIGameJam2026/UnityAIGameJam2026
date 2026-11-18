using System;
using System.Collections.Generic;
using UnityEngine;

namespace OYM
{
    public enum HitKind
    {
        Kinetic,     // railgun / slug on armour: cyan-white flash, glint, hot-metal sparks, shards
        Explosion,   // missile / WICK detonation: flash, fireballs, smoke, shock ring (refraction), debris
        LaserBurn,   // HUSK beam held on a surface (looping, driven by BeamFX): molten core, spark spray, heat haze
        EnergyBolt,  // HUSK energy bolt on the player: magenta ripple, arcs, sparks
    }

    [Serializable]
    public class HitFXEntry
    {
        public HitKind kind;
        public HitFXInstance prefab;
        [Tooltip("HDR colour of the HitFlash overlay on the target (hot spot + hex wave)")]
        [ColorUsage(false, true)] public Color overlayColor = Color.white;
        [Tooltip("Overlay hot spot / wave size in metres")] public float overlaySize = 1f;
        [Tooltip("Whole-body flash on the target, 0..1")] public float overlayFlash = 0.5f;
        [Tooltip("Camera shake (trauma) when it happens right at the camera")] public float shake = 0.2f;
        [Tooltip("Shake falls off to 0 at this distance (m)")] public float shakeRange = 40f;
        [Tooltip("Hit-stop seconds when it happens close to the camera (the snap)")] public float hitStop;
        [Tooltip("Screen white-out (post exposure) when it happens close to the camera, 0..1")] public float screenFlash;
    }

    /// Hit / impact FX table (Resources/OYM_HitFXLibrary). Built by OYM/FX/Hit: Build All.
    [CreateAssetMenu(menuName = "OYM/Hit FX Library", fileName = "OYM_HitFXLibrary")]
    public class HitFXLibrary : ScriptableObject
    {
        public HitFXEntry[] entries = Array.Empty<HitFXEntry>();
        public Material overlayMaterial;

        public HitFXEntry Get(HitKind kind)
        {
            foreach (var e in entries) if (e != null && e.kind == kind) return e;
            return null;
        }
    }

    /// Game-facing API:  HitFX.Play(HitKind.Kinetic, hit.point, hit.normal, hit.collider);
    /// Spawns the pooled impact prefab facing the surface normal, lights up the target's HitFlash overlay (if any)
    /// and kicks the camera (HitScreenFX: distance-scaled shake + hit-stop).
    public static class HitFX
    {
        static HitFXLibrary _lib;
        static readonly Dictionary<HitFXInstance, Stack<HitFXInstance>> Pool = new Dictionary<HitFXInstance, Stack<HitFXInstance>>();
        static Transform _poolRoot;

        public static HitFXLibrary Library
        {
            get { if (!_lib) _lib = Resources.Load<HitFXLibrary>("OYM_HitFXLibrary"); return _lib; }
            set => _lib = value;
        }

        public static HitFXInstance Play(HitKind kind, Vector3 point, Vector3 normal, Component target = null, float scale = 1f)
        {
            var lib = Library;
            var e = lib ? lib.Get(kind) : null;
            if (e == null || !e.prefab) { Debug.LogWarning("[HitFX] no entry for " + kind); return null; }
            var inst = Spawn(e.prefab);
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
            inst.transform.SetPositionAndRotation(point, Quaternion.LookRotation(normal.normalized));
            inst.transform.localScale = Vector3.one * scale;
            inst.Play();
            if (target)
            {
                var hf = target.GetComponentInParent<HitFlash>();
                if (hf) hf.AddHit(point, e.overlayColor, e.overlaySize * scale, e.overlayFlash);
            }
            HitScreenFX.Impulse(point, e.shake * scale, e.shakeRange, e.hitStop, e.screenFlash);
            return inst;
        }

        public static HitFXInstance Spawn(HitFXInstance prefab)
        {
            HitFXInstance inst = null;
            if (Pool.TryGetValue(prefab, out var stack))
                while (stack.Count > 0 && !inst) inst = stack.Pop();
            if (!inst)
            {
                if (!_poolRoot) { var go = new GameObject("[HitFX pool]"); UnityEngine.Object.DontDestroyOnLoad(go); _poolRoot = go.transform; }
                inst = UnityEngine.Object.Instantiate(prefab, _poolRoot);
                inst.prefabKey = prefab;
            }
            inst.gameObject.SetActive(true);
            return inst;
        }

        internal static void Release(HitFXInstance inst)
        {
            if (!inst) return;
            if (!inst.prefabKey || !Application.isPlaying) { inst.gameObject.SetActive(false); return; }
            inst.gameObject.SetActive(false);
            if (!Pool.TryGetValue(inst.prefabKey, out var stack)) Pool[inst.prefabKey] = stack = new Stack<HitFXInstance>();
            stack.Push(inst);
        }
    }
}
