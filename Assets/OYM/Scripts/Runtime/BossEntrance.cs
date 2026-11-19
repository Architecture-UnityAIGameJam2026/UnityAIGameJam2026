using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OYM
{
    /// CITADEL entrance in the playable showcase: the 300 m boss rises out of the debris field in front of the
    /// characters, eclipses the planet, halts with a "쿵" right in front of them, lights its sensors one by one and
    /// roars (HDR flash), then the game camera comes back with the boss hanging there.
    ///
    /// Everything is a pure function of the entrance clock (Evaluate(t)): play mode drives it in real time (hit-stop
    /// freezes the world, not the cinematic), batch stills (OYMBossEntrance.Preview) call Evaluate(t) directly.
    /// Stage frame at the start: O = centre of the characters, F = horizontal direction to the planet (the boss comes
    /// in over the planet), U = up, R = right.
    /// Scale reads through slowness: everything about the hull is slow and heavy (angular speed ~7 deg/s on screen).
    ///   0.0  rumble, letterbox, camera eases out of the game view into an over-the-shoulder at the planet
    ///   0.4  the boss surfaces from below the debris, nose up, accelerating gently, decelerating for seconds
    ///   6.4  HALT: residual momentum carries it ~5 m on, the bow dips and slowly settles over ~3 s - cut to a
    ///        worm's-eye at the hero's heels, slow heavy heave, the world runs at half speed for a moment, the bow
    ///        wave shoves the drifting rocks
    ///   7.3  sensors ignite one by one in the dark
    ///   9.3  ROAR: sensors flash (EV 20), claws open over 1.2 s, launchers swing over 1.6 s, bloom / exposure punch
    ///  10.4  wide: tiny characters, the boss over the planet, crane back
    ///  12.4  blend back to the game camera (aimed at the boss), letterbox out; control returns at 13.4
    [DefaultExecutionOrder(200)]                          // after FlightCamera (100): overrides the pose it just set
    public class BossEntrance : MonoBehaviour
    {
        // a 300 m hull moves slowly: ~6 s to surface and come to station, heavy multi-second settle, slow claws
        public const float TAppear = 0.4f, THit = 6.4f, TIgnite = 7.3f, TRoar = 9.3f, TWide = 10.4f, TBack = 12.4f, TEnd = 13.4f;
        public const float SensorGap = 0.32f;
        public const float TLaunch0 = 1.55f, TLaunch1 = 3.65f;      // cut to the launcher pods for the salvo, then back

        [Header("Cast")]
        public Transform boss;
        public PlayTestDirector director;
        public FlightCamera flightCamera;
        public Transform[] actors;
        public AudioSource audioSource;
        public AudioClip entranceClip;
        [Tooltip("Direction from the arena to the planet (OYMPlanet.PlanetDir); the boss arrives over it")]
        public Vector3 planetDir = new Vector3(-0.4951f, 0.1392f, 0.8576f);

        [Header("Staging (stage frame: F toward the planet, R right, U up)")]
        public float endDistance = 200f, endHeight = 38f, endSide = 40f, endYaw = 24f;
        public float lurch = 5f;
        const float SettleT = 1.0f;

        [Header("Missile salvo as it surfaces (HomingDartFX)")]
        public HomingDartFX dartPrefab;
        public float salvoStart = 1.8f, salvoGap = 0.11f;
        public float dartBlastScale = 2.2f, dartTrailSeconds = 1.1f;
        // flight times per dart (s) - mirrored in pipeline/boss_entrance/make_entrance_audio.py for the impact booms
        public static readonly float[] DartFlights = { 2.1f, 2.4f, 1.9f, 2.6f, 2.2f, 2.0f, 2.5f, 2.3f, 1.95f, 2.45f, 2.15f, 2.55f, 2.05f, 2.35f, 2.25f, 2.6f };

        [Header("Shock debris")]
        public Mesh debrisMesh;
        public Material debrisMaterial;
        public int debrisCount = 70;

        [Header("Play")]
        public bool playOnStart = true;
        public float autoStartDelay = 4f;

        // ---------------------------------------------------------------------------------------------- state
        public bool Playing { get; private set; }
        public bool Done { get; private set; }
        public float Clock { get; private set; }
        public Vector3 Stage => _O;

        Vector3 _O, _F, _R, _U = Vector3.up, _hero, _bow, _D, _Rd, _wormEye;
        Camera _cam;
        bool _prepared;
        readonly List<Material> _sensors = new List<Material>();
        readonly List<Material> _tubes = new List<Material>();
        readonly List<Material> _engines = new List<Material>();
        readonly List<(Transform t, Quaternion rest)> _claws = new List<(Transform, Quaternion)>();
        readonly List<(Transform t, Quaternion rest, float sign)> _launchers = new List<(Transform, Quaternion, float)>();
        readonly List<Light> _eyes = new List<Light>();
        Transform _head;
        Vector3 _headLocal;                                // head front in boss space
        readonly List<(Drift d, Vector3 dir, float amp)> _pushed = new List<(Drift, Vector3, float)>();
        readonly Dictionary<Drift, Vector3> _shoved = new Dictionary<Drift, Vector3>();
        readonly List<(Transform t, Vector3 p0, Vector3 v, Vector3 axis, float spin, float delay)> _chunks = new List<(Transform, Vector3, Vector3, Vector3, float, float)>();
        Transform _chunkRoot;
        Renderer[] _bossRenderers;
        sealed class Dart { public HomingDartFX fx; public float launchT, flight, ticked; public int pod, tube; public Vector3 target; public bool launched; }
        readonly List<Dart> _darts = new List<Dart>();
        readonly Vector3[] _podFace = new Vector3[2], _podExt = new Vector3[2];
        bool _pods;
        Transform _salvoRoot;
        Light _key;
        readonly List<Renderer> _near = new List<Renderer>();
        public const uint BossLightLayer = 1u << 1;                // boss-only key light; the arena keeps its look

        Volume _volume;
        VolumeProfile _profile;
        Bloom _bloom; ColorAdjustments _color; ChromaticAberration _chroma; Vignette _vignette; MotionBlur _blur; DepthFocusFade _fade;
        float _startAt = -1f, _cutBlurFrames;
        bool[] _wasScripted;
        bool _camWasScripted;
        float _shakeSeed;

        // ---------------------------------------------------------------------------------------------- setup
        public void Prepare()
        {
            if (_prepared) return;
            _prepared = true;
            _shakeSeed = 17.3f;
            if (!flightCamera) flightCamera = FindAnyObjectByType<FlightCamera>();
            _cam = flightCamera ? flightCamera.GetComponent<Camera>() : Camera.main;
            if (!director) director = FindAnyObjectByType<PlayTestDirector>();

            // animated material copies (sensors, launcher tubes, engine) so the scene assets stay untouched
            _bossRenderers = boss.GetComponentsInChildren<Renderer>(true);
            var copies = new Dictionary<Material, Material>();
            foreach (var r in _bossRenderers)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (!m) continue;
                    string n = m.name.ToLowerInvariant();
                    bool sensor = n.Contains("head") || n.Contains("claw"), tube = n.Contains("launcher") && !n.Contains("damaged"), engine = n.Contains("engine");
                    if (!sensor && !tube && !engine) continue;
                    if (!copies.TryGetValue(m, out var c))
                    {
                        c = new Material(m) { name = m.name + " (entrance)" };
                        copies[m] = c;
                        (sensor ? _sensors : tube ? _tubes : _engines).Add(c);
                    }
                    mats[i] = c;
                }
                r.sharedMaterials = mats;
            }

            foreach (var t in boss.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("citadel_claw_") && !t.name.Contains("_LOD")) _claws.Add((t, t.localRotation));
                if (t.name == "citadel_launcher_L" || t.name == "citadel_launcher_R") _launchers.Add((t, t.localRotation, t.name.EndsWith("L") ? 1f : -1f));
                if (t.name == "citadel_head") _head = t;
            }

            // eye lights at the front of the head: pink spill on the claws and the debris round the bow
            Bounds hb = default; bool any = false;
            foreach (var r in (_head ? _head : boss).GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.Contains("_LOD1") || r.name.Contains("_LOD2")) continue;
                if (!any) { hb = r.bounds; any = true; } else hb.Encapsulate(r.bounds);
            }
            Vector3 front = any ? hb.center + boss.forward * hb.extents.z * 0.85f : boss.position + boss.forward * 140f;
            _headLocal = boss.InverseTransformPoint(front);
            for (int k = -1; k <= 1; k += 2)
            {
                var go = new GameObject("EyeLight_" + (k < 0 ? "L" : "R"));
                go.transform.SetParent(boss, false);
                go.transform.position = front + boss.right * (k * (any ? hb.extents.x * 0.35f : 25f)) + boss.up * (any ? hb.extents.y * 0.1f : 5f);
                var hd = go.AddHDLight(LightType.Point);
                var l = go.GetComponent<Light>();
                l.color = new Color(1f, 0.16f, 0.36f);
                l.range = 140f;
                l.shadows = LightShadows.None;
                l.lightUnit = LightUnit.Lumen;
                l.intensity = 0f;
                hd.affectsVolumetric = false;
                _eyes.Add(l);
            }

            // launcher pods: tube face (+Z end of the pod in boss space) for the salvo
            _pods = false;
            for (int k = 0; k < 2; k++)
            {
                var pod = boss.GetComponentsInChildren<Transform>(true).FirstOrDefaultNamed(k == 0 ? "citadel_launcher_L" : "citadel_launcher_R");
                if (!pod) continue;
                Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
                foreach (var mf in pod.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!mf.sharedMesh || mf.name.Contains("_LOD1") || mf.name.Contains("_LOD2")) continue;
                    var b = mf.sharedMesh.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                        var q = boss.InverseTransformPoint(mf.transform.TransformPoint(corner));
                        mn = Vector3.Min(mn, q); mx = Vector3.Max(mx, q);
                    }
                }
                if (mn.x > mx.x) continue;
                _podFace[k] = new Vector3((mn.x + mx.x) * 0.5f, (mn.y + mx.y) * 0.5f, mx.z);
                _podExt[k] = (mx - mn) * 0.5f;
                _pods = true;
            }
            if (dartPrefab && _pods)
            {
                _salvoRoot = new GameObject("Salvo (darts)").transform;
                _salvoRoot.SetParent(transform, false);
                for (int i = 0; i < DartFlights.Length; i++)
                {
                    var fx = Instantiate(dartPrefab, _salvoRoot);
                    fx.name = "dart_" + i;
                    fx.blastScale = dartBlastScale; fx.trailSeconds = dartTrailSeconds;
                    fx.gameObject.SetActive(false);
                    _darts.Add(new Dart { fx = fx, flight = DartFlights[i], launchT = salvoStart + i * salvoGap, pod = i % 2, tube = i / 2 });
                }
            }

            // key light on the hull only (light layer 1): the sun grazes it from the side and the planetshine hits its
            // back, so the face toward the characters would be a black hole without it
            foreach (var r in _bossRenderers) if (r) r.renderingLayerMask |= BossLightLayer;
            var kg = new GameObject("BossKey (light layer 1)");
            kg.transform.SetParent(transform, false);
            var khd = kg.AddHDLight(LightType.Directional);
            _key = kg.GetComponent<Light>();
            _key.lightUnit = LightUnit.Lux; _key.intensity = 0f; _key.color = new Color(0.80f, 0.87f, 1.0f);
            _key.shadows = LightShadows.None;
            khd.lightlayersMask = (UnityEngine.Rendering.HighDefinition.RenderingLayerMask)BossLightLayer;
            khd.affectsVolumetric = false;

            // post: own global volume, weight 1 while playing
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "BossEntrance (runtime)";
            _bloom = _profile.Add<Bloom>(); _bloom.intensity.overrideState = true; _bloom.scatter.Override(0.75f);
            _color = _profile.Add<ColorAdjustments>(); _color.postExposure.overrideState = true;
            _chroma = _profile.Add<ChromaticAberration>(); _chroma.intensity.overrideState = true;
            _vignette = _profile.Add<Vignette>(); _vignette.intensity.overrideState = true; _vignette.smoothness.Override(0.5f);
            _blur = _profile.Add<MotionBlur>(); _blur.intensity.overrideState = true;
            _fade = _profile.Add<DepthFocusFade>();
            _fade.startDistance.Override(420f); _fade.endDistance.Override(5200f);       // the boss stays crisp
            _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true; _volume.priority = 60f; _volume.weight = 0f; _volume.sharedProfile = _profile;

            if (debrisMesh && debrisMaterial) BuildChunks();
            if (audioSource && entranceClip) { audioSource.clip = entranceClip; audioSource.playOnAwake = false; audioSource.loop = false; audioSource.spatialBlend = 0f; }
            SetBossVisible(false);
        }

        void BuildChunks()
        {
            _chunkRoot = new GameObject("Shock debris").transform;
            _chunkRoot.SetParent(transform, false);
            var rnd = new System.Random(4646);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            for (int i = 0; i < debrisCount; i++)
            {
                var go = new GameObject("chunk_" + i);
                go.transform.SetParent(_chunkRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = debrisMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = debrisMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                go.transform.localScale = Vector3.one * Mathf.Pow(R(0f, 1f), 2.2f) * 3.2f + Vector3.one * 0.3f;
                // stage-space offset from the bow at the halt (x right, y up, z toward the planet); blasted outward and
                // toward the characters
                var p0 = new Vector3(R(-55f, 55f), R(-45f, 40f), R(-50f, 25f));
                var v = p0.normalized * R(10f, 28f) + new Vector3(0f, R(-2f, 4f), -R(12f, 34f));
                var axis = new Vector3(R(-1f, 1f), R(-1f, 1f), R(-1f, 1f)).normalized;
                _chunks.Add((go.transform, p0, v, axis, R(90f, 520f), R(0f, 0.18f)));
                go.SetActive(false);
            }
        }

        int _visible = -1;
        void SetBossVisible(bool on)
        {
            if (_bossRenderers == null || _visible == (on ? 1 : 0)) return;
            _visible = on ? 1 : 0;
            foreach (var r in _bossRenderers) if (r) r.enabled = on;
            foreach (var l in _eyes) l.enabled = on;
        }

        /// Stage frame from where the characters are now.
        public void BeginStage()
        {
            Vector3 o = Vector3.zero; int n = 0;
            if (actors != null) foreach (var a in actors) if (a) { o += a.position; n++; }
            if (n == 0 && director && director.controllers != null) foreach (var c in director.controllers) if (c) { o += c.transform.position; n++; }
            _O = n > 0 ? o / n : transform.position;                       // the characters' feet
            _U = Vector3.up;
            _F = Vector3.ProjectOnPlane(planetDir, _U).normalized;
            if (_F.sqrMagnitude < 0.5f) _F = Vector3.forward;
            _R = Vector3.Cross(_U, _F);
            // When pilots split up, both screens frame a pilot who actually reached the boss encounter.
            var hero = flightCamera ? flightCamera.target : null;
            if (actors != null && actors.Length > 0 && System.Array.IndexOf(actors, hero) < 0) hero = actors[0];
            _hero = hero ? hero.position : _O;
            _bow = HeadPos(THit);
            _D = Vector3.ProjectOnPlane(_bow - _hero, _U).normalized;          // horizontal: hero -> the boss's head
            if (_D.sqrMagnitude < 0.5f) _D = _F;
            _Rd = Vector3.Cross(_U, _D);
            Physics.SyncTransforms();
            if (_key) _key.transform.rotation = Quaternion.LookRotation((_F - _U * 0.4f - _R * 0.35f).normalized, _U);   // from behind-left-above the characters

            // salvo targets: drifting rocks in front of / beside the characters (15-150 m), never the characters
            var rocks = new List<Vector3>();
            foreach (var d in FindObjectsByType<Drift>(FindObjectsSortMode.None))
            {
                Vector3 off = d.transform.position - _hero;
                float dist = off.magnitude;
                if (dist < 15f || dist > 150f || Vector3.Dot(off / dist, _F) < 0.05f) continue;
                rocks.Add(d.transform.position);
            }
            rocks.Sort((a, b) => Vector3.Distance(a, _hero).CompareTo(Vector3.Distance(b, _hero)));
            var rnd = new System.Random(77);
            for (int i = 0; i < _darts.Count; i++)
            {
                var dt = _darts[i];
                if (rocks.Count > 0) dt.target = rocks[(i * 3) % rocks.Count];
                else dt.target = Stg((float)(rnd.NextDouble() * 140 - 70), (float)(rnd.NextDouble() * 30 - 6), (float)(rnd.NextDouble() * 90 + 30));
                dt.launched = false;
                if (dt.fx) dt.fx.gameObject.SetActive(false);
            }

            // props round the hero that could hide it (wreck plates without colliders): bounds tests
            _near.Clear();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r || !r.enabled || r is ParticleSystemRenderer) continue;
                if (boss && r.transform.IsChildOf(boss)) continue;
                if (_chunkRoot && r.transform.IsChildOf(_chunkRoot)) continue;
                if (IsActor(r.transform)) continue;
                var b = r.bounds;
                if (b.size.magnitude > 80f || (b.center - _hero).sqrMagnitude > 30f * 30f) continue;
                if (b.Contains(_hero + _U * 0.2f)) continue;                     // the deck under the hero
                _near.Add(r);
            }

            // worm's-eye spot: behind the hero, low; must see the hero's head and the bow, and not be lifted by the floor
            (float back, float side)[] spots = { (1.5f, -0.75f), (1.5f, 0.75f), (2.1f, 0.6f), (2.1f, -0.45f), (1.1f, 1.05f), (1.1f, -1.05f), (1.8f, 1.4f), (1.8f, -1.4f), (2.6f, 0f) };
            _wormEye = Vector3.zero; float best = float.MaxValue;
            foreach (var sp in spots)
            {
                Vector3 want = _hero - _D * sp.back + _Rd * sp.side + _U * 0.35f;
                Vector3 got = Ground(want);
                float lift = got.y - want.y;
                bool seeHero = Clear(got, _hero + _U * 1.45f, 99f), seeBow = Clear(got, _bow, 30f);
                float score = lift + (seeHero ? 0f : 10f) + (seeBow ? 0f : 5f);
                if (score < best) { best = score; _wormEye = got; }
                if (score < 0.25f) break;
            }

            // drifting rocks near the halt point get shoved off the bow
            _pushed.Clear();
            foreach (var d in FindObjectsByType<Drift>(FindObjectsSortMode.None))
            {
                Vector3 off = d.transform.position - _bow;
                float dist = off.magnitude;
                if (dist > 420f) continue;
                var dir = (off.normalized + -_F * 0.6f).normalized;
                _pushed.Add((d, dir, Mathf.Lerp(38f, 6f, dist / 420f)));
            }
        }

        // ---------------------------------------------------------------------------------------------- play
        void Start()
        {
            Prepare();
            if (playOnStart && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-oymBossDemo") < 0)
                _startAt = Time.unscaledTime + autoStartDelay;
        }

        public void Play()
        {
            Prepare();
            BeginStage();
            Clock = 0f; Playing = true; Done = false; _startAt = -1f; _lastCut = -1;
            SetBossVisible(true);
            if (director && director.controllers != null)
            {
                _wasScripted = new bool[director.controllers.Length];
                for (int i = 0; i < director.controllers.Length; i++)
                {
                    var c = director.controllers[i]; if (!c) continue;
                    _wasScripted[i] = c.scripted; c.scripted = true; c.scriptedInput = default;
                    c.RefreshView(true);
                    if (actors == null || actors.Length == 0 || System.Array.IndexOf(actors, c.transform) >= 0) c.ResetMotion();
                }
            }
            if (flightCamera) { _camWasScripted = flightCamera.scripted; flightCamera.scripted = true; }
            if (audioSource && entranceClip) { audioSource.Stop(); audioSource.time = 0f; audioSource.Play(); }
            Evaluate(0f, true);
        }

        void Finish()
        {
            Playing = false; Done = true;
            if (director && director.controllers != null && _wasScripted != null)
                for (int i = 0; i < director.controllers.Length; i++)
                {
                    var pilot = director.controllers[i];
                    if (!pilot) continue;
                    pilot.scripted = _wasScripted[i];
                    pilot.RefreshView(false);
                }
            if (flightCamera) flightCamera.scripted = _camWasScripted;
        }

        void Update()
        {
            if (_startAt > 0f && Time.unscaledTime >= _startAt) Play();
        }

        void LateUpdate()
        {
            if (!_prepared || !boss) return;
            if (Playing)
            {
                float prev = Clock;
                Clock += GameFeel.RealDt;
                if (prev < THit && Clock >= THit) GameFeel.HitStop(0.4f, 0.5f);            // the world drags for a moment as the mass stops
                if (prev < TRoar && Clock >= TRoar) GameFeel.HitStop(0.12f, 0.3f);
                if (prev < TBack && Clock >= TBack && flightCamera)
                {
                    Vector3 d = BossCenter(TBack) - flightCamera.transform.position;   // the game camera comes back looking at it
                    flightCamera.SetLook(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Asin(d.normalized.y) * Mathf.Rad2Deg + 4f);
                }
                Evaluate(Clock, true);
                if (Clock >= TEnd) Finish();
            }
            else if (Done) Idle(Clock += GameFeel.RealDt);
        }

        void OnGUI()
        {
            float lb = Letterbox(Clock);
            if (!Playing || lb <= 0f) return;
            GUI.depth = -100;
            float h = Screen.height * 0.11f * lb;
            var c = GUI.color; GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, h), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, Screen.height - h, Screen.width, h), Texture2D.whiteTexture);
            GUI.color = c;
        }

        public static float Letterbox(float t) => S(0f, 0.6f, t) * (1f - S(TBack, TEnd, t));

        // ---------------------------------------------------------------------------------------------- the clock
        static float S(float a, float b, float x) { x = Mathf.Clamp01((x - a) / (b - a)); return x * x * (3f - 2f * x); }
        static float Kick(float h, float decay) => h < 0f ? 0f : Mathf.Exp(-h / decay);

        Vector3 Stg(float x, float y, float z) => _O + _R * x + _U * y + _F * z;
        Vector3 EndPos() => Stg(endSide, endHeight, endDistance);

        // boss path: cubic Bezier from below-far, rising, levelling, arriving at the halt with momentum left
        Vector3 Bez(float u)
        {
            Vector3 p3 = EndPos();
            Vector3 p0 = p3 + _R * -40f + _U * -195f + _F * 95f;                       // ~28 deg below the eye line
            Vector3 p1 = p3 + _R * -28f + _U * -120f + _F * 64f;
            Vector3 p2 = p3 + _R * -8f + _U * -22f + _F * 20f;
            float a = 1f - u;
            return a * a * a * p0 + 3f * a * a * u * p1 + 3f * a * u * u * p2 + u * u * u * p3;
        }

        // ease: starts from rest, peaks at 1.44x the mean speed, arrives with a quarter of it left (the settle takes over)
        static float Glide(float x) { x = Mathf.Clamp01(x); return x * x * (2.75f - 1.75f * x); }

        public Vector3 BossCenter(float t)
        {
            float x = (t - TAppear) / (THit - TAppear);
            Vector3 p = Bez(Glide(x));
            float h = t - THit;
            if (h > 0f)
            {
                // momentum left at the halt (critically damped): carries it on ~lurch m over ~1 s, settles back over ~3 s
                Vector3 v = (Bez(1f) - Bez(0.98f)).normalized;
                p += v * (lurch * Mathf.Exp(1f) * (h / SettleT) * Mathf.Exp(-h / SettleT));
            }
            return p;
        }

        Quaternion BossRot(float t)
        {
            float x = Mathf.Clamp01((t - TAppear) / (THit - TAppear));
            float u = Glide(x);
            Vector3 tan = (Bez(Mathf.Min(1f, u + 0.01f)) - Bez(Mathf.Max(0f, u - 0.01f))).normalized;
            Vector3 face = Quaternion.AngleAxis(endYaw, _U) * (-_F);
            Vector3 fwd = Vector3.Slerp(tan, face, S(0.15f, 1.0f, x)).normalized;          // turns to face them over ~5 s
            var rot = Quaternion.LookRotation(fwd, _U);
            float h = t - THit;
            float nod = h > 0f ? 2.6f * (h / 1.1f) * Mathf.Exp(1f - h / 1.1f) : 0f;          // inertia: the bow dips slowly, recovers over seconds
            float bank = -5f * Mathf.Sin(Mathf.Clamp01(x) * Mathf.PI);
            return rot * Quaternion.Euler(nod, 0f, bank);
        }

        Vector3 HeadPos(float t) => BossCenter(t) + BossRot(t) * Vector3.Scale(_headLocal, boss.lossyScale);

        // ---------------------------------------------------------------------------------------------- evaluate
        public void Evaluate(float t, bool driveCamera)
        {
            if (!_prepared) Prepare();
            float hit = t - THit, roar = t - TRoar;
            int shot = ShotIndex(t);                                            // hard cuts: no motion blur on the cut frames
            if (shot != _lastCut) { if (_lastCut >= 0) _cutBlurFrames = 2; _lastCut = shot; }

            // ---- boss
            bool shown = t >= 0f;                                                          // there from the start, deep below: no pop-in
            if (boss.gameObject.activeSelf != true) boss.gameObject.SetActive(true);
            SetBossVisible(shown);
            boss.SetPositionAndRotation(BossCenter(t), BossRot(t));
            float claw = roar >= 0f ? 34f * S(0f, 1.2f, roar) + 3f * Mathf.Sin(Mathf.Clamp01((roar - 1.0f) / 1.2f) * Mathf.PI) * (roar > 1.0f ? 1f : 0f) : 0f;   // slow, heavy, a small sway past open
            foreach (var c in _claws) c.t.localRotation = c.rest * Quaternion.Euler(-claw, 0f, 0f);
            float aim = S(TRoar - 0.1f, TRoar + 1.6f, t) * 20f;
            foreach (var l in _launchers) l.t.localRotation = l.rest * Quaternion.Euler(-aim * 0.4f, aim * l.sign, 0f);

            // ---- glow: dark on approach, sensors ignite in sequence, roar flash, settle
            for (int i = 0; i < _sensors.Count; i++)
            {
                float on = S(TIgnite + i * SensorGap, TIgnite + i * SensorGap + 0.35f, t);
                float flicker = on > 0f && on < 1f ? 0.6f + 0.4f * Mathf.Sin(t * 90f + i) : 1f;
                float idle = Mathf.Lerp(12.8f, 9f, S(THit, THit + 0.6f, t));             // faint running lights on the way in, dark at the halt
                float ev = Mathf.Lerp(idle, 17.2f, on * flicker) + (roar >= 0f ? 2.8f * Kick(roar, 0.5f) : 0f);
                HDMaterial.SetEmissiveIntensity(_sensors[i], ev, EmissiveIntensityUnit.EV100);
            }
            float salvoFlash = 0f;
            foreach (var d in _darts) if (t >= d.launchT) salvoFlash = Mathf.Max(salvoFlash, Mathf.Exp(-(t - d.launchT) / 0.18f));
            float tubeNits = Mathf.Max(salvoFlash > 0.01f ? LightUnitUtils.Ev100ToNits(16f + 3f * salvoFlash) : 0f,
                                       roar < 0f ? 0f : LightUnitUtils.Ev100ToNits(Mathf.Lerp(16.5f, 19.5f, Kick(roar, 0.4f))));
            foreach (var m in _tubes) HDMaterial.SetEmissiveIntensity(m, tubeNits, EmissiveIntensityUnit.Nits);
            Salvo(t);
            foreach (var m in _engines) HDMaterial.SetEmissiveIntensity(m, 16.5f - 2f * S(THit, THit + 2f, t), EmissiveIntensityUnit.EV100);
            if (_key) _key.intensity = 12000f * S(0f, 1.2f, t) * (1f - 0.4f * S(THit, TRoar, t)) + (roar >= 0f ? 6000f * Kick(roar, 0.5f) : 0f);
            float eye = S(TIgnite, TIgnite + 0.9f, t) * 2.5e6f + (roar >= 0f ? 1.2e7f * Kick(roar, 0.45f) : 0f);
            foreach (var l in _eyes) l.intensity = eye;

            // ---- shock: drifting rocks shoved off the bow, loose chunks blasted toward the camera
            float shove = hit > 0f ? 1f - Mathf.Exp(-hit / 1.4f) : 0f;
            foreach (var p in _pushed)
            {
                // team repo: Drift has no offset field (perf/fps45 moves it in a job) - shift its orbit centre by the
                // change instead, which carries the rock the same way in either Drift
                if (!p.d) continue;
                Vector3 want = p.dir * (p.amp * shove);
                _shoved.TryGetValue(p.d, out var had);
                Vector3 delta = want - had;
                if (delta.sqrMagnitude < 1e-8f) continue;
                _shoved[p.d] = want;
                p.d.center += delta;
                if (!Application.isPlaying) p.d.transform.position += delta;     // edit-mode stills: Drift does not run
            }
            Quaternion stage = Quaternion.LookRotation(_F, _U);
            foreach (var c in _chunks)
            {
                float h = hit - c.delay;
                bool on = t >= 0f;
                if (c.t.gameObject.activeSelf != on) c.t.gameObject.SetActive(on);
                if (!on) continue;
                float k = 0.8f, travel = h > 0f ? (1f - Mathf.Exp(-k * h)) / k : 0f;
                c.t.position = _bow + stage * (c.p0 + c.v * travel);
                c.t.rotation = Quaternion.AngleAxis(c.spin * 0.35f * (h > 0f ? travel : 0f) + c.spin * 0.02f * t, c.axis);
            }

            // ---- post
            if (_volume)
            {
                _volume.weight = Playing || !Application.isPlaying ? 1f : 0f;
                float hk = hit >= 0f ? Kick(hit, 0.7f) : 0f, rk = roar >= 0f ? Kick(roar, 0.8f) : 0f;
                _bloom.intensity.value = 0.25f + 0.25f * S(TIgnite, TRoar, t) + 0.25f * hk + 0.75f * rk;
                float punch = roar >= 0f && roar < 0.1f ? 1.1f : roar >= 0.1f ? 1.1f * Kick(roar - 0.1f, 0.18f) : 0f;
                _color.postExposure.value = punch + 0.45f * (hit >= 0f && hit < 0.07f ? 1f : 0f) - 0.35f * S(THit, TIgnite, t) * (1f - S(TRoar, TRoar + 0.3f, t));
                _chroma.intensity.value = 0.06f + 0.25f * hk + 0.5f * rk;
                _vignette.intensity.value = 0.24f + 0.16f * Letterbox(t);
                _blur.intensity.value = _cutBlurFrames > 0f ? 0f : 0.25f;
                if (_cutBlurFrames > 0f) _cutBlurFrames--;
            }

            if (driveCamera) DriveCamera(t);
        }

        /// Launch each dart when the clock passes its time, from its tube on the pod face (pose at that moment).
        /// Play mode: darts tick themselves (Update). Edit-mode stills: ticked here so impacts happen on time.
        void Salvo(float t)
        {
            foreach (var d in _darts)
            {
                if (!d.fx) continue;
                if (!d.launched)
                {
                    if (t < d.launchT) continue;
                    var br = BossRot(d.launchT);
                    int gx = d.tube % 4, gy = (d.tube / 4) % 2;
                    var e = _podExt[d.pod];
                    var face = _podFace[d.pod] + new Vector3((gx - 1.5f) / 1.5f * e.x * 0.55f, (gy - 0.5f) * e.y * 0.6f, 0f);
                    Vector3 tubePos = BossCenter(d.launchT) + br * Vector3.Scale(face, boss.lossyScale);
                    Vector3 tubeDir = br * (Quaternion.Euler(-30f, 0f, 0f) * Vector3.forward);
                    d.fx.gameObject.SetActive(true);
                    d.fx.Launch(tubePos, tubeDir, d.target, d.flight, 900 + d.tube * 13 + d.pod * 7);
                    d.launched = true; d.ticked = d.launchT;
                }
                if (!Application.isPlaying && t > d.ticked) { d.fx.Tick(t - d.ticked); d.ticked = t; }
            }
        }

        void Idle(float t)
        {
            // after the entrance: the boss hangs there, breathing; sensors pulse
            float s = t - TEnd;
            boss.SetPositionAndRotation(BossCenter(TEnd) + _U * (Mathf.Sin(s * 0.35f) * 2.2f), BossRot(TEnd) * Quaternion.Euler(Mathf.Sin(s * 0.21f) * 0.8f, Mathf.Sin(s * 0.17f) * 1.5f, 0f));
            float pulse = 0.5f + 0.5f * Mathf.Sin(s * 1.3f);
            foreach (var m in _sensors) HDMaterial.SetEmissiveIntensity(m, 16.6f + 0.8f * pulse, EmissiveIntensityUnit.EV100);
            foreach (var l in _eyes) l.intensity = 2.0e6f + 0.8e6f * pulse;
            if (_key) _key.intensity = 4500f;
            if (_volume)
            {
                _volume.weight = 1f;                                                 // keep the boss out of the depth fade
                _bloom.intensity.value = 0.3f; _color.postExposure.value = 0f; _chroma.intensity.value = 0.06f; _vignette.intensity.value = 0.24f; _blur.intensity.value = 0.25f;
            }
        }

        // ---------------------------------------------------------------------------------------------- camera
        public struct Shot { public Vector3 eye; public Quaternion rot; public float fov; public int cut; }

        public static int ShotIndex(float t) => t < TLaunch0 ? 0 : t < TLaunch1 ? 3 : t < THit ? 4 : t < TWide ? 1 : 2;

        public Shot CameraAt(float t)
        {
            Vector3 bc = BossCenter(t);
            if (t >= TLaunch0 && t < TLaunch1 && _pods)
            {
                // the might first: a locked-off camera off the sunlit flank, a little above the launcher pods; the hull
                // glides slowly through the frame while the tubes ripple-fire and the darts climb out
                float tm = 0.5f * (TLaunch0 + TLaunch1);
                var brm = BossRot(tm);
                Vector3 podLocal = (_podFace[0] + _podFace[1]) * 0.5f;
                Vector3 podM = BossCenter(tm) + brm * Vector3.Scale(podLocal, boss.lossyScale);
                float k = S(TLaunch0, TLaunch1, t);
                Vector3 eye = podM + brm * new Vector3(-140f, 34f, 70f) + _U * (5f * k);
                Vector3 podNow = bc + BossRot(t) * Vector3.Scale(podLocal, boss.lossyScale);
                Vector3 look = Vector3.Lerp(podNow, podNow + BossRot(t) * new Vector3(0f, 60f, 70f), 0.18f + 0.25f * k);
                return new Shot { eye = eye, rot = Quaternion.LookRotation(look - eye, _U), fov = 40f, cut = 3 };
            }
            if (t < THit)
            {
                // over the hero's shoulder at shoulder height, slow push; tilts up with the boss as it rises
                float k = S(0.3f, THit, t);
                Vector3 eye = Ground(_hero - _F * (2.7f - 0.6f * k) + _R * 0.8f + _U * 1.6f);
                Vector3 level = _hero + _F * 100f + _U * 4f;                                  // the planet; the hull rises into frame
                Vector3 look = Vector3.Lerp(level, bc, 0.55f * S(2.0f, THit, t));
                return new Shot { eye = eye, rot = Quaternion.LookRotation(look - eye, _U), fov = Mathf.Lerp(54f, 47f, k), cut = 0 };
            }
            if (t < TWide)
            {
                // worm's-eye at the hero's heels: the hero towers at the frame's side, the bow fills the top. The spot was
                // picked in BeginStage where the deck is no higher than the hero's feet; the view direction sits between
                // the bow and the hero's head so both are in frame.
                float k = S(THit, TWide, t);
                Vector3 eye = _wormEye + _D * (0.35f * k);
                Vector3 toBow = (Vector3.Lerp(HeadPos(t), bc, 0.2f) - eye).normalized;
                Vector3 toHero = (_hero + _U * 1.5f - eye).normalized;
                Vector3 dir = Vector3.Slerp(toBow, toHero, 0.42f);
                return new Shot { eye = eye, rot = Quaternion.LookRotation(dir, _U), fov = Mathf.Lerp(66f, 60f, k), cut = 1 };
            }
            {
                // wide: both characters small against the boss and the planet; crane back and up
                float k = S(TWide, TEnd, t);
                Vector3 eye = Ground(Vector3.Lerp(_hero - _D * 18f + _Rd * 6f + _U * 4.5f, _hero - _D * 26f + _Rd * 8.5f + _U * 9f, k));
                Vector3 look = Vector3.Lerp(bc, _hero, 0.12f) + _U * 14f;
                return new Shot { eye = eye, rot = Quaternion.LookRotation(look - eye, _U), fov = 52f, cut = 2 };
            }
        }

        bool IsActor(Transform t)
        {
            if (actors != null) foreach (var a in actors) if (a && t.IsChildOf(a)) return true;
            return false;
        }

        /// Line of sight from a to b (only the first `upTo` metres are tested): colliders and nearby collider-less props.
        bool Clear(Vector3 a, Vector3 b, float upTo)
        {
            Vector3 d = b - a; float len = Mathf.Min(d.magnitude, upTo); d.Normalize();
            foreach (var h in Physics.RaycastAll(a, d, len, ~0, QueryTriggerInteraction.Ignore))
            {
                if (boss && h.transform.IsChildOf(boss)) continue;
                if (IsActor(h.transform)) continue;
                return false;
            }
            var ray = new Ray(a, d);
            foreach (var r in _near)
                if (r && r.bounds.IntersectRay(ray, out float dist) && dist < len && !r.bounds.Contains(a)) return false;
            return true;
        }

        /// Keep a camera above whatever it stands over (deck, wreck plates) - never inside the floor.
        Vector3 Ground(Vector3 eye)
        {
            var hits = Physics.RaycastAll(eye + _U * 8f, -_U, 16f, ~0, QueryTriggerInteraction.Ignore);
            float top = float.NegativeInfinity;
            foreach (var h in hits)
            {
                if (boss && h.transform.IsChildOf(boss)) continue;
                bool actor = false;
                if (actors != null) foreach (var a in actors) if (a && h.transform.IsChildOf(a)) actor = true;
                if (!actor && h.point.y > top && h.point.y < eye.y + 3f) top = h.point.y;
            }
            if (top > float.NegativeInfinity && eye.y < top + 0.3f) eye.y = top + 0.3f;
            return eye;
        }

        int _lastCut = -1;

        void DriveCamera(float t)
        {
            if (!_cam) return;
            var shot = CameraAt(t);

            // shake (on top of the shot): rumble during the approach, hard on the halt and the roar
            float hit = t - THit, roar = t - TRoar;
            float amp = 0.3f * S(0f, THit, t) * (t < THit ? 1f : 0f) + (hit >= 0f ? 2.2f * Kick(hit, 1.0f) : 0f) + (roar >= 0f ? 1.4f * Kick(roar, 0.8f) : 0f) + 0.12f;
            float q = t * (hit >= 0f && hit < 2.5f ? 7f : 4.5f);                              // low frequency: mass, not jitter
            var shake = Quaternion.Euler(amp * (Mathf.PerlinNoise(_shakeSeed, q) - 0.5f) * 2f, amp * (Mathf.PerlinNoise(_shakeSeed + 5f, q) - 0.5f) * 2f, amp * 0.6f * (Mathf.PerlinNoise(_shakeSeed + 9f, q) - 0.5f) * 2f);
            Vector3 eye = shot.eye; Quaternion rot = shot.rot * shake; float fov = shot.fov;
            if (hit >= 0f) fov += 3.5f * Kick(hit, 0.7f) * Mathf.Clamp01(hit / 0.18f);          // slow fov swell on the halt

            // ease in from / back out to the game camera (its pose was set this frame by FlightCamera)
            float w = S(0.15f, 1.1f, t) * (1f - S(TBack, TEnd, t));
            if (Application.isPlaying && w < 1f)
            {
                var gt = _cam.transform;
                eye = Vector3.Lerp(gt.position, eye, w);
                rot = Quaternion.Slerp(gt.rotation, rot, w);
                fov = Mathf.Lerp(_cam.fieldOfView, fov, w);
            }
            _cam.transform.SetPositionAndRotation(eye, rot);
            _cam.fieldOfView = fov;
            _cam.nearClipPlane = 0.1f;
        }

        void OnDestroy() { if (_profile) Destroy(_profile); }
    }

    static class BossEntranceExt
    {
        public static Transform FirstOrDefaultNamed(this Transform[] ts, string name)
        {
            foreach (var t in ts) if (t && t.name == name) return t;
            return null;
        }
    }
}
