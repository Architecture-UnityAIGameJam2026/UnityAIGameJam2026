// OnYourMark - one-shot HDRP project + debris-field arena builder.
// Batch:  Unity.exe -batchmode -projectPath <proj> -executeMethod OYM.EditorTools.OYMSetup.BatchAll -quit
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OYM.EditorTools
{
    [Serializable] public class WreckEntry { public string name, mesh, collider; public float[] pos, fwd, up, spin_axis; public float radius, temp, spin_deg_s; public int tris, hot_tris; public bool big; }
    [Serializable] public class RockEntry { public int variant; public float[] pos, rot, spin_axis; public float radius, tint, spin_deg_s; public bool ice; }
    [Serializable] public class RockVariant { public string name; public string[] lods; }
    [Serializable] public class LightEntry { public float[] pos; public float temp; }
    [Serializable] public class HotColor { public float temp, rel; public float[] rgb; }
    [Serializable] public class Manifest
    {
        public WreckEntry[] wreck; public RockEntry[] rocks; public RockVariant[] rock_variants; public LightEntry[] lights;
        public float[] sun_dir, sun_color, wreck_center; public HotColor[] hot_palette;
    }

    public static class OYMSetup
    {
        public const string Root = "Assets/OYM";
        public const string Gen = Root + "/Generated";
        public const string MatDir = Root + "/Materials";
        public const string SetDir = Root + "/Settings";
        public const string ScenePath = Root + "/Scenes/DebrisField.unity";
        public const string SkyPath = Gen + "/Sky/skybox_equirect_7680_nosun.hdr";   // 16K bake downscaled for git (import caps faces at 2048)
        // Team project layers (ProjectSettings/TagManager): 8 Player, 9 Enemy, 10 Environment, 11 Terminal.
        // Debris, asteroids and props block shots and sight lines, so they go on the team's Environment layer.
        public const string ObstacleLayer = "Environment";

        // ---- look-dev constants (physical units) ----
        public const float SunLux = 30000f;
        public const float CameraEV100 = 13.0f;
        public const float SkyEV = 13.2f;
        // Fill from the sunlit giant planet (cool, shadowless). Physically ~500 lux at this phase/size; pushed up so
        // the shadow side of the wreck stays readable (most arena sight-lines face the sun).
        public const float PlanetshineLux = 4000f;
        public static readonly Color PlanetshineColor = new Color(0.62f, 0.78f, 1.0f);
        public const float IndirectDiffuseBoost = 5f;      // lifts the teal nebula ambient in shadows
        // Torn-edge heat glow: only fragments still above HotMinTemp glow, with luminance ~ blackbody Y(T)^0.6
        // relative to the 1700 K bucket (manifest hot_palette.rel). HotNits = nits of a rel==1 emitter.
        public const float HotMinTemp = 1150f;
        public const float HotNits = 110000f;
        public static readonly Vector3 PlanetDir = new Vector3(-0.55f, 0.30f, 0.78f);
        public const float PlanetDistance = 26000f;
        public const float PlanetRadius = 8500f;

        static V3 v(float[] a) => new V3(a[0], a[1], a[2]);
        struct V3 { public float x, y, z; public V3(float a, float b, float c) { x = a; y = b; z = c; } public static implicit operator Vector3(V3 q) => new Vector3(q.x, q.y, q.z); }

        public static Manifest LoadManifest() => JsonUtility.FromJson<Manifest>(File.ReadAllText(Gen + "/arena_manifest.json"));

        [MenuItem("OYM/Rebuild Everything")]
        public static void BatchAll()
        {
            try
            {
                ConfigureProject();
                BuildScene();
                Debug.Log("[OYM] BatchAll finished OK");
            }
            catch (Exception e)
            {
                Debug.LogError("[OYM] BatchAll failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        // =====================================================================================
        // Project / pipeline
        // =====================================================================================
        [MenuItem("OYM/1. Configure Project (HDRP)")]
        public static void ConfigureProject()
        {
            foreach (var d in new[] { MatDir, SetDir, Root + "/Scenes" }) Directory.CreateDirectory(d);
            // Team repo: ProjectSettings (gravity, layers, graphics APIs, default pipeline, quality levels) belong to the
            // shared project and are NOT written here any more. Zero-g is per body (useGravity = false / kinematic),
            // and OYM_HDRP stays an optional asset next to the team's "HDRP High Fidelity" default.
            if (LayerMask.NameToLayer(ObstacleLayer) < 0)
                Debug.LogWarning($"[OYM] layer '{ObstacleLayer}' missing in TagManager; obstacles fall back to Default");

            var hdrp = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(SetDir + "/OYM_HDRP.asset");
            if (hdrp == null)
            {
                hdrp = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
                AssetDatabase.CreateAsset(hdrp, SetDir + "/OYM_HDRP.asset");
            }
            ConfigureHDRPAsset(hdrp);
            AssetDatabase.SaveAssets();
            Debug.Log("[OYM] OYM assets configured (team ProjectSettings left untouched)");
        }


        static void ConfigureHDRPAsset(HDRenderPipelineAsset asset)
        {
            var so = new SerializedObject(asset);
            void B(string p, bool val) { var sp = so.FindProperty("m_RenderPipelineSettings." + p); if (sp != null) sp.boolValue = val; else Debug.LogWarning("[OYM] HDRP setting not found: " + p); }
            void I(string p, int val) { var sp = so.FindProperty("m_RenderPipelineSettings." + p); if (sp != null) sp.intValue = val; else Debug.LogWarning("[OYM] HDRP setting not found: " + p); }
            B("supportSSR", true);
            B("supportSSAO", true);
            B("supportSSGI", true);
            B("supportVolumetrics", true);
            B("supportMotionVectors", true);
            B("supportLightLayers", true);
            B("supportDataDrivenLensFlare", true);
            B("supportScreenSpaceLensFlare", true);
            B("supportDecals", true);
            I("colorBufferFormat", (int)RenderPipelineSettings.ColorBufferFormat.R16G16B16A16);
            // directional shadows: 4096 at every scalable level, high-quality (PCSS) filtering
            var dir = so.FindProperty("m_RenderPipelineSettings.hdShadowInitParams.shadowResolutionDirectional.m_Values");
            if (dir != null) for (int i = 0; i < dir.arraySize; i++) dir.GetArrayElementAtIndex(i).intValue = 4096;
            I("hdShadowInitParams.maxShadowRequests", 256);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        // =====================================================================================
        // Scene
        // =====================================================================================
        [MenuItem("OYM/2. Build Debris Field Scene")]
        public static void BuildScene()
        {
            var M = LoadManifest();
            ImportSettings();
            var tex = LoadTextures();
            var mats = CreateMaterials(M, tex);
            var flare = CreateLensFlare();
            var fogTex = CreateFogNoise();
            var sky = AssetDatabase.LoadAssetAtPath<Cubemap>(SkyPath);
            if (sky == null) Debug.LogError("[OYM] Sky cubemap missing: " + SkyPath);
            var profile = CreateVolumeProfile(sky);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Vector3 wc = v(M.wreck_center);
            Vector3 sunDir = ((Vector3)v(M.sun_dir)).normalized;

            // ---- global volume ----
            var gv = new GameObject("Global Volume (Space Look)");
            var vol = gv.AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 10; vol.sharedProfile = profile;

            // ---- sun ----
            var sunGo = new GameObject("Sun");
            var hdSun = sunGo.AddHDLight(LightType.Directional);   // creates Light + HDAdditionalLightData
            var sun = sunGo.GetComponent<Light>();
            sun.lightUnit = LightUnit.Lux;
            sun.intensity = SunLux;
            // same colour the skybox bake used for the sun disc (manifest sun_color), no extra temperature filter
            sun.useColorTemperature = false;
            sun.color = new Color(M.sun_color[0], M.sun_color[1], M.sun_color[2]);
            sun.shadows = LightShadows.Soft;
            hdSun.angularDiameter = 0.55f;
            hdSun.volumetricDimmer = 1.0f;
            SetSerialized(hdSun, "m_ShadowResolution.m_Level", 3);
            sunGo.transform.rotation = Quaternion.LookRotation(-sunDir, Vector3.up);
            var lf = sunGo.AddComponent<LensFlareComponentSRP>();
            lf.lensFlareData = flare;
            lf.intensity = 1f;
            lf.scale = 1f;
            lf.useOcclusion = true;
            lf.occlusionRadius = 0.02f;
            lf.sampleCount = 32;
            lf.allowOffScreen = false;

            // ---- planetshine fill (sunlight bounced off the giant planet) ----
            var psGo = new GameObject("Planetshine (fill)");
            psGo.AddHDLight(LightType.Directional);
            var ps = psGo.GetComponent<Light>();
            ps.lightUnit = LightUnit.Lux;
            ps.intensity = PlanetshineLux;
            ps.useColorTemperature = false;
            ps.color = PlanetshineColor;
            ps.shadows = LightShadows.None;
            psGo.transform.rotation = Quaternion.LookRotation(-PlanetDir.normalized, Vector3.up);
            var psHd = psGo.GetComponent<HDAdditionalLightData>();
            psHd.affectsVolumetric = false;      // no second god-ray set
            psHd.angularDiameter = 18f;          // broad source -> soft specular

            // ---- player camera ----
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 80000f; cam.fieldOfView = 70f;
            var hdc = camGo.AddComponent<HDAdditionalCameraData>();
            hdc.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<OYM.FreeFlyCamera>();
            camGo.transform.position = Vector3.zero;
            camGo.transform.rotation = Quaternion.LookRotation(wc.normalized, Vector3.up);
            AddSpaceDust(camGo.transform, mats["Rock_A"], AssetDatabase.LoadAssetAtPath<Mesh>(Gen + "/Rocks/rock_00_lod2.oymmesh"));
            new GameObject("PlayerSpawn").transform.SetPositionAndRotation(Vector3.zero, camGo.transform.rotation);

            // ---- wreck ----
            var wreckRoot = new GameObject("Wreck (simulated, t+55s)").transform;
            int obstacle = Mathf.Max(0, LayerMask.NameToLayer(ObstacleLayer));
            foreach (var w in M.wreck)
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Gen + "/" + Cap(w.mesh));
                if (mesh == null) { Debug.LogWarning("[OYM] missing mesh " + w.mesh); continue; }
                var go = new GameObject(w.name);
                go.transform.SetParent(wreckRoot, false);
                go.transform.SetPositionAndRotation(v(w.pos), Quaternion.LookRotation(v(w.fwd), v(w.up)));
                go.layer = obstacle;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { mats["Hull_Exterior"], mats["Hull_Interior"], HotMaterial(mats, w.temp) };
                if (!string.IsNullOrEmpty(w.collider))
                {
                    var cm = AssetDatabase.LoadAssetAtPath<Mesh>(Gen + "/" + Cap(w.collider));
                    if (cm != null) go.AddComponent<MeshCollider>().sharedMesh = cm;
                }
                if (!w.big && w.spin_deg_s > 0.05f)
                {
                    var rb = go.AddComponent<Rigidbody>();
                    rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.Interpolate;
                    var sp = go.AddComponent<OYM.Spin>();
                    sp.axis = v(w.spin_axis); sp.degreesPerSecond = w.spin_deg_s;
                }
                else go.isStatic = true;
            }

            // ---- asteroids (LOD groups) ----
            var rockRoot = new GameObject("Asteroids").transform;
            var lodMeshes = new List<Mesh[]>();
            foreach (var rv in M.rock_variants)
            {
                var arr = new Mesh[rv.lods.Length];
                for (int l = 0; l < arr.Length; l++) arr[l] = AssetDatabase.LoadAssetAtPath<Mesh>(Gen + "/" + Cap(rv.lods[l]));
                lodMeshes.Add(arr);
            }
            float[] lodH = { 0.12f, 0.03f, 0.0015f };
            int ri = 0;
            foreach (var r in M.rocks)
            {
                var root = new GameObject($"Rock_{ri++:000}");
                root.transform.SetParent(rockRoot, false);
                root.transform.SetPositionAndRotation(v(r.pos), new Quaternion(r.rot[0], r.rot[1], r.rot[2], r.rot[3]).normalized);
                root.transform.localScale = Vector3.one * r.radius;
                root.layer = obstacle;
                var mat = r.ice ? mats["Rock_Ice"] : (r.tint > 0.95f ? mats["Rock_A"] : mats["Rock_B"]);
                var meshes = lodMeshes[r.variant];
                var lods = new LOD[meshes.Length];
                for (int l = 0; l < meshes.Length; l++)
                {
                    var c = new GameObject("LOD" + l);
                    c.transform.SetParent(root.transform, false);
                    c.layer = obstacle;
                    c.AddComponent<MeshFilter>().sharedMesh = meshes[l];
                    var mr = c.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    lods[l] = new LOD(lodH[l], new Renderer[] { mr });
                }
                var lg = root.AddComponent<LODGroup>();
                lg.SetLODs(lods);
                lg.RecalculateBounds();
                root.AddComponent<MeshCollider>().sharedMesh = meshes[meshes.Length - 1];
                if (r.spin_deg_s > 0.01f)
                {
                    var rb = root.AddComponent<Rigidbody>();
                    rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.Interpolate;
                    var sp = root.AddComponent<OYM.Spin>();
                    sp.axis = v(r.spin_axis); sp.degreesPerSecond = r.spin_deg_s;
                }
            }

            // ---- blast cores: hot point lights + smoke volumes ----
            var fx = new GameObject("Blast Cores").transform;
            int li = 0;
            foreach (var L in M.lights)
            {
                var lgo = new GameObject($"CoreLight_{li}");
                lgo.transform.SetParent(fx, false);
                lgo.transform.position = v(L.pos);
                var hdl = lgo.AddHDLight(LightType.Point);
                var pl = lgo.GetComponent<Light>();
                pl.lightUnit = LightUnit.Lumen;
                pl.intensity = li == 0 ? 6e8f : 3e8f;
                pl.range = li == 0 ? 900f : 650f;
                pl.useColorTemperature = true;
                pl.colorTemperature = 1800f;
                pl.color = new Color(1f, 0.85f, 0.7f);
                pl.shadows = li == 0 ? LightShadows.Soft : LightShadows.None;
                hdl.volumetricDimmer = 1.5f;
                AddFog(fx, $"CoreSmoke_{li}", v(L.pos), new Vector3(520, 380, 520) * (li == 0 ? 1f : 0.7f), new Color(0.62f, 0.55f, 0.50f), li == 0 ? 160f : 210f, fogTex, 2.2f, 0.12f);
                li++;
            }
            // ---- teal/blue nebula haze around the arena ----
            AddFog(fx, "WreckHaze", wc, new Vector3(2800, 1000, 2800), new Color(0.42f, 0.70f, 0.78f), 2200f, fogTex, 3.0f, 0.03f);
            AddFog(fx, "BeltHaze", wc * 0.55f + new Vector3(0, -150, 0), new Vector3(4600, 800, 4600), new Color(0.32f, 0.48f, 0.85f), 4200f, fogTex, 1.6f, 0.02f);

            // ---- giant planet placeholder (replace with Meshy model) ----
            var planet = new GameObject("GiantPlanet_PLACEHOLDER (replace with Meshy model)");
            planet.transform.position = PlanetDir.normalized * PlanetDistance;
            planet.transform.localScale = Vector3.one * PlanetRadius;
            planet.transform.rotation = Quaternion.Euler(18f, 40f, -12f);
            planet.AddComponent<MeshFilter>().sharedMesh = SphereMesh();
            var pmr = planet.AddComponent<MeshRenderer>();
            pmr.sharedMaterial = mats["Planet_Placeholder"];
            pmr.shadowCastingMode = ShadowCastingMode.Off;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddBuildScene(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[OYM] Scene built: {M.wreck.Length} wreck pieces, {M.rocks.Length} asteroids -> {ScenePath}");
        }

        /// <summary>Appends a scene to the build list if missing (never reorders or drops the team's scenes).</summary>
        public static void AddBuildScene(string path)
        {
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (list.Exists(s => s.path == path)) return;
            list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        static string Cap(string rel)
        {
            // manifest paths are lower-case folder names ("wreck/..", "rocks/..")
            if (rel.StartsWith("wreck/")) return "Wreck/" + rel.Substring(6);
            if (rel.StartsWith("rocks/")) return "Rocks/" + rel.Substring(6);
            return rel;
        }

        static void SetSerialized(UnityEngine.Object o, string path, int val)
        {
            var so = new SerializedObject(o);
            var p = so.FindProperty(path);
            if (p != null) { p.intValue = val; so.ApplyModifiedPropertiesWithoutUndo(); }
        }

        static void AddFog(Transform parent, string name, Vector3 pos, Vector3 size, Color albedo, float mfp, Texture3D mask, float tiling, float scroll)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var f = go.AddComponent<LocalVolumetricFog>();
            var p = f.parameters;
            p.albedo = albedo;
            p.meanFreePath = mfp;
            p.size = size;
            p.volumeMask = mask;
            p.textureTiling = Vector3.one * tiling;
            p.textureScrollingSpeed = new Vector3(scroll, scroll * 0.3f, scroll * 0.6f);
            p.positiveFade = new Vector3(0.35f, 0.35f, 0.35f);
            p.negativeFade = new Vector3(0.35f, 0.35f, 0.35f);
            p.falloffMode = LocalVolumetricFogFalloffMode.Exponential;
            p.distanceFadeStart = 20000f;
            p.distanceFadeEnd = 30000f;
            f.parameters = p;
        }

        static void AddSpaceDust(Transform cam, Material mat, Mesh mesh)
        {
            var go = new GameObject("SpaceDust");
            go.transform.SetParent(cam, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = true;
            main.startLifetime = 12f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.35f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1500;
            main.gravityModifier = 0f;
            var em = ps.emission; em.rateOverTime = 120f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 70f; sh.radiusThickness = 1f;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-1f, 1f); rot.y = new ParticleSystem.MinMaxCurve(-1f, 1f); rot.z = new ParticleSystem.MinMaxCurve(-1f, 1f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = mesh;
            r.sharedMaterial = mat;
            r.enableGPUInstancing = true;
        }

        // =====================================================================================
        // Assets: textures, materials, flare, fog noise, volume profile
        // =====================================================================================
        static void ImportSettings()
        {
            foreach (var n in new[] { "rock", "hull" })
            {
                SetTex($"{Gen}/Textures/{n}_albedo.png", false, true);
                SetTex($"{Gen}/Textures/{n}_normal.png", true, false);
                SetTex($"{Gen}/Textures/{n}_mask.png", false, false);
            }
            var ti = AssetImporter.GetAtPath(SkyPath) as TextureImporter;
            if (ti != null && (ti.textureShape != TextureImporterShape.TextureCube || ti.maxTextureSize != 4096))
            {
                ti.textureShape = TextureImporterShape.TextureCube;
                ti.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
                ti.maxTextureSize = 4096;
                ti.mipmapEnabled = true;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.filterMode = FilterMode.Trilinear;
                ti.SaveAndReimport();
            }
        }

        static void SetTex(string path, bool normal, bool srgb)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("[OYM] texture missing " + path); return; }
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = srgb;
            ti.maxTextureSize = 2048;
            ti.anisoLevel = 8;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }

        static Dictionary<string, Texture2D> LoadTextures()
        {
            var d = new Dictionary<string, Texture2D>();
            foreach (var n in new[] { "rock_albedo", "rock_normal", "rock_mask", "hull_albedo", "hull_normal", "hull_mask" })
                d[n] = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Gen}/Textures/{n}.png");
            return d;
        }

        static Material Lit(string name, Color baseColor, Texture2D alb, Texture2D nrm, Texture2D mask, float worldScale, float normalScale = 1f)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("HDRP/Lit")) { name = name }; AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("HDRP/Lit");
            m.SetColor("_BaseColor", baseColor);
            m.SetTexture("_BaseColorMap", alb);
            m.SetTexture("_NormalMap", nrm);
            m.SetFloat("_NormalScale", normalScale);
            m.SetTexture("_MaskMap", mask);
            m.SetFloat("_UVBase", 5f);                   // triplanar
            m.SetFloat("_TexWorldScale", worldScale);
            m.SetFloat("_ObjectSpaceUVMapping", 1f);     // texture sticks to tumbling debris
            m.SetFloat("_MetallicRemapMin", 0f); m.SetFloat("_MetallicRemapMax", 1f);
            m.SetFloat("_SmoothnessRemapMin", 0f); m.SetFloat("_SmoothnessRemapMax", 1f);
            m.SetFloat("_AORemapMin", 0f); m.SetFloat("_AORemapMax", 1f);
            m.enableInstancing = true;
            HDShaderUtils.ResetMaterialKeywords(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Dictionary<string, Material> CreateMaterials(Manifest M, Dictionary<string, Texture2D> t)
        {
            var d = new Dictionary<string, Material>();
            d["Hull_Exterior"] = Lit("Hull_Exterior", new Color(0.60f, 0.62f, 0.67f), t["hull_albedo"], t["hull_normal"], t["hull_mask"], 1f / 24f);
            d["Hull_Exterior"].SetFloat("_MetallicRemapMax", 0.45f);
            d["Hull_Interior"] = Lit("Hull_Interior", new Color(0.40f, 0.39f, 0.37f), t["hull_albedo"], t["hull_normal"], t["hull_mask"], 1f / 9f);
            d["Hull_Interior"].SetFloat("_MetallicRemapMax", 0.6f);
            d["Rock_A"] = Lit("Rock_A", new Color(0.92f, 0.86f, 0.80f), t["rock_albedo"], t["rock_normal"], t["rock_mask"], 0.7f, 1.3f);
            d["Rock_B"] = Lit("Rock_B", new Color(0.70f, 0.72f, 0.76f), t["rock_albedo"], t["rock_normal"], t["rock_mask"], 0.7f, 1.3f);
            // albedo must stay <= 1: an energy-gaining surface feeds back through screen-space GI / multi-bounce
            var ice = Lit("Rock_Ice", new Color(0.82f, 0.92f, 1.0f), t["rock_albedo"], t["rock_normal"], t["rock_mask"], 0.7f, 0.8f);
            ice.SetFloat("_SmoothnessRemapMin", 0.35f); ice.SetFloat("_SmoothnessRemapMax", 0.85f);
            d["Rock_Ice"] = ice;
            // slate-blue ice/gas-giant stand-in (matches the planetshine fill colour); swap for the Meshy planet
            var pm = Lit("Planet_Placeholder", new Color(0.30f, 0.37f, 0.46f), null, null, null, 2.5f, 1.0f);
            pm.SetFloat("_Smoothness", 0.15f);
            d["Planet_Placeholder"] = pm;
            // torn-edge heat glow: blackbody colours from the simulation's temperature buckets
            foreach (var h in M.hot_palette)
            {
                string n = "Hot_" + (int)h.temp + "K";
                var m = Lit(n, new Color(0.18f, 0.17f, 0.16f), t["hull_albedo"], t["hull_normal"], t["hull_mask"], 1f / 9f);
                var c = new Color(h.rgb[0], h.rgb[1], h.rgb[2]);
                float nits = HotNits * h.rel;
                m.SetColor("_EmissiveColor", c * nits);
                m.SetColor("_EmissiveColorLDR", c);
                m.SetFloat("_UseEmissiveIntensity", 0f);
                m.SetFloat("_EmissiveExposureWeight", 1f);
                HDShaderUtils.ResetMaterialKeywords(m);
                EditorUtility.SetDirty(m);
                d[n] = m;
            }
            _palette = M.hot_palette;
            AssetDatabase.SaveAssets();
            return d;
        }

        static HotColor[] _palette;
        static Material HotMaterial(Dictionary<string, Material> mats, float temp)
        {
            if (temp < HotMinTemp) return mats["Hull_Interior"];   // cooled below visible glow at t+55 s
            HotColor best = _palette[0];
            foreach (var h in _palette) if (Mathf.Abs(h.temp - temp) < Mathf.Abs(best.temp - temp)) best = h;
            return mats["Hot_" + (int)best.temp + "K"];
        }

        static LensFlareDataSRP CreateLensFlare()
        {
            string path = SetDir + "/OYM_SunFlare.asset";
            var data = AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(path);
            if (data == null) { data = ScriptableObject.CreateInstance<LensFlareDataSRP>(); AssetDatabase.CreateAsset(data, path); }
            var els = new List<LensFlareDataElementSRP>();
            LensFlareDataElementSRP E(SRPLensFlareType type, float intensity, Color tint, float scale, float pos, float falloff, float edge)
            {
                var e = new LensFlareDataElementSRP();
                e.flareType = type; e.localIntensity = intensity; e.tint = tint; e.uniformScale = scale; e.position = pos;
                e.fallOff = falloff; e.edgeOffset = edge; e.blendMode = SRPLensFlareBlendMode.Additive; e.modulateByLightColor = true;
                els.Add(e); return e;
            }
            E(SRPLensFlareType.Circle, 3.0f, new Color(1f, 0.97f, 0.9f), 0.10f, 0f, 1f, 0.15f);                 // hot core
            E(SRPLensFlareType.Circle, 0.35f, new Color(1f, 0.85f, 0.6f), 0.9f, 0f, 0.25f, 1f);                 // wide glare
            var streak = E(SRPLensFlareType.Circle, 0.9f, new Color(0.75f, 0.9f, 1f), 1f, 0f, 1f, 1f);          // anamorphic streak
            streak.sizeXY = new Vector2(3.6f, 0.012f);
            var ring = E(SRPLensFlareType.Ring, 0.05f, new Color(0.6f, 0.9f, 1f), 0.55f, 0f, 1f, 0.05f);
            E(SRPLensFlareType.Polygon, 0.06f, new Color(0.3f, 0.85f, 0.8f), 0.07f, -0.45f, 0.6f, 0.3f).sideCount = 6;
            E(SRPLensFlareType.Polygon, 0.045f, new Color(1f, 0.7f, 0.35f), 0.13f, -0.9f, 0.6f, 0.4f).sideCount = 6;
            E(SRPLensFlareType.Circle, 0.035f, new Color(0.4f, 0.6f, 1f), 0.22f, 1.35f, 0.5f, 0.6f);
            data.elements = els.ToArray();
            EditorUtility.SetDirty(data);
            return data;
        }

        static Texture3D CreateFogNoise()
        {
            string path = Gen + "/FogNoise3D.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture3D>(path);
            if (existing != null) return existing;
            const int N = 64;
            var tex = new Texture3D(N, N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var c = new Color32[N * N * N];
            for (int z = 0; z < N; z++)
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float fx = (float)x / N, fy = (float)y / N, fz = (float)z / N;
                        float s = 0f, a = 0.55f; int p = 4;
                        for (int o = 0; o < 5; o++) { s += a * TileNoise(fx * p, fy * p, fz * p, p, o * 17); a *= 0.5f; p *= 2; }
                        float d = Mathf.Clamp01((s - 0.38f) * 2.2f);
                        d = d * d;
                        c[(z * N + y) * N + x] = new Color32(255, 255, 255, (byte)(d * 255));
                    }
            tex.SetPixels32(c);
            tex.Apply();
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        static float Hash(int x, int y, int z, int s)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177 + s * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffffff) / 16777216f;
            }
        }

        static float TileNoise(float x, float y, float z, int period, int seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y), iz = Mathf.FloorToInt(z);
            float fx = x - ix, fy = y - iy, fz = z - iz;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
            float V(int a, int b, int c) => Hash(((a % period) + period) % period, ((b % period) + period) % period, ((c % period) + period) % period, seed);
            float x00 = Mathf.Lerp(V(ix, iy, iz), V(ix + 1, iy, iz), fx);
            float x10 = Mathf.Lerp(V(ix, iy + 1, iz), V(ix + 1, iy + 1, iz), fx);
            float x01 = Mathf.Lerp(V(ix, iy, iz + 1), V(ix + 1, iy, iz + 1), fx);
            float x11 = Mathf.Lerp(V(ix, iy + 1, iz + 1), V(ix + 1, iy + 1, iz + 1), fx);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
        }

        static Mesh SphereMesh()
        {
            string path = Gen + "/PlanetSphere.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m != null) return m;
            int seg = 256, rings = 128;
            var vtx = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float th = Mathf.PI * r / rings;
                for (int s = 0; s <= seg; s++)
                {
                    float ph = 2f * Mathf.PI * s / seg;
                    var p = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    vtx.Add(p); nrm.Add(p); uv.Add(new Vector2((float)s / seg, 1f - (float)r / rings));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + seg + 1;
                    tri.Add(a); tri.Add(a + 1); tri.Add(b);
                    tri.Add(a + 1); tri.Add(b + 1); tri.Add(b);
                }
            m = new Mesh { name = "PlanetSphere", indexFormat = IndexFormat.UInt32 };
            m.SetVertices(vtx); m.SetNormals(nrm); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateTangents(); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static VolumeProfile CreateVolumeProfile(Cubemap sky)
        {
            string path = SetDir + "/OYM_SpaceVolume.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            T Add<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(false);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }
            var ve = Add<VisualEnvironment>();
            ve.skyType.Override((int)SkyType.HDRI);
            ve.skyAmbientMode.Override(SkyAmbientMode.Dynamic);

            var hs = Add<HDRISky>();
            hs.hdriSky.Override(sky);
            hs.skyIntensityMode.Override(SkyIntensityMode.Exposure);
            hs.exposure.Override(SkyEV);

            var ex = Add<Exposure>();
            ex.mode.Override(ExposureMode.Fixed);
            ex.fixedExposure.Override(CameraEV100);

            // Global fog must be ON for the local volumetric fog volumes (blast smoke, nebula haze) to render, but
            // HDRP's height fog also attenuates every directional light by the fog it crosses on the way to the
            // fragment (LightEvaluation.hlsl, TransmittanceHeightFog). Space has no "ground", so the height-fog layer
            // is parked 100 km *below* the arena: zero global density here, sun transmittance = 1.
            // (With the old layer base at +60 km the sun crossed ~150 km of fog and arrived at ~3.6 % strength.)
            var fog = Add<Fog>();
            fog.enabled.Override(true);
            fog.meanFreePath.Override(45000f);
            fog.baseHeight.Override(-100000f);
            fog.maximumHeight.Override(-99000f);
            fog.maxFogDistance.Override(80000f);
            fog.colorMode.Override(FogColorMode.SkyColor);
            fog.albedo.Override(new Color(0.75f, 0.85f, 0.95f));
            fog.enableVolumetricFog.Override(true);
            fog.anisotropy.Override(0.7f);
            fog.depthExtent.Override(3500f);
            fog.sliceDistributionUniformity.Override(0.55f);
            fog.quality.Override(2);

            var bloom = Add<Bloom>();
            bloom.intensity.Override(0.25f);
            bloom.scatter.Override(0.72f);
            bloom.threshold.Override(0f);
            bloom.quality.Override(2);                    // high quality filtering

            var tm = Add<Tonemapping>();
            tm.mode.Override(TonemappingMode.ACES);

            var ca = Add<ColorAdjustments>();
            ca.contrast.Override(12f);
            ca.saturation.Override(8f);
            var wb = Add<WhiteBalance>();
            wb.temperature.Override(-6f);
            var vig = Add<Vignette>();
            vig.intensity.Override(0.24f);
            vig.smoothness.Override(0.45f);
            var chr = Add<ChromaticAberration>();
            chr.intensity.Override(0.06f);
            var fg = Add<FilmGrain>();
            fg.type.Override(FilmGrainLookup.Thin1);
            fg.intensity.Override(0.12f);

            // SSGI OFF. On HDRP 17.3 / D3D12 it samples uninitialised history for the first ~40 frames of a fresh
            // process (green cast, see OYMDebug.FirstFrameNoSSGI), and its frame-to-frame feedback can lock that
            // garbage onto bright surfaces for good. In open space it adds little: shadow fill comes from the
            // planetshine light + the sky ambient (IndirectDiffuseBoost), which have no feedback loop.
            var gi = Add<GlobalIllumination>();
            gi.enable.Override(false);
            var ilc = Add<IndirectLightingController>();
            ilc.indirectDiffuseLightingMultiplier.Override(IndirectDiffuseBoost);
            var ssr = Add<ScreenSpaceReflection>();
            ssr.enabled.Override(true);
            var ao = Add<ScreenSpaceAmbientOcclusion>();
            ao.intensity.Override(1.2f);
            var cs = Add<ContactShadows>();
            cs.enable.Override(true);
            var sh = Add<HDShadowSettings>();
            sh.maxShadowDistance.Override(1200f); // perf: fight within ~80 m, DepthFocusFade hazes the rest (was 2600)
            sh.cascadeShadowSplitCount.Override(4);
            var sslf = Add<ScreenSpaceLensFlare>();
            sslf.intensity.Override(0.12f);
            var mb = Add<MotionBlur>();
            mb.intensity.Override(0.2f);

            // far debris / sky fade so the fight in front reads first (custom post process, see OYMDepthFade)
            OYMDepthFade.AddTo(profile);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
