using System.Collections.Generic;
using OYM.Net;
using OYM.Signal;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace OYM.Field
{
    /// The field kit of the showcase scene (ShowcaseScene.Name): link relays that fix the crossed visor HUD a piece at a
    /// time, and floating heal packs. Installs itself on sceneLoaded like the other game-scene modules (no scene edits,
    /// so it survives the showcase / level rebuilds) and puts everything on the stepping stones of the debris path
    /// ("Debris Path (spawn fragment + stepping stones)/PathStone_NN"):
    ///   relays   on the stone at RelayStep.at along the path, in route order. A pilot whose chest stays within
    ///            relayRadius of the lamp for relayHold seconds calibrates it, and CoopHud.Restore gives that step's
    ///            modules back (by default the minimap, then reserves + skills, the vitals last). Progress leaks away
    ///            when the pilot leaves; nothing runs under the arrival cutscene, level cutaways or the boss entrance.
    ///   packs    float over the packStones: flying through one while hurt gives healAmount HP (HuskEncounter.Heal),
    ///            then it is gone for `respawn` seconds; at full HP it stays.
    /// Co-op: either pilot near a relay calibrates it on both screens and whichever PC finishes first tells the other
    /// (CoopMsg.FieldRelay); the modules come back on both visors (the link is crossed both ways). Packs: each PC
    /// checks its own pilot (the HP is its own) and tells the other (CoopMsg.FieldMed), so a taken pack is gone on both.
    /// Both PCs place the same pieces from the same scene, so the indices match.
    /// Tuning: Resources/OYM_FieldKit (FieldKitConfig). -oymNoField = off. Test run: -fieldTest <folder> (FieldKitTest).
    public sealed class FieldKit : MonoBehaviour
    {
        public static FieldKit Current { get; private set; }

        const string PathRoot = "Debris Path (spawn fragment + stepping stones)";
        static readonly Color Ink = new Color32(0xF3, 0xEE, 0xDD, 255), Lavender = new Color32(0xB7, 0xA6, 0xF6, 255),
                              Lime = new Color32(0xD5, 0xEF, 0x86, 255), HealInk = new Color32(0x8C, 0xF5, 0xA4, 255);

        public FieldKitConfig config;

        readonly List<LinkRelay> _relays = new List<LinkRelay>();
        readonly List<HealPack> _packs = new List<HealPack>();
        HuskFXCanvas _fx;
        CoopSession _session;
        AudioSource _one, _loop;
        float _loopVol;

        UIDocument _doc;
        VisualElement _calib, _barFill;
        Label _calibLabel, _toast, _hint;
        float _calibA, _toastT = -1f, _hintT = -1f, _shownProgress;
        bool _hinted;

        public IReadOnlyList<LinkRelay> Relays => _relays;
        public IReadOnlyList<HealPack> Packs => _packs;

        // ------------------------------------------------------------------------------------------------ install
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { SceneManager.sceneLoaded -= OnLoaded; Current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { SceneManager.sceneLoaded += OnLoaded; }

        static bool Flag(string f) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), f) >= 0;

        /// another session's recorder / demo drives the scene (switch flags -oymNo... and the perf HUD are not automation)
        static bool OtherAutomation()
        {
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a.StartsWith("-oym") && !a.StartsWith("-oymNo") && a != "-oymPerfHud" && a != "-oymCineDemo" && a != "-oymHudDemo") return true;
            return false;
        }

        static void OnLoaded(Scene s, LoadSceneMode mode)
        {
            if (Application.isBatchMode || s.name != ShowcaseScene.Name || Flag("-oymNoField") || OtherAutomation()) return;
            var cfg = Resources.Load<FieldKitConfig>("OYM_FieldKit");
            if (!cfg) { Debug.LogWarning("[OYM-FIELD] Resources/OYM_FieldKit missing: no link relays / heal packs"); return; }
            var go = new GameObject("Field Kit (link relays, heal packs)");
            SceneManager.MoveGameObjectToScene(go, s);
            go.AddComponent<FieldKit>().config = cfg;
            if (FieldKitTest.Asked) go.AddComponent<FieldKitTest>();
        }

        void Awake() { Current = this; }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_session) _session.Message -= OnCoopMessage;
            if (_fx != null) { _fx.Destroy(); _fx = null; }
        }

        // ------------------------------------------------------------------------------------------------ layout
        void Start()
        {
            var stones = Stones(out Vector3 fwd);
            if (stones.Count < 2) { Debug.LogWarning($"[OYM-FIELD] no stepping stones under '{PathRoot}': no field kit"); enabled = false; return; }
            var mats = FxMaterials();
            if (mats != null) _fx = new HuskFXCanvas(transform, mats);
            else Debug.LogWarning("[OYM-FIELD] no additive FX materials (Resources/OYM_HuskEncounter dart prefab): lamps are lights only");

            Physics.SyncTransforms();
            var used = new HashSet<Transform>();
            var relayLog = new List<string>();
            if (config.relays != null)
                for (int i = 0; i < config.relays.Length; i++)
                {
                    var step = config.relays[i];
                    if (step == null) continue;
                    var stone = stones[Mathf.Clamp(Mathf.RoundToInt(step.at * (stones.Count - 1)), 0, stones.Count - 1)];
                    bool onRock = Spot(stone, 1.2f, out Vector3 at, out Vector3 n);
                    var r = LinkRelay.Build(config, transform, at, onRock ? n : Vector3.up, -fwd, _relays.Count, step);
                    _relays.Add(r);
                    used.Add(stone);
                    relayLog.Add($"{stone.name}{(onRock ? "" : " (bounds top)")} -> modules {string.Join("+", step.modules ?? new int[0])}");
                }
            Physics.SyncTransforms();
            var byNumber = new Dictionary<int, Transform>();
            foreach (var s in stones)
                if (int.TryParse(s.name.Substring("PathStone_".Length), out int k)) byNumber[k] = s;
            var packLog = new List<string>();
            if (config.packStones != null)
                foreach (int k in config.packStones)
                {
                    if (!byNumber.TryGetValue(k, out var stone)) continue;
                    bool onRock = Spot(stone, 0.8f, out Vector3 at, out _, config.packHover);
                    _packs.Add(HealPack.Build(config, transform, at + Vector3.up * config.packHover, _packs.Count));
                    packLog.Add(stone.name + (onRock ? "" : " (bounds top)"));
                }
            BuildUI();
            BuildAudio();
            HookCoop();
            var e = HuskEncounter.Current;
            Debug.Log($"[OYM-FIELD] {_relays.Count} link relays [{string.Join("; ", relayLog)}], {_packs.Count} heal packs [{string.Join(", ", packLog)}], " +
                      $"heal {config.healAmount:F0} HP, back in {config.respawn:F0} s, pilot HP {(e && e.config ? e.config.playerHP.ToString("F0") : "?")}" +
                      (_session ? " (co-op: synced)" : ""));
        }

        /// the stepping stones in path order and the path's forward direction (spawn fragment -> last stone, flat)
        static List<Transform> Stones(out Vector3 fwd)
        {
            var list = new List<Transform>();
            fwd = Vector3.forward;
            var root = GameObject.Find(PathRoot);
            if (!root) return list;
            foreach (Transform t in root.transform)
                if (t.name.StartsWith("PathStone_") && t.gameObject.activeInHierarchy) list.Add(t);
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            var spawn = root.transform.Find("SpawnFragment");
            Vector3 origin = spawn ? spawn.position : list.Count > 0 ? list[0].position : Vector3.zero;
            if (list.Count > 0)
            {
                Vector3 f = list[list.Count - 1].position - origin; f.y = 0f;
                if (f.sqrMagnitude > 1f) fwd = f.normalized;
            }
            return list;
        }

        /// A flat spot on top of `stone` near its middle with `clear` metres free above it (at `lift` over the surface):
        /// a spiral of downward rays where only hits on the stone's own collider count, so props already sitting on it
        /// are stepped round. False = no usable hit (the bounds top centre comes back).
        static bool Spot(Transform stone, float clear, out Vector3 point, out Vector3 normal, float lift = 0f)
        {
            normal = Vector3.up;
            var cols = stone.GetComponentsInChildren<Collider>();
            var rend = stone.GetComponent<Renderer>();
            Bounds b = rend ? rend.bounds : new Bounds(stone.position, Vector3.one * 4f);
            point = new Vector3(b.center.x, b.max.y, b.center.z);
            if (cols.Length == 0) return false;
            float top = b.max.y + 4f, len = b.size.y + 10f;
            float maxR = Mathf.Max(1f, Mathf.Min(b.extents.x, b.extents.z) * 0.8f);
            for (int ring = 0; ring <= 8; ring++)
            {
                float rad = ring * 1.1f;
                if (ring > 0 && rad > maxR) break;
                int n = ring == 0 ? 1 : 6 * ring;
                for (int k = 0; k < n; k++)
                {
                    float a = (k + 0.5f * (ring % 2)) * Mathf.PI * 2f / n;
                    var o = new Vector3(b.center.x + Mathf.Cos(a) * rad, top, b.center.z + Mathf.Sin(a) * rad);
                    if (!Physics.Raycast(o, Vector3.down, out var hit, len, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (System.Array.IndexOf(cols, hit.collider) < 0) continue;      // a prop on the stone is in the way
                    if (hit.normal.y < 0.8f) continue;
                    Vector3 q = hit.point + Vector3.up * (Mathf.Max(lift, clear + 0.25f));
                    if (Physics.CheckSphere(q, clear, ~0, QueryTriggerInteraction.Ignore)) continue;
                    point = hit.point; normal = hit.normal;
                    return true;
                }
            }
            return false;
        }

        static HuskFXMaterials FxMaterials()
        {
            var cfg = Resources.Load<HuskEncounterConfig>("OYM_HuskEncounter");
            var m = cfg && cfg.dartPrefab ? cfg.dartPrefab.materials : null;
            return m != null && m.glow && m.dot && m.streak && m.arc ? m : null;
        }

        // ------------------------------------------------------------------------------------------------ co-op
        void HookCoop()
        {
            if (!CoopSession.InGame) return;
            _session = CoopSession.Active;
            if (_session) _session.Message += OnCoopMessage;
        }

        void OnCoopMessage(CoopMsg.R r)
        {
            if (r.Type == CoopMsg.FieldRelay) CompleteRelay(r.U8(), false);
            else if (r.Type == CoopMsg.FieldMed) TakePack(r.U8(), false, 0f);
        }

        void Send(byte type, int index)
        {
            if (_session && _session.Connected && CoopSession.InGame) _session.Send(new CoopMsg.W(type).U8(index).Done());
        }

        // ------------------------------------------------------------------------------------------------ loop
        static Vector3 ChestOf(HuskEncounter e, Transform t)
        {
            if (e) return e.ChestOf(t);
            var f = t ? t.GetComponent<ZeroGFlight>() : null;
            return t ? t.position + Vector3.up * (f ? f.bodyCenter + 0.15f : 1.15f) : Vector3.zero;
        }

        void Update()
        {
            float dt = Time.deltaTime, now = Time.time;
            var e = HuskEncounter.Current;
            bool view = SignalInstall.PlayerView(e);
            var me = FlightCamera.Main ? FlightCamera.Main.target : null;
            bool alive = !e || e.PlayerAlive;
            Vector3 chest = me ? ChestOf(e, me) : Vector3.zero;
            Transform partner = e != null && e.Remote != null && e.Remote.t && e.Remote.alive && e.Remote.t.gameObject.activeInHierarchy ? e.Remote.t : null;
            float r2 = config.relayRadius * config.relayRadius;

            // relays: either pilot in reach calibrates (the partner's PC does the same and the first to finish tells)
            LinkRelay active = null;
            bool mine = false;
            for (int i = 0; i < _relays.Count; i++)
            {
                var r = _relays[i];
                r.Tick(dt);
                if (r.Done) continue;
                Transform who = null;
                if (view)
                {
                    if (me && alive && (chest - r.lamp).sqrMagnitude <= r2) who = me;
                    else if (partner && (ChestOf(e, partner) - r.lamp).sqrMagnitude <= r2) who = partner;
                }
                r.Calibrator = who;
                if (who)
                {
                    r.CalibratorChest = who == me ? chest : ChestOf(e, partner);
                    r.Progress = Mathf.Min(1f, r.Progress + dt / Mathf.Max(0.1f, config.relayHold));
                    if (active == null || who == me) { active = r; mine = who == me; }
                    if (r.Progress >= 1f) { CompleteRelay(i, true); continue; }
                }
                else r.Progress = Mathf.Max(0f, r.Progress - dt / 0.8f);
                // the first time a pilot comes near a relay while the link is crossed: a line says what it is for
                if (!_hinted && me && view && CoopHud.Current && CoopHud.Current.Crossed &&
                    (chest - r.lamp).sqrMagnitude < config.relayHintRange * config.relayHintRange)
                {
                    _hinted = true;
                    _hint.text = "링크 보정기  ·  곁에 잠깐 머무르면 꼬인 HUD가 하나씩 돌아옵니다";
                    _hintT = 0f;
                }
            }

            // heal packs: this PC's pilot only (the HP is this PC's); at full HP they stay
            for (int i = 0; i < _packs.Count; i++)
            {
                var p = _packs[i];
                p.Tick(dt, now);
                if (!p.Available || !me || !alive || !view || !e || !e.PlayerHurt) continue;
                if ((chest - p.Center).sqrMagnitude > config.packRadius * config.packRadius) continue;
                float got = e.Heal(config.healAmount);
                if (got > 0f) { TakePack(i, true, got); Glow(me, chest); }
            }

            Prompts(dt, view, active, mine);
            LoopSound(dt, active, mine);
        }

        void LateUpdate()
        {
            var cam = FlightCamera.Main && FlightCamera.Main.Cam ? FlightCamera.Main.Cam.transform.position : Camera.main ? Camera.main.transform.position : Vector3.zero;
            float now = Time.time;
            bool fx = _fx != null && _fx.Valid;
            if (fx) _fx.Begin();
            foreach (var r in _relays) if (r) r.Draw(fx ? _fx : null, now, cam);
            foreach (var p in _packs) if (p) p.Draw(fx ? _fx : null, now, cam);
            if (fx) _fx.End();
        }

        // ------------------------------------------------------------------------------------------------ events
        void CompleteRelay(int i, bool here)
        {
            if (i < 0 || i >= _relays.Count) return;
            var r = _relays[i];
            if (r.Done) return;
            r.Complete();
            var hud = CoopHud.Current;
            if (hud) hud.Restore(r.step.modules, r.step.title, r.step.body);
            Play(config.relayDone, config.relayDoneVolume);
            if (here) Send(CoopMsg.FieldRelay, i);
            Debug.Log($"[OYM-FIELD] relay {i} calibrated {(here ? "here" : "on the partner's PC")}: visor modules {string.Join("+", r.step.modules ?? new int[0])} back");
        }

        void TakePack(int i, bool mine, float got)
        {
            if (i < 0 || i >= _packs.Count) return;
            var p = _packs[i];
            bool wasThere = p.Available;
            p.Take(config.respawn);
            if (mine)
            {
                Play(config.healTaken, config.healVolume);
                _toast.text = $"힐팩  +{Mathf.RoundToInt(got)}";
                _toastT = 0f;
                Send(CoopMsg.FieldMed, i);
                var e = HuskEncounter.Current;
                Debug.Log($"[OYM-FIELD] heal pack {i} taken: +{got:F0} HP (now {(e ? e.PlayerHP01 * 100f : 0f):F0}%), back in {config.respawn:F0} s");
            }
            else
            {
                if (wasThere && config.healTaken) AudioSource.PlayClipAtPoint(config.healTaken, p.Center, config.healVolume);
                Debug.Log($"[OYM-FIELD] heal pack {i} taken by the partner");
            }
        }

        /// a soft green swell over the suit (HitFlash, capped like every body flash)
        static void Glow(Transform pilot, Vector3 chest)
        {
            var hf = pilot.GetComponent<HitFlash>();
            if (!hf)
            {
                hf = pilot.gameObject.AddComponent<HitFlash>();
                hf.renderers = pilot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            }
            hf.AddHit(chest, new Color(0.45f, 3.6f, 1.2f), 0.9f, 0.2f);
        }

        // ------------------------------------------------------------------------------------------------ sound
        void BuildAudio()
        {
            _one = gameObject.AddComponent<AudioSource>();
            _one.playOnAwake = false; _one.spatialBlend = 0f;
            _loop = gameObject.AddComponent<AudioSource>();
            _loop.playOnAwake = false; _loop.spatialBlend = 0f; _loop.loop = true; _loop.volume = 0f;
            _loop.clip = config.calibrateLoop;
        }

        void Play(AudioClip c, float volume) { if (c && _one) _one.PlayOneShot(c, volume); }

        void LoopSound(float dt, LinkRelay active, bool mine)
        {
            if (!_loop || !_loop.clip) return;
            float goal = active != null ? config.calibrateVolume * (mine ? 1f : 0.45f) : 0f;
            _loopVol = Mathf.MoveTowards(_loopVol, goal, dt * 4f);
            _loop.volume = _loopVol;
            if (active != null) _loop.pitch = Mathf.Lerp(0.94f, 1.12f, active.Progress);
            if (_loopVol > 0.001f && !_loop.isPlaying) _loop.Play();
            else if (_loopVol <= 0.001f && _loop.isPlaying) _loop.Stop();
        }

        // ------------------------------------------------------------------------------------------------ prompts
        void BuildUI()
        {
            _doc = SignalUI.Document(transform, "field prompts", 4.5f);
            var root = _doc.rootVisualElement;

            // calibration: one line + a thin bar under the crosshair, in the visor's colours
            _calib = new VisualElement { pickingMode = PickingMode.Ignore };
            _calib.style.position = Position.Absolute; _calib.style.left = 0; _calib.style.right = 0; _calib.style.top = Length.Percent(61);
            _calib.style.alignItems = Align.Center; _calib.style.opacity = 0f;
            _calibLabel = SignalUI.Text(22f, Ink, 2, true);
            _calibLabel.text = "링크 보정 중";
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.style.width = 300; track.style.height = 4; track.style.marginTop = 10;
            track.style.backgroundColor = new Color(Lavender.r, Lavender.g, Lavender.b, 0.28f);
            _barFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _barFill.style.height = 4; _barFill.style.width = 0; _barFill.style.backgroundColor = Lime;
            track.Add(_barFill);
            _calib.Add(_calibLabel); _calib.Add(track);
            root.Add(_calib);

            // heal pack taken: "힐팩 +60", pops in under the crosshair
            _toast = SignalUI.Text(30f, HealInk, 3, true);
            _toast.style.position = Position.Absolute; _toast.style.left = 0; _toast.style.right = 0; _toast.style.top = Length.Percent(55);
            _toast.style.unityTextAlign = TextAnchor.MiddleCenter; _toast.style.opacity = 0f;
            root.Add(_toast);

            // the first relay in sight: what it is for (once)
            _hint = SignalUI.Text(24f, Ink, 1, true);
            _hint.style.position = Position.Absolute; _hint.style.left = 0; _hint.style.right = 0; _hint.style.top = Length.Percent(30);
            _hint.style.unityTextAlign = TextAnchor.MiddleCenter; _hint.style.opacity = 0f;
            root.Add(_hint);
        }

        void Prompts(float dt, bool view, LinkRelay active, bool mine)
        {
            if (_calib == null) return;
            bool on = active != null && view;
            _calibA = Mathf.MoveTowards(_calibA, on ? 1f : 0f, dt / (on ? 0.12f : 0.35f));
            _calib.style.opacity = _calibA;
            if (active != null)
            {
                _shownProgress = active.Progress;
                string who = mine || !active.Calibrator ? "" : SignalInstall.NameOf(active.Calibrator.GetComponent<ZeroGFlight>()) + "  ";
                _calibLabel.text = who + "링크 보정 중";
                _calibLabel.style.color = Color.Lerp(Ink, Lime, _shownProgress);
            }
            _barFill.style.width = 300f * _shownProgress;

            if (_toastT >= 0f)
            {
                _toastT += dt;
                float a = _toastT < 0.1f ? _toastT / 0.1f : _toastT > 1.2f ? 1f - (_toastT - 1.2f) / 0.5f : 1f;
                float y = 14f * (1f - HuskFX.Snap(Mathf.Clamp01(_toastT / 0.22f), 0.15f));
                _toast.style.opacity = view ? Mathf.Clamp01(a) : 0f;
                _toast.style.translate = new Translate(0f, y);
                if (_toastT > 1.7f) { _toastT = -1f; _toast.style.opacity = 0f; }
            }
            if (_hintT >= 0f)
            {
                _hintT += dt;
                float a = _hintT < 0.25f ? _hintT / 0.25f : _hintT > 4.5f ? 1f - (_hintT - 4.5f) / 0.6f : 1f;
                _hint.style.opacity = view ? Mathf.Clamp01(a) : 0f;
                if (_hintT > 5.1f) { _hintT = -1f; _hint.style.opacity = 0f; }
            }
        }
    }
}
