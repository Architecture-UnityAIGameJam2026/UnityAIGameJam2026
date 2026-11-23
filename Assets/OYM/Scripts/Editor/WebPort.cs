using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using OYM.Web;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using HD = UnityEngine.Rendering.HighDefinition;
using U = UnityEngine.Rendering.Universal;

namespace OYM.WebTools
{
    /// The web (WebGL) port of the co-op build, on the web/webgl branch only. HDRP cannot run in a browser, so:
    /// 1. Setup: a URP asset becomes the active pipeline (HDRP stays installed so the code compiles), HDRP Lit / Unlit
    ///    materials become URP Lit / Unlit with their textures and exposure-scaled emission, every HDRP volume profile
    ///    gets URP post-processing overrides, the build scenes get a skybox / fog / ambient from their HDRP sky, and the
    ///    WebGL player settings (OYM_RELAY, compression, memory) are set.
    /// 2. Build: a WebGL build of the build-settings scenes to ../oym-web-build.
    /// Batch: Unity -batchmode -projectPath . -buildTarget WebGL -executeMethod OYM.WebTools.WebPort.Setup -quit
    ///        Unity -batchmode -projectPath . -buildTarget WebGL -executeMethod OYM.WebTools.WebPort.Build -quit
    public static class WebPort
    {
        const string Dir = "Assets/WebPort";
        const string MainScene = "Assets/OYM/Scenes/Showcase_DebrisField.unity";
        public static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../../oym-web-build/OYM_Coop_Web"));

        static void Log(string s) => Debug.Log("[WEBPORT] " + s);

        [MenuItem("Tools/On Your Mark/Web/1 Setup URP + convert")]
        public static void Setup()
        {
            Directory.CreateDirectory(Dir);
            float ev = MainEV();
            WriteEvAsset(ev);
            Log($"material EV {ev:0.##}");
            var asset = MakeUrpAsset();
            UsePipeline(asset);
            ConvertMaterials(ev);
            ConvertProfiles();
            ConvertScenes();
            PlayerSetup();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Log("setup done");
        }

        [MenuItem("Tools/On Your Mark/Web/1b Shrink assets for web")]
        public static void Shrink()
        {
            int nt = 0, nm = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var guid in AssetDatabase.FindAssets("t:Texture", new[] { "Assets" }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                    var cur = ti.GetPlatformTextureSettings("WebGL");
                    bool normal = ti.textureType == TextureImporterType.NormalMap;
                    bool ui = ti.textureType == TextureImporterType.Sprite || ti.textureType == TextureImporterType.GUI || path.Contains("/UI/") || path.Contains("/Title/");
                    bool sky = path.Contains("/Sky/") || ti.textureShape == TextureImporterShape.TextureCube;
                    int max = sky ? 4096 : normal ? 1024 : ui ? 2048 : 2048;
                    max = Mathf.Min(max, ti.maxTextureSize);
                    var w = new TextureImporterPlatformSettings
                    {
                        name = "WebGL", overridden = true, maxTextureSize = max,
                        format = TextureImporterFormat.Automatic,
                        textureCompression = TextureImporterCompression.Compressed,
                        crunchedCompression = !ui && !sky, compressionQuality = 50,
                        resizeAlgorithm = TextureResizeAlgorithm.Mitchell,
                    };
                    if (cur.overridden && cur.maxTextureSize == w.maxTextureSize && cur.crunchedCompression == w.crunchedCompression && cur.textureCompression == w.textureCompression) continue;
                    ti.SetPlatformTextureSettings(w);
                    ti.SaveAndReimport(); nt++;
                }
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets" }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) continue;
                    if (mi.meshCompression >= ModelImporterMeshCompression.Medium) continue;
                    mi.meshCompression = ModelImporterMeshCompression.Medium;
                    mi.SaveAndReimport(); nm++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;   // Brotli took ~25 min on the 380 MB data file
            PlayerSettings.WebGL.decompressionFallback = true;
            AssetDatabase.SaveAssets();
            Log($"shrink: {nt} textures, {nm} models");
        }

        [MenuItem("Tools/On Your Mark/Web/2 Build WebGL")]
        public static void ShrinkAndBuild() { Shrink(); Build(); }

        /// re-run the scene pass (sky / fog / cameras) and the material pass after a fix, then shrink and build
        public static void RescenesAndBuild() { ConvertScenes(); Shrink(); Build(); }

        /// profiles + scenes again (post-processing / sky / ambient fixes), then shrink and build
        /// editor look-dev: profiles + scenes only (no build)
        public static void RefreshOnly() { ConvertProfiles(); ConvertScenes(); AssetDatabase.SaveAssets(); }

        public static void RefreshAndBuild() { ConvertProfiles(); ConvertScenes(); PlayerSetup(); Shrink(); Build(); }

        public static void Build()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            Directory.CreateDirectory(OutDir);
            var opt = new BuildPlayerOptions { scenes = scenes, locationPathName = OutDir, target = BuildTarget.WebGL, options = BuildOptions.None };
            var r = BuildPipeline.BuildPlayer(opt);
            Log($"build {r.summary.result}: {r.summary.totalSize / (1024f * 1024f):0.0} MB, {r.summary.totalErrors} errors, {r.summary.totalTime}");
            foreach (var st in r.steps) foreach (var m in st.messages)
                if (m.type == LogType.Error || m.type == LogType.Exception) Log("ERR " + m.content);
            if (Application.isBatchMode) EditorApplication.Exit(r.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        // ------------------------------------------------------------------------------------------------ exposure
        static float MainEV()
        {
            float ev = 10f;
            var prev = EditorSceneManager.GetActiveScene().path;
            if (File.Exists(MainScene))
            {
                EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);
                if (TrySceneEV(out var e)) ev = e;
            }
            return ev;
        }

        static bool TrySceneEV(out float ev)
        {
            ev = 0; float bestPrio = float.NegativeInfinity; bool found = false;
            foreach (var v in UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!v.isGlobal || !v.sharedProfile) continue;
                if (!WebLightAdapter.TryEV(v.sharedProfile, out float e)) continue;
                if (v.priority >= bestPrio) { bestPrio = v.priority; ev = e; found = true; }
            }
            return found;
        }

        static VolumeProfile SceneProfile()
        {
            VolumeProfile best = null; float bestPrio = float.NegativeInfinity;
            foreach (var v in UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!v.isGlobal || !v.sharedProfile || !v.enabled) continue;
                if (v.priority >= bestPrio) { bestPrio = v.priority; best = v.sharedProfile; }
            }
            return best;
        }

        /// the component T from the highest-priority enabled global volume that has it active (a scene can split its
        /// look over several volumes: MainTitle keeps the space sky in OYM_SpaceVolume and DOF in MainTitle_Volume)
        static T FindInGlobals<T>() where T : VolumeComponent
        {
            T best = null; float bestPrio = float.NegativeInfinity;
            foreach (var v in UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!v.isGlobal || !v.sharedProfile || !v.enabled || !v.gameObject.activeInHierarchy) continue;
                if (!v.sharedProfile.TryGet<T>(out var c) || !c.active) continue;
                if (v.priority >= bestPrio) { bestPrio = v.priority; best = c; }
            }
            return best;
        }

        /// measured from the source .hdr (cos-weighted mean radiance: up > 30 deg, |lat| <= 30, down < -30), linear
        static readonly Dictionary<string, Color[]> SkyAverages = new Dictionary<string, Color[]>
        {
            ["skybox_equirect_7680_nosun"] = new[] { new Color(0.01456f, 0.02284f, 0.02528f), new Color(0.01711f, 0.02727f, 0.03156f), new Color(0.01000f, 0.01546f, 0.01869f) },
        };

        static void WriteEvAsset(float ev)
        {
            // the runtime reads it from a tiny Resources text asset
            Directory.CreateDirectory("Assets/WebPort/Resources");
            File.WriteAllText("Assets/WebPort/Resources/oym_web_ev.txt", ev.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AssetDatabase.ImportAsset("Assets/WebPort/Resources/oym_web_ev.txt");
        }

        // ------------------------------------------------------------------------------------------------ pipeline
        static UniversalRenderPipelineAsset MakeUrpAsset()
        {
            string rdPath = Dir + "/OYM_Web_Renderer.asset", aPath = Dir + "/OYM_Web_URP.asset";
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rdPath);
            if (!rd)
            {
                rd = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rd, rdPath);
            }
            var pp = rd.postProcessData;
            if (!pp)
            {
                var guid = AssetDatabase.FindAssets("t:PostProcessData", new[] { "Packages/com.unity.render-pipelines.universal" }).FirstOrDefault();
                if (guid != null) rd.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(AssetDatabase.GUIDToAssetPath(guid));
            }
            EditorUtility.SetDirty(rd);

            var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(aPath);
            if (!a)
            {
                a = UniversalRenderPipelineAsset.Create(rd);
                AssetDatabase.CreateAsset(a, aPath);
            }
            a.supportsCameraDepthTexture = true;
            a.supportsCameraOpaqueTexture = true;
            a.supportsHDR = true;
            a.msaaSampleCount = 1;
            a.renderScale = 1f;
            a.shadowDistance = 120f;
            a.shadowCascadeCount = 2;
            a.maxAdditionalLightsCount = 4;
            a.colorGradingMode = ColorGradingMode.HighDynamicRange;
            // the opaque texture only feeds the hit FX heat haze (OYM/FX/Distort): a quarter-res copy is plenty
            var so = new SerializedObject(a);
            so.FindProperty("m_OpaqueDownsampling").intValue = (int)Downsampling._4xBilinear;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            return a;
        }

        static void UsePipeline(RenderPipelineAsset a)
        {
            GraphicsSettings.defaultRenderPipeline = a;
            int cur = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = a;
            }
            QualitySettings.SetQualityLevel(cur, false);
            // URP global settings (created on demand by the URP editor)
            var t = Type.GetType("UnityEditor.Rendering.Universal.UniversalGlobalSettingsCreator, Unity.RenderPipelines.Universal.Editor");
            Log("pipeline set to URP");
        }

        // ------------------------------------------------------------------------------------------------ materials
        static readonly Type BaseGUI = Type.GetType("UnityEditor.BaseShaderGUI, Unity.RenderPipelines.Universal.Editor");
        static readonly Type LitGUIType = Type.GetType("UnityEditor.Rendering.Universal.ShaderGUI.LitGUI, Unity.RenderPipelines.Universal.Editor");

        static void Keywords(Material m, bool lit)
        {
            try
            {
                var set = BaseGUI?.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(x => x.Name == "SetMaterialKeywords");
                if (set == null) return;
                Action<Material> shading = null;
                if (lit && LitGUIType != null)
                {
                    var lk = LitGUIType.GetMethod("SetMaterialKeywords", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Material) }, null);
                    if (lk != null) shading = mm => lk.Invoke(null, new object[] { mm });
                }
                var ps = set.GetParameters();
                var args = new object[ps.Length];
                args[0] = m;
                for (int i = 1; i < ps.Length; i++) args[i] = i == 1 ? shading : (ps[i].HasDefaultValue ? ps[i].DefaultValue : null);
                set.Invoke(null, args);
            }
            catch (Exception e) { Debug.LogWarning("[WEBPORT] keywords " + m.name + ": " + e.Message); }
        }

        static float F(Material m, string p, float d = 0) => m.HasProperty(p) ? m.GetFloat(p) : d;
        static Color C(Material m, string p, Color d) => m.HasProperty(p) ? m.GetColor(p) : d;
        static Texture T(Material m, string p) => m.HasProperty(p) ? m.GetTexture(p) : null;

        static void ConvertMaterials(float ev)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            float exposure = 1f / (1.2f * Mathf.Pow(2f, ev));
            int nLit = 0, nUnlit = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m || !m.shader) continue;
                string sn = m.shader.name;
                if (sn == "HDRP/Lit" || sn == "HDRP/LitTessellation" || sn == "HDRP/LayeredLit")
                {
                    var baseCol = C(m, "_BaseColor", Color.white);
                    var baseMap = T(m, "_BaseColorMap");
                    var st = m.HasProperty("_BaseColorMap") ? new Vector4(m.GetTextureScale("_BaseColorMap").x, m.GetTextureScale("_BaseColorMap").y, m.GetTextureOffset("_BaseColorMap").x, m.GetTextureOffset("_BaseColorMap").y) : new Vector4(1, 1, 0, 0);
                    var normal = T(m, "_NormalMap"); float nScale = F(m, "_NormalScale", 1);
                    var mask = T(m, "_MaskMap");
                    float metal = F(m, "_Metallic"), smooth = mask ? F(m, "_SmoothnessRemapMax", 1) : F(m, "_Smoothness", 0.5f);
                    if (mask) metal = F(m, "_MetallicRemapMax", 1);
                    var emis = C(m, "_EmissiveColor", Color.black); var emisMap = T(m, "_EmissiveColorMap");
                    float w = F(m, "_EmissiveExposureWeight", 1);
                    float surface = F(m, "_SurfaceType"), blend = F(m, "_BlendMode"), clip = F(m, "_AlphaCutoffEnable"), cutoff = F(m, "_AlphaCutoff", 0.5f);
                    bool twoSided = F(m, "_DoubleSidedEnable") > 0.5f;

                    m.shader = lit;
                    m.SetColor("_BaseColor", baseCol);
                    m.SetTexture("_BaseMap", baseMap);
                    m.SetTextureScale("_BaseMap", new Vector2(st.x, st.y)); m.SetTextureOffset("_BaseMap", new Vector2(st.z, st.w));
                    m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", nScale);
                    m.SetTexture("_MetallicGlossMap", mask);
                    m.SetTexture("_OcclusionMap", mask); m.SetFloat("_OcclusionStrength", mask ? 1f : 0f);
                    m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
                    m.SetFloat("_WorkflowMode", 1);
                    float k = Mathf.Lerp(1f, exposure, w);
                    m.SetColor("_EmissionColor", emis * k);
                    m.SetTexture("_EmissionMap", emisMap);
                    m.SetFloat("_Surface", surface > 0.5f ? 1 : 0);
                    m.SetFloat("_Blend", blend == 1 ? 1 : blend == 2 ? 2 : 0);   // HDRP: 0 alpha 1 additive 2 premultiply
                    m.SetFloat("_AlphaClip", clip > 0.5f ? 1 : 0); m.SetFloat("_Cutoff", cutoff);
                    m.SetFloat("_Cull", twoSided ? 0 : 2);
                    m.renderQueue = -1;
                    Keywords(m, true);
                    m.EnableKeyword("_EMISSION");                  // runtime hit flashes write _EmissionColor
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    EditorUtility.SetDirty(m); nLit++;
                }
                else if (sn == "HDRP/Unlit")
                {
                    var col = C(m, "_UnlitColor", Color.white); var map = T(m, "_UnlitColorMap");
                    var emis = C(m, "_EmissiveColor", Color.black);
                    float surface = F(m, "_SurfaceType"), blend = F(m, "_BlendMode");
                    bool twoSided = F(m, "_DoubleSidedEnable") > 0.5f;
                    m.shader = unlit;
                    // HDRP unlit is exposed like everything else: bring it into display range
                    float k = exposure;
                    var c = col; float a = c.a;
                    if (Mathf.Max(c.r, c.g, c.b) * k < 0.02f) k = 1f;  // an LDR colour authored for EV 0: keep as is
                    c = (c + emis) * k; c.a = a;
                    m.SetColor("_BaseColor", c);
                    m.SetTexture("_BaseMap", map);
                    m.SetFloat("_Surface", surface > 0.5f ? 1 : 0);
                    m.SetFloat("_Blend", blend == 1 ? 1 : blend == 2 ? 2 : 0);
                    m.SetFloat("_Cull", twoSided ? 0 : 2);
                    m.renderQueue = -1;
                    Keywords(m, false);
                    EditorUtility.SetDirty(m); nUnlit++;
                }
            }
            AssetDatabase.SaveAssets();
            Log($"materials: {nLit} lit, {nUnlit} unlit");
        }

        // ------------------------------------------------------------------------------------------------ post processing
        static T Add<T>(VolumeProfile p) where T : VolumeComponent
        {
            if (p.TryGet<T>(out var c)) return c;
            c = p.Add<T>(false);
            c.name = typeof(T).Name;
            if (EditorUtility.IsPersistent(p)) AssetDatabase.AddObjectToAsset(c, p);
            return c;
        }

        static void ConvertProfiles()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:VolumeProfile", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
                if (!p || !p.components.Any(c => c && c.GetType().Namespace == "UnityEngine.Rendering.HighDefinition")) continue;
                ConvertProfile(p);
                EditorUtility.SetDirty(p); n++;
            }
            AssetDatabase.SaveAssets();
            Log($"volume profiles: {n}");
        }

        static void ConvertProfile(VolumeProfile p)
        {
            if (p.TryGet<HD.Bloom>(out var hb) && hb.active)
            {
                var b = Add<U.Bloom>(p); b.active = true;
                b.intensity.Override(Mathf.Clamp(hb.intensity.value, 0f, 3f));
                b.threshold.Override(hb.threshold.overrideState ? hb.threshold.value : 1f);
                b.scatter.Override(hb.scatter.overrideState ? hb.scatter.value : 0.7f);
                if (hb.tint.overrideState) b.tint.Override(hb.tint.value);
            }
            var tm = Add<U.Tonemapping>(p); tm.active = true;
            if (p.TryGet<HD.Tonemapping>(out var ht) && ht.active)
                tm.mode.Override(ht.mode.value == HD.TonemappingMode.None ? U.TonemappingMode.None : ht.mode.value == HD.TonemappingMode.Neutral ? U.TonemappingMode.Neutral : U.TonemappingMode.ACES);
            else tm.mode.Override(U.TonemappingMode.ACES);
            if (p.TryGet<HD.ColorAdjustments>(out var hc) && hc.active)
            {
                var c = Add<U.ColorAdjustments>(p); c.active = true;
                if (hc.postExposure.overrideState) c.postExposure.Override(hc.postExposure.value);
                if (hc.contrast.overrideState) c.contrast.Override(hc.contrast.value);
                if (hc.colorFilter.overrideState) c.colorFilter.Override(hc.colorFilter.value);
                if (hc.hueShift.overrideState) c.hueShift.Override(hc.hueShift.value);
                if (hc.saturation.overrideState) c.saturation.Override(hc.saturation.value);
            }
            if (p.TryGet<HD.Vignette>(out var hv) && hv.active)
            {
                var v = Add<U.Vignette>(p); v.active = true;
                if (hv.intensity.overrideState) v.intensity.Override(hv.intensity.value);
                if (hv.color.overrideState) v.color.Override(hv.color.value);
                if (hv.smoothness.overrideState) v.smoothness.Override(hv.smoothness.value);
                if (hv.center.overrideState) v.center.Override(hv.center.value);
            }
            if (p.TryGet<HD.ChromaticAberration>(out var hca) && hca.active)
            {
                var ca = Add<U.ChromaticAberration>(p); ca.active = true;
                if (hca.intensity.overrideState) ca.intensity.Override(hca.intensity.value);
            }
            if (p.TryGet<HD.FilmGrain>(out var hf) && hf.active)
            {
                var f = Add<U.FilmGrain>(p); f.active = true;
                if (hf.intensity.overrideState) f.intensity.Override(hf.intensity.value);
            }
            if (p.TryGet<HD.WhiteBalance>(out var hw) && hw.active)
            {
                var w = Add<U.WhiteBalance>(p); w.active = true;
                if (hw.temperature.overrideState) w.temperature.Override(hw.temperature.value);
                if (hw.tint.overrideState) w.tint.Override(hw.tint.value);
            }
            if (p.TryGet<HD.LiftGammaGain>(out var hl) && hl.active)
            {
                var l = Add<U.LiftGammaGain>(p); l.active = true;
                if (hl.lift.overrideState) l.lift.Override(hl.lift.value);
                if (hl.gamma.overrideState) l.gamma.Override(hl.gamma.value);
                if (hl.gain.overrideState) l.gain.Override(hl.gain.value);
            }
        }

        // ------------------------------------------------------------------------------------------------ scenes
        static void ConvertScenes()
        {
            foreach (var s in EditorBuildSettings.scenes.Where(x => x.enabled))
            {
                var scene = EditorSceneManager.OpenScene(s.path, OpenSceneMode.Single);
                float ev = TrySceneEV(out var e) ? e : 10f;
                float exposure = 1f / (1.2f * Mathf.Pow(2f, ev));
                var prof = SceneProfile();
                string name = Path.GetFileNameWithoutExtension(s.path);

                // sky
                Material sky = null;
                var hs = FindInGlobals<HD.HDRISky>();
                if (hs && hs.hdriSky.value is Cubemap cube)
                {
                    sky = new Material(Shader.Find("Skybox/Cubemap")) { name = "Sky_" + name };
                    sky.SetTexture("_Tex", cube);
                    sky.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 1f));
                    float skyNits = Mathf.Pow(2f, hs.exposure.value) * hs.multiplier.value;
                    sky.SetFloat("_Exposure", Mathf.Clamp(skyNits * exposure, 0.02f, 8f));
                    sky.SetFloat("_Rotation", -hs.rotation.value);
                }
                else if (FindInGlobals<HD.GradientSky>() is HD.GradientSky gs && gs)
                {
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                    float k = Mathf.Pow(2f, gs.exposure.value) * gs.multiplier.value * exposure;
                    RenderSettings.ambientSkyColor = gs.top.value * k;
                    RenderSettings.ambientEquatorColor = gs.middle.value * k;
                    RenderSettings.ambientGroundColor = gs.bottom.value * k;
                }
                if (sky)
                {
                    var mp = $"{Dir}/Sky_{name}.mat";
                    AssetDatabase.DeleteAsset(mp);
                    AssetDatabase.CreateAsset(sky, mp);
                    RenderSettings.skybox = sky;
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
                    var ilc = FindInGlobals<HD.IndirectLightingController>();
                    float mul = ilc ? ilc.indirectDiffuseLightingMultiplier.value : 1f;
                    // HDRP lit the scene with the sky's own irradiance x the indirect multiplier. URP's skybox ambient
                    // needs baked lighting these scenes do not have (it falls back to a grey probe), so the ambient is
                    // the sky's measured average radiance (up / horizon / down, cosine weighted) x sky exposure x mul.
                    if (SkyAverages.TryGetValue(Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(sky.GetTexture("_Tex"))), out var av))
                    {
                        float k = sky.GetFloat("_Exposure") * mul;
                        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                        RenderSettings.ambientSkyColor = (av[0] * k).gamma;
                        RenderSettings.ambientEquatorColor = (av[1] * k).gamma;
                        RenderSettings.ambientGroundColor = (av[2] * k).gamma;
                    }
                    else RenderSettings.ambientIntensity = Mathf.Clamp(mul, 0f, 8f);
                }

                // fog (HDRP volumetric fog -> exponential distance fog)
                var hfog = FindInGlobals<HD.Fog>();
                if (hfog && hfog.enabled.value)
                {
                    RenderSettings.fog = true;
                    RenderSettings.fogMode = FogMode.Exponential;
                    RenderSettings.fogDensity = 1f / Mathf.Max(5f, hfog.meanFreePath.value);
                    var fc = hfog.colorMode.value == HD.FogColorMode.ConstantColor ? hfog.color.value : (Color)hfog.albedo.value * 0.15f;
                    RenderSettings.fogColor = fc * Mathf.Clamp01(exposure * 1200f);
                }
                else RenderSettings.fog = false;

                // cameras: post-processing on, sky background
                foreach (var cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var d = cam.GetUniversalAdditionalCameraData();
                    d.renderPostProcessing = true;
                    d.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                    EditorUtility.SetDirty(cam);
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Log($"scene {name}: EV {ev:0.##}, sky {(sky ? "cubemap" : "none")}, fog {RenderSettings.fog}");
            }
        }

        // ------------------------------------------------------------------------------------------------ player
        static void PlayerSetup()
        {
            var nt = NamedBuildTarget.WebGL;
            var defs = PlayerSettings.GetScriptingDefineSymbols(nt).Split(';').Where(x => x.Length > 0).ToList();
            if (!defs.Contains("OYM_RELAY")) defs.Add("OYM_RELAY");
            PlayerSettings.SetScriptingDefineSymbols(nt, string.Join(";", defs));
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.initialMemorySize = 512;
            PlayerSettings.WebGL.maximumMemorySize = 4096;
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
            PlayerSettings.WebGL.showDiagnostics = false;
            PlayerSettings.WebGL.template = "PROJECT:OYM";   // Assets/WebGLTemplates/OYM: Default + devicePixelRatio cap
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.runInBackground = true;
            EditorUserBuildSettings.overrideMaxTextureSize = 2048;   // keeps the download small (16K sky, 4K props)
            PlayerSettings.SetManagedStrippingLevel(nt, ManagedStrippingLevel.Minimal);
            Log("player settings: " + string.Join(";", defs));
            AlwaysInclude("OYM/FX/AdditiveHDR", "Hidden/OYM/FocusOutline", "OYM/FX/FocusHighlight", "Hidden/OYM/DepthFocusFade", "Skybox/Cubemap");
        }

        static void AlwaysInclude(params string[] names)
        {
            var gs = new SerializedObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/GraphicsSettings.asset"));
            var arr = gs.FindProperty("m_AlwaysIncludedShaders");
            var have = new HashSet<Shader>();
            for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue is Shader sh) have.Add(sh);
            foreach (var n in names)
            {
                var sh = Shader.Find(n);
                if (!sh || have.Contains(sh)) continue;
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
                have.Add(sh);
            }
            gs.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
