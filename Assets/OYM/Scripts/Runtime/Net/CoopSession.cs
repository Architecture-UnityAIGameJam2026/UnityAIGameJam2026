using UnityEngine;
using UnityEngine.SceneManagement;

namespace OYM.Net
{
    /// The two-player room (the team build's TitleMatch flow on this project's title): opening the friend panel hosts
    /// a room on the LAN and shows its 8-character code; JOIN with the partner's code leaves that room and connects
    /// to the partner's instead. When the guest says hello the host hands out the seats (the host keeps the pilot on
    /// its title, the guest flies the other one) and both titles launch into the game scene together. Survives the
    /// scene load (DontDestroyOnLoad); the game side is CoopGame. Closing the panel, going back to the title or quitting
    /// closes it. Test from one PC: start one instance with -coopHost and the other with -coopJoin 127.0.0.1
    /// (-coopPilot 0 / 1 picks the pilot first).
    public sealed class CoopSession : MonoBehaviour
    {
        public static CoopSession Active { get; private set; }
        /// two pilots are deploying / flying (the game scene installs CoopGame on this)
        public static bool InGame => Active && Active._inGame;

        public bool IsHost { get; private set; }
        /// 0 KESTREL, 1 ORBIT FRAME (title pilot index); -1 before the seats are handed out
        public int LocalPilot { get; private set; } = -1;
        public int RemotePilot => LocalPilot < 0 ? -1 : 1 - LocalPilot;
        public string RoomCode { get; private set; }
        public bool Connected => _link != null && _link.Connected;
        public float Silence => _link != null ? _link.Silence : 999f;

        /// main thread: every message from the other side once in the game scene (CoopGame and its parts)
        public event System.Action<CoopMsg.R> Message;
        /// the connection dropped (reason)
        public event System.Action<string> Lost;

        CoopLink _link;
        TitleDirector _title;
        bool _inGame, _helloSent, _welcomed, _lostRaised;
        float _connectAt = -1f;
        string _joinCode;

        const float ConnectTimeout = 8f;
        public const float LostAfter = 12f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active = null; }

        // ------------------------------------------------------------------------------------------------ title API
        /// Friend panel opened: host a room (or show the one that is already open).
        public static void OpenRoom(TitleDirector title)
        {
            if (Active)
            {
                Active._title = title;
                Active.ShowState();
                return;
            }
            Create(title).StartHosting();
        }

        /// JOIN with the partner's code (or a plain IP from the command line).
        public static void JoinRoom(TitleDirector title, string code)
        {
            if (!CoopCode.TryParseTarget(code, out string ip))
            {
                Ui(title, null, "THAT CODE DOES NOT LOOK RIGHT. CHECK ALL 8 CHARACTERS", true);
                return;
            }
            var s = Active ? Active : Create(title);
            s._title = title;
            if (s._inGame) return;
            if (s.IsHost && s.RoomCode != null && s.RoomCode == code.Replace("-", "").ToUpperInvariant())
            {
                s.ShowRoom("THAT IS YOUR OWN CODE. SEND IT TO YOUR PARTNER", true);
                return;
            }
            s.StartJoin(ip, code);
        }

        /// Friend panel closed before anything launched: close the room.
        public static void CloseRoom()
        {
            if (Active && !Active._inGame) Active.End(null);
        }

        static CoopSession Create(TitleDirector title)
        {
            var go = new GameObject("Co-op Session");
            DontDestroyOnLoad(go);
            var s = go.AddComponent<CoopSession>();
            s._title = title;
            Active = s;
            // two windows on one PC (or the partner's game behind a chat app): the unfocused one must keep playing and
            // sending, or the other side sees it freeze and drop (runtime only, ProjectSettings stay as they are)
            Application.runInBackground = true;
            CoopShots.InstallIfAsked(go);
            return s;
        }

        // team repo port: the room panel (TitleUI.ShowRoom), DeployCoop and PlayTestDirector.SwitchLocked belong to the
        // local title / director; the team's versions do not have them. Used when present, skipped otherwise.
        static readonly System.Reflection.MethodInfo s_showRoom =
            typeof(TitleUI).GetMethod("ShowRoom", new[] { typeof(string), typeof(string), typeof(bool) });
        static string s_lastStatus;

        static void Ui(TitleDirector t, string code, string status, bool error)
        {
            if (!t || !t.ui) return;
            if (s_showRoom != null) { s_showRoom.Invoke(t.ui, new object[] { code, status, error }); return; }
            if (string.IsNullOrEmpty(status) || status == s_lastStatus) return;      // team title: no panel, the log says it
            s_lastStatus = status;
            Debug.Log($"[OYM-COOP] room {code ?? "-"}: {status}");
        }

        static bool CallTitle(TitleDirector t, string method, int arg)
        {
            var m = t ? typeof(TitleDirector).GetMethod(method, new[] { typeof(int) }) : null;
            if (m == null) return false;
            m.Invoke(t, new object[] { arg });
            return true;
        }

        static void SetStatic(System.Type type, string name, bool value)
        {
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
            var f = type.GetField(name, F);
            if (f != null && f.FieldType == typeof(bool)) { f.SetValue(null, value); return; }
            var p = type.GetProperty(name, F);
            if (p != null && p.CanWrite && p.PropertyType == typeof(bool)) p.SetValue(null, value);
        }

        void ShowRoom(string status, bool error = false) => Ui(_title, IsHost ? RoomCode : null, status, error);

        void ShowState()
        {
            if (IsHost && _link != null && _link.Current == CoopLink.State.Listening) ShowRoom("WAITING FOR YOUR PARTNER  ·  SHARE THIS CODE");
            else if (!IsHost && _link != null && _link.Current != CoopLink.State.Closed) ShowRoom("CONNECTING TO THE HOST ...");
            else ShowRoom(null);
        }

        // ------------------------------------------------------------------------------------------------ host
        void StartHosting(string note = null, bool noteIsError = false)
        {
            DropLink();
            IsHost = true; _helloSent = _welcomed = false; _connectAt = -1f;
            string ip = LocalAddress.Get() ?? "127.0.0.1";                   // no LAN: still joinable from this PC
            RoomCode = CoopCode.Encode(ip);
            _link = new CoopLink();
            if (!_link.Listen())
            {
                // the port is taken: another title on this PC hosts. This one can still join it.
                Debug.Log($"[OYM-COOP] could not open a room ({_link.Error}): join only");
                RoomCode = null;
                ShowRoom(note ?? "A ROOM IS ALREADY OPEN ON THIS PC. ENTER ITS CODE TO JOIN", true);
                return;
            }
            Debug.Log($"[OYM-COOP] room open on {ip}:{CoopLink.Port}  code {RoomCode}");
            ShowRoom(note ?? "WAITING FOR YOUR PARTNER  ·  SHARE THIS CODE", noteIsError);
        }

        // ------------------------------------------------------------------------------------------------ guest
        void StartJoin(string ip, string code)
        {
            DropLink();
            IsHost = false; RoomCode = null; _helloSent = _welcomed = false;
            _joinCode = code;
            _link = new CoopLink();
            _link.Connect(ip);
            _connectAt = Time.unscaledTime;
            Debug.Log($"[OYM-COOP] joining {ip}:{CoopLink.Port}");
            ShowRoom("CONNECTING TO THE HOST ...");
        }

        void JoinFailed(string why)
        {
            Debug.Log("[OYM-COOP] join failed: " + why);
            // like the team build: back to an open room of our own, so the partner can still join us
            StartHosting(why, true);
        }

        // ------------------------------------------------------------------------------------------------ loop
        void Update()
        {
            if (_link == null) return;
            if (!IsHost && !_welcomed && _connectAt >= 0f)
            {
                if (_link.Connected && !_helloSent)
                {
                    _helloSent = true;
                    int want = _title ? Mathf.Max(0, _title.Focus) : 0;
                    Send(new CoopMsg.W(CoopMsg.Hello).U8(CoopMsg.Version).U8(want).Done());
                }
                if (_link.Current == CoopLink.State.Closed) { JoinFailed("COULD NOT REACH THE HOST. CHECK THE CODE, WI-FI AND FIREWALL"); return; }
                if (Time.unscaledTime - _connectAt > ConnectTimeout) { JoinFailed("THE HOST DID NOT ANSWER. CHECK THE CODE, WI-FI AND FIREWALL"); return; }
            }

            while (_link != null && _link.Poll(out var frame))
            {
                var r = new CoopMsg.R(frame);
                if (!Handle(r) && _inGame) Message?.Invoke(r);
            }
            if (_link == null) return;

            bool dropped = _link.Current == CoopLink.State.Closed && (_welcomed || (IsHost && _helloSent));
            if (_link.Connected && _link.Silence > LostAfter) dropped = true;
            if (dropped)
            {
                string why = _link.Error ?? "NO SIGNAL FROM THE OTHER PILOT";
                if (_inGame) RaiseLost(why);
                else if (IsHost) StartHosting("YOUR PARTNER LEFT. WAITING AGAIN", true);
                else JoinFailed("THE HOST CLOSED THE ROOM");
            }
            else if (IsHost && !_inGame && _link.Current == CoopLink.State.Closed && _link.Error != null && !_helloSent)
                StartHosting("THE CONNECTION FAILED. WAITING AGAIN", true);
        }

        /// session-level messages; false = for the game
        bool Handle(CoopMsg.R r)
        {
            switch (r.Type)
            {
                case CoopMsg.Hello when IsHost && !_inGame:
                {
                    int version = r.U8(), want = r.U8();
                    _helloSent = true;
                    if (version != CoopMsg.Version)
                    {
                        _link.Shutdown(CoopMsg.RejectMsg("THE OTHER PC RUNS A DIFFERENT BUILD"));
                        _link = null;
                        StartHosting("YOUR PARTNER RUNS A DIFFERENT BUILD", true);
                        return true;
                    }
                    int hostPilot = _title && _title.Focus >= 0 ? _title.Focus : 1 - Mathf.Clamp(want, 0, 1);
                    Send(new CoopMsg.W(CoopMsg.Welcome).U8(hostPilot).U8(1 - hostPilot).Done());
                    Debug.Log($"[OYM-COOP] guest {_link.RemoteAddress} joined: host flies {hostPilot}, guest {1 - hostPilot}");
                    Deploy(hostPilot);
                    return true;
                }
                case CoopMsg.Welcome when !IsHost && !_welcomed:
                {
                    int hostPilot = r.U8(), guestPilot = r.U8();
                    _welcomed = true;
                    Debug.Log($"[OYM-COOP] welcomed: host flies {hostPilot}, we fly {guestPilot}");
                    Deploy(guestPilot);
                    return true;
                }
                case CoopMsg.Reject:
                {
                    string why = r.Str();
                    if (!_inGame) JoinFailed(why);
                    return true;
                }
                case CoopMsg.Bye:
                    if (_inGame) RaiseLost("THE OTHER PILOT LEFT");
                    else if (IsHost) StartHosting("YOUR PARTNER LEFT. WAITING AGAIN", true);
                    else JoinFailed("THE HOST CLOSED THE ROOM");
                    return true;
            }
            return false;
        }

        void Deploy(int pilot)
        {
            LocalPilot = Mathf.Clamp(pilot, 0, 1);
            _inGame = true;
            SetGameFlags(true);
            ShowRoom("CONNECTED  ·  DEPLOYING TOGETHER");
            if (CallTitle(_title, "DeployCoop", LocalPilot)) return;
            // team title: no DeployCoop - into the showcase with this seat's pilot first (ShowcaseSession puts it in slot 0)
            ShowcaseSession.SelectedPilot = LocalPilot == 0 ? "KESTREL" : "ORBIT FRAME";
            ShowcaseSession.LoadScene(ShowcaseScene.Name);
        }

        void RaiseLost(string why)
        {
            if (_lostRaised) return;
            _lostRaised = true;
            Debug.Log("[OYM-COOP] connection lost: " + why);
            DropLink();
            Lost?.Invoke(why);
        }

        public void Send(byte[] payload) { if (_link != null) _link.Send(payload); }

        // ------------------------------------------------------------------------------------------------ teardown
        /// The game hooks: the guest's HUSK follow the host, the arrival cutscene waits for both and its lines run on
        /// their own (only the host may skip), Tab does not swap pilots.
        static void SetGameFlags(bool coop)
        {
            HuskEncounter.CoopGuest = coop && Active && !Active.IsHost;
            OYM.Cine.ArrivalCutscene.PlayOnGameStart = !coop;
            OYM.Cine.ArrivalCutscene.DialogueInput = !coop;
            OYM.Cine.ArrivalCutscene.SkipInput = !coop || (Active && Active.IsHost);
            SetStatic(typeof(PlayTestDirector), "SwitchLocked", coop);
        }

        void DropLink()
        {
            if (_link == null) return;
            _link.Shutdown(new[] { CoopMsg.Bye });
            _link = null;
        }

        public void End(string reason)
        {
            if (reason != null) Debug.Log("[OYM-COOP] session closed: " + reason);
            DropLink();
            if (_inGame) SetGameFlags(false);
            _inGame = false;
            if (Active == this) Active = null;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_link != null) { _link.Dispose(); _link = null; }
            if (Active == this) { Active = null; SetGameFlags(false); }
        }

        void OnApplicationQuit() => DropLink();

        // ------------------------------------------------------------------------------------------------ launch args
        static bool s_argsUsed;

        /// -coopHost / -coopJoin <code or IPv4> [-coopPilot 0|1]: open the friend panel and host / join on their own
        /// (two instances on one PC, or a scripted test). Called by the title once its menu is in.
        public static void AutoStart(TitleDirector title)
        {
            if (s_argsUsed || !title || !title.ui) return;
            s_argsUsed = true;
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-coopPilot");
            if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int p)) title.SetFocus(Mathf.Clamp(p, 0, 1));
            int j = System.Array.IndexOf(args, "-coopJoin");
            bool host = System.Array.IndexOf(args, "-coopHost") >= 0;
            if (!host && j < 0) return;
            // the local title's friend panel hosts a room; the team title's friend panel is the team's own NGO room,
            // so there the LAN room is opened straight
            if (s_showRoom != null) title.ui.ToggleFriend();
            else OpenRoom(title);
            if (j >= 0 && j + 1 < args.Length) JoinRoom(title, args[j + 1]);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetArgs() { s_argsUsed = false; }
    }
}
