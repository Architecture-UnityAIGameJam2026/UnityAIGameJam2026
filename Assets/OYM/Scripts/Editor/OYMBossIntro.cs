// OnYourMark - CITADEL boss entrance cinematic (HDRP, rendered offline frame by frame).
// Shot plan: meshy/enemies/cinematic/BOSS_INTRO_SHOTS.md   Audio (hit at 6.8 s): meshy/enemies/cinematic/citadel_intro_rumble.wav
//
//   1) python Tools/prep_cinematic.py                       (red-mask emission for launcher tubes / engine, cinematic only)
//   2) Unity.exe -batchmode -projectPath <proj> -executeMethod OYM.EditorTools.OYMBossIntro.Preview -quit
//        -> Screenshots/boss_intro_v2_preview/t_XX.XX.png (key frames, 960x540)
//   3) Unity.exe -batchmode -projectPath <proj> -executeMethod OYM.EditorTools.OYMBossIntro.RenderChunk -quit -oymFrom 0 -oymTo 105
//        -> Screenshots/boss_intro_v2/frame_0000.png ... (run 0-105, 105-210, 210-315 in separate processes: batch-mode
//           Camera.Render() keeps ~25 MB per frame in this project, so one process for all 315 frames would swap)
//
// Everything is a pure function of time (Evaluate(t)), so any frame range renders identically in any process.
// The scene: the arena's HDRI starfield, a planet cap 40 km below (radius 637 km: Earth's limb angles at 1/10 scale, so
// the limb sits in the lower third), an additive atmosphere shell (OYM/Cine/AtmosphereRim) glowing above the limb, the
// sun just above the limb behind the boss (lens flare only - the no-sun sky has no disc), and the boss rising over the
// limb from 6 km, closing to 1 km, then passing over a low camera.
// HDRP's PhysicallyBasedSky was tried first: in batch rendering its precomputed lighting stays black (ground and
// atmosphere = 0 even at sea level with a high sun), so the planet is plain geometry here.
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
    public static class OYMBossIntro
    {
        public const string Dir = OYMSetup.Root + "/Cinematic";
        public const string ScenePath = OYMSetup.Root + "/Scenes/BossIntro.unity";
        public const float Fps = 30f, Duration = 10.5f, HitT = 6.8f;
        public const int Frames = 315;

        // world
        const float PlanetR = 637100f, Altitude = 40000f;       // limb dips acos(R/(R+h)) = 19.8 deg below horizontal
        const float AtmoH = 26000f, ScaleH = 4500f;              // atmosphere shell height / air scale height (broad glow)
        public static float SunElevDeg = -18.0f, SunAzDeg = 5f;  // just above the limb: sunlight grazes ~70 km of air
        public static float SunLux = 30000f;
        public static float ExposureEV = 13f;

        // emission (EV100). camera EV 13; game material = EV 18 for x8 sensors
        const float SensorStartEV = 14f, SensorEV = 16f, SensorFlashEV = 19f, TubeEV = 16.5f, TubeFlashEV = 18.5f, EngineEV = 16.5f;

        class Rig
        {
            public Camera cam; public Transform boss;
            public readonly List<(Transform t, Quaternion rest, float sign)> claws = new List<(Transform, Quaternion, float)>();
            public readonly List<(Transform t, Quaternion rest, float sign)> launchers = new List<(Transform, Quaternion, float)>();
            public readonly List<Material> sensors = new List<Material>();
            public Material tubes, engine;
            public Bloom bloom; public MotionBlur blur; public Exposure exposure; public ColorAdjustments color;
            public readonly List<Light> engineLights = new List<Light>();
            public readonly List<(Transform t, Vector3 offset)> soldiers = new List<(Transform, Vector3)>();
        }

        // ---------------------------------------------------------------------------------------------
        static Material CineMat(Material src, string name, Texture2D emissiveMap)
        {
            Directory.CreateDirectory(Dir);
            string path = $"{Dir}/{name}.mat";
            AssetDatabase.DeleteAsset(path);
            var m = new Material(src) { name = name };
            if (emissiveMap != null) m.SetTexture("_EmissiveColorMap", emissiveMap);
            HDMaterial.SetUseEmissiveIntensity(m, true);
            HDMaterial.SetEmissiveColor(m, Color.white);
            HDMaterial.SetEmissiveIntensity(m, 0f, EmissiveIntensityUnit.Nits);
            m.SetFloat("_EmissiveExposureWeight", 1f);
            HDShaderUtils.ResetMaterialKeywords(m);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Texture2D CineTex(string file)
        {
            string p = $"{Dir}/{file}";
            var ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (ti == null) { Debug.LogWarning("[OYM-BI] missing " + p + " (run Tools/prep_cinematic.py)"); return null; }
            if (!ti.sRGBTexture || ti.maxTextureSize != 2048) { ti.sRGBTexture = true; ti.maxTextureSize = 2048; ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.SaveAndReimport(); }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }

        static VolumeProfile Profile()
        {
            Directory.CreateDirectory(Dir);
            string path = Dir + "/BossIntro_Volume.asset";
            AssetDatabase.DeleteAsset(path);
            var src = AssetDatabase.LoadAssetAtPath<VolumeProfile>(OYMSetup.SetDir + "/OYM_SpaceVolume.asset");
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
            foreach (var c in src.components)
            {
                if (c is Fog || c is GlobalIllumination) continue;                   // no height fog; SSGI stays off
                var k = UnityEngine.Object.Instantiate(c); k.name = c.GetType().Name;
                p.components.Add(k); AssetDatabase.AddObjectToAsset(k, p);
            }
            T Get<T>() where T : VolumeComponent { if (!p.TryGet<T>(out var c)) { c = p.Add<T>(false); c.name = typeof(T).Name; AssetDatabase.AddObjectToAsset(c, p); } return c; }

            // HDRP's default volume has height fog (base 0 m, 400 m mean free path). With the sun below the camera's
            // horizontal, TransmittanceHeightFog sees an endless path and blacks the sun out completely -> force fog off.
            var fog = Get<Fog>(); fog.enabled.Override(false);
            var ex = Get<Exposure>(); ex.mode.Override(ExposureMode.Fixed); ex.fixedExposure.Override(ExposureEV);
            var ca = Get<ColorAdjustments>(); ca.postExposure.Override(0f);                // fade to black at the end
            var bl = Get<Bloom>(); bl.intensity.Override(0.3f); bl.scatter.Override(0.7f); bl.threshold.Override(0f); bl.quality.Override(2);
            var mb = Get<MotionBlur>(); mb.intensity.Override(0.25f);   // keep the hit frames readable under the shake
            var ilc = Get<IndirectLightingController>(); ilc.indirectDiffuseLightingMultiplier.Override(1.5f);
            var sh = Get<HDShadowSettings>(); sh.maxShadowDistance.Override(8000f);
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            return p;
        }

        [MenuItem("OYM/Cinematic/Build Boss Intro Scene")]
        public static void Build() => BuildRig(true);

        static Rig BuildRig(bool save)
        {
            // new shaders (atmosphere rim, cinematic material variants): compile synchronously so no batch frame is
            // rendered with a placeholder shader
            ShaderUtil.allowAsyncCompilation = false;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var rig = new Rig();
            var profile = Profile();
            var gv = new GameObject("Global Volume (Boss Intro)").AddComponent<Volume>();
            gv.isGlobal = true; gv.priority = 10; gv.sharedProfile = profile;
            profile.TryGet(out rig.bloom); profile.TryGet(out rig.blur); profile.TryGet(out rig.exposure); profile.TryGet(out rig.color);

            // sun just above the planet limb, behind the boss
            var sunGo = new GameObject("Sun");
            var hd = sunGo.AddHDLight(LightType.Directional);
            var sun = sunGo.GetComponent<Light>();
            sun.lightUnit = LightUnit.Lux; sun.intensity = SunLux;
            sun.useColorTemperature = false; sun.color = new Color(1f, 0.95f, 0.88f);
            sun.shadows = LightShadows.Soft;
            hd.angularDiameter = 0.55f;
            hd.interactsWithSky = true;
            float el = SunElevDeg * Mathf.Deg2Rad, az = SunAzDeg * Mathf.Deg2Rad;
            var toSun = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
            sunGo.transform.rotation = Quaternion.LookRotation(-toSun, Vector3.up);
            var lf = sunGo.AddComponent<LensFlareComponentSRP>();
            lf.lensFlareData = AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(OYMSetup.SetDir + "/OYM_SunFlare.asset");
            lf.intensity = 0.8f; lf.useOcclusion = true; lf.occlusionRadius = 0.02f; lf.sampleCount = 32; lf.allowOffScreen = false;

            // planet: only the visible cap, densely tessellated so the limb stays round at 1080p
            var planetC = new Vector3(0f, -(PlanetR + Altitude), 0f);
            float capDeg = Mathf.Acos(PlanetR / (PlanetR + Altitude)) * Mathf.Rad2Deg + 0.8f;
            var ground = new Material(Shader.Find("HDRP/Lit")) { name = "cine_planet" };
            ground.SetColor("_BaseColor", new Color(0.07f, 0.085f, 0.11f));
            ground.SetFloat("_Metallic", 0f); ground.SetFloat("_Smoothness", 0.35f);
            HDShaderUtils.ResetMaterialKeywords(ground);
            AssetDatabase.DeleteAsset($"{Dir}/cine_planet.mat"); AssetDatabase.CreateAsset(ground, $"{Dir}/cine_planet.mat");
            Cap("Planet", "cine_planet_cap", PlanetR, capDeg, 420, 2048, ground, planetC);
            var rim = new Material(Shader.Find("OYM/Cine/AtmosphereRim")) { name = "cine_atmosphere" };
            rim.SetVector("_SunDir", toSun); rim.SetFloat("_PlanetRadius", PlanetR); rim.SetFloat("_ScaleHeight", ScaleH);
            rim.SetFloat("_Intensity", 1.0f); rim.SetFloat("_ForwardPower", 18f); rim.SetFloat("_ForwardBoost", 3f);
            rim.SetFloat("_SunLobePower", 300f); rim.SetFloat("_SunLobeBoost", 6f);
            AssetDatabase.DeleteAsset($"{Dir}/cine_atmosphere.mat"); AssetDatabase.CreateAsset(rim, $"{Dir}/cine_atmosphere.mat");
            Cap("Atmosphere", "cine_atmo_cap", PlanetR + AtmoH, capDeg, 260, 2048, rim, planetC);

            // cool fill from behind the camera / below (the boss is backlit; keeps its plates readable)
            var fillGo = new GameObject("Fill (cinematic)");
            fillGo.AddHDLight(LightType.Directional);
            var fill = fillGo.GetComponent<Light>();
            fill.lightUnit = LightUnit.Lux; fill.intensity = 3000f; fill.color = new Color(0.55f, 0.7f, 1f); fill.shadows = LightShadows.None;
            fillGo.GetComponent<HDAdditionalLightData>().affectsVolumetric = false;
            fillGo.transform.rotation = Quaternion.LookRotation(new Vector3(0.15f, 0.35f, 1f).normalized);
            var warmGo = new GameObject("Warm fill (planet / limb bounce)");
            warmGo.AddHDLight(LightType.Directional);
            var warmL = warmGo.GetComponent<Light>();
            warmL.lightUnit = LightUnit.Lux; warmL.intensity = 10000f; warmL.color = new Color(1f, 0.58f, 0.32f); warmL.shadows = LightShadows.None;
            warmGo.GetComponent<HDAdditionalLightData>().affectsVolumetric = false;
            warmGo.transform.rotation = Quaternion.LookRotation(new Vector3(-0.15f, 0.6f, 0.78f).normalized);   // up and forward: lights the underside the camera sees
            var kickGo = new GameObject("Rim kicker (sunlit edges)");
            kickGo.AddHDLight(LightType.Directional);
            var kick = kickGo.GetComponent<Light>();
            kick.lightUnit = LightUnit.Lux; kick.intensity = 25000f; kick.color = new Color(1f, 0.78f, 0.55f); kick.shadows = LightShadows.None;
            kickGo.GetComponent<HDAdditionalLightData>().affectsVolumetric = false;
            kickGo.transform.rotation = Quaternion.LookRotation(-new Vector3(0.6f, 0.35f, 0.72f).normalized);    // from behind-right-above: grazes the visible edges

            // boss with cinematic material copies
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OYMEnemies.PrefabDir + "/citadel_boss.prefab");
            var boss = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            boss.name = "CITADEL";
            rig.boss = boss.transform;
            var tubeTex = CineTex("citadel_launcher_cine_emissive.png");
            var engineTex = CineTex("citadel_engine_cine_emissive.png");
            var swap = new Dictionary<Material, Material>();
            foreach (var r in boss.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (m == null) continue;
                    if (!swap.TryGetValue(m, out var c))
                    {
                        c = m;
                        if (m.name == "citadel_head" || m.name == "citadel_claw") { c = CineMat(m, "cine_" + m.name, null); rig.sensors.Add(c); }
                        else if (m.name == "citadel_launcher") { c = rig.tubes = CineMat(m, "cine_citadel_launcher", tubeTex); }
                        else if (m.name == "citadel_engine") { c = rig.engine = CineMat(m, "cine_citadel_engine", engineTex); }
                        swap[m] = c;
                    }
                    mats[i] = c;
                }
                r.sharedMaterials = mats;
            }
            foreach (var lg in boss.GetComponentsInChildren<LODGroup>(true)) lg.ForceLOD(0);   // 520k tris is fine for offline frames
            foreach (var t in boss.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("citadel_claw_") && !t.name.Contains("_LOD"))
                    rig.claws.Add((t, t.localRotation, t.name.Contains("_R") ? -1f : 1f));
                if ((t.name == "citadel_launcher_L" || t.name == "citadel_launcher_R"))
                    rig.launchers.Add((t, t.localRotation, t.name.EndsWith("L") ? 1f : -1f));
                if (t.name == "citadel_engine")
                    for (int k = -1; k <= 1; k += 2)
                    {
                        var lgo = new GameObject("EngineGlow_" + (k < 0 ? "L" : "R"));
                        lgo.transform.SetParent(t, false);
                        lgo.transform.localPosition = new Vector3(0.12f * k, 0f, 0f);    // engine part space (boss FBX units)
                        lgo.AddHDLight(LightType.Point);
                        var l = lgo.GetComponent<Light>();
                        l.lightUnit = LightUnit.Lumen; l.color = new Color(1f, 0.16f, 0.3f); l.range = 260f; l.shadows = LightShadows.None;
                        rig.engineLights.Add(l);
                    }
            }

            // tiny booster lights (player soldiers) for scale in the final low-angle phase
            var sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            Color[] cols = { new Color(0.35f, 0.9f, 1f), new Color(1f, 0.7f, 0.3f), new Color(0.4f, 0.65f, 1f), new Color(0.35f, 0.9f, 1f), new Color(1f, 0.7f, 0.3f) };
            Vector3[] offs = { new Vector3(-14, 22, 38), new Vector3(18, 34, 58), new Vector3(4, 48, 30), new Vector3(-28, 38, 66), new Vector3(24, 18, 26) };
            for (int i = 0; i < cols.Length; i++)
            {
                var um = new Material(Shader.Find("HDRP/Unlit")) { name = "cine_booster_" + i };
                um.SetColor("_UnlitColor", cols[i] * 3f);                               // de-exposed: value reaches the frame directly
                HDShaderUtils.ResetMaterialKeywords(um);
                AssetDatabase.DeleteAsset($"{Dir}/cine_booster_{i}.mat"); AssetDatabase.CreateAsset(um, $"{Dir}/cine_booster_{i}.mat");
                var s = new GameObject("Soldier_" + i);
                s.AddComponent<MeshFilter>().sharedMesh = sphere;
                s.AddComponent<MeshRenderer>().sharedMaterial = um;
                s.transform.localScale = Vector3.one * 0.3f;                            // pin-point booster lights, not lamps
                rig.soldiers.Add((s.transform, offs[i]));
            }

            var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
            rig.cam = camGo.AddComponent<Camera>();
            rig.cam.nearClipPlane = 1f; rig.cam.farClipPlane = 320000f;            // limb is ~229 km away
            var hdc = camGo.AddComponent<HDAdditionalCameraData>();
            hdc.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;

            Evaluate(rig, 0f);
            var warm = RenderTexture.GetTemporary(640, 360, 24);
            rig.cam.targetTexture = warm;
            for (int i = 0; i < 600 && (i < 3 || ShaderUtil.anythingCompiling); i++) { rig.cam.Render(); System.Threading.Thread.Sleep(50); }
            rig.cam.targetTexture = null; RenderTexture.ReleaseTemporary(warm);
            Debug.Log("[OYM-BI] shaders ready (anythingCompiling=" + ShaderUtil.anythingCompiling + ")");
            if (save)
            {
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log("[OYM-BI] scene saved " + ScenePath);
            }
            return rig;
        }

        // Planet / atmosphere caps are ~1.4M vertices: built on load by OYM.ProceduralCapMesh instead of being
        // serialized into the scene (that made BossIntro.unity 128 MB, over GitHub's 100 MB limit).
        static GameObject Cap(string name, string meshName, float r, float capDeg, int rings, int segs, Material mat, Vector3 pos)
        {
            var g = Sphere(name, null, mat, pos);
            var cap = g.AddComponent<OYM.ProceduralCapMesh>();
            cap.meshName = meshName; cap.radius = r; cap.capDeg = capDeg; cap.rings = rings; cap.segments = segs;
            cap.enabled = false; cap.enabled = true;   // rebuild with the values above (OnEnable ran with defaults)
            return g;
        }

        static GameObject Sphere(string name, Mesh mesh, Material mat, Vector3 pos)
        {
            var g = new GameObject(name);
            g.transform.position = pos;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            return g;
        }

        // ---------------------------------------------------------------------------------------------
        static float S(float a, float b, float x) { x = Mathf.Clamp01((x - a) / (b - a)); return x * x * (3f - 2f * x); }

        // piecewise Hermite through keys (time, value) with Catmull-Rom tangents
        static Vector3 Spline(float t, (float t, Vector3 v)[] k)
        {
            if (t <= k[0].t) return k[0].v;
            for (int i = 0; i < k.Length - 1; i++)
            {
                if (t > k[i + 1].t) continue;
                float h = k[i + 1].t - k[i].t, u = (t - k[i].t) / h;
                Vector3 m0 = i > 0 ? (k[i + 1].v - k[i - 1].v) / (k[i + 1].t - k[i - 1].t) * h : (k[i + 1].v - k[i].v);
                Vector3 m1 = i + 2 < k.Length ? (k[i + 2].v - k[i].v) / (k[i + 2].t - k[i].t) * h : (k[i + 1].v - k[i].v);
                float u2 = u * u, u3 = u2 * u;
                return (2 * u3 - 3 * u2 + 1) * k[i].v + (u3 - 2 * u2 + u) * m0 + (-2 * u3 + 3 * u2) * k[i + 1].v + (u3 - u2) * m1;
            }
            return k[k.Length - 1].v;
        }

        // boss centre: rises over the limb 6 km out, closes to ~1 km at the hit, then slides over the low camera
        // v2 (user review of v1): starts ~3 km out at a 3/4 front angle with the glowing limb kept in frame for the whole
        // approach; ~15 % of frame height at 2.5 s and ~55-60 % at the 6.8 s hit (~450 m); big hit (sensors EV 20, tubes
        // ignite, bloom 1.0, +1 EV exposure punch for 3 frames, heavy shake); overhead pass 7-10 s; fade to black 10-10.5 s.
        public static (float t, Vector3 v)[] BossKeys =
        {
            (0f,    new Vector3(390f, -912f, 2777f)),         // 2.95 km, 18 deg down
            (2.5f,  new Vector3(169f, -511f, 1613f)),         // 1.7 km, 17.5 deg
            (5f,    new Vector3(47f, -181f, 675f)),           // 0.7 km, 15 deg
            (6.8f,  new Vector3(52f, -85f, 423f)),            // ~350 m from the camera, 11.5 deg below it
            (8.5f,  new Vector3(30f, 70f, 170f)),
            (10f,   new Vector3(-40f, 170f, -130f)),
            (10.5f, new Vector3(-60f, 200f, -225f)),
        };
        public static (float t, Vector3 v)[] CamKeys =
        {
            (0f,    new Vector3(0f, 0f, 0f)),
            (6.8f,  new Vector3(40f, -15f, 80f)),
            (8f,    new Vector3(40f, -40f, 100f)),
            (10.5f, new Vector3(42f, -46f, 112f)),
        };
        const float LimbDeg = 19.8f;                                                       // limb below horizontal
        static readonly Vector3 PassLook = new Vector3(-0.05f, 0.78f, 0.62f).normalized;    // overhead pass: up and forward

        static float Fov(float t) => t < HitT ? Mathf.Lerp(22f, 32f, S(0f, HitT, t)) : Mathf.Lerp(32f, 72f, S(HitT, 8.2f, t));

        static void Evaluate(Rig rig, float t)
        {
            float h = t - (HitT - 1e-4f);                                                  // >= 0 from frame 204 on
            float flash = h >= 0f ? Mathf.Exp(-h / 0.6f) : 0f;

            // --- boss: crabs in ~35 deg off its track (3/4 front: claws and launchers read), lining up as it passes over
            Vector3 p = Spline(t, BossKeys);
            Vector3 vel = (Spline(t + 0.05f, BossKeys) - Spline(t - 0.05f, BossKeys)).normalized;
            Vector3 fwd = Quaternion.AngleAxis(Mathf.Lerp(35f, 15f, S(6.8f, 9f, t)), Vector3.up) * vel;
            rig.boss.SetPositionAndRotation(p, Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, 0f, -6f * S(2f, 9f, t)));

            // --- parts: claws unfold forward (snap open on the hit), launchers swing to aim
            float claw = S(2.5f, 6.6f, t) * 28f + (h >= 0f ? S(0f, 0.35f, h) * 10f : 0f);
            foreach (var c in rig.claws) c.t.localRotation = c.rest * Quaternion.Euler(-claw, 0f, 0f);
            float aim = S(3f, 6.4f, t) * 18f;
            foreach (var l in rig.launchers) l.t.localRotation = l.rest * Quaternion.Euler(-aim * 0.35f, aim * l.sign, 0f);

            // --- glow: sensors creep up, flash to EV 20 on the hit; tubes ignite; engine burns harder on the pass
            float sensorEV = h < 0f ? Mathf.Lerp(15f, 17f, S(0f, HitT, t)) : Mathf.Lerp(17.5f, 20f, flash);
            foreach (var m in rig.sensors) HDMaterial.SetEmissiveIntensity(m, sensorEV, EmissiveIntensityUnit.EV100);
            if (rig.tubes) HDMaterial.SetEmissiveIntensity(rig.tubes, h < 0f ? 0f : LightUnitUtils.Ev100ToNits(Mathf.Lerp(17.5f, 20f, flash)), EmissiveIntensityUnit.Nits);
            if (rig.engine) HDMaterial.SetEmissiveIntensity(rig.engine, Mathf.Lerp(15f, 17.5f, S(1f, HitT, t)) + S(8f, 9.5f, t), EmissiveIntensityUnit.EV100);
            foreach (var l in rig.engineLights) l.intensity = Mathf.Lerp(2e8f, 1.2e9f, S(1f, HitT, t)) * (1f + S(8f, 9.5f, t));

            // --- post: bloom spike to 1.0, +1 EV exposure punch for 3 frames (then 4 frames back), fade to black
            if (rig.bloom) rig.bloom.intensity.value = 0.3f + 0.7f * (h >= 0f ? Mathf.Exp(-h / 0.8f) : 0f);
            float punch = h < 0f ? 0f : (h < 3f / Fps ? 1f : Mathf.Clamp01(1f - (h - 3f / Fps) / (4f / Fps)));
            if (rig.exposure) rig.exposure.fixedExposure.value = ExposureEV - punch;
            if (rig.color) rig.color.postExposure.value = -12f * S(10f, 10.5f, t);

            // --- camera: tracks the boss with the pitch clamped so the glowing limb stays in the lower frame during the
            //     approach, then swings up-forward for the overhead pass; heavy shake on the hit
            Vector3 c0 = Spline(t, CamKeys);
            float fov = Fov(t);
            Vector3 toBoss = (p - c0).normalized;
            float pitch = Mathf.Asin(Mathf.Clamp(toBoss.y, -1f, 1f)) * Mathf.Rad2Deg + 4f;    // + up; boss in the lower half, on the limb
            float maxPitch = -LimbDeg + 0.64f * (fov * 0.5f);                              // limb >= 18 % above the bottom edge
            float yaw = Mathf.Atan2(toBoss.x, toBoss.z) * Mathf.Rad2Deg;
            var rot = Quaternion.Slerp(Quaternion.Euler(-Mathf.Min(pitch, maxPitch), yaw, 0f),
                                       Quaternion.LookRotation(PassLook, Vector3.up), S(6.9f, 8.0f, t));
            if (h >= 0f)
            {
                float a = 2.2f * Mathf.Exp(-h / 0.5f);
                float k = h * 40f;
                rot = rot * Quaternion.Euler(a * Mathf.Sin(k * 1.3f + 0.4f), a * Mathf.Sin(k * 0.9f + 1.7f), a * 0.7f * Mathf.Sin(k * 1.7f));
                c0 += rot * new Vector3(Mathf.Sin(k * 1.1f), Mathf.Sin(k * 1.5f + 2f), 0f) * (3f * a / 2.2f);
            }
            rig.cam.transform.SetPositionAndRotation(c0, rot);
            rig.cam.fieldOfView = fov;

            // --- booster lights (soldiers) between the low camera and the passing hull
            foreach (var s in rig.soldiers)
            {
                s.t.position = Spline(t, CamKeys) + s.offset + new Vector3(0f, 2f, 4f) * Mathf.Max(h, 0f);
                s.t.gameObject.SetActive(t > 6.9f);
            }
        }

        // ---------------------------------------------------------------------------------------------
        static void RenderFrames(Rig rig, IEnumerable<float> times, int w, int h, Func<float, string> outPath, int warm)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create(); rig.cam.targetTexture = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            foreach (var t in times)
            {
                for (int i = warm; i >= 1; i--) { Evaluate(rig, t - i / Fps); rig.cam.Render(); }   // the real preceding frames: TAA + motion blur history
                Evaluate(rig, t); rig.cam.Render();
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = null;
                var p = outPath(t);
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllBytes(p, tex.EncodeToPNG());
            }
            rig.cam.targetTexture = null; rt.Release();
        }

        [MenuItem("OYM/Cinematic/Render Preview Key Frames")]
        public static void Preview()
        {
            try
            {
                var rig = BuildRig(true);
                string dir = Path.Combine(OYMCapture.OutDir, "boss_intro_v2_preview");
                var times = new[] { 0f, 1.5f, 2.5f, 4f, 5.5f, 204f / Fps, 6.95f, 7.6f, 8.4f, 9.2f, 9.9f, 10.3f };
                // first key gets a long warm-up (sky / exposure / TAA), the others the 8 real preceding frames
                RenderFrames(rig, new[] { 0f }, 960, 540, t => Path.Combine(dir, $"t_{t:00.00}.png"), 30);
                var rest = new List<float>(times); rest.RemoveAt(0);
                RenderFrames(rig, rest, 960, 540, t => Path.Combine(dir, $"t_{t:00.00}.png"), 8);
                Debug.Log("[OYM-BI] preview done -> " + dir);
            }
            catch (Exception e) { Debug.LogError("[OYM-BI] failed: " + e); if (Application.isBatchMode) EditorApplication.Exit(1); }
        }

        public static void RenderChunk()
        {
            try
            {
                int from = 0, to = Frames;
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] == "-oymFrom") from = int.Parse(args[i + 1]);
                    if (args[i] == "-oymTo") to = int.Parse(args[i + 1]);
                }
                var rig = BuildRig(false);
                string dir = Path.Combine(OYMCapture.OutDir, "boss_intro_v2");
                // warm-up on the frames before `from` (or the first frame held, for the opening chunk)
                for (int i = 24; i >= 1; i--) { Evaluate(rig, Mathf.Max(0f, (from - i) / Fps)); RenderOnce(rig); }
                var times = new List<float>();
                for (int f = from; f < to; f++) times.Add(f / Fps);
                RenderFrames(rig, times, 1920, 1080, t => Path.Combine(dir, $"frame_{Mathf.RoundToInt(t * Fps):0000}.png"), 0);
                Debug.Log($"[OYM-BI] frames {from}-{to - 1} -> {dir}");
            }
            catch (Exception e) { Debug.LogError("[OYM-BI] failed: " + e); if (Application.isBatchMode) EditorApplication.Exit(1); }
        }

        static void RenderOnce(Rig rig)
        {
            var rt = RenderTexture.GetTemporary(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rig.cam.targetTexture = rt; rig.cam.Render(); rig.cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
        }
    }
}
