// Depth focus fade setup: adds OYM.DepthFocusFade to the arena volume profile, keeps its shader in player builds,
// and captures off/on/debug comparisons. Rendering is DepthFocusFadePass, an HDRP global custom pass that registers
// itself (Runtime/DepthFocusFade.cs), so no scene object or HDRP global-settings entry is needed.
//   menu:  OYM > Look > Apply Depth Focus Fade        (adds it if missing; keeps tuned values)
//          OYM > Look > Reset Depth Focus Fade         (re-applies the constants below)
//          OYM > Look > Capture Depth Fade Compare     (Screenshots/depthfade/)
//   batch: Unity.exe -batchmode -projectPath <proj> -executeMethod OYM.EditorTools.OYMDepthFade.BatchCompare -quit
// OYMSetup.CreateVolumeProfile calls AddTo(), so Rebuild Everything keeps the effect (and the fog look, OYMFogLook.Final).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace OYM.EditorTools
{
    [InitializeOnLoad]
    public static class OYMDepthFade
    {
        public const string ProfilePath = "Assets/OYM/Settings/OYM_SpaceVolume.asset";
        const string ShowcaseScene = "Assets/OYM/Scenes/Showcase_DebrisField.unity";
        const string AppliedMarker = "Library/OYM_DepthFade_v1.applied";

        // ---- look (the arena: fight within ~80 m, wreck debris field 0.9-2.6 km, rocks out to 3.9 km) ----
        public const float StartDistance = 80f;       // m, untouched inside
        public const float EndDistance = 1600f;       // m, full fade (log ramp in between)
        public const float SkyWeight = 0.5f;          // stars / nebula get half the fade
        public const float Desaturation = 0.6f;
        public const float DarkenEV = 1f;
        public const float HighlightLimit = 0.5f;     // pre-exposed; hot hull cuts are ~2-11 before this
        public const float HighlightCompress = 1f;
        public static readonly Color HazeColor = new Color(0.19f, 0.22f, 0.26f);   // sRGB swatch ~ linear (0.03, 0.04, 0.055)
        public const float Haze = 0.6f;
        // EVE-style: far hulls dissolve into the blurred nebula behind them rather than into a flat colour
        public const float HazeSkyMix = 0.85f;
        public const float HazeSkyBlur = 3f;          // mip of the convolved sky cubemap
        public const float HazeSkyGain = 1.25f;
        // depth glow: backdrop + far-layer light blurred wide, laid over far pixels by distance ("glow eats the layer behind")
        public const float Glow = 0.6f;
        public const float GlowSize = 0.08f;          // fraction of screen height
        public const float GlowSourceLimit = 3f;      // pre-exposed luminance cap of glow sources (sun, flares)

        static OYMDepthFade()
        {
            // One-shot per Library, so a deliberate removal from the profile sticks.
            if (File.Exists(AppliedMarker)) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating || File.Exists(AppliedMarker)) return;
                try
                {
                    Apply();
                    File.WriteAllText(AppliedMarker, DateTime.Now.ToString("s"));
                }
                catch (Exception e) { Debug.LogWarning("[OYM-DF] auto apply skipped: " + e.Message); }
            };
        }

        [MenuItem("OYM/Look/Apply Depth Focus Fade")]
        public static void Apply()
        {
            EnsureAlwaysIncluded();
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null) { Debug.LogWarning("[OYM-DF] no profile at " + ProfilePath); return; }
            AddTo(profile);
            AssetDatabase.SaveAssets();
            Debug.Log("[OYM-DF] depth focus fade on " + ProfilePath);
        }

        [MenuItem("OYM/Look/Reset Depth Focus Fade")]
        public static void ResetToDefaults()
        {
            Apply();
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile != null && profile.TryGet(out DepthFocusFade c))
            {
                Configure(c);
                OYMFogLook.Write(profile, OYMFogLook.Final);
                EditorUtility.SetDirty(c); AssetDatabase.SaveAssets();
            }
        }

        /// Adds the component to a profile (configured from the constants when newly added). Works for profiles that
        /// are already assets and for ones still being built (OYMSetup).
        public static DepthFocusFade AddTo(VolumeProfile profile)
        {
            DepthFocusFadePass.EnsureRegistered();
            if (!profile.TryGet(out DepthFocusFade c))
            {
                c = profile.Add<DepthFocusFade>(false);
                c.name = nameof(DepthFocusFade);
                if (AssetDatabase.Contains(profile)) AssetDatabase.AddObjectToAsset(c, profile);
                Configure(c);
                OYMFogLook.Write(profile, OYMFogLook.Final);     // arena fog + brightness (11-19), also on rebuilds
            }
            EditorUtility.SetDirty(c);
            EditorUtility.SetDirty(profile);
            return c;
        }

        static void Configure(DepthFocusFade c)
        {
            c.active = true;
            c.intensity.Override(1f);
            c.startDistance.Override(StartDistance);
            c.endDistance.Override(EndDistance);
            c.skyWeight.Override(SkyWeight);
            c.desaturation.Override(Desaturation);
            c.darkenEV.Override(DarkenEV);
            c.highlightLimit.Override(HighlightLimit);
            c.highlightCompress.Override(HighlightCompress);
            c.hazeColor.Override(HazeColor);
            c.haze.Override(Haze);
            c.hazeSkyMix.Override(HazeSkyMix);
            c.hazeSkyBlur.Override(HazeSkyBlur);
            c.hazeSkyGain.Override(HazeSkyGain);
            c.glow.Override(Glow);
            c.glowSize.Override(GlowSize);
            c.glowSourceLimit.Override(GlowSourceLimit);
            c.debugView.Override(false);
        }

        static void EnsureAlwaysIncluded()
        {
            var shader = Shader.Find(DepthFocusFade.ShaderName);
            if (shader == null) { Debug.LogWarning("[OYM-DF] shader not imported yet: " + DepthFocusFade.ShaderName); return; }
            var gfx = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
            if (gfx == null) return;
            var so = new SerializedObject(gfx);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            arr.InsertArrayElementAtIndex(arr.arraySize);
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = shader;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log("[OYM-DF] added " + DepthFocusFade.ShaderName + " to Always Included Shaders");
        }

        // ---------------------------------------------------------------- comparison stills ----

        public static void BatchCompare()
        {
            try
            {
                ResetToDefaults();
                CaptureCompare();
                Debug.Log("[OYM-DF] BatchCompare finished OK");
            }
            catch (Exception e)
            {
                Debug.LogError("[OYM-DF] failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        /// Showcase scene from a few viewpoints: <shot>_off.png, <shot>_on.png, <shot>_mask.png (fade amount).
        /// The profile values are only toggled in memory and restored.
        [MenuItem("OYM/Look/Capture Depth Fade Compare")]
        public static void CaptureCompare()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[OYM-DF] stop Play mode first"); return; }
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null || !profile.TryGet(out DepthFocusFade fade)) { Debug.LogError("[OYM-DF] apply the effect first"); return; }

            EditorSceneManager.OpenScene(ShowcaseScene);
            var cam = Camera.main;
            var graphs = PoseIdle();
            var start = (cam.transform.position, cam.transform.rotation, cam.fieldOfView);
            Vector3 ctr = Vector3.zero; int n = 0;
            foreach (var a in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None)) { ctr += a.transform.position; n++; }
            ctr = n > 0 ? ctr / n + Vector3.up * 1.0f : new Vector3(0f, 1.2f, 0f);
            var wreck = new Vector3(0f, 47f, 949f);
            var shots = new (string name, Vector3 eye, Vector3 look, float fov)[]
            {
                ("f01_follow_cam", start.position, start.position + start.rotation * Vector3.forward, start.fieldOfView),
                ("f02_wide_arena", ctr + new Vector3(7f, 3f, -9f), ctr + new Vector3(0f, 0f, 18f), 55f),
                ("f03_debris_path", ctr + new Vector3(-16f, 11f, -22f), ctr + new Vector3(4f, 0f, 75f), 55f),
                ("f04_toward_wreck", ctr + new Vector3(-3f, 2f, -6f), wreck, 60f),
            };

            string outDir = Path.Combine(OYMCapture.OutDir, "depthfade");
            Directory.CreateDirectory(outDir);
            int W = 1920, H = 1080;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create(); cam.targetTexture = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            float keepIntensity = fade.intensity.value; bool keepDebug = fade.debugView.value;
            bool warm = false;
            try
            {
                foreach (var s in shots)
                {
                    cam.fieldOfView = s.fov;
                    cam.transform.SetPositionAndRotation(s.eye, Quaternion.LookRotation(s.look - s.eye, Vector3.up));
                    foreach (var (suffix, intensity, debug) in new[] { ("off", 0f, false), ("on", 1f, false), ("mask", 1f, true) })
                    {
                        fade.intensity.value = intensity; fade.debugView.value = debug;
                        for (int i = 0; i < (warm ? 30 : 120); i++) { foreach (var g in graphs) g.Evaluate(0f); cam.Render(); }
                        warm = true;
                        RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                        RenderTexture.active = null;
                        File.WriteAllBytes(Path.Combine(outDir, $"{s.name}_{suffix}.png"), tex.EncodeToPNG());
                    }
                    Debug.Log("[OYM-DF] captured " + s.name);
                }
            }
            finally
            {
                fade.intensity.value = keepIntensity; fade.debugView.value = keepDebug;
                cam.targetTexture = null; rt.Release();
                foreach (var g in graphs) g.Destroy();
            }
        }

        static List<PlayableGraph> PoseIdle()
        {
            var graphs = new List<PlayableGraph>();
            foreach (var anim in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                var ctl = anim.runtimeAnimatorController;
                if (ctl == null) continue;
                var clip = ctl.animationClips.FirstOrDefault(c => c.name == "CombatIdle") ?? ctl.animationClips.FirstOrDefault(c => c.name.Contains("Idle")) ?? ctl.animationClips.FirstOrDefault();
                if (clip == null) continue;
                var g = PlayableGraph.Create("depthfade-" + anim.name);
                var cp = AnimationClipPlayable.Create(g, clip);
                cp.SetApplyFootIK(true);
                AnimationPlayableOutput.Create(g, "out", anim).SetSourcePlayable(cp);
                cp.SetTime(0.6f); cp.SetTime(0.6f);
                g.Evaluate(0f);
                graphs.Add(g);
            }
            return graphs;
        }
    }
}
