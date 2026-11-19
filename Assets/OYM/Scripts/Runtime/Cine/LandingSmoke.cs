using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace OYM.Cine
{
    /// Touchdown burst ("퐈아아아"): the booster wash slams the deck and throws a ring of smoke that rolls out along the
    /// surface, slows down and keeps swelling for seconds, plus a core puff, hot grit and a light flash in the pilot's
    /// colour. All particles are emitted by hand (EmitParams) into three world-space systems, so one component serves
    /// any number of bursts. Burst(point, normal, strength, tint, fly) — fly = the arrival direction (the ring is thrown
    /// further forward).
    public class LandingSmoke : MonoBehaviour
    {
        public static LandingSmoke Instance { get; private set; }

        public Material smokeMaterial, gritMaterial;
        [Header("Ring (rolls out along the deck)")]
        public int ringCount = 72;
        public Vector2 ringSpeed = new Vector2(7f, 15f);
        public Vector2 ringSize = new Vector2(0.9f, 1.7f);
        public Vector2 ringLife = new Vector2(3.2f, 5.5f);
        [Header("Core puff")]
        public int puffCount = 16;
        public Vector2 puffSpeed = new Vector2(1.5f, 4.5f);
        public Vector2 puffSize = new Vector2(1.6f, 2.6f);
        [Header("Haze (lingers)")]
        public int hazeCount = 8;
        [Header("Grit")]
        public int gritCount = 40;
        [Header("Flash light")]
        public float flashLumen = 60000f;
        public float flashDecay = 0.22f;

        ParticleSystem _smoke, _grit;
        Light _light;
        HDAdditionalLightData _hd;
        float _flash, _lumen;
        readonly List<Vector4> _custom = new List<Vector4>();

        public static LandingSmoke Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("LandingSmoke");
            return go.AddComponent<LandingSmoke>();
        }

        void Awake()
        {
            Instance = this;
            Build();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        // ------------------------------------------------------------------------------------------------ setup
        static Material LoadMat(string res, string shader, System.Action<Material> fill)
        {
            var m = Resources.Load<Material>("OYM_Cine/" + res);
            if (m) return m;
            var sh = Shader.Find(shader);
            if (!sh) { Debug.LogWarning("[OYM-CINE] shader missing: " + shader); return null; }
            m = new Material(sh) { name = res + " (runtime)" };
            fill(m);
            return m;
        }

        static Texture2D EditorTex(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
#else
            return null;
#endif
        }

        void Build()
        {
            if (!smokeMaterial)
                smokeMaterial = LoadMat("FX_CineSmoke", "OYM/FX/SmokeLit", m =>
                {
                    m.SetTexture("_MainTex", EditorTex("Assets/OYM/FX/Hit/Textures/T_FX_Smoke_2x2.png"));
                    m.SetTexture("_NoiseTex", EditorTex("Assets/OYM/FX/Hit/Textures/T_FX_Noise_Tile.png"));
                });
            if (!gritMaterial)
                gritMaterial = LoadMat("FX_CineGrit", "OYM/FX/AdditiveHDR", m =>
                {
                    var t = EditorTex("Assets/OYM/FX/fx_glow.png");
                    if (t) m.SetTexture("_MainTex", t);
                    m.SetFloat("_Intensity", 6f);
                    m.SetColor("_Tint", new Color(1f, 0.9f, 0.75f, 1f));
                });
            // per-scene copies: the sun direction is written into the material, never into the asset
            if (smokeMaterial) smokeMaterial = new Material(smokeMaterial);
            if (gritMaterial) gritMaterial = new Material(gritMaterial);
            SunToMaterial();

            _smoke = MakeSystem("smoke", smokeMaterial, true);
            _grit = MakeSystem("grit", gritMaterial, false);

            var lg = new GameObject("flash light");
            lg.transform.SetParent(transform, false);
            _light = lg.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.range = 14f;
            _light.shadows = LightShadows.None;
            _hd = lg.TryGetComponent<HDAdditionalLightData>(out var hd) ? hd : lg.AddComponent<HDAdditionalLightData>();
            _light.enabled = false;
        }

        /// The smoke is lit by the scene's strongest directional light (direction + tint; strength is art-directed).
        void SunToMaterial()
        {
            if (!smokeMaterial) return;
            Light sun = null;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.isActiveAndEnabled && (!sun || l.intensity > sun.intensity)) sun = l;
            if (!sun) return;
            smokeMaterial.SetVector("_SunDir", -sun.transform.forward);
            Color c = sun.color; float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b), 1e-3f);
            smokeMaterial.SetColor("_SunColor", new Color(c.r / m, c.g / m, c.b / m) * 0.85f);
        }

        ParticleSystem MakeSystem(string n, Material mat, bool smoke)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;                                            // stays alive: particles are only emitted by hand
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 2000;
            main.startSpeed = 0f;

            main.gravityModifier = 0f;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;

            var lv = ps.limitVelocityOverLifetime;                       // drag: the burst slows down into a slow swell
            lv.enabled = true;
            lv.drag = smoke ? 1.9f : 1.2f;
            lv.multiplyDragByParticleSize = false;
            lv.multiplyDragByParticleVelocity = false;
            lv.limit = 1000f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            if (smoke)
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.03f), new GradientAlphaKey(0.85f, 0.35f), new GradientAlphaKey(0f, 1f) });
            else
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.55f, 0.25f), 0.5f), new GradientColorKey(new Color(0.6f, 0.2f, 0.1f), 1f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = smoke ? new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.35f, 0f, 5f), new Keyframe(0.25f, 0.75f, 1f, 1f), new Keyframe(1f, 1f, 0.15f, 0f)))
                            : new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

            if (smoke)
            {
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
                // Custom1: x = erosion (thins out at the end), y = inner glow (thruster wash, first ~0.3 s)
                var cd = ps.customData;
                cd.enabled = true;
                cd.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
                cd.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 2);
                cd.SetVector(ParticleSystemCustomData.Custom1, 0, new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.45f, 0.08f), new Keyframe(1f, 0.85f))));
                cd.SetVector(ParticleSystemCustomData.Custom1, 1, new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f, 0f, -14f), new Keyframe(0.12f, 0.12f, -1.5f, -0.4f), new Keyframe(1f, 0f))));
            }

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = smoke ? ParticleSystemSortMode.Distance : ParticleSystemSortMode.None;
            r.minParticleSize = 0f; r.maxParticleSize = 20f;
            if (smoke)
            {
                r.renderMode = ParticleSystemRenderMode.Billboard;
                r.alignment = ParticleSystemRenderSpace.View;
                r.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
                {
                    ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color,
                    ParticleSystemVertexStream.UV, ParticleSystemVertexStream.Custom1XY, ParticleSystemVertexStream.StableRandomX,
                    ParticleSystemVertexStream.Tangent,
                });
            }
            else
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.045f; r.lengthScale = 1.5f;
            }
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------------------------------------ burst
        /// One touchdown. `strength` ~1 (KESTREL 0.85, ORBIT FRAME 1.25), `tint` = booster colour (glow + light),
        /// `fly` = arrival velocity direction (optional): the ring is thrown further along it.
        public void Burst(Vector3 point, Vector3 normal, float strength, Color tint, Vector3 fly = default, int seed = 0)
        {
            if (!_smoke) return;
            var rnd = new System.Random(seed != 0 ? seed : Random.Range(1, int.MaxValue));
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            Vector3 n = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up;
            Vector3 t1 = Vector3.Cross(n, Mathf.Abs(n.y) < 0.95f ? Vector3.up : Vector3.right).normalized, t2 = Vector3.Cross(n, t1);
            Vector3 flyP = Vector3.ProjectOnPlane(fly, n);
            float flyK = Mathf.Clamp01(flyP.magnitude / 20f);
            if (flyP.sqrMagnitude > 1e-4f) flyP.Normalize();
            Color glowC = new Color(tint.r, tint.g, tint.b, 1f);
            float s = Mathf.Max(0.2f, strength);

            // ring: radial along the deck, a little lift, thrown further along the arrival direction
            int ring = Mathf.RoundToInt(ringCount * Mathf.Lerp(0.7f, 1.2f, Mathf.InverseLerp(0.6f, 1.4f, s)));
            for (int i = 0; i < ring; i++)
            {
                float a = (i + R(-0.4f, 0.4f)) / ring * Mathf.PI * 2f;
                Vector3 dir = t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a);
                float fwd = flyP.sqrMagnitude > 0f ? Mathf.Max(0f, Vector3.Dot(dir, flyP)) : 0f;
                float spd = R(ringSpeed.x, ringSpeed.y) * s * (1f + 0.6f * fwd * flyK);
                var ep = new ParticleSystem.EmitParams
                {
                    position = point + dir * R(0.2f, 0.6f) + n * R(0.15f, 0.5f),
                    velocity = dir * spd + n * R(0.3f, 1.6f) * s,
                    startSize = R(ringSize.x, ringSize.y) * Mathf.Sqrt(s) * 2.6f,
                    startLifetime = R(ringLife.x, ringLife.y),
                    rotation = R(0f, 360f),
                    startColor = glowC,
                    randomSeed = (uint)rnd.Next(),
                };
                _smoke.Emit(ep, 1);
            }
            // core puff: up and out of the impact, slower, bigger
            for (int i = 0; i < puffCount; i++)
            {
                Vector3 dir = (n * R(0.6f, 1.2f) + (t1 * R(-1f, 1f) + t2 * R(-1f, 1f)) * 0.7f).normalized;
                var ep = new ParticleSystem.EmitParams
                {
                    position = point + n * R(0.3f, 1.0f) + (t1 * R(-0.4f, 0.4f) + t2 * R(-0.4f, 0.4f)),
                    velocity = dir * R(puffSpeed.x, puffSpeed.y) * s,
                    startSize = R(puffSize.x, puffSize.y) * Mathf.Sqrt(s) * 2.2f,
                    startLifetime = R(2.6f, 4.2f),
                    rotation = R(0f, 360f),
                    startColor = glowC,
                    randomSeed = (uint)rnd.Next(),
                };
                _smoke.Emit(ep, 1);
            }
            // haze: a few huge faint ones that linger and drift (no glow)
            for (int i = 0; i < hazeCount; i++)
            {
                float a = R(0f, Mathf.PI * 2f);
                Vector3 dir = t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a);
                var ep = new ParticleSystem.EmitParams
                {
                    position = point + dir * R(1f, 2.5f) + n * R(0.6f, 1.4f),
                    velocity = dir * R(2f, 4f) * s + n * R(0.2f, 0.6f),
                    startSize = R(4.5f, 7f) * Mathf.Sqrt(s),
                    startLifetime = R(6f, 8.5f),
                    rotation = R(0f, 360f),
                    startColor = new Color(0f, 0f, 0f, 0.45f),
                    randomSeed = (uint)rnd.Next(),
                };
                _smoke.Emit(ep, 1);
            }
            // grit: hot specks skipping off the deck
            for (int i = 0; i < Mathf.RoundToInt(gritCount * s); i++)
            {
                float a = R(0f, Mathf.PI * 2f);
                Vector3 dir = (t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a)) + n * R(0.05f, 0.7f);
                var ep = new ParticleSystem.EmitParams
                {
                    position = point + n * 0.2f,
                    velocity = dir.normalized * R(9f, 24f) * s,
                    startSize = R(0.03f, 0.08f),
                    startLifetime = R(0.35f, 1.1f),
                    startColor = Color.Lerp(Color.white, tint, 0.35f),
                    randomSeed = (uint)rnd.Next(),
                };
                _grit.Emit(ep, 1);
            }
            // light: the wash lights the deck and the pilot's legs for a moment
            _light.transform.position = point + n * 0.8f;
            _light.color = tint;
            _lumen = flashLumen * s * ExposureScale();
            _flash = 1f;
            _light.enabled = true;
        }

        static float ExposureScale()
        {
            // same convention as JetpackFX: lumens at EV 9, scaled to the scene's fixed exposure
            foreach (var v in FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None))
                if (v.isGlobal && v.sharedProfile && v.sharedProfile.TryGet<Exposure>(out var ex) && ex.mode.value == ExposureMode.Fixed)
                    return Mathf.Pow(2f, ex.fixedExposure.value - 9f);
            return 1f;
        }

        void Update()
        {
            if (!_light || !_light.enabled) return;
            _flash *= Mathf.Exp(-Time.deltaTime / flashDecay);
            if (_flash < 0.003f) { _light.enabled = false; return; }
            _light.intensity = _lumen * _flash;
        }
    }
}
