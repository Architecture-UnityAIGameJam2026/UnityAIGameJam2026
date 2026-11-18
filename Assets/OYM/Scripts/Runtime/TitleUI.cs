using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace OYM
{
    /// Main title overlay (UI Toolkit, Title/MainTitle.uxml + .uss): exactly the user's 11-18 sketch, no small text.
    ///   top left     generated logo (wipe-in with a scan line)
    ///   top right    Discord link
    ///   right        co-op circle -> friend code flyout (your code + COPY, a friend's code + JOIN)
    ///   bottom right pilot icons (white glyphs, image-gen): the selected pilot big on top, the other small below
    ///                (click = switch)
    ///   bottom left  GAME START / OPTION / EXIT (mouse hover or W/S + ENTER), OPTIONS panel, quit dialog
    ///   bottom       a line with the team logo (Team Architecture, AI chip mark)
    /// The 3D side (pilot swap, camera, deploy) is TitleDirector: it forwards keys (Navigate / Submit / Cancel /
    /// ToggleFriend) and listens to DeployClicked / PilotClicked. No Buttons (their runtime theme fights the look):
    /// clickable things are VisualElements with the "pick" class and ClickEvent callbacks.
    /// Font: Bahnschrift from the OS when it is there, else Segoe UI, else Unity's built-in font.
    [RequireComponent(typeof(UIDocument))]
    public class TitleUI : MonoBehaviour
    {
        [System.Serializable]
        public class PilotInfo
        {
            public string code = "K-07", name = "KESTREL", role = "BLADE", description = "", weapon = "", controls = "";
            public string accentClass = "accent-kestrel";
            [Tooltip("white glyph icon for the pilot select (Title/Icons)")]
            public Texture2D icon;
            [Range(0f, 1f)] public float[] stats = { 0.5f, 0.5f, 0.5f, 0.5f };
        }

        public enum Mode { Intro, Main, Options, Friend, Exit, Deploy }

        public PilotInfo[] pilots;
        public string[] statNames = { "MOBILITY", "POWER", "RANGE", "ARMOR" };
        [Tooltip("title logo (Title/Logo/oym_logo_*.png, straight alpha)")]
        public Texture2D logo;
        [Tooltip("team logo in the middle of the bottom line (Title/Icons/team_logo.png)")]
        public Texture2D teamLogo;
        [Tooltip("Discord invite link (https://discord.gg/...). Empty = the button says the link is not set yet.")]
        public string discordUrl = "";

        public event System.Action DeployClicked;
        public event System.Action<int> PilotClicked;

        /// scripted demo: the real mouse over the editor window (hover or a click on a pilot icon) must not change
        /// anything, so nothing catches the pointer while this is on
        public bool scripted { get => _scripted; set { _scripted = value; if (_ready) RefreshPicking(); } }
        bool _scripted;

        public Mode CurrentMode => _mode;
        /// may a click on a pilot in the 3D scene switch pilots right now
        public bool AllowsPilotPick => _mode == Mode.Main;
        /// the friend-code field has the keyboard (also true for the frame after it let go, so its ESC / ENTER is not
        /// read a second time by the director)
        public bool IsTyping => FieldFocused || Time.frameCount - _typingFrame <= 1;

        // pilot icon tiles (sketch: B big on top, A smaller below, both against the right edge)
        const float CardBig = 136f, CardSmall = 92f, CardGap = 12f;
        static readonly Color IconWhite = new Color(0.93f, 0.95f, 0.96f, 1f);
        static readonly Color Blurple = new Color(0.345f, 0.396f, 0.949f, 1f);

        UIDocument _doc;
        VisualElement _root, _title, _chrome, _logo, _logoImage, _menu, _cursor, _options, _optRows, _friend, _friendBtn, _friendPanel;
        VisualElement _pilotsBox, _cards, _hints, _modal, _fade, _deployFlash, _discord, _copy, _join, _quit, _cancel;
        Label _toast, _fpCode, _fpStatus, _fpPlaceholder;
        VisualElement _deployRow;
        TextField _field;
        TitleIcon _discordIcon, _friendIcon;
        readonly List<VisualElement> _menuItems = new List<VisualElement>();
        readonly List<Card> _cardList = new List<Card>();
        readonly List<OptRow> _rows = new List<OptRow>();
        Mode _mode = Mode.Intro;
        int _shown = -2, _menuSel, _optSel, _exitSel = 1, _typingFrame = -10;
        string _myCode = "";
        IVisualElementScheduledItem _toastJob, _joinJob, _copyJob;
        bool _ready;

        static FontDefinition? s_font;

        class Card { public VisualElement root; }

        class OptRow
        {
            public string label;
            public bool toggle, back;
            public float min, max = 1f, step = 0.05f;
            public System.Func<float> get;
            public System.Action<float> set;
            public System.Func<float, string> fmt;
            public VisualElement row, fill, knob;
            public Label value, on, off;
        }

        void OnEnable() { _ready = false; Init(); }

        bool Init()
        {
            if (_ready) return true;
            if (!_doc) _doc = GetComponent<UIDocument>();
            _root = _doc ? _doc.rootVisualElement : null;
            _title = _root?.Q("title-root");
            if (_title == null) return false;

            _chrome = _root.Q("chrome"); _logo = _root.Q("logo"); _logoImage = _root.Q("logo-image");
            _menu = _root.Q("menu"); _cursor = _root.Q("menu-cursor");
            _options = _root.Q("options"); _optRows = _root.Q("opt-rows");
            _friend = _root.Q("friend"); _friendBtn = _root.Q("btn-friend"); _friendPanel = _root.Q("friend-panel");
            _pilotsBox = _root.Q("pilots"); _cards = _root.Q("cards");
            _hints = _root.Q("hints");
            _modal = _root.Q("modal"); _fade = _root.Q("fade"); _deployFlash = _root.Q("deploy");
            _toast = _root.Q<Label>("toast"); _deployRow = _root.Q("deploy-row");
            _discord = _root.Q("btn-discord");
            _fpCode = _root.Q<Label>("fp-code"); _fpStatus = _root.Q<Label>("fp-status"); _fpPlaceholder = _root.Q<Label>("fp-placeholder");
            _copy = _root.Q("btn-copy"); _join = _root.Q("btn-join"); _quit = _root.Q("btn-quit"); _cancel = _root.Q("btn-cancel");
            _field = _root.Q<TextField>("fp-field");

            if (_logoImage != null && logo) _logoImage.style.backgroundImage = new StyleBackground(logo);
            var team = _root.Q("team-logo");
            if (team != null && teamLogo) { team.style.backgroundImage = new StyleBackground(teamLogo); _root.Q("hints-mark")?.AddToClassList("hidden-mark"); }
            else team?.AddToClassList("hidden-mark");
            AddIcons();
            BuildMenu();
            BuildCards();
            BuildOptions();
            BindFriend();
            BindModal();
            BindDiscord();

            var font = Font();
            if (font.HasValue) _root.Query<TextElement>().ForEach(e => e.style.unityFontDefinition = font.Value);
            _ready = true;
            SetMode(Mode.Intro);
            return true;
        }

        void AddIcons()
        {
            var ds = _root.Q("discord-icon-slot");
            if (ds != null && _discordIcon == null) ds.Add(_discordIcon = new TitleIcon(TitleIcon.Kind.Discord));
            var fs = _root.Q("friend-icon-slot");
            if (fs != null && _friendIcon == null) fs.Add(_friendIcon = new TitleIcon(TitleIcon.Kind.Friends));
        }


        // ------------------------------------------------------------------------------------------------ menu
        void BuildMenu()
        {
            _menuItems.Clear();
            for (int i = 0; ; i++)
            {
                var it = _root.Q("mi-" + i);
                if (it == null) break;
                int idx = i;
                _menuItems.Add(it);
                it.RegisterCallback<PointerEnterEvent>(_ => { if (_mode == Mode.Main && !scripted) SelectMenu(idx); });
                it.RegisterCallback<ClickEvent>(_ => { if (_mode == Mode.Main) ActivateMenu(idx); });
            }
            _menu?.RegisterCallback<GeometryChangedEvent>(_ => PlaceCursor());
            SelectMenu(0);
        }

        void SelectMenu(int i)
        {
            int n = _menuItems.Count;
            if (n == 0) return;
            i = ((i % n) + n) % n;
            _menuSel = i;
            for (int k = 0; k < n; k++) Toggle(_menuItems[k], "menu-item--sel", k == i);
            PlaceCursor();
        }

        void PlaceCursor()
        {
            if (_cursor == null || _menuSel >= _menuItems.Count) return;
            var r = _menuItems[_menuSel].layout;
            if (float.IsNaN(r.y) || r.height <= 0f) return;
            _cursor.style.top = r.y;
            _cursor.style.height = r.height;
        }

        void ActivateMenu(int i)
        {
            SelectMenu(i);
            Pulse(_menuItems[i], "menu-item--press", 170);
            Pulse(_cursor, "menu-cursor--press", 170);
            switch (i)
            {
                case 0: DeployClicked?.Invoke(); break;
                case 1: OpenOptions(); break;
                default: OpenExit(); break;
            }
        }

        // ------------------------------------------------------------------------------------------------ pilot cards
        void BuildCards()
        {
            if (_cards == null || pilots == null) return;
            _cards.Clear(); _cardList.Clear();
            for (int i = 0; i < pilots.Length; i++)
            {
                var p = pilots[i];
                int idx = i;
                var c = new Card { root = El("card") };
                var b = c.root;
                b.AddToClassList("pick"); b.AddToClassList(p.accentClass);
                var icon = El("card-icon");
                if (p.icon) icon.style.backgroundImage = new StyleBackground(p.icon);
                else icon.Add(L(p.name, "card-name"));                          // no icon yet: the name stands in
                b.Add(icon);
                b.Add(El("card-bar"));
                b.RegisterCallback<ClickEvent>(_ => { if (_mode == Mode.Main || _mode == Mode.Friend) PilotClicked?.Invoke(idx); });
                Place(b, false, i);
                _cards.Add(b);
                _cardList.Add(c);
            }
            _cards.style.height = CardBig + Mathf.Max(0, pilots.Length - 1) * (CardSmall + CardGap);
            _cards.style.width = CardBig;
        }

        static void Place(VisualElement b, bool big, int slot)
        {
            b.style.top = big ? 0f : CardBig + CardGap + slot * (CardSmall + CardGap);
            b.style.left = big ? 0f : CardBig - CardSmall;
            b.style.width = big ? CardBig : CardSmall;
            b.style.height = big ? CardBig : CardSmall;
        }

        void LayoutCards(int sel)
        {
            int k = 0;
            for (int i = 0; i < _cardList.Count; i++)
            {
                var c = _cardList[i];
                bool on = i == sel;
                Toggle(c.root, "card--sel", on);
                Place(c.root, on, on ? 0 : k++);
                if (on) { c.root.BringToFront(); Pulse(c.root, "card--flash", 200); }
            }
        }

        // ------------------------------------------------------------------------------------------------ options
        void BuildOptions()
        {
            if (_optRows == null) return;
            _optRows.Clear(); _rows.Clear();
            AddSlider("MASTER VOLUME", 0f, 1f, 0.05f, () => GameSettings.Master, v => GameSettings.Master = v, v => Mathf.RoundToInt(v * 100f).ToString());
            AddSlider("MUSIC VOLUME", 0f, 1f, 0.05f, () => GameSettings.Music, v => GameSettings.Music = v, v => Mathf.RoundToInt(v * 100f).ToString());
            AddToggle("MUSIC", () => BgmPlayer.Muted ? 0f : 1f, v => BgmPlayer.Muted = v < 0.5f);
            AddToggle("FULLSCREEN", () => GameSettings.Fullscreen ? 1f : 0f, v => GameSettings.Fullscreen = v > 0.5f);
            AddSlider("MOUSE SENSITIVITY", 0.2f, 3f, 0.1f, () => GameSettings.Sensitivity, v => GameSettings.Sensitivity = v, v => v.ToString("0.0"));
            var back = NewRow(new OptRow { label = "BACK", back = true });
            back.AddToClassList("opt-row--back");
            back.RegisterCallback<ClickEvent>(_ => { if (_mode == Mode.Options) CloseOptions(); });
            RefreshRows();
        }

        VisualElement NewRow(OptRow r)
        {
            int idx = _rows.Count;
            var row = El("opt-row");
            row.AddToClassList("pick");
            row.Add(El("opt-row-bar"));
            row.Add(L(r.label, "opt-label"));
            row.RegisterCallback<PointerEnterEvent>(_ => { if (_mode == Mode.Options && _optSel != idx && !scripted) { _optSel = idx; RefreshRows(); } });
            r.row = row;
            _rows.Add(r);
            _optRows.Add(row);
            return row;
        }

        void AddSlider(string label, float min, float max, float step, System.Func<float> get, System.Action<float> set, System.Func<float, string> fmt)
        {
            var r = new OptRow { label = label, min = min, max = max, step = step, get = get, set = set, fmt = fmt };
            var row = NewRow(r);
            var hit = El("opt-hit"); hit.AddToClassList("pick");
            var track = El("opt-track");
            r.fill = El("opt-fill"); r.knob = El("opt-knob");
            track.Add(r.fill); track.Add(r.knob); hit.Add(track);
            row.Add(hit);
            r.value = L("", "opt-value");
            row.Add(r.value);
            hit.RegisterCallback<PointerDownEvent>(e => { if (_mode != Mode.Options) return; hit.CapturePointer(e.pointerId); SetFromX(r, hit, e.localPosition.x); });
            hit.RegisterCallback<PointerMoveEvent>(e => { if (hit.HasPointerCapture(e.pointerId)) SetFromX(r, hit, e.localPosition.x); });
            hit.RegisterCallback<PointerUpEvent>(e => { if (hit.HasPointerCapture(e.pointerId)) hit.ReleasePointer(e.pointerId); });
        }

        void AddToggle(string label, System.Func<float> get, System.Action<float> set)
        {
            var r = new OptRow { label = label, toggle = true, get = get, set = set };
            var row = NewRow(r);
            var box = El("opt-toggle");
            r.on = L("ON", "opt-seg"); r.off = L("OFF", "opt-seg");
            foreach (var (seg, val) in new[] { (r.on, 1f), (r.off, 0f) })
            {
                seg.AddToClassList("pick");
                seg.RegisterCallback<ClickEvent>(_ => { if (_mode == Mode.Options) { r.set(val); RefreshRows(); } });
                box.Add(seg);
            }
            row.Add(box);
        }

        void SetFromX(OptRow r, VisualElement hit, float x)
        {
            float w = hit.layout.width;
            if (!(w > 1f)) return;
            float v = Mathf.Lerp(r.min, r.max, Mathf.Clamp01(x / w));
            v = Mathf.Clamp(Mathf.Round(v / r.step) * r.step, r.min, r.max);
            r.set(v);
            RefreshRows();
        }

        void Adjust(int dir)
        {
            if (_optSel < 0 || _optSel >= _rows.Count) return;
            var r = _rows[_optSel];
            if (r.back) return;
            float v = r.get();
            if (r.toggle) r.set(v > 0.5f ? 0f : 1f);
            else r.set(Mathf.Clamp(Mathf.Round((v + dir * r.step) / r.step) * r.step, r.min, r.max));
            RefreshRows();
        }

        void RefreshRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                Toggle(r.row, "opt-row--sel", i == _optSel);
                if (r.back) continue;
                float v = r.get();
                if (r.toggle)
                {
                    Toggle(r.on, "opt-seg--on", v > 0.5f);
                    Toggle(r.off, "opt-seg--on", v <= 0.5f);
                }
                else
                {
                    float t = Mathf.InverseLerp(r.min, r.max, v);
                    r.fill.style.width = Length.Percent(t * 100f);
                    r.knob.style.left = Length.Percent(t * 100f);
                    r.value.text = r.fmt(v);
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ friend code
        void BindFriend()
        {
            _myCode = GameSettings.FriendCode;
            SetText(_fpCode, FormatCode(_myCode));
            if (_friendBtn != null)
            {
                _friendBtn.RegisterCallback<ClickEvent>(_ => ToggleFriend());
                _friendBtn.RegisterCallback<PointerEnterEvent>(_ => _friend?.AddToClassList("friend--hover"));
                _friendBtn.RegisterCallback<PointerLeaveEvent>(_ => _friend?.RemoveFromClassList("friend--hover"));
            }
            _copy?.RegisterCallback<ClickEvent>(_ => CopyCode());
            _join?.RegisterCallback<ClickEvent>(_ => Join());
            if (_field == null) return;
            _field.maxLength = GameSettings.CodeLength + 1;
            _field.RegisterValueChangedCallback(e =>
            {
                string f = FormatCode(CleanCode(e.newValue));
                if (f != e.newValue) { _field.SetValueWithoutNotify(f); _field.SelectRange(f.Length, f.Length); }
                if (_fpPlaceholder != null) _fpPlaceholder.style.display = f.Length == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            });
            _field.RegisterCallback<KeyDownEvent>(e =>
            {
                _typingFrame = Time.frameCount;
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Join(); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.Escape) { CloseFriend(); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);
        }

        static string CleanCode(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(GameSettings.CodeLength);
            foreach (char ch in s.ToUpperInvariant())
                if (((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9')) && sb.Length < GameSettings.CodeLength) sb.Append(ch);
            return sb.ToString();
        }

        static string FormatCode(string c) => c.Length > 4 ? c.Substring(0, 4) + "-" + c.Substring(4) : c;

        void CopyCode()
        {
            if (_mode != Mode.Friend) return;
            GUIUtility.systemCopyBuffer = FormatCode(_myCode);
            if (_copy is Label l)
            {
                l.text = "COPIED"; l.AddToClassList("sbtn--done");
                _copyJob?.Pause();
                _copyJob = l.schedule.Execute(() => { l.text = "COPY"; l.RemoveFromClassList("sbtn--done"); }).StartingIn(1400);
            }
        }

        void Join()
        {
            if (_mode != Mode.Friend) return;
            string code = CleanCode(_field != null ? _field.value : "");
            _joinJob?.Pause();
            if (!GameSettings.IsValidCode(code)) { SetStatus("ENTER ALL 8 CHARACTERS OF YOUR FRIEND'S CODE", "fp-status--err"); return; }
            if (code == _myCode) { SetStatus("THAT IS YOUR OWN CODE", "fp-status--err"); return; }
            SetStatus("CONNECTING TO " + FormatCode(code) + " ...", "fp-status--busy");
            if (_fpStatus != null)
                _joinJob = _fpStatus.schedule.Execute(() => SetStatus("CO-OP SERVICE IS NOT CONNECTED IN THIS BUILD", "fp-status--err")).StartingIn(1600);
        }

        void SetStatus(string s, string cls)
        {
            if (_fpStatus == null) return;
            _fpStatus.text = s;
            _fpStatus.style.display = string.IsNullOrEmpty(s) ? DisplayStyle.None : DisplayStyle.Flex;
            _fpStatus.RemoveFromClassList("fp-status--err");
            _fpStatus.RemoveFromClassList("fp-status--busy");
            if (!string.IsNullOrEmpty(cls)) _fpStatus.AddToClassList(cls);
        }

        /// scripted demo: type into the friend-code field
        public void SetFriendInput(string s) { if (Init() && _field != null) _field.value = s; }

        // ------------------------------------------------------------------------------------------------ modal / discord
        void BindModal()
        {
            _quit?.RegisterCallback<PointerEnterEvent>(_ => { if (scripted) return; _exitSel = 0; RefreshModal(); });
            _cancel?.RegisterCallback<PointerEnterEvent>(_ => { if (scripted) return; _exitSel = 1; RefreshModal(); });
            _quit?.RegisterCallback<ClickEvent>(_ => { if (_mode == Mode.Exit) Quit(); });
            _cancel?.RegisterCallback<ClickEvent>(_ => CloseExit());
            RefreshModal();
        }

        void RefreshModal() { Toggle(_quit, "mbtn--sel", _exitSel == 0); Toggle(_cancel, "mbtn--sel", _exitSel == 1); }

        void BindDiscord()
        {
            if (_discord == null) return;
            _discord.RegisterCallback<ClickEvent>(_ => OpenDiscord());
            _discord.RegisterCallback<PointerEnterEvent>(_ => { if (_discordIcon != null) _discordIcon.Tint = Blurple; });
            _discord.RegisterCallback<PointerLeaveEvent>(_ => { if (_discordIcon != null) _discordIcon.Tint = IconWhite; });
        }

        void OpenDiscord()
        {
            if (_mode != Mode.Main && _mode != Mode.Friend) return;
            if (string.IsNullOrWhiteSpace(discordUrl) || !discordUrl.StartsWith("https://")) { Toast("DISCORD INVITE LINK IS NOT SET YET"); return; }
            Application.OpenURL(discordUrl);
            Toast("OPENING DISCORD");
        }

        void Quit()
        {
            Debug.Log("[OYM-Title] quit");
            GameSettings.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------------------------------------ modes
        void SetMode(Mode m)
        {
            _mode = m;
            RefreshPicking();
        }

        /// Only what the current mode shows may catch the pointer (hidden panels must not block clicks or the 3D pick).
        void RefreshPicking()
        {
            SetPickTree(_root, true);
            _root.pickingMode = PickingMode.Ignore;
            if (_mode == Mode.Intro || _mode == Mode.Deploy || _scripted) { SetPickTree(_chrome, false); SetPickTree(_modal, false); return; }
            if (_mode != Mode.Main) SetPickTree(_menu, false);
            if (_mode != Mode.Options) SetPickTree(_options, false);
            if (_mode != Mode.Friend) SetPickTree(_friendPanel, false);
            if (_mode != Mode.Exit) SetPickTree(_modal, false);
            else SetPickTree(_chrome, false);
            if (_scripted) SetPickTree(_root, false);
        }

        static void SetPickTree(VisualElement root, bool on)
        {
            if (root == null) return;
            root.Query<VisualElement>().ForEach(e =>
            {
                bool want = e.ClassListContains("pick") || InTextField(e);
                e.pickingMode = on && want ? PickingMode.Position : PickingMode.Ignore;
            });
        }

        static bool InTextField(VisualElement e)
        {
            for (; e != null; e = e.parent) if (e is TextField) return true;
            return false;
        }

        void OpenOptions()
        {
            if (_mode != Mode.Main) return;
            _optSel = 0;
            RefreshRows();
            Toggle(_menu, "menu--away", true);
            Toggle(_options, "options--open", true);
            SetMode(Mode.Options);
        }

        void CloseOptions()
        {
            if (_mode != Mode.Options) return;
            GameSettings.Save();
            Toggle(_menu, "menu--away", false);
            Toggle(_options, "options--open", false);
            SetMode(Mode.Main);
        }

        void OpenFriend()
        {
            if (_mode != Mode.Main) return;
            Toggle(_friend, "friend--open", true);
            SetStatus("", null);
            SetMode(Mode.Friend);
            _field?.schedule.Execute(() => { if (_mode == Mode.Friend) _field.Focus(); }).StartingIn(80);
        }

        void CloseFriend()
        {
            if (_mode != Mode.Friend) return;
            Toggle(_friend, "friend--open", false);
            _field?.Blur();
            _typingFrame = Time.frameCount;
            _joinJob?.Pause();
            SetMode(Mode.Main);
        }

        void OpenExit()
        {
            if (_mode != Mode.Main) return;
            _exitSel = 1;                                                       // CANCEL first: ENTER twice must not quit
            RefreshModal();
            Toggle(_modal, "modal--open", true);
            SetMode(Mode.Exit);
        }

        void CloseExit()
        {
            if (_mode != Mode.Exit) return;
            Toggle(_modal, "modal--open", false);
            SetMode(Mode.Main);
        }

        // ------------------------------------------------------------------------------------------------ director API
        public void Bind(TitleDirector d) { Init(); }

        /// Arrow / WASD input. Returns false when the director should use it (A / D on the main menu = switch pilot).
        public bool Navigate(int dx, int dy)
        {
            if (!Init()) return false;
            switch (_mode)
            {
                case Mode.Main:
                    if (dy != 0) { SelectMenu(_menuSel + dy); return true; }
                    return false;
                case Mode.Options:
                    if (dy != 0) { _optSel = Mathf.Clamp(_optSel + dy, 0, _rows.Count - 1); RefreshRows(); }
                    if (dx != 0) Adjust(dx);
                    return true;
                case Mode.Exit:
                    if (dx != 0) { _exitSel = 1 - _exitSel; RefreshModal(); }
                    return true;
                default:
                    return true;
            }
        }

        /// Put the menu cursor on item i (0 GAME START, 1 OPTION, 2 EXIT) on the main menu.
        public void SelectMenuItem(int i) { if (Init() && _mode == Mode.Main) SelectMenu(i); }

        public void Submit()
        {
            if (!Init()) return;
            switch (_mode)
            {
                case Mode.Main: ActivateMenu(_menuSel); break;
                case Mode.Options:
                    var r = _rows.Count > 0 ? _rows[_optSel] : null;
                    if (r == null) break;
                    if (r.back) CloseOptions(); else if (r.toggle) Adjust(1);
                    break;
                case Mode.Friend: Join(); break;
                case Mode.Exit: if (_exitSel == 0) Quit(); else CloseExit(); break;
            }
        }

        public void Cancel()
        {
            if (!Init()) return;
            switch (_mode)
            {
                case Mode.Main: OpenExit(); break;
                case Mode.Options: CloseOptions(); break;
                case Mode.Friend: CloseFriend(); break;
                case Mode.Exit: CloseExit(); break;
            }
        }

        public void ToggleFriend()
        {
            if (!Init()) return;
            if (_mode == Mode.Friend) CloseFriend(); else OpenFriend();
        }

        /// Logo wipe, then the corners and the menu come in one after another; the menu takes input at the end.
        public void PlayIntro()
        {
            if (!Init() || _mode != Mode.Intro) return;
            _logo?.AddToClassList("logo--in");
            _logo?.AddToClassList("logo--scan");
            Later(800, () => _logo?.RemoveFromClassList("logo--scan"));
            Later(220, () => { In(_discord); In(_friend); });
            Later(360, () => In(_menu));
            for (int i = 0; i < _menuItems.Count; i++) { var it = _menuItems[i]; Later(420 + 90 * i, () => it.AddToClassList("mi--in")); }
            Later(500, () => In(_pilotsBox));
            Later(700, () => { In(_hints); SelectMenu(_menuSel); _cursor?.AddToClassList("menu-cursor--on"); });
            Later(780, () => { if (_mode == Mode.Intro) SetMode(Mode.Main); });
        }

        void Later(long ms, System.Action a) => _root.schedule.Execute(a).StartingIn(ms);
        static void In(VisualElement e) => e?.AddToClassList("in");

        public void FadeIn(float seconds)
        {
            if (!Init() || _fade == null) return;
            _fade.style.transitionDuration = new StyleList<TimeValue>(new List<TimeValue> { new TimeValue(seconds) });
            _fade.AddToClassList("fade--on");
            _fade.schedule.Execute(() => _fade.RemoveFromClassList("fade--on")).StartingIn(60);
        }

        public void FadeOut(float seconds)
        {
            if (!Init() || _fade == null) return;
            _fade.style.transitionDuration = new StyleList<TimeValue>(new List<TimeValue> { new TimeValue(seconds) });
            _fade.AddToClassList("fade--on");
        }

        public void ShowDeploy(string pilot)
        {
            if (!Init()) return;
            Toggle(_modal, "modal--open", false);
            Toggle(_friend, "friend--open", false);
            _field?.Blur();
            SetMode(Mode.Deploy);
            _chrome?.AddToClassList("chrome--out");
            BuildDeployName(pilot);
            _deployFlash?.AddToClassList("deploy--on");
        }

        /// The deploy name, one Label per letter: even spacing at 84 px (one big label with letter-spacing got uneven
        /// gaps, "ORB IT  FRAM E") and the letters land one after another with a small overshoot.
        void BuildDeployName(string pilot)
        {
            if (_deployRow == null || string.IsNullOrEmpty(pilot)) return;
            _deployRow.Clear();
            var font = Font();
            int n = 0;
            foreach (char ch in pilot)
            {
                if (ch == ' ') { _deployRow.Add(El("deploy-gap")); continue; }
                var l = L(ch.ToString(), "deploy-char");
                l.pickingMode = PickingMode.Ignore;
                if (font.HasValue) l.style.unityFontDefinition = font.Value;
                _deployRow.Add(l);
                int k = n++;
                l.schedule.Execute(() => l.AddToClassList("deploy-char--in")).StartingIn(40 + 32 * k);
            }
        }

        /// The selected pilot changed (-1 = none yet, during the opening shot).
        public void ShowFocus(int i)
        {
            if (!Init() || i == _shown) return;
            _shown = i;
            if (i < 0 || pilots == null || i >= pilots.Length) return;
            foreach (var p in pilots) _title.RemoveFromClassList(Theme(p));
            _title.AddToClassList(Theme(pilots[i]));
            LayoutCards(i);
        }

        static string Theme(PilotInfo p) => p.accentClass.Replace("accent-", "theme-");

        public void Toast(string msg, int ms = 1900)
        {
            if (!Init() || _toast == null) return;
            _toast.text = msg;
            _toast.AddToClassList("toast--on");
            _toastJob?.Pause();
            _toastJob = _toast.schedule.Execute(() => _toast.RemoveFromClassList("toast--on")).StartingIn(ms);
        }

        /// Every frame (from TitleDirector): keeps the "typing" flag of the friend-code field current.
        public void Tick(Camera cam, TitleCharacter[] chars, int focus, int hover, bool deploying)
        {
            if (!Init()) return;
            if (FieldFocused) _typingFrame = Time.frameCount;
        }

        /// Is the pointer (screen pixels, origin bottom left) over something clickable? Then the 3D pick is skipped.
        public bool IsOverInteractive(Vector2 screen)
        {
            if (!Init() || _root.panel == null) return false;
            Vector2 pp = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screen.x, Screen.height - screen.y));
            for (var e = _root.panel.Pick(pp); e != null; e = e.parent)
                if (e.ClassListContains("pick") || e is TextField) return true;
            return false;
        }

        bool FieldFocused
        {
            get
            {
                if (_field == null || _root == null || _root.panel == null) return false;
                var f = _root.panel.focusController?.focusedElement as VisualElement;
                return f != null && (f == _field || _field.Contains(f));
            }
        }

        // ------------------------------------------------------------------------------------------------ helpers
        static VisualElement El(string cls) { var e = new VisualElement(); e.AddToClassList(cls); return e; }
        static Label L(string text, string cls) { var l = new Label(text); l.AddToClassList(cls); return l; }
        static void SetText(Label l, string s) { if (l != null) l.text = s; }

        static void Toggle(VisualElement e, string cls, bool on)
        {
            if (e == null) return;
            if (on) e.AddToClassList(cls); else e.RemoveFromClassList(cls);
        }

        static void Pulse(VisualElement e, string cls, int ms)
        {
            if (e == null) return;
            e.AddToClassList(cls);
            e.schedule.Execute(() => e.RemoveFromClassList(cls)).StartingIn(ms);
        }

        static FontDefinition? Font()
        {
            if (s_font.HasValue) return s_font;
            foreach (var family in new[] { "Bahnschrift", "Segoe UI" })
            {
                try
                {
                    var fa = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(family, "Regular");
                    if (fa != null) { s_font = FontDefinition.FromSDFFont(fa); Debug.Log("[OYM-Title] UI font: " + family); return s_font; }
                }
                catch (System.Exception e) { Debug.LogWarning($"[OYM-Title] font {family}: {e.Message}"); }
            }
            var builtin = Resources.GetBuiltinResource<UnityEngine.Font>("LegacyRuntime.ttf");
            if (builtin) s_font = FontDefinition.FromFont(builtin);
            return s_font;
        }
    }
}
