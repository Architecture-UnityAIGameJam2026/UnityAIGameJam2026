using System.Collections;
using System.Collections.Generic;
using OYM;
using Unity.Netcode;
using Unity.Collections;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OnYourMark
{
    // The title owns the room. Its NetworkManager survives the single synchronized load into Arena.
    public sealed class TitleMatch : MonoBehaviour
    {
        public enum Mode { Solo, Host, Join }
        public static TitleMatch Active { get; private set; }
        public bool Solo => mode == Mode.Solo;
        public int HostRole { get; private set; }
        public bool CanChoosePilot => !closing && !changing && !deploying && !connected && !Guest.HasValue;
        public void PreferPilot(int role) { if (CanChoosePilot) HostRole = Mathf.Clamp(role, 0, 1); }
        public ulong? Guest { get; private set; }
        public string RoomCode { get; private set; }

        NetworkManager net;
        UnityTransport transport;
        TitleDirector title;
        Mode mode;
        bool changing, closing, deploying, inMission, connected, quitting;
        string rejection;
        const ushort LanPort = 7777;

        public static void Begin(TitleDirector director, Mode mode, int role, string code = null)
        {
            string ip = null;
            if (mode == Mode.Join && !OnYourMark.RoomCode.TryDecode(code, out ip))
            {
                director.ui.ShowMatch("Enter the host's 6-character room code.", true, Active ? Active.RoomCode : null);
                return;
            }
            if (Active)
            {
                if (Active.closing) director.StartCoroutine(BeginAfterClose(director, mode, role, code));
                else if (mode == Mode.Join) Active.Join(ip);
                return;
            }
            if (NetworkManager.Singleton) return;
            var net = Instantiate(Resources.Load<NetworkManager>("NetworkManager"));
            var match = net.gameObject.AddComponent<TitleMatch>();
            Active = match;
            match.net = net;
            match.title = director;
            match.mode = mode;
            match.HostRole = Mathf.Clamp(role, 0, 1);
            match.transport = net.GetComponent<UnityTransport>();
            net.NetworkConfig.ConnectionApproval = true;
            net.ConnectionApprovalCallback = match.Approve;
            net.OnClientStarted += match.RegisterSeatMessage;
            net.OnClientConnectedCallback += match.Connected;
            net.OnClientDisconnectCallback += match.Disconnected;
            net.OnClientStopped += match.Stopped;
            net.OnTransportFailure += match.TransportFailed;
            if (mode == Mode.Join) match.Join(ip);
            else match.StartRoom();
        }

        static IEnumerator BeginAfterClose(TitleDirector director, Mode mode, int role, string code)
        {
            while (Active && Active.closing) yield return null;
            if (director && (director.ui.CurrentMode == TitleUI.Mode.Friend || (mode == Mode.Solo && director.ui.CurrentMode == TitleUI.Mode.SelectMode)))
                Begin(director, mode, role, code);
        }

        void StartRoom(string feedback = null)
        {
            string ip = Solo ? "127.0.0.1" : LocalAddress.Get();
            if (!Solo && (ip == null || !CanListen()))
            {
                // Another instance on the same PC may own the LAN port. This instance can still JOIN it.
                title.ui.ShowMatch(feedback ?? (ip == null ? "Connect to a LAN to share a room." : "Enter the code of the room already open on this PC."), feedback != null);
                return;
            }
            transport.SetConnectionData("127.0.0.1", Solo ? (ushort)0 : LanPort, Solo ? "127.0.0.1" : "0.0.0.0");
            if (!net.StartHost())
            {
                if (Solo) Close("Could not start the single-player session.");
                else title.ui.ShowMatch("Could not open a room. You can still join your partner.", true);
                return;
            }
            net.SceneManager.OnLoadEventCompleted += Loaded;
            if (Solo) StartDeployment();
            else
            {
                RoomCode = OnYourMark.RoomCode.Encode(ip);
                title.ui.ShowMatch(feedback, feedback != null, RoomCode);
            }
        }

        static bool CanListen()
        {
            try
            {
                using var probe = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork,
                    System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
                probe.ExclusiveAddressUse = true;
                probe.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, LanPort));
                return true;
            }
            catch (System.Net.Sockets.SocketException) { return false; }
        }

        void Join(string ip)
        {
            if (changing || closing || deploying || Guest.HasValue || connected) return;
            if (mode == Mode.Join && net.IsListening) return;
            if (net.IsHost && RoomCode == OnYourMark.RoomCode.Encode(ip))
            {
                title.ui.ShowMatch("This is your room. Share your code with your partner.", false, RoomCode);
                return;
            }
            changing = true;
            RoomCode = null;
            title.ui.ShowMatch("Connecting to the host...");
            StartCoroutine(SwitchToClient(ip));
        }

        IEnumerator SwitchToClient(string ip)
        {
            // Release our empty listener before connecting to the other title's room.
            if (net.SceneManager != null) net.SceneManager.OnLoadEventCompleted -= Loaded;
            if (net.IsListening && !net.ShutdownInProgress) net.Shutdown();
            while (net.IsListening || net.ShutdownInProgress) yield return null;
            yield return null;
            if (closing) yield break;
            mode = Mode.Join;
            rejection = null;
            changing = false;
            transport.SetConnectionData(ip, LanPort);
            if (!net.StartClient()) RecoverLobby("Could not start the connection. Try again.");
        }

        void RecoverLobby(string reason)
        {
            if (changing || closing) return;
            changing = true;
            StartCoroutine(RestoreRoom(reason));
        }

        IEnumerator RestoreRoom(string reason)
        {
            yield return null;
            while (net.ShutdownInProgress || net.IsListening) yield return null;
            if (closing) yield break;
            mode = Mode.Host;
            changing = false;
            StartRoom(reason);
        }

        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            bool host = request.ClientNetworkId == NetworkManager.ServerClientId;
            response.Approved = host || (!Solo && !closing && !changing && !Guest.HasValue && (!deploying || inMission));
            response.CreatePlayerObject = false;
            if (!response.Approved) response.Reason = "The room already has two pilots.";
            else if (!host) Guest = request.ClientNetworkId;
        }

        const string SeatMessage = "OYM.LaunchSeat";
        void RegisterSeatMessage()
        {
            if (!net.IsServer) net.CustomMessagingManager.RegisterNamedMessageHandler(SeatMessage, ReceiveSeat);
        }

        void ReceiveSeat(ulong sender, FastBufferReader message)
        {
            if (!title || sender != NetworkManager.ServerClientId) return;
            message.ReadValueSafe(out byte role);
            title.SetFocus(role);
            title.AnimateDeployment();
        }

        void Connected(ulong id)
        {
            if (closing || changing) return;
            if (!net.IsServer)
            {
                if (id == net.LocalClientId)
                {
                    connected = true;
                    if (title) title.ui.ShowMatch("Connected. Deploying with the host…");
                }
                return;
            }
            if (Guest != id || inMission) return;
            using var message = new FastBufferWriter(1, Allocator.Temp);
            message.WriteValueSafe((byte)(1 - HostRole));
            net.CustomMessagingManager.SendNamedMessage(SeatMessage, id, message);
            StartDeployment();
        }

        void StartDeployment()
        {
            if (deploying || closing) return;
            deploying = true;
            title.AnimateDeployment();
            StartCoroutine(LoadArena());
        }

        IEnumerator LoadArena()
        {
            yield return new WaitForSecondsRealtime(title.deployDelay);
            while (!closing)
            {
                var status = net.SceneManager.LoadScene(GameSession.GameScene, LoadSceneMode.Single);
                if (status == SceneEventProgressStatus.Started) yield break;
                if (status != SceneEventProgressStatus.SceneEventInProgress) { Close("The mission could not load. Please reopen the room."); yield break; }
                yield return null;
            }
        }

        void Loaded(string scene, LoadSceneMode loadMode, List<ulong> completed, List<ulong> timedOut)
        {
            if (scene != GameSession.GameScene || closing || inMission) return;
            if (!Solo && (!Guest.HasValue || !completed.Contains(Guest.Value)))
            {
                Close("The other pilot did not finish loading. Please reopen the room.");
                return;
            }
            inMission = true;
            FindFirstObjectByType<RoleSelection>().StartMatchedPlayers(HostRole, Guest);
        }

        void Disconnected(ulong id)
        {
            if (closing || changing || quitting) return;
            if (net.IsServer)
            {
                if (Guest != id) return;
                Guest = null;
                if (deploying && !inMission) Close("The other pilot left before deployment.");
            }
            else if (!string.IsNullOrEmpty(net.DisconnectReason) &&
                     !net.DisconnectReason.StartsWith("[Disconnect Event]"))
                rejection = net.DisconnectReason;
        }

        void Stopped(bool wasHost)
        {
            if (closing || changing || quitting) return;
            if (mode == Mode.Join && !connected && title)
                RecoverLobby(rejection ?? "Could not reach the host. Check the code, Wi-Fi and firewall.");
            else if (wasHost && mode == Mode.Host && !deploying && title)
                title.ui.ShowMatch("Could not open a room. You can still join your partner.", true);
            else Close(rejection ?? "The host closed the room.");
        }

        void TransportFailed()
        {
            if (closing || changing || quitting || mode == Mode.Join) return;
            RoomCode = null;
            if (mode == Mode.Host && !deploying && title) title.ui.ShowMatch("Could not open the LAN room.", true);
            else Close("The network session stopped.");
        }

        public void Close(string message = null)
        {
            if (closing) return;
            closing = true;
            StartCoroutine(Finish(message));
        }

        IEnumerator Finish(string message)
        {
            // A stop/disconnect callback can run inside NGO's update. Let it finish first.
            yield return null;
            if (net.IsListening && !net.ShutdownInProgress) net.Shutdown();
            while (net.IsListening || net.ShutdownInProgress) yield return null;
            bool reopen = !title || deploying || title.Deploying;
            if (reopen) GameSession.ReturnMessage = message;
            else title.ui.ShowMatch(message, message != null);
            Destroy(gameObject);
            if (reopen) GameSession.ReturnToTitle();
        }

        void OnApplicationQuit() => quitting = true;
        void OnDestroy()
        {
            if (Active == this) Active = null;
            if (!net) return;
            net.OnClientStarted -= RegisterSeatMessage;
            net.OnClientConnectedCallback -= Connected;
            net.OnClientDisconnectCallback -= Disconnected;
            net.OnClientStopped -= Stopped;
            net.OnTransportFailure -= TransportFailed;
            if (net.SceneManager != null) net.SceneManager.OnLoadEventCompleted -= Loaded;
        }
    }
}
