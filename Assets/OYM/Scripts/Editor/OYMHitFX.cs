// OnYourMark - hit / impact FX: image-gen textures (pipeline/fx/process_fx_sheets.py) -> materials on the OYM/FX/*
// shaders -> four pooled prefabs + Resources/OYM_HitFXLibrary -> HitFXTest.unity -> batch capture.
//
//   Unity.exe -batchmode -projectPath <proj> -executeMethod OYM.EditorTools.OYMHitFX.BuildAndCapture -quit
//
// Captures go to Screenshots/hitfx/ (stills/<fx>_<ms>.png, frames_<fx>/NNN.png); pipeline/fx/make_hitfx_media.py
// assembles the contact sheets + GIFs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;
using PSys = UnityEngine.ParticleSystem;

namespace OYM.EditorTools
{
    public static class OYMHitFX
    {
        public const string Dir = OYMSetup.Root + "/FX/Hit";
        public const string TexDir = Dir + "/Textures";
        public const string MatDir = Dir + "/Materials";
        public const string PrefabDir = Dir + "/Prefabs";
        public const string LibPath = Dir + "/Resources/OYM_HitFXLibrary.asset";
        public const string VolumePath = Dir + "/HitFX_TestVolume.asset";
        public const string TestScene = OYMSetup.Root + "/Scenes/HitFXTest.unity";
        static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/hitfx"));

        // ------------------------------------------------------------------------------------------------ entry points
        [MenuItem("OYM/FX/Hit: Build All (materials, prefabs, test scene)")]
        public static void BuildAll() { Guard(() => { ImportTextures(); var lib = BuildLibrary(); BuildTestScene(lib); }); }

        [MenuItem("OYM/FX/Hit: Capture stills + GIF frames")]
        public static void Capture() { Guard(CaptureInner); }

        public static void BuildAndCapture() { Guard(() => { ImportTextures(); var lib = BuildLibrary(); BuildTestScene(lib); CaptureInner(); }); }

        static void Guard(Action a)
        {
            try { a(); }
            catch (Exception e)
            {
                Debug.LogError("[OYM-HitFX] " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        // ------------------------------------------------------------------------------------------------ textures
        static void ImportTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti == null) continue;
                string n = Path.GetFileNameWithoutExtension(p);
                bool ramp = n.StartsWith("T_Ramp");
                bool tile = n.Contains("_Tile") || n.Contains("BeamStrip");
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = ramp;                                   // shapes / noise are data (linear)
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = false;
                ti.wrapModeU = tile ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.wrapModeV = n.Contains("_Tile") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.mipmapEnabled = !ramp;
                ti.filterMode = FilterMode.Bilinear;
                // uncompressed: gradients x HDR intensity band visibly with BC3/BC7 alpha (same call as the jet FX)
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
        }

        static Texture2D Tex(string n)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{n}.png");
            if (t == null) throw new Exception("missing texture " + n + " (run pipeline/fx/process_fx_sheets.py)");
            return t;
        }

        // ------------------------------------------------------------------------------------------------ materials
        enum Pal { Fire, Husk, Player, Metal }

        static Texture2D Ramp(Pal p) => Tex(p == Pal.Fire ? "T_Ramp_Blackbody" : p == Pal.Husk ? "T_Ramp_Husk" : p == Pal.Player ? "T_Ramp_Player" : "T_Ramp_HotMetal");

        static Material Mat(string name, string shader)
        {
            Directory.CreateDirectory(MatDir);
            var sh = Shader.Find(shader);
            if (sh == null) throw new Exception("shader missing " + shader);
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Erode(string name, string tex, Pal pal, float intensity, bool premul = false, Color? baseCol = null,
                              float distort = 0.03f, float erodeNoise = 0.5f, float edgeGlow = 0.6f, float heatContrast = 1.4f,
                              float softFade = 0.4f, float softStart = 0.6f)
        {
            var m = Mat(name, "OYM/FX/Erode");
            m.SetTexture("_MainTex", Tex(tex));
            m.SetTexture("_Ramp", Ramp(pal));
            m.SetTexture("_NoiseTex", Tex("T_FX_Noise_Tile"));
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_Distort", distort);
            m.SetFloat("_ErodeNoise", erodeNoise);
            m.SetFloat("_EdgeGlow", edgeGlow);
            m.SetFloat("_HeatContrast", heatContrast);
            m.SetFloat("_SoftFade", softFade);
            m.SetFloat("_SoftStart", softStart);
            m.SetColor("_BaseColor", baseCol ?? Color.black);
            m.SetFloat("_Opacity", premul ? 1f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", premul ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.One);
            return m;
        }

        static Material Distort(string name, string mask, float radial, float turbulence, float glow, Pal pal, float blur = 0.08f)
        {
            var m = Mat(name, "OYM/FX/Distort");
            m.SetTexture("_MainTex", Tex(mask));
            m.SetTexture("_NoiseTex", Tex("T_FX_Noise_Tile"));
            m.SetTexture("_Ramp", Ramp(pal));
            m.SetFloat("_Radial", radial);
            m.SetFloat("_Turbulence", turbulence);
            m.SetFloat("_Glow", glow);
            m.SetFloat("_Blur", blur);
            return m;
        }

        static Material Beam(string name, Pal pal, float intensity)
        {
            var m = Mat(name, "OYM/FX/Beam");
            m.SetTexture("_MainTex", Tex("T_FX_BeamStrip"));
            m.SetTexture("_NoiseTex", Tex("T_FX_Noise_Tile"));
            m.SetTexture("_Ramp", Ramp(pal));
            m.SetFloat("_Intensity", intensity);
            return m;
        }

        static Material Overlay()
        {
            var m = Mat("M_FX_HitOverlay", "OYM/FX/HitOverlay");
            m.SetTexture("_NoiseTex", Tex("T_FX_Noise_Tile"));
            m.SetTexture("_PlasmaTex", Tex("T_FX_Plasma_Tile"));
            return m;
        }

        class Mats
        {
            public Material coreFire, corePlayer, coreHusk, glintPlayer, glintFire, burstMetal, burstHusk;
            public Material sparkMetal, sparkHusk, molten, debris, fireball, fireSmoke, smoke, dropMetal;
            public Material lensHusk, arcHusk, rippleHusk;
            public Material ringPlayer, ringFire, ringHusk, haze;
            public Material beamHusk, beamPlayer, overlay;
        }

        static Mats BuildMaterials()
        {
            var smokeCol = new Color(0.05f, 0.047f, 0.045f);
            var metalCol = new Color(0.06f, 0.06f, 0.065f);
            return new Mats
            {
                coreFire = Erode("M_FX_Core_Fire", "T_FX_CoreBurst", Pal.Fire, 55f, distort: 0.01f, erodeNoise: 0.2f, heatContrast: 0.6f),
                corePlayer = Erode("M_FX_Core_Player", "T_FX_CoreBurst", Pal.Player, 40f, distort: 0.01f, erodeNoise: 0.2f, heatContrast: 0.6f),
                coreHusk = Erode("M_FX_Core_Husk", "T_FX_CoreBurst", Pal.Husk, 36f, distort: 0.01f, erodeNoise: 0.2f, heatContrast: 0.6f),
                glintPlayer = Erode("M_FX_Glint_Player", "T_FX_Glint", Pal.Player, 30f, distort: 0f, erodeNoise: 0f, heatContrast: 0.5f, softFade: 0.05f),
                glintFire = Erode("M_FX_Glint_Fire", "T_FX_Glint", Pal.Fire, 30f, distort: 0f, erodeNoise: 0f, heatContrast: 0.5f, softFade: 0.05f),
                burstMetal = Erode("M_FX_SparkBurst_Metal", "T_FX_SparkBurst", Pal.Metal, 16f, distort: 0f, erodeNoise: 0.3f, heatContrast: 0.7f, softFade: 0.05f),
                burstHusk = Erode("M_FX_SparkBurst_Husk", "T_FX_SparkBurst", Pal.Husk, 14f, distort: 0f, erodeNoise: 0.3f, heatContrast: 0.7f, softFade: 0.05f),
                sparkMetal = Erode("M_FX_Spark_Metal", "T_FX_Streak", Pal.Metal, 14f, distort: 0f, erodeNoise: 0f, edgeGlow: 0f, heatContrast: 0.8f, softFade: 0.02f, softStart: 0.3f),
                sparkHusk = Erode("M_FX_Spark_Husk", "T_FX_Streak", Pal.Husk, 13f, distort: 0f, erodeNoise: 0f, edgeGlow: 0f, heatContrast: 0.8f, softFade: 0.02f, softStart: 0.3f),
                dropMetal = Erode("M_FX_Droplet_Metal", "T_FX_Dot", Pal.Metal, 9f, distort: 0f, erodeNoise: 0f, edgeGlow: 0f, heatContrast: 0.8f, softFade: 0.02f, softStart: 0.3f),
                molten = Erode("M_FX_Molten", "T_FX_MoltenSplash", Pal.Metal, 9f, distort: 0.015f, erodeNoise: 0.6f, edgeGlow: 0.8f, heatContrast: 0.9f, softFade: 0.08f),
                debris = Erode("M_FX_Debris", "T_FX_Debris_2x2", Pal.Metal, 4f, true, metalCol, distort: 0f, erodeNoise: 0.4f, edgeGlow: 1.2f, heatContrast: 1.2f, softFade: 0.05f, softStart: 0.15f),
                fireball = Erode("M_FX_Fireball", "T_FX_Fireball_2x2", Pal.Fire, 9f, distort: 0.04f, erodeNoise: 0.35f, edgeGlow: 0.6f, heatContrast: 1.1f, softFade: 0.5f),
                fireSmoke = Erode("M_FX_FireSmoke", "T_FX_Smoke_2x2", Pal.Fire, 2.2f, true, smokeCol, distort: 0.035f, erodeNoise: 0.6f, edgeGlow: 0.4f, heatContrast: 1.6f, softFade: 0.6f),
                smoke = Erode("M_FX_Smoke", "T_FX_Smoke_2x2", Pal.Fire, 0.8f, true, smokeCol, distort: 0.03f, erodeNoise: 0.6f, edgeGlow: 0.2f, heatContrast: 1.8f, softFade: 0.4f),
                lensHusk = Erode("M_FX_LensCore_Husk", "T_FX_LensCore", Pal.Husk, 22f, distort: 0f, erodeNoise: 0.1f, heatContrast: 0.6f, softFade: 0.05f),
                arcHusk = Erode("M_FX_Arc_Husk", "T_FX_Arc", Pal.Husk, 14f, distort: 0.01f, erodeNoise: 0.3f, heatContrast: 0.7f, softFade: 0.05f),
                rippleHusk = Erode("M_FX_Ripple_Husk", "T_FX_Ripple", Pal.Husk, 10f, distort: 0.02f, erodeNoise: 0.5f, edgeGlow: 0.8f, heatContrast: 0.9f, softFade: 0.05f),
                ringPlayer = Distort("M_FX_Shock_Player", "T_FX_ShockRing", 22f, 0f, 0.3f, Pal.Player),
                ringFire = Distort("M_FX_Shock_Fire", "T_FX_ShockRing", 50f, 3f, 0.4f, Pal.Fire, 0.15f),
                ringHusk = Distort("M_FX_Shock_Husk", "T_FX_ShockRing", 24f, 0f, 0.4f, Pal.Husk),
                haze = Distort("M_FX_HeatHaze", "T_FX_Dot", 0f, 7f, 0f, Pal.Fire, 0.05f),
                beamHusk = Beam("M_FX_Beam_Husk", Pal.Husk, 11f),
                beamPlayer = Beam("M_FX_Beam_Player", Pal.Player, 11f),
                overlay = Overlay(),
            };
        }

        // ------------------------------------------------------------------------------------------------ particle helpers
        static PSys.MinMaxCurve MM(float a, float b) => Mathf.Approximately(a, b) ? new PSys.MinMaxCurve(a) : new PSys.MinMaxCurve(a, b);

        /// Linear curve from (t, v) pairs.
        static AnimationCurve Lin(params float[] tv)
        {
            var c = new AnimationCurve();
            for (int i = 0; i + 1 < tv.Length; i += 2) c.AddKey(new Keyframe(tv[i], tv[i + 1]));
            for (int i = 0; i < c.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.Linear);
            }
            return c;
        }

        static readonly List<ParticleSystemVertexStream> ErodeStreams = new List<ParticleSystemVertexStream>
        { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.Custom1XY, ParticleSystemVertexStream.StableRandomX };
        static readonly List<ParticleSystemVertexStream> PlainStreams = new List<ParticleSystemVertexStream>
        { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV };

        /// One particle layer. burst > 0: one-shot burst at t = 0 (after `delay`); rate > 0: looping emitter.
        static PSys Ps(Transform parent, string name, Material mat, int burst, float rate, Vector2 life, Vector2 speed, Vector2 size,
                       float delay = 0f, bool world = true, int fudge = 0)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<PSys>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = rate > 0f;
            main.duration = rate > 0f ? 1f : Mathf.Max(0.05f, delay + 0.05f);
            main.maxParticles = 400;
            main.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startDelay = delay;
            main.startLifetime = MM(life.x, life.y);
            main.startSpeed = MM(speed.x, speed.y);
            main.startSize = MM(size.x, size.y);
            main.startColor = Color.white;
            main.gravityModifier = 0f;                            // zero-g
            var em = ps.emission;
            em.rateOverTime = rate;
            em.SetBursts(burst > 0 ? new[] { new PSys.Burst(0f, (short)burst) } : new PSys.Burst[0]);
            var shape = ps.shape; shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.alignment = ParticleSystemRenderSpace.View;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.maxParticleSize = 5f;
            r.sortingFudge = fudge;
            bool erode = mat.shader.name == "OYM/FX/Erode";
            r.SetActiveVertexStreams(erode ? ErodeStreams : PlainStreams);
            if (erode)
            {
                var cd = ps.customData;
                cd.enabled = true;
                cd.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
                cd.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 2);
                cd.SetVector(ParticleSystemCustomData.Custom1, 0, new PSys.MinMaxCurve(0f));   // erosion
                cd.SetVector(ParticleSystemCustomData.Custom1, 1, new PSys.MinMaxCurve(1f));   // heat
            }
            return ps;
        }

        static PSys Erosion(this PSys ps, AnimationCurve erosion, AnimationCurve heat)
        {
            var cd = ps.customData;
            if (erosion != null) cd.SetVector(ParticleSystemCustomData.Custom1, 0, new PSys.MinMaxCurve(1f, erosion));
            if (heat != null) cd.SetVector(ParticleSystemCustomData.Custom1, 1, new PSys.MinMaxCurve(1f, heat));
            return ps;
        }

        static PSys Size(this PSys ps, AnimationCurve c) { var s = ps.sizeOverLifetime; s.enabled = true; s.size = new PSys.MinMaxCurve(1f, c); return ps; }

        static PSys Alpha(this PSys ps, params float[] ta)
        {
            var g = new Gradient();
            var alphas = new List<GradientAlphaKey>();
            for (int i = 0; i + 1 < ta.Length; i += 2) alphas.Add(new GradientAlphaKey(ta[i + 1], ta[i]));
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, alphas.ToArray());
            var col = ps.colorOverLifetime; col.enabled = true; col.color = new PSys.MinMaxGradient(g);
            return ps;
        }

        static PSys Atlas(this PSys ps)
        {
            var ts = ps.textureSheetAnimation;
            ts.enabled = true;
            ts.mode = ParticleSystemAnimationMode.Grid;
            ts.numTilesX = 2; ts.numTilesY = 2;
            ts.animation = ParticleSystemAnimationType.WholeSheet;
            ts.frameOverTime = new PSys.MinMaxCurve(0f, 0.999f);   // random variant per particle, held for life
            ts.cycleCount = 1;
            return ps;
        }

        static PSys Rot(this PSys ps, float spinDeg = 0f)
        {
            var main = ps.main; main.startRotation = new PSys.MinMaxCurve(0f, Mathf.PI * 2f);
            if (spinDeg > 0f) { var ro = ps.rotationOverLifetime; ro.enabled = true; ro.z = MM(-spinDeg * Mathf.Deg2Rad, spinDeg * Mathf.Deg2Rad); }
            return ps;
        }

        static PSys Cone(this PSys ps, float angle, float radius = 0.02f)
        {
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = angle; sh.radius = radius; sh.radiusThickness = 1f;
            return ps;
        }

        static PSys Sphere(this PSys ps, float radius)
        {
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius; sh.radiusThickness = 1f;
            return ps;
        }

        /// Hemisphere around +Z (the surface normal): blasts bloom off the hull instead of half into it.
        static PSys Hemi(this PSys ps, float radius)
        {
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = radius; sh.radiusThickness = 1f;
            sh.rotation = Vector3.zero;
            return ps;
        }

        static PSys Drag(this PSys ps, float drag)
        {
            var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.drag = new PSys.MinMaxCurve(drag);
            lv.multiplyDragByParticleSize = false; lv.multiplyDragByParticleVelocity = false;
            return ps;
        }

        static PSys Stretch(this PSys ps, float velScale, float lenScale)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = velScale; r.lengthScale = lenScale;
            return ps;
        }

        static Light FxLight(Transform parent, Color c, float range, float radius)
        {
            var go = new GameObject("FlashLight");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.35f);   // just off the surface
            var hd = go.AddHDLight(LightType.Point);
            var l = go.GetComponent<Light>();
            l.lightUnit = LightUnit.Lumen; l.intensity = 0f; l.range = range; l.color = c; l.useColorTemperature = false;
            l.shadows = LightShadows.None; l.enabled = false;
            hd.affectsVolumetric = false;
            var so = new SerializedObject(hd);
            var rad = so.FindProperty("m_ShapeRadius"); if (rad != null) rad.floatValue = radius;
            var fd = so.FindProperty("m_FadeDistance"); if (fd != null) fd.floatValue = 80f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return l;
        }

        static HitFXInstance Finish(GameObject root, Light light, float lumen, float decay, float duration, bool loop = false, float sustain = 0f)
        {
            var fx = root.AddComponent<HitFXInstance>();
            fx.systems = root.GetComponentsInChildren<PSys>(true);
            fx.flashLight = light; fx.flashLumen = lumen; fx.flashDecay = decay; fx.duration = duration; fx.loop = loop; fx.sustainLumen = sustain;
            Directory.CreateDirectory(PrefabDir);
            var saved = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{root.name}.prefab");
            Object.DestroyImmediate(root);
            Debug.Log("[OYM-HitFX] prefab " + saved.name);
            return saved.GetComponent<HitFXInstance>();
        }

        // ------------------------------------------------------------------------------------------------ prefabs
        /// Player railgun slug on armour: blinding cyan-white pop + anamorphic glint, white-hot spark spray cooling to
        /// orange, a molten splash, a few hot shards tumbling off, a puff of smoke and a small refraction ring.
        static HitFXInstance BuildKinetic(Mats m)
        {
            var root = new GameObject("FX_Hit_Kinetic"); var t = root.transform;
            Ps(t, "Core", m.corePlayer, 1, 0, new Vector2(0.07f, 0.09f), Vector2.zero, new Vector2(1.5f, 1.5f), world: false, fudge: -4)
                .Size(Lin(0, 0.55f, 0.3f, 1f, 1, 1.2f)).Alpha(0, 1, 0.5f, 0.8f, 1, 0).Erosion(Lin(0, 0, 1, 0.45f), Lin(0, 1.2f, 1, 0.7f));
            Ps(t, "Glint", m.glintPlayer, 1, 0, new Vector2(0.10f, 0.12f), Vector2.zero, new Vector2(2.8f, 2.8f), world: false, fudge: -5)
                .Size(Lin(0, 1f, 1, 0.55f)).Alpha(0, 1, 1, 0).Erosion(null, Lin(0, 1.3f, 1, 0.8f));
            Ps(t, "SparkBurst", m.burstMetal, 1, 0, new Vector2(0.06f, 0.08f), Vector2.zero, new Vector2(1.4f, 1.4f), world: false, fudge: -3)
                .Rot().Size(Lin(0, 0.7f, 1, 1.2f)).Alpha(0, 1, 1, 0).Erosion(Lin(0, 0, 1, 0.6f), Lin(0, 1.3f, 1, 0.8f));
            Ps(t, "Sparks", m.sparkMetal, 38, 0, new Vector2(0.18f, 0.55f), new Vector2(6f, 24f), new Vector2(0.010f, 0.022f), fudge: -2)
                .Cone(62f).Stretch(0.05f, 2.2f).Drag(2.5f).Alpha(0, 1, 0.7f, 1, 1, 0).Erosion(null, Lin(0, 1.25f, 0.4f, 0.75f, 1, 0.3f));
            Ps(t, "Molten", m.molten, 1, 0, new Vector2(0.14f, 0.18f), Vector2.zero, new Vector2(0.9f, 0.9f), world: false, fudge: -1)
                .Rot().Size(Lin(0, 0.55f, 1, 1.15f)).Erosion(Lin(0, 0, 1, 0.95f), Lin(0, 1.2f, 1, 0.45f));
            Ps(t, "Debris", m.debris, 5, 0, new Vector2(0.8f, 1.4f), new Vector2(2f, 7f), new Vector2(0.05f, 0.12f))
                .Cone(50f).Atlas().Rot(540f).Drag(0.6f).Alpha(0, 1, 0.85f, 1, 1, 0).Erosion(Lin(0, 0, 0.7f, 0, 1, 0.6f), Lin(0, 1f, 0.45f, 0.25f, 1, 0));
            Ps(t, "Smoke", m.smoke, 2, 0, new Vector2(0.6f, 1.0f), new Vector2(0.4f, 1.2f), new Vector2(0.35f, 0.6f), delay: 0.02f, fudge: 2)
                .Cone(25f).Atlas().Rot(40f).Size(Lin(0, 0.5f, 1, 1.6f)).Alpha(0, 0, 0.1f, 0.7f, 1, 0.3f).Erosion(Lin(0, 0.15f, 1, 1f), Lin(0, 0.4f, 0.4f, 0f));
            Ps(t, "Shock", m.ringPlayer, 1, 0, new Vector2(0.22f, 0.22f), Vector2.zero, new Vector2(2.4f, 2.4f), world: false)
                .Size(Lin(0, 0.08f, 0.35f, 0.7f, 1, 1f)).Alpha(0, 1, 1, 0);
            var light = FxLight(t, new Color(0.62f, 0.86f, 1f), 9f, 0.15f);
            return Finish(root, light, 9000f, 0.05f, 1.5f);
        }

        /// Missile / WICK blast (~2.5 m): white core flash, rolling black-body fireballs that boil and erode into
        /// dark smoke, fast flame licks, a spark shell, glowing shards, a big refraction shock ring and heat haze.
        static HitFXInstance BuildExplosion(Mats m)
        {
            var root = new GameObject("FX_Hit_Explosion"); var t = root.transform;
            Ps(t, "Core", m.coreFire, 1, 0, new Vector2(0.11f, 0.13f), Vector2.zero, new Vector2(4.2f, 4.2f), world: false, fudge: -6)
                .Size(Lin(0, 0.5f, 0.3f, 1f, 1, 1.3f)).Alpha(0, 1, 0.5f, 0.8f, 1, 0).Erosion(Lin(0, 0, 1, 0.5f), Lin(0, 1.3f, 1, 0.8f));
            Ps(t, "Glint", m.glintFire, 1, 0, new Vector2(0.14f, 0.16f), Vector2.zero, new Vector2(7f, 7f), world: false, fudge: -6)
                .Size(Lin(0, 1f, 1, 0.6f)).Alpha(0, 1, 1, 0).Erosion(null, Lin(0, 1.2f, 1, 0.8f));
            Ps(t, "Fireballs", m.fireball, 9, 0, new Vector2(0.55f, 1.0f), new Vector2(1.5f, 4.5f), new Vector2(1.6f, 2.8f), fudge: -2)
                .Hemi(0.5f).Atlas().Rot(60f).Drag(3f).Size(Lin(0, 0.45f, 0.25f, 0.85f, 1, 1.1f)).Alpha(0, 1, 0.9f, 1, 1, 0)
                .Erosion(Lin(0, 0, 0.35f, 0.15f, 1, 1f), Lin(0, 1.25f, 0.3f, 0.85f, 0.7f, 0.45f, 1, 0.25f));
            Ps(t, "FlameLicks", m.fireball, 14, 0, new Vector2(0.25f, 0.5f), new Vector2(2.5f, 5.5f), new Vector2(0.5f, 1.0f), fudge: -3)
                .Hemi(0.3f).Atlas().Rot(120f).Drag(4f).Size(Lin(0, 0.6f, 1, 1.2f)).Erosion(Lin(0, 0, 1, 1f), Lin(0, 1.2f, 1, 0.4f));
            Ps(t, "Smoke", m.fireSmoke, 10, 0, new Vector2(1.6f, 2.6f), new Vector2(0.6f, 2.0f), new Vector2(2.2f, 3.6f), delay: 0.06f, fudge: 3)
                .Hemi(0.8f).Atlas().Rot(25f).Drag(1.2f).Size(Lin(0, 0.6f, 1, 1.5f)).Alpha(0, 0, 0.08f, 0.85f, 1, 0.6f)
                .Erosion(Lin(0, 0.25f, 0.3f, 0.3f, 1, 1f), Lin(0, 0.6f, 0.2f, 0.3f, 0.5f, 0f));
            Ps(t, "Sparks", m.sparkMetal, 70, 0, new Vector2(0.4f, 1.0f), new Vector2(10f, 32f), new Vector2(0.014f, 0.03f), fudge: -1)
                .Hemi(0.3f).Stretch(0.045f, 2.2f).Drag(1.2f).Alpha(0, 1, 0.75f, 1, 1, 0).Erosion(null, Lin(0, 1.25f, 0.5f, 0.6f, 1, 0.2f));
            Ps(t, "Debris", m.debris, 12, 0, new Vector2(1.5f, 2.5f), new Vector2(4f, 12f), new Vector2(0.08f, 0.22f))
                .Hemi(0.4f).Atlas().Rot(720f).Drag(0.4f).Alpha(0, 1, 0.85f, 1, 1, 0).Erosion(Lin(0, 0, 0.75f, 0, 1, 0.6f), Lin(0, 1.1f, 0.5f, 0.2f, 1, 0));
            Ps(t, "ShockRing", m.ringFire, 1, 0, new Vector2(0.42f, 0.42f), Vector2.zero, new Vector2(13f, 13f), world: false)
                .Size(Lin(0, 0.05f, 0.3f, 0.6f, 1, 1f)).Alpha(0, 1, 0.6f, 0.6f, 1, 0);
            Ps(t, "HeatHaze", m.haze, 1, 0, new Vector2(1.4f, 1.4f), Vector2.zero, new Vector2(6.5f, 6.5f), delay: 0.05f, world: false)
                .Size(Lin(0, 0.7f, 1, 1.1f)).Alpha(0, 0, 0.1f, 1, 1, 0);
            var light = FxLight(t, new Color(1f, 0.62f, 0.3f), 35f, 1f);
            return Finish(root, light, 90000f, 0.14f, 3f);
        }

        /// HUSK beam held on a surface (loops, BeamFX moves it every frame): flickering magenta lens core, white-hot
        /// molten splash, spark spray off the normal, molten droplets, smoke and heat shimmer, crackling arcs.
        static HitFXInstance BuildLaserBurn(Mats m)
        {
            var root = new GameObject("FX_Hit_LaserBurn"); var t = root.transform;
            Ps(t, "Core", m.lensHusk, 0, 30f, new Vector2(0.06f, 0.10f), Vector2.zero, new Vector2(0.5f, 0.8f), world: false, fudge: -4)
                .Rot().Alpha(0, 1, 1, 0).Erosion(null, Lin(0, 1.2f, 1, 0.8f));
            Ps(t, "Molten", m.molten, 0, 14f, new Vector2(0.10f, 0.16f), Vector2.zero, new Vector2(0.5f, 0.9f), world: false, fudge: -2)
                .Rot().Size(Lin(0, 0.6f, 1, 1.1f)).Erosion(Lin(0, 0, 1, 0.9f), Lin(0, 1.2f, 1, 0.5f));
            Ps(t, "Spray", m.sparkMetal, 0, 110f, new Vector2(0.15f, 0.5f), new Vector2(4f, 15f), new Vector2(0.008f, 0.018f), fudge: -1)
                .Cone(65f).Stretch(0.05f, 2.0f).Drag(2f).Alpha(0, 1, 0.7f, 1, 1, 0).Erosion(null, Lin(0, 1.25f, 0.5f, 0.6f, 1, 0.2f));
            Ps(t, "Droplets", m.dropMetal, 0, 18f, new Vector2(0.5f, 1.0f), new Vector2(1.5f, 5f), new Vector2(0.03f, 0.06f))
                .Cone(70f).Drag(0.5f).Alpha(0, 1, 0.8f, 1, 1, 0).Erosion(null, Lin(0, 1.1f, 1, 0.1f));
            Ps(t, "Smoke", m.smoke, 0, 7f, new Vector2(0.8f, 1.5f), new Vector2(0.4f, 1.2f), new Vector2(0.25f, 0.45f), fudge: 2)
                .Cone(25f).Atlas().Rot(40f).Size(Lin(0, 0.5f, 1, 2.2f)).Alpha(0, 0, 0.1f, 0.7f, 1, 0.4f).Erosion(Lin(0, 0.2f, 1, 1f), Lin(0, 0.35f, 0.4f, 0f));
            Ps(t, "Haze", m.haze, 0, 5f, new Vector2(0.6f, 0.9f), new Vector2(0.3f, 0.6f), new Vector2(0.8f, 1.4f))
                .Cone(15f).Alpha(0, 0, 0.2f, 1, 1, 0);
            Ps(t, "Arcs", m.arcHusk, 0, 9f, new Vector2(0.05f, 0.09f), Vector2.zero, new Vector2(0.5f, 0.9f), world: false, fudge: -3)
                .Rot().Alpha(0, 1, 1, 0).Erosion(Lin(0, 0, 1, 0.7f), Lin(0, 1.2f, 1, 0.8f));
            var light = FxLight(t, new Color(1f, 0.38f, 0.55f), 7f, 0.1f);
            return Finish(root, light, 3000f, 0.05f, 1.2f, loop: true, sustain: 5000f);
        }

        /// HUSK energy bolt on the player: magenta pop, an expanding ripple ring, crackling arcs, magenta sparks.
        static HitFXInstance BuildEnergy(Mats m)
        {
            var root = new GameObject("FX_Hit_Energy"); var t = root.transform;
            Ps(t, "Core", m.coreHusk, 1, 0, new Vector2(0.08f, 0.10f), Vector2.zero, new Vector2(1.3f, 1.3f), world: false, fudge: -4)
                .Size(Lin(0, 0.55f, 0.3f, 1f, 1, 1.2f)).Alpha(0, 1, 0.5f, 0.8f, 1, 0).Erosion(Lin(0, 0, 1, 0.45f), Lin(0, 1.2f, 1, 0.7f));
            Ps(t, "Ripple", m.rippleHusk, 1, 0, new Vector2(0.32f, 0.32f), Vector2.zero, new Vector2(2.4f, 2.4f), world: false, fudge: -2)
                .Size(Lin(0, 0.15f, 0.4f, 0.75f, 1, 1f)).Erosion(Lin(0, 0, 0.5f, 0.1f, 1, 1f), Lin(0, 1.2f, 1, 0.5f));
            Ps(t, "Arcs", m.arcHusk, 4, 0, new Vector2(0.06f, 0.16f), Vector2.zero, new Vector2(0.6f, 1.2f), world: false, fudge: -3)
                .Rot().Alpha(0, 1, 1, 0).Erosion(Lin(0, 0, 1, 0.7f), Lin(0, 1.2f, 1, 0.8f));
            Ps(t, "SparkBurst", m.burstHusk, 1, 0, new Vector2(0.07f, 0.07f), Vector2.zero, new Vector2(1.2f, 1.2f), world: false, fudge: -3)
                .Rot().Size(Lin(0, 0.7f, 1, 1.2f)).Alpha(0, 1, 1, 0).Erosion(Lin(0, 0, 1, 0.6f), Lin(0, 1.3f, 1, 0.8f));
            Ps(t, "Sparks", m.sparkHusk, 26, 0, new Vector2(0.15f, 0.4f), new Vector2(5f, 16f), new Vector2(0.008f, 0.018f), fudge: -1)
                .Cone(70f).Stretch(0.05f, 2.0f).Drag(2.5f).Alpha(0, 1, 0.7f, 1, 1, 0).Erosion(null, Lin(0, 1.2f, 1, 0.5f));
            Ps(t, "Shock", m.ringHusk, 1, 0, new Vector2(0.25f, 0.25f), Vector2.zero, new Vector2(2.4f, 2.4f), world: false)
                .Size(Lin(0, 0.08f, 0.35f, 0.7f, 1, 1f)).Alpha(0, 1, 1, 0);
            var light = FxLight(t, new Color(1f, 0.18f, 0.45f), 8f, 0.15f);
            return Finish(root, light, 9000f, 0.06f, 1.2f);
        }

        static HitFXLibrary BuildLibrary()
        {
            var m = BuildMaterials();
            var kinetic = BuildKinetic(m);
            var explosion = BuildExplosion(m);
            var laser = BuildLaserBurn(m);
            var energy = BuildEnergy(m);

            Directory.CreateDirectory(Path.GetDirectoryName(LibPath));
            var lib = AssetDatabase.LoadAssetAtPath<HitFXLibrary>(LibPath);
            if (lib == null) { lib = ScriptableObject.CreateInstance<HitFXLibrary>(); AssetDatabase.CreateAsset(lib, LibPath); }
            lib.overlayMaterial = m.overlay;
            lib.entries = new[]
            {
                new HitFXEntry { kind = HitKind.Kinetic, prefab = kinetic, overlayColor = new Color(6f, 3.4f, 1.6f), overlaySize = 0.6f, overlayFlash = 0.45f,
                                 shake = 0.18f, shakeRange = 25f, hitStop = 0.035f },
                new HitFXEntry { kind = HitKind.Explosion, prefab = explosion, overlayColor = new Color(8f, 3.6f, 1.2f), overlaySize = 1.6f, overlayFlash = 0.9f,
                                 shake = 0.7f, shakeRange = 45f, hitStop = 0.06f, screenFlash = 0.6f },
                new HitFXEntry { kind = HitKind.LaserBurn, prefab = laser, overlayColor = new Color(6f, 0.5f, 1.8f), overlaySize = 0.55f, overlayFlash = 0.2f,
                                 shake = 0.05f, shakeRange = 15f },
                new HitFXEntry { kind = HitKind.EnergyBolt, prefab = energy, overlayColor = new Color(7f, 0.6f, 2.4f), overlaySize = 0.8f, overlayFlash = 0.6f,
                                 shake = 0.35f, shakeRange = 20f, hitStop = 0.05f },
            };
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            HitFX.Library = lib;
            Debug.Log("[OYM-HitFX] library " + LibPath);
            return lib;
        }

        // ------------------------------------------------------------------------------------------------ test scene
        static VolumeProfile TestVolume()
        {
            AssetDatabase.DeleteAsset(VolumePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumePath);
            T Add<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(false); c.name = typeof(T).Name; AssetDatabase.AddObjectToAsset(c, profile); return c;
            }
            const float ev = 9f;   // dark map, same as the character tests
            var ve = Add<VisualEnvironment>();
            ve.skyType.Override((int)SkyType.HDRI); ve.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            var hs = Add<HDRISky>();
            hs.hdriSky.Override(AssetDatabase.LoadAssetAtPath<Cubemap>(OYMSetup.SkyPath));
            hs.skyIntensityMode.Override(SkyIntensityMode.Exposure); hs.exposure.Override(ev - 0.6f);
            var ex = Add<Exposure>(); ex.mode.Override(ExposureMode.Fixed); ex.fixedExposure.Override(ev);
            var bloom = Add<Bloom>(); bloom.intensity.Override(0.3f); bloom.scatter.Override(0.72f); bloom.threshold.Override(0f); bloom.quality.Override(2);
            var tm = Add<Tonemapping>(); tm.mode.Override(TonemappingMode.ACES);
            var vig = Add<Vignette>(); vig.intensity.Override(0.22f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static void DirLight(string name, float lux, Color c, Vector3 euler, bool shadows)
        {
            var go = new GameObject(name);
            go.AddHDLight(LightType.Directional);
            var l = go.GetComponent<Light>();
            l.lightUnit = LightUnit.Lux; l.intensity = lux; l.color = c; l.useColorTemperature = false;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            go.transform.rotation = Quaternion.Euler(euler);
        }

        static Material LitMat(string name, Color baseCol, float metal, float smooth)
        {
            var m = Mat(name, "HDRP/Lit");
            m.SetColor("_BaseColor", baseCol); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            HDShaderUtils.ResetMaterialKeywords(m);
            return m;
        }

        static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>().Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static GameObject PlaceCentered(string prefabPath, string name, Vector3 center, Quaternion rot)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) throw new Exception("missing prefab " + prefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = name;
            go.transform.SetPositionAndRotation(Vector3.zero, rot);
            var b = RendererBounds(go);
            go.transform.position = center - b.center;
            return go;
        }

        /// Mesh colliders on the LOD0 meshes (or every mesh without a LODGroup): hit points land on the real hull.
        static void AddMeshColliders(GameObject go)
        {
            var lg = go.GetComponentInChildren<LODGroup>();
            IEnumerable<Renderer> rs = lg != null && lg.GetLODs().Length > 0 ? lg.GetLODs()[0].renderers : go.GetComponentsInChildren<MeshRenderer>();
            foreach (var r in rs)
            {
                var mf = r ? r.GetComponent<MeshFilter>() : null;
                if (mf == null || mf.sharedMesh == null) continue;
                var mc = r.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh;
            }
        }

        static void BuildTestScene(HitFXLibrary lib)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var gv = new GameObject("Global Volume (dark map)");
            var vol = gv.AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = TestVolume();
            DirLight("Key (planetshine)", 1500f, new Color(0.80f, 0.86f, 1.0f), new Vector3(35, 150, 0), true);
            DirLight("Rim (star)", 4000f, new Color(0.65f, 0.85f, 1.0f), new Vector3(20, -20, 0), false);

            var deck = GameObject.CreatePrimitive(PrimitiveType.Plane);
            deck.name = "Deck"; deck.transform.localScale = new Vector3(8, 1, 8);
            deck.GetComponent<MeshRenderer>().sharedMaterial = LitMat("M_HitFX_Deck", new Color(0.05f, 0.052f, 0.058f), 0.15f, 0.28f);

            // PRISM (kinetic target + beam emitter)
            var prism = PlaceCentered(OYMEnemies.PrefabDir + "/prism_body.prefab", "PRISM", new Vector3(-2.8f, 2.0f, 0.5f), Quaternion.Euler(0, 20, 0));
            var pb = RendererBounds(prism);
            AddMeshColliders(prism);
            prism.AddComponent<HitFlash>();

            // hull plate (explosion + laser burn target)
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Hull Plate";
            plate.transform.SetPositionAndRotation(new Vector3(3.2f, 1.7f, 1.2f), Quaternion.Euler(0, -28, 0));
            plate.transform.localScale = new Vector3(4.2f, 3.2f, 0.35f);
            plate.GetComponent<MeshRenderer>().sharedMaterial = LitMat("M_HitFX_Hull", new Color(0.22f, 0.22f, 0.23f), 0.75f, 0.42f);
            plate.AddComponent<HitFlash>();

            // KESTREL (energy bolt target), facing the camera
            var kestrel = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(OYMKestrel.PrefabPath));
            kestrel.name = "KESTREL";
            kestrel.transform.SetPositionAndRotation(new Vector3(0.3f, 0f, -1.0f), Quaternion.Euler(0, 200, 0));
            foreach (var mb in kestrel.GetComponents<MonoBehaviour>())
                if (mb is KestrelController) mb.enabled = false;
            var kc = kestrel.AddComponent<CapsuleCollider>(); kc.center = new Vector3(0, 1.0f, 0); kc.height = 1.8f; kc.radius = 0.22f;
            kestrel.AddComponent<HitFlash>();

            // PRISM's beam onto the plate
            var beamGo = new GameObject("PRISM Beam");
            Vector3 lens = pb.center + new Vector3(0.25f, pb.extents.y * 0.25f, 0f);
            Vector3 burn = plate.transform.position + plate.transform.right * 0.6f + Vector3.up * 0.4f - plate.transform.forward * 0.2f;
            beamGo.transform.SetPositionAndRotation(lens, Quaternion.LookRotation(burn - lens));
            var lr = beamGo.AddComponent<LineRenderer>();
            lr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_FX_Beam_Husk.mat");
            lr.textureMode = LineTextureMode.Tile;
            lr.alignment = LineAlignment.View;
            lr.useWorldSpace = true; lr.positionCount = 2; lr.numCapVertices = 0;
            lr.widthMultiplier = 0.3f;
            lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;
            lr.enabled = false;
            var beam = beamGo.AddComponent<BeamFX>();
            beam.origin = beamGo.transform; beam.line = lr;
            beam.impactPrefab = lib.Get(HitKind.LaserBurn).prefab;
            beam.mask = ~0;

            // camera
            var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.05f; cam.farClipPlane = 80000f; cam.fieldOfView = 45f;
            var hdc = camGo.AddComponent<HDAdditionalCameraData>();
            hdc.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(0f, 2.2f, -9f);
            camGo.transform.rotation = Quaternion.LookRotation(new Vector3(0, 1.6f, 0.5f) - camGo.transform.position);
            var fly = camGo.AddComponent<FreeFlyCamera>(); fly.speed = 5f; fly.boost = 4f;   // a 10 m test bench, not the debris field
            camGo.AddComponent<HitScreenFX>();

            var demo = new GameObject("HitFX Demo").AddComponent<HitFXDemo>();
            demo.kineticTarget = prism.transform; demo.explosionTarget = plate.transform; demo.energyTarget = kestrel.transform;
            demo.shooter = camGo.transform; demo.beam = beam;

            EditorSceneManager.SaveScene(scene, TestScene);
            Debug.Log("[OYM-HitFX] test scene " + TestScene);
        }

        // ------------------------------------------------------------------------------------------------ capture
        class Shot
        {
            public string name; public HitKind kind; public Transform target; public Vector3 from;
            public Vector3 eyeOffset; public float fov; public float[] stills; public float gifSeconds; public float scale = 1f;
        }

        static PlayableGraph PoseKestrel(GameObject kestrel)
        {
            var anim = kestrel.GetComponentInChildren<Animator>();
            var clip = AssetDatabase.FindAssets("t:AnimationClip", new[] { OYMKestrel.AnimDir })
                .SelectMany(g => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)).OfType<AnimationClip>())
                .FirstOrDefault(c => c.name == "CombatIdle") ?? AssetDatabase.FindAssets("t:AnimationClip", new[] { OYMKestrel.AnimDir })
                .SelectMany(g => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)).OfType<AnimationClip>())
                .FirstOrDefault(c => c.name == "Idle");
            var graph = PlayableGraph.Create("hitfx-pose");
            if (anim == null || clip == null) return graph;
            var output = AnimationPlayableOutput.Create(graph, "out", anim);
            var cp = AnimationClipPlayable.Create(graph, clip);
            output.SetSourcePlayable(cp);
            cp.SetTime(0.6f); cp.SetTime(0.6f);
            graph.Evaluate(0f);
            return graph;
        }

        static void CaptureInner()
        {
            EditorSceneManager.OpenScene(TestScene);
            var lib = AssetDatabase.LoadAssetAtPath<HitFXLibrary>(LibPath);
            HitFX.Library = lib;
            var cam = Camera.main;
            var hdc = cam.GetComponent<HDAdditionalCameraData>();
            var screen = cam.GetComponent<HitScreenFX>();
            var prism = GameObject.Find("PRISM"); var plate = GameObject.Find("Hull Plate"); var kestrel = GameObject.Find("KESTREL");
            var beam = Object.FindFirstObjectByType<BeamFX>();
            var graph = PoseKestrel(kestrel);
            Physics.SyncTransforms();

            string stillDir = Path.Combine(OutDir, "stills");
            Directory.CreateDirectory(stillDir);
            const int W = 1600, H = 900;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            rt.Create(); cam.targetTexture = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            void Save(string path)
            {
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }

            Vector3 shooter = new Vector3(0f, 1.9f, -9f);
            var shots = new[]
            {
                new Shot { name = "kinetic", kind = HitKind.Kinetic, target = prism.transform, from = shooter,
                           eyeOffset = new Vector3(1.0f, 0.35f, 3.4f), fov = 40f, stills = new[] { 0.015f, 0.04f, 0.08f, 0.15f, 0.3f, 0.6f }, gifSeconds = 1.3f },
                new Shot { name = "explosion", kind = HitKind.Explosion, target = plate.transform, from = shooter,
                           eyeOffset = new Vector3(2.5f, 1.4f, 8.5f), fov = 50f, stills = new[] { 0.02f, 0.08f, 0.2f, 0.45f, 0.9f, 1.6f }, gifSeconds = 2.4f },
                new Shot { name = "energy", kind = HitKind.EnergyBolt, target = kestrel.transform, from = shooter,
                           eyeOffset = new Vector3(-1.1f, 0.25f, 2.6f), fov = 40f, stills = new[] { 0.015f, 0.05f, 0.1f, 0.2f, 0.35f, 0.6f }, gifSeconds = 1.2f },
                new Shot { name = "laser", kind = HitKind.LaserBurn, target = plate.transform, from = Vector3.zero,
                           eyeOffset = new Vector3(-2.2f, 0.6f, 3.4f), fov = 42f, stills = new[] { 0.3f, 0.8f, 1.4f }, gifSeconds = 2.0f },
            };

            // warm-up (SSGI / TAA history on a fresh process)
            for (int i = 0; i < 90; i++) cam.Render();

            foreach (var s in shots)
            {
                foreach (var hf in Object.FindObjectsByType<HitFlash>(FindObjectsSortMode.None)) hf.ResetAll();
                Vector3 p, n;
                if (s.kind == HitKind.LaserBurn)
                {
                    var ray = new Ray(beam.origin.position, beam.origin.forward);
                    if (!HitFXDemo.RaycastTarget(s.target, ray, out var bh)) throw new Exception("beam misses the plate");
                    p = bh.point; n = bh.normal;
                }
                else
                {
                    var c = HitFXDemo.Bounds(s.target).center + (s.kind == HitKind.EnergyBolt ? Vector3.up * 0.35f : Vector3.zero);
                    if (!HitFXDemo.RaycastTarget(s.target, new Ray(s.from, (c - s.from).normalized), out var hit)) throw new Exception("no hit on " + s.target.name);
                    p = hit.point; n = hit.normal;
                }
                // camera: in front of the surface, offset by eyeOffset in the surface frame (x right, y up, z along normal)
                var frame = Quaternion.LookRotation(n, Vector3.up);
                Vector3 eye = p + frame * s.eyeOffset;
                var camRot = Quaternion.LookRotation(p + n * (s.kind == HitKind.Explosion ? 1.2f : 0.25f) - eye, Vector3.up);
                cam.fieldOfView = s.fov;
                var entry = lib.Get(s.kind);
                var flash = s.target.GetComponent<HitFlash>();

                // ---------------- stills
                if (s.kind == HitKind.LaserBurn)
                {
                    foreach (float t in s.stills)
                    {
                        beam.firing = false; for (int i = 0; i < 30; i++) beam.Step(0.02f);   // fully off + impact released
                        foreach (var hf in Object.FindObjectsByType<HitFlash>(FindObjectsSortMode.None)) hf.ResetAll();
                        beam.firing = true;
                        for (float tt = 0f; tt < t; tt += 0.0125f) { beam.Step(0.0125f); if (flash) flash.Tick(0.0125f); }
                        cam.transform.SetPositionAndRotation(eye, camRot);
                        for (int i = 0; i < 10; i++) cam.Render();
                        Save(Path.Combine(stillDir, $"{s.name}_{Mathf.RoundToInt(t * 1000):0000}.png"));
                    }
                    beam.firing = false; for (int i = 0; i < 30; i++) beam.Step(0.02f);
                }
                else
                {
                    var inst = Object.Instantiate(entry.prefab);
                    inst.transform.SetPositionAndRotation(p, Quaternion.LookRotation(n));
                    foreach (var ps in inst.systems) { ps.useAutoRandomSeed = false; ps.randomSeed = 1234u + (uint)ps.name.Length; }
                    foreach (float t in s.stills)
                    {
                        inst.PreviewAt(t);
                        if (flash)
                        {
                            flash.ResetAll();
                            flash.AddHit(p, entry.overlayColor, entry.overlaySize, entry.overlayFlash);
                            for (float tt = 0f; tt < t; tt += 1f / 240f) flash.Tick(1f / 240f);
                        }
                        cam.transform.SetPositionAndRotation(eye, camRot);
                        for (int i = 0; i < 10; i++) cam.Render();
                        Save(Path.Combine(stillDir, $"{s.name}_{Mathf.RoundToInt(t * 1000):0000}.png"));
                    }
                    Object.DestroyImmediate(inst.gameObject);
                }
                Debug.Log("[OYM-HitFX] stills " + s.name);

                // ---------------- GIF frames (20 fps, 4 sim ticks per frame, camera shake + screen flash on)
                string fdir = Path.Combine(OutDir, "frames_" + s.name);
                if (Directory.Exists(fdir)) Directory.Delete(fdir, true);
                Directory.CreateDirectory(fdir);
                foreach (var hf in Object.FindObjectsByType<HitFlash>(FindObjectsSortMode.None)) hf.ResetAll();
                if (screen) screen.EnsureVolumes();
                HitFXInstance gi = null;
                const float pre = 0.15f, dt = 0.0125f;
                int frames = Mathf.RoundToInt((pre + s.gifSeconds) / 0.05f);
                float simT = -pre, clock = 0f;
                bool fired = false;
                for (int f = 0; f < frames; f++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        simT += dt; clock += dt;
                        if (!fired && simT >= 0f)
                        {
                            fired = true;
                            if (s.kind == HitKind.LaserBurn) beam.firing = true;
                            else
                            {
                                gi = Object.Instantiate(entry.prefab);
                                gi.transform.SetPositionAndRotation(p, Quaternion.LookRotation(n));
                                gi.Play();
                                if (flash) flash.AddHit(p, entry.overlayColor, entry.overlaySize, entry.overlayFlash);
                                float dist = Vector3.Distance(eye, p);
                                float kd = Mathf.Clamp01(1f - dist / entry.shakeRange);
                                if (screen)
                                {
                                    screen.AddTrauma(entry.shake * kd);
                                    screen.AddFlash(entry.screenFlash * kd);
                                    if (s.kind == HitKind.EnergyBolt) screen.AddDamage(0.6f);
                                }
                            }
                        }
                        if (s.kind == HitKind.LaserBurn)
                        {
                            if (simT > s.gifSeconds - 0.5f) beam.firing = false;
                            beam.Step(dt);
                        }
                        if (gi) gi.Tick(dt);
                        if (flash) flash.Tick(dt);
                        cam.transform.SetPositionAndRotation(eye, camRot);
                        if (screen) screen.Step(dt, clock);
                    }
                    cam.Render(); cam.Render();
                    Save(Path.Combine(fdir, $"{f:000}.png"));
                }
                if (gi) Object.DestroyImmediate(gi.gameObject);
                beam.firing = false; for (int i = 0; i < 30; i++) beam.Step(0.02f);
                if (screen) { screen.Step(10f, clock + 10f); }
                Debug.Log($"[OYM-HitFX] {frames} GIF frames {s.name}");
            }

            // overview still: all four at once, from the scene camera
            {
                foreach (var hf in Object.FindObjectsByType<HitFlash>(FindObjectsSortMode.None)) hf.ResetAll();
                var camGo = cam.gameObject;
                cam.fieldOfView = 45f;
                camGo.transform.SetPositionAndRotation(new Vector3(0f, 2.2f, -9f), Quaternion.LookRotation(new Vector3(0, 1.6f, 0.5f) - new Vector3(0f, 2.2f, -9f)));
                var spawned = new List<HitFXInstance>();
                void At(HitKind kind, Transform c, float t, Vector3 aimOff)
                {
                    var e = lib.Get(kind);
                    var from = camGo.transform.position;
                    if (!HitFXDemo.RaycastTarget(c, new Ray(from, (HitFXDemo.Bounds(c).center + aimOff - from).normalized), out var h)) return;
                    var i2 = Object.Instantiate(e.prefab); i2.transform.SetPositionAndRotation(h.point, Quaternion.LookRotation(h.normal));
                    i2.PreviewAt(t); spawned.Add(i2);
                    var hf2 = c.GetComponentInParent<HitFlash>();
                    if (hf2) { hf2.AddHit(h.point, e.overlayColor, e.overlaySize, e.overlayFlash); for (float tt = 0; tt < t; tt += 1f / 240f) hf2.Tick(1f / 240f); }
                }
                beam.firing = true; for (float tt = 0f; tt < 0.6f; tt += 0.0125f) { beam.Step(0.0125f); var bf = plate.GetComponent<HitFlash>(); bf.Tick(0.0125f); }
                At(HitKind.Kinetic, prism.transform, 0.05f, Vector3.zero);
                At(HitKind.EnergyBolt, kestrel.transform, 0.06f, Vector3.up * 0.35f);
                At(HitKind.Explosion, plate.transform, 0.12f, new Vector3(-0.8f, -0.6f, 0f));
                for (int i = 0; i < 12; i++) cam.Render();
                Save(Path.Combine(OutDir, "overview.png"));
                foreach (var s2 in spawned) Object.DestroyImmediate(s2.gameObject);
                beam.firing = false; for (int i = 0; i < 30; i++) beam.Step(0.02f);
            }

            graph.Destroy();
            cam.targetTexture = null; rt.Release();
            Debug.Log("[OYM-HitFX] capture -> " + OutDir);
        }
    }
}
