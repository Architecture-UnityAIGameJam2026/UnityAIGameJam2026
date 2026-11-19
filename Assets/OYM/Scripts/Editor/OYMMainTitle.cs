// OnYourMark - main title: KESTREL and ORBIT FRAME float back to back at the debris-field spawn, each looking its own
// way, with the wreck, the giant planet and the 16K sky behind them. Click a pilot (or A / D, arrows, Tab): it boosts
// to the camera (ignition flash, the snap), turns to look into the lens and takes its stance; the other is pushed back
// into the depth of field. Click the one in the back to swap, Esc for both again, ENTER / DEPLOY to launch into the
// game scene with that pilot (OYM.GameSession). Music: Resources/OYM_BgmRoutes (MainTitle -> main theme, game -> battle).
//
//   batch: Unity.exe -batchmode -projectPath <proj> -executeMethod OYM.EditorTools.OYMMainTitle.BuildAndCapture -quit
//          (scene + music import + routes, then edit-mode stills -> Screenshots/title/t*.png)
//   play:  Unity.exe -projectPath <proj> -executeMethod OYM.EditorTools.OYMMainTitle.OpenAndPlay [-oymTitleDemo]
//          (-oymTitleDemo: scripted run, frames + GIF -> Screenshots/title/demo, then quits)
//
// Built from DebrisField.unity (like the enemy test / showcase), saved under another name; the source scene is untouched.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UIElements;

namespace OYM.EditorTools
{
    public static class OYMMainTitle
    {
        public const string ScenePath = OYMSetup.Root + "/Scenes/MainTitle.unity";
        public const string Dir = OYMSetup.Root + "/Title";
        const string MusicDir = OYMSetup.Root + "/Audio/Music";
        public const string ThemePath = MusicDir + "/bgm_main_theme_01.wav";
        public const string BattlePath = MusicDir + "/bgm_battle_01.wav";
        const string ResDir = OYMSetup.Root + "/Resources";
        const string RoutesPath = ResDir + "/" + OYM.BgmRoutes.ResourceName + ".asset";

        // ---- layout: metres, the camera looks down +Z (wreck ahead, giant planet front-left, sun front-right out of frame)
        // yaw: 0 = +Z, 90 = +X (screen right), 180 = facing the camera. look: (+ = the pilot's own right, + = down)
        static readonly TitleCharacter.Pose KHome = new TitleCharacter.Pose(new Vector3(-0.42f, 0.08f, 0.00f), 222f, new Vector2(18f, 2f));     // 3/4: the mask reads
        static readonly TitleCharacter.Pose OHome = new TitleCharacter.Pose(new Vector3(0.46f, -0.04f, 0.24f), 134f, new Vector2(-10f, -4f));
        public static TitleCharacter.Pose KFront = new TitleCharacter.Pose(new Vector3(-0.42f, 0.04f, -1.70f), 196f, Vector2.zero);
        static readonly TitleCharacter.Pose OFront = new TitleCharacter.Pose(new Vector3(0.42f, 0.00f, -1.70f), 202f, Vector2.zero);
        static readonly TitleCharacter.Pose KBack = new TitleCharacter.Pose(new Vector3(-1.00f, 0.50f, 3.30f), 248f, new Vector2(22f, 2f));
        static readonly TitleCharacter.Pose OBack = new TitleCharacter.Pose(new Vector3(-2.00f, 0.38f, 3.45f), 112f, new Vector2(-8f, -4f));
        // neutral: both pilots fill ~80 % of the height (user 11-18: zoom in); the KESTREL focus mirrors the ORBIT FRAME one
        static readonly TitleDirector.Shot Neutral = new TitleDirector.Shot(new Vector3(0f, 1.0f, -4.4f), new Vector3(0f, 1.18f, 0f), 30f);
        public static TitleDirector.Shot FocusK = new TitleDirector.Shot(new Vector3(-0.90f, 1.12f, -4.6f), new Vector3(-0.97f, 1.30f, -1.7f), 30f);   // 11-18 UI: front pilot in the right third, menu on the left
        static readonly TitleDirector.Shot FocusO = new TitleDirector.Shot(new Vector3(-0.12f, 1.02f, -4.95f), new Vector3(-0.20f, 1.22f, -1.7f), 30f);
        // KESTREL's hero move on focus: clip, played from -> held at (seconds), slash arc window
        public static string KFrontClip = "Slash_Left";
        // Slash_Left swing-out: the blade comes out and is held level, pointing into the frame at chest height with the
        // mask to the camera (mirrors the RG-77 shot; picked from the skinned PoseSheet renders 11-18, pose 16)
        public static float KFrom = 0.3f, KHold = 0.75f;
        public static Vector2 KTrail = new Vector2(0.42f, 0.74f);
        // the hold arm (blade lowered toward the bottom-left corner, user sketch 11-18) is TitleCharacter.holdArm: its
        // defaults are KESTREL's (wrist kept natural, user 11-19 "팔 꺾이는데")

        // logo: image-gen variants a-d (design/title/logo_*.png -> pipeline/title/logo_alpha.py -> Title/Logo)
        public const string LogoDir = Dir + "/Logo";
        public static string LogoVariant = "b";                                // user 11-18: stacked stencil + hazard chevrons
        public const string IconDir = Dir + "/Icons";                       // pilot icons + team logo (white glyphs)
        // Discord invite for the top-right button (empty = the button says the link is not set yet)
        public static string DiscordUrl = "";

        [MenuItem("OYM/Title/Build Main Title")]
        public static void Build()
        {
            ImportMusic();
            BuildRoutes();
            var panel = PanelAsset();
            var logo = ImportLogo();
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Dir + "/MainTitle.uxml");
            if (uxml == null) throw new FileNotFoundException("title UI missing", Dir + "/MainTitle.uxml");
            var profile = TitleProfile();

            var scene = EditorSceneManager.OpenScene(OYMSetup.ScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            foreach (var g in roots) if (g.name == "PlayerSpawn") Object.DestroyImmediate(g);
            var camGo = roots.First(g => g.name == "Main Camera");          // keeps the debris field's space dust
            var ff = camGo.GetComponent<OYM.FreeFlyCamera>();
            if (ff) Object.DestroyImmediate(ff);
            var cam = camGo.GetComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 80000f; cam.fieldOfView = Neutral.fov;
            camGo.transform.SetPositionAndRotation(Neutral.pos, Quaternion.LookRotation(Neutral.look - Neutral.pos));

            var root = new GameObject("Main Title").transform;
            var k = Pilot(OYMKestrel.PrefabPath, "KESTREL", root);
            var o = Pilot(OYMOrbitFrame.PrefabPath, "ORBIT FRAME", root);
            Setup(k, KHome, KFront, KBack, new Vector3(0.6f, 8f, -14f));
            Setup(o, OHome, OFront, OBack, new Vector3(-0.6f, 8f, -14f));
            k.idleClip = Clip(k, "Hover"); k.frontClip = Clip(k, KFrontClip); k.frontFrom = KFrom; k.frontHold = KHold; k.trailWindow = KTrail;
            k.trail = k.GetComponentInChildren<OYM.BladeTrail>(true);
            o.idleClip = Clip(o, "AimIdle") ?? Clip(o, "Hover"); o.frontClip = null;
            o.railgun = o.GetComponentInChildren<OYM.RailgunFX>(true);
            o.fireDelay = 0.32f;                                                 // shoot once it has landed: the beam crosses the frame
            foreach (var c in new[] { k, o }) c.animator.runtimeAnimatorController = null;   // TitleCharacter drives its own graph
            Debug.Log($"[OYM-Title] clips: KESTREL idle {k.idleClip?.name} front {k.frontClip?.name} {KFrom}->{KHold}, trail {(k.trail ? "yes" : "no")}; ORBIT FRAME idle {o.idleClip?.name}, railgun {(o.railgun ? "yes" : "no")}");

            // light: the sun rims them from the right / behind, planetshine from the left; a cool key from camera left,
            // a faint cold bounce from below, and a hero spot per front pose (comes up with the focus)
            var lights = new GameObject("Title Lights").transform; lights.SetParent(root, false);
            // the suits are dark (albedo ~0.05): readable fronts need light near sun level (30000 lux at EV 13)
            Spot("Key", lights, new Vector3(-2.4f, 3.0f, -4.6f), new Vector3(0f, 1.15f, -0.3f), new Color(0.84f, 0.9f, 1f), 14000f, 46f, 16f, true);
            Spot("Bounce", lights, new Vector3(1.6f, -1.6f, -3.6f), new Vector3(0f, 0.9f, -0.4f), new Color(0.55f, 0.7f, 1f), 2500f, 55f, 12f, false);
            Spot("Rim", lights, new Vector3(-2.8f, 2.6f, 3.4f), new Vector3(0f, 1.3f, -0.4f), new Color(0.6f, 0.8f, 1f), 12000f, 50f, 14f, false);
            k.heroLight = Spot("Hero KESTREL", lights, KFront.pos + new Vector3(0.5f, 1.85f, -2.3f), KFront.pos + Vector3.up * 1.55f, new Color(0.82f, 0.93f, 1f), 20000f, 34f, 9f, true);
            o.heroLight = Spot("Hero ORBIT FRAME", lights, OFront.pos + new Vector3(-1.2f, 1.9f, -2.0f), OFront.pos + Vector3.up * 1.45f, new Color(1f, 0.9f, 0.78f), 20000f, 34f, 9f, true);
            k.heroIntensity = k.heroLight.intensity; o.heroIntensity = o.heroLight.intensity;
            k.heroLight.enabled = o.heroLight.enabled = false;
            NearRocks(root);

            var volGo = new GameObject("Title Volume (depth of field)"); volGo.transform.SetParent(root, false);
            var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 50f; vol.sharedProfile = profile;

            var uiGo = new GameObject("Title UI"); uiGo.transform.SetParent(root, false);
            var doc = uiGo.AddComponent<UIDocument>(); doc.panelSettings = panel; doc.visualTreeAsset = uxml;
            var ui = uiGo.AddComponent<OYM.TitleUI>();
            ui.pilots = new[]
            {
                new OYM.TitleUI.PilotInfo
                {
                    code = "K-07", name = "KESTREL", role = "CLOSE-QUARTERS BLADE", accentClass = "accent-kestrel",
                    description = "Hooded duelist in a slim pressure suit. A compact twin-nozzle pack closes any gap in a single boost; the black forged blade finishes it.",
                    weapon = "BLACK FORGED-STEEL BLADE", controls = "LMB  SLASH (CHAIN)   ·   Q  STANCE   ·   SHIFT  DASH",
                    stats = new[] { 0.95f, 0.72f, 0.18f, 0.34f },
                },
                new OYM.TitleUI.PilotInfo
                {
                    code = "OF-04", name = "ORBIT FRAME", role = "RAILGUN MARKSMAN", accentClass = "accent-orbit",
                    description = "Armored frame on a load-bearing leg exoskeleton. The RG-77 lights its coils in sequence and drives a straight line through anything in the debris field.",
                    weapon = "RG-77 TWIN-RAIL RAILGUN", controls = "RMB  AIM   ·   LMB  FIRE (CHARGE)   ·   SHIFT  DASH",
                    stats = new[] { 0.55f, 0.92f, 0.95f, 0.74f },
                },
            };

            ui.logo = logo;
            ui.discordUrl = DiscordUrl;
            ui.teamLogo = AssetDatabase.LoadAssetAtPath<Texture2D>(IconDir + "/team_logo.png");
            if (ui.pilots.Length > 1)
            {
                ui.pilots[0].icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconDir + "/icon_kestrel.png");
                ui.pilots[1].icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconDir + "/icon_orbit.png");
            }

            var dir = new GameObject("Title Director").AddComponent<OYM.TitleDirector>();
            dir.transform.SetParent(root, false);
            dir.cam = cam; dir.pilots = new[] { k, o }; dir.ui = ui; dir.dofVolume = vol;
            dir.neutralShot = Neutral; dir.focusShots = new[] { FocusK, FocusO };
            dir.neutralFarStart = 2e5f; dir.neutralFarEnd = 4e5f;             // neutral: no far blur (stars became hexagons)
            k.lookTarget = o.lookTarget = cam.transform;
            AddFX(dir, cam);
            k.Snap(TitleCharacter.Slot.Home); o.Snap(TitleCharacter.Slot.Home);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("[OYM-Title] main title saved -> " + ScenePath);
        }

        static OYM.TitleCharacter Pilot(string prefabPath, string name, Transform parent)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (pf == null) throw new FileNotFoundException("pilot prefab missing", prefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            // presentation only: no flight controller (it would read WASD and turn the body to the camera), no viewmodel
            foreach (var c in go.GetComponentsInChildren<OYM.ZeroGFlight>(true)) Object.DestroyImmediate(c);
            foreach (var fp in go.GetComponentsInChildren<OYM.FirstPersonRig>(true).Select(f => f.gameObject).ToArray()) Object.DestroyImmediate(fp);
            var anim = go.GetComponentsInChildren<Animator>(true).First();
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.applyRootMotion = false;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;

            var b = new Bounds(go.transform.position + Vector3.up, Vector3.zero); bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
            }
            var tc = go.AddComponent<OYM.TitleCharacter>();
            tc.displayName = name; tc.animator = anim; tc.jetpack = go.GetComponentInChildren<OYM.JetpackFX>(true);
            float h = Mathf.Clamp(b.max.y, 1.4f, 2.4f);
            tc.pivotHeight = h * 0.55f;
            var cap = go.AddComponent<CapsuleCollider>();                     // mouse pick only
            cap.isTrigger = true; cap.direction = 1; cap.height = h + 0.1f; cap.radius = 0.45f; cap.center = new Vector3(0f, h * 0.5f, 0f);
            Debug.Log($"[OYM-Title] pilot {name}: bounds {b.min.ToString("F2")} .. {b.max.ToString("F2")}, jetpack {(tc.jetpack ? "yes" : "no")}, human {anim.isHuman}");
            return tc;
        }

        static void Setup(OYM.TitleCharacter c, TitleCharacter.Pose home, TitleCharacter.Pose front, TitleCharacter.Pose back, Vector3 launch)
        {
            c.home = home; c.front = front; c.back = back; c.launchVelocity = launch;
        }

        // presentation layer on top of the director: camera punches, hit-stop on the strike, post pulses (OYM.TitleFX)
        static void AddFX(OYM.TitleDirector dir, Camera cam)
        {
            var fx = dir.gameObject.AddComponent<OYM.TitleFX>();
            fx.director = dir; fx.cam = cam;
        }

        static AnimationClip Clip(OYM.TitleCharacter c, string name)
        {
            var ctl = c.animator.runtimeAnimatorController;
            var clip = ctl ? ctl.animationClips.FirstOrDefault(x => x.name == name) : null;
            return clip != null ? clip : AnimClips(c.displayName).FirstOrDefault(x => x.name == name);
        }

        /// every clip in the pilot's Animations folder (also the ones its controller does not use)
        public static List<AnimationClip> AnimClips(string pilot)
        {
            string dir = pilot == "KESTREL" ? OYMKestrel.AnimDir : OYMOrbitFrame.AnimDir;
            return AssetDatabase.FindAssets("t:AnimationClip", new[] { dir })
                .SelectMany(g => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)).OfType<AnimationClip>())
                .Where(x => !x.name.StartsWith("__preview__")).GroupBy(x => x.name).Select(g => g.First()).ToList();
        }

        static Light Spot(string name, Transform parent, Vector3 pos, Vector3 aim, Color color, float lux, float angle, float range, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(aim - pos, Vector3.up));
            var hd = go.AddHDLight(LightType.Spot);
            var l = go.GetComponent<Light>();
            float d = Vector3.Distance(pos, aim);
            l.lightUnit = LightUnit.Candela;
            l.intensity = lux * d * d;                                         // illuminance `lux` at the aim point
            l.useColorTemperature = false; l.color = color;
            l.spotAngle = angle; l.innerSpotAngle = angle * 0.45f; l.range = range;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            hd.affectsVolumetric = false;
            return l;
        }

        // a few small rocks between the pilots and the wreck: parallax for the camera moves
        static void NearRocks(Transform parent)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(OYMSetup.MatDir + "/Rock_A.mat");
            var matB = AssetDatabase.LoadAssetAtPath<Material>(OYMSetup.MatDir + "/Rock_B.mat");
            if (mat == null) return;
            var rt = new GameObject("Near Rocks").transform; rt.SetParent(parent, false);
            var spots = new (Vector3 p, float s, int v)[]
            {
                (new Vector3(-4.6f, 2.4f, 9f), 0.32f, 0), (new Vector3(5.4f, -1.4f, 13f), 0.55f, 3), (new Vector3(-8.5f, -2.6f, 21f), 0.9f, 5),
                (new Vector3(3.4f, 3.6f, 25f), 0.7f, 7), (new Vector3(11f, 0.8f, 31f), 1.4f, 9), (new Vector3(-2.2f, -3.4f, 15f), 0.26f, 11),
            };
            var rnd = new System.Random(7);
            foreach (var s in spots)
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{OYMSetup.Gen}/Rocks/rock_{s.v % 12:00}_lod1.oymmesh");
                if (mesh == null) continue;
                var g = new GameObject("NearRock");
                g.transform.SetParent(rt, false);
                g.transform.SetPositionAndRotation(s.p, Quaternion.Euler(rnd.Next(360), rnd.Next(360), rnd.Next(360)));
                g.transform.localScale = Vector3.one * s.s;
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                g.AddComponent<MeshRenderer>().sharedMaterial = (s.v % 2 == 0 || matB == null) ? mat : matB;
                var sp = g.AddComponent<OYM.Spin>();
                sp.axis = new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f);
                sp.degreesPerSecond = 3f + (float)rnd.NextDouble() * 7f;
            }
        }

        // ------------------------------------------------------------------------------------------------ assets
        static void ImportMusic()
        {
            foreach (var p in new[] { ThemePath, BattlePath })
            {
                var imp = AssetImporter.GetAtPath(p) as AudioImporter;
                if (imp == null) { Debug.LogWarning("[OYM-Title] music missing: " + p); continue; }
                var s = imp.defaultSampleSettings;
                // 2-3 minute tracks: stream from a compressed file instead of decompressing ~50 MB each into memory
                if (s.loadType == AudioClipLoadType.Streaming && s.compressionFormat == AudioCompressionFormat.Vorbis && Mathf.Abs(s.quality - 0.7f) < 0.01f && imp.loadInBackground) continue;
                s.loadType = AudioClipLoadType.Streaming;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.7f;
                s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                imp.defaultSampleSettings = s;
                imp.loadInBackground = true;
                imp.SaveAndReimport();
                Debug.Log("[OYM-Title] music import: streaming vorbis " + p);
            }
        }

        static void BuildRoutes()
        {
            if (!AssetDatabase.IsValidFolder(ResDir)) AssetDatabase.CreateFolder(OYMSetup.Root, "Resources");
            var r = AssetDatabase.LoadAssetAtPath<OYM.BgmRoutes>(RoutesPath);
            if (r == null) { r = ScriptableObject.CreateInstance<OYM.BgmRoutes>(); AssetDatabase.CreateAsset(r, RoutesPath); }
            var theme = AssetDatabase.LoadAssetAtPath<AudioClip>(ThemePath);
            var battle = AssetDatabase.LoadAssetAtPath<AudioClip>(BattlePath);
            r.routes = new[]
            {
                new OYM.BgmRoutes.Route { scene = OYM.GameSession.TitleScene, clip = theme, volume = 0.8f, fade = 2.5f },
                new OYM.BgmRoutes.Route { scene = OYM.GameSession.GameScene, clip = battle, volume = 0.7f, fade = 1.2f },
                new OYM.BgmRoutes.Route { scene = "Showcase_DebrisField", clip = battle, volume = 0.7f, fade = 1.2f },
                new OYM.BgmRoutes.Route { scene = "DebrisField", clip = battle, volume = 0.7f, fade = 1.2f },
            };
            EditorUtility.SetDirty(r);
            Debug.Log($"[OYM-Title] BGM routes: theme={(theme ? theme.name : "MISSING")}, battle={(battle ? battle.name : "MISSING")}");
        }

        static Texture2D ImportLogo()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { LogoDir, IconDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter imp)) continue;
                if (imp.textureType == TextureImporterType.Default && imp.alphaIsTransparency && imp.npotScale == TextureImporterNPOTScale.None
                    && imp.textureCompression == TextureImporterCompression.Uncompressed && imp.wrapMode == TextureWrapMode.Clamp && imp.mipmapEnabled) continue;
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = true;
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.alphaIsTransparency = true;
                imp.npotScale = TextureImporterNPOTScale.None;                  // UI image: keep the exact aspect
                imp.mipmapEnabled = true;                                      // drawn ~440 px wide from ~1300 px
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.filterMode = FilterMode.Trilinear;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.maxTextureSize = 2048;
                imp.SaveAndReimport();
                Debug.Log("[OYM-Title] logo import: " + path);
            }
            string want = $"{LogoDir}/oym_logo_{LogoVariant}.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(want);
            if (tex == null) Debug.LogWarning("[OYM-Title] logo missing: " + want);
            return tex;
        }

        static PanelSettings PanelAsset()
        {
            string tss = Dir + "/OYM_RuntimeTheme.tss";
            if (!File.Exists(tss)) { File.WriteAllText(tss, "@import url(\"unity-theme://default\");\n"); AssetDatabase.ImportAsset(tss); }
            string path = Dir + "/OYM_TitlePanel.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (ps == null) { ps = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(ps, path); }
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(tss);
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            EditorUtility.SetDirty(ps);
            return ps;
        }

        static VolumeProfile TitleProfile()
        {
            string path = Dir + "/MainTitle_Volume.asset";
            AssetDatabase.DeleteAsset(path);
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
            var dof = p.Add<DepthOfField>(false);
            dof.name = nameof(DepthOfField);
            AssetDatabase.AddObjectToAsset(dof, p);
            dof.focusMode.Override(DepthOfFieldMode.Manual);
            dof.nearFocusStart.Override(0f); dof.nearFocusEnd.Override(0.25f);   // only the space dust right at the lens
            dof.farFocusStart.Override(2e5f); dof.farFocusEnd.Override(4e5f);    // driven by TitleDirector at runtime
            // custom quality with a small far radius: at the default 8-13 px the star field becomes big hexagon bokeh
            dof.quality.Override(ScalableSettingLevelParameter.LevelCount);
            var so = new SerializedObject(dof);
            void F(string n, float v) { var sp = so.FindProperty(n); sp.FindPropertyRelative("m_OverrideState").boolValue = true; sp.FindPropertyRelative("m_Value").floatValue = v; }
            void I(string n, int v) { var sp = so.FindProperty(n); sp.FindPropertyRelative("m_OverrideState").boolValue = true; sp.FindPropertyRelative("m_Value").intValue = v; }
            F("m_FarMaxBlur", 4f); F("m_NearMaxBlur", 3f); I("m_FarSampleCount", 10); I("m_NearSampleCount", 5);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(p);
            return p;
        }

        // Team repo: the boot scene (index 0) is the team's call, so the title is only appended when missing.
        static void AddToBuildSettings()
        {
            OYMSetup.AddBuildScene(ScenePath);
            if (File.Exists(OYMShowcase.ScenePath)) OYMSetup.AddBuildScene(OYMShowcase.ScenePath);
        }

        // ------------------------------------------------------------------------------------------------ run / check
        [MenuItem("OYM/Title/Open + Play")]
        public static void OpenAndPlay()
        {
            if (!File.Exists(ScenePath)) Build();
            EditorSceneManager.OpenScene(ScenePath);
            var gameView = System.Type.GetType("UnityEditor.GameView,UnityEditor");
            if (gameView != null) { var gv = EditorWindow.GetWindow(gameView); gv.maximized = true; gv.Focus(); }
            EditorApplication.EnterPlaymode();
        }

        public static void BuildAndCapture()
        {
            try
            {
                Build();
                Capture();
                Debug.Log("[OYM-Title] BuildAndCapture finished OK");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[OYM-Title] failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        /// Edit-mode stills of the three resting states (no UI: overlays are not in Camera.Render) -> Screenshots/title/.
        [MenuItem("OYM/Title/Capture Stills")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var dir = Object.FindFirstObjectByType<OYM.TitleDirector>();
            var cam = dir.cam;
            var dof = PrivateDof(dir);
            ForceSkin();
            string outDir = Path.Combine(OYMCapture.OutDir, "title");
            Directory.CreateDirectory(outDir);
            int W = 1920, H = 1080;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); rt.Create();
            cam.targetTexture = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var states = new (string name, int focus)[] { ("t0_neutral", -1), ("t1_focus_kestrel", 0), ("t2_focus_orbit", 1) };
            bool warm = false;
            foreach (var st in states)
            {
                dir.PoseInstant(st.focus, dof);
                foreach (var p in dir.pilots) Debug.Log($"[OYM-Title] {st.name}: {p.displayName} {p.CurrentSlot} at {p.transform.position.ToString("F2")}");
                for (int f = 0; f < (warm ? 40 : 120); f++)
                {
                    foreach (var p in dir.pilots) { p.EvaluatePose(0f); p.ApplyArm(); p.ApplyLook(0f); }
                    cam.Render();
                }
                warm = true;
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, st.name + ".png"), tex.EncodeToPNG());
                Debug.Log("[OYM-Title] captured " + st.name);
            }
            foreach (var p in dir.pilots) p.DisposeGraph();
            cam.targetTexture = null; rt.Release();
        }

        // Edit mode skins a SkinnedMeshRenderer once per editor frame, but a batch capture renders many poses inside one
        // frame: the bodies stayed in the first pose while the sword (a plain child transform of the hand) moved on, so
        // the blade looked detached from KESTREL's hand. Recalculate the skin matrices on every render instead.
        static void ForceSkin()
        {
            foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                smr.forceMatrixRecalculationPerRender = true;
                smr.updateWhenOffscreen = true;
            }
        }

        static DepthOfField PrivateDof(OYM.TitleDirector dir)
        {
            var vol = dir.dofVolume;
            var prof = Object.Instantiate(vol.sharedProfile);                  // never write the stills' focus into the asset
            vol.sharedProfile = prof;
            prof.TryGet(out DepthOfField dof);
            return dof;
        }

        /// KESTREL's focus pose candidates: clip frames held in the front pose, seen through the focus camera
        /// -> Screenshots/title/poses/NN_clip_time.png (make a labelled sheet with pipeline/title/pose_sheet.py).
        public static void PoseSheet()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var dir = Object.FindFirstObjectByType<OYM.TitleDirector>();
            var cam = dir.cam;
            var dof = PrivateDof(dir);
            ForceSkin();
            var hd = cam.GetComponent<HDAdditionalCameraData>();
            if (hd) hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;   // no history between poses
            var k = dir.pilots[0]; var o = dir.pilots[1];
            var clips = AnimClips("KESTREL").ToDictionary(c => c.name);
            Debug.Log("[OYM-Title] KESTREL clips: " + string.Join(", ", clips.Values.Select(c => $"{c.name} {c.length:F2}s")));
            var cand = new (string clip, float t)[]
            {
                ("Slash_Thrust", 0.3f), ("Slash_Thrust", 0.6f), ("Slash_Thrust", 0.9f), ("Slash_Thrust", 1.1f), ("Slash_Thrust", 1.3f), ("Slash_Thrust", 1.45f), ("Slash_Thrust", 1.6f), ("Slash_Thrust", 1.85f),
                ("Slash_Charged", 0.25f), ("Slash_Charged", 0.5f), ("Slash_Charged", 0.75f), ("Slash_Charged", 1.0f), ("Slash_Charged", 1.2f), ("Slash_Charged", 1.4f),
                ("Slash_Left", 0.3f), ("Slash_Left", 0.6f), ("Slash_Left", 0.75f), ("Slash_Left", 1.1f), ("Slash_Left", 1.4f), ("Slash_Left", 1.7f),
                ("CombatIdle", 0.2f), ("CombatIdle", 0.8f), ("CombatIdle", 1.3f), ("DashStop", 0.2f), ("DashStop", 0.5f),
            };
            string outDir = Path.Combine(OYMCapture.OutDir, "title", "poses");
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);
            int W = 960, H = 540;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); rt.Create();
            cam.targetTexture = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            dir.PoseInstant(0, dof);
            int n = 0;
            foreach (var c in cand)
            {
                if (!clips.TryGetValue(c.clip, out var clip)) { Debug.LogWarning("[OYM-Title] no clip " + c.clip); continue; }
                k.DisposeGraph(); k.frontClip = clip; k.frontHold = c.t; k.Snap(TitleCharacter.Slot.Front);
                for (int f = 0; f < (n == 0 ? 30 : 5); f++)
                {
                    k.EvaluatePose(0f); k.ApplyArm(); k.ApplyLook(0f); o.EvaluatePose(0f); o.ApplyArm(); o.ApplyLook(0f);
                    cam.Render();
                }
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, $"{n:00}_{c.clip}_{c.t:F2}.png"), tex.EncodeToPNG());
                n++;
            }
            k.DisposeGraph(); o.DisposeGraph();
            cam.targetTexture = null; rt.Release();
            Debug.Log($"[OYM-Title] pose sheet: {n} poses -> {outDir}");
        }

        /// stills + pose candidates of the saved scene, without rebuilding it
        public static void StillsAndPoses()
        {
            try { Capture(); PoseSheet(); Debug.Log("[OYM-Title] StillsAndPoses finished OK"); }
            catch (System.Exception e)
            {
                Debug.LogError("[OYM-Title] failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        public static void BuildAndPoseSheet()
        {
            try { Build(); Capture(); PoseSheet(); Debug.Log("[OYM-Title] BuildAndPoseSheet finished OK"); }
            catch (System.Exception e)
            {
                Debug.LogError("[OYM-Title] failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }
    }
}
