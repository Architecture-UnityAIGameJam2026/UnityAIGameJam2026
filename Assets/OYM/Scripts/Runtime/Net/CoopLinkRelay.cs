#if OYM_RELAY
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Networking.Transport.Utilities;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using UnityEngine;

namespace OYM.Net
{
    /// The web build's wire (OYM_RELAY): the same API as the LAN CoopLink, but the two players meet through Unity Relay
    /// over secure WebSockets, so it works between two browsers. The host asks Relay for a one-guest allocation and its
    /// join code becomes the room code; the guest joins with that code. Messages go through a reliable, fragmented
    /// pipeline, so CoopSession / CoopGame see the same ordered frames as over TCP. Everything is main thread: a small
    /// hidden pump drives the driver every frame (also while CoopSession's Update is not running).
    public sealed class CoopLink : IDisposable
    {
        public const int Port = 0;                  // not used over Relay (kept for log lines)
        const int MaxFrame = 60000;
        const int MaxPlayers = 1;                   // guests per room

        public enum State { Idle, Listening, Connecting, Connected, Closed }

        NetworkDriver _driver;
        NetworkPipeline _pipe;
        NetworkConnection _conn;
        readonly Queue<byte[]> _in = new Queue<byte[]>();
        readonly Queue<byte[]> _out = new Queue<byte[]>();
        State _state = State.Idle;
        string _error;
        bool _disposed, _closing;
        float _lastRecv = Time.realtimeSinceStartup, _lastSend;
        bool _listenPending;

        public State Current => _state;
        public bool Connected => _state == State.Connected;
        public string Error => _error;
        public string RemoteAddress { get; private set; } = "relay";
        public float Silence => Time.realtimeSinceStartup - _lastRecv;
        /// the host's Relay join code once the allocation is ready (the room code to share); null before
        public string JoinCode { get; private set; }

        // ------------------------------------------------------------------------------------------------ services
        static Task _signIn;
        static Task SignIn()
        {
            if (_signIn == null || _signIn.IsFaulted || _signIn.IsCanceled) _signIn = DoSignIn();
            return _signIn;
        }
        static async Task DoSignIn()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        static string Short(Exception e)
        {
            while (e is AggregateException ae && ae.InnerException != null) e = ae.InnerException;
            return string.IsNullOrEmpty(e.Message) ? e.GetType().Name : e.Message;
        }

        NetworkSettings Settings(ref RelayServerData relay)
        {
            var s = new NetworkSettings();
            s.WithRelayParameters(ref relay);
            s.WithFragmentationStageParameters(payloadCapacity: MaxFrame + 64);
            s.WithReliableStageParameters(windowSize: 64);
            s.WithNetworkConfigParameters(disconnectTimeoutMS: 15000, heartbeatTimeoutMS: 1000);
            return s;
        }

        void MakeDriver(ref RelayServerData relay)
        {
            var settings = Settings(ref relay);
#if UNITY_WEBGL && !UNITY_EDITOR
            _driver = NetworkDriver.Create(new WebSocketNetworkInterface(), settings);
#else
            _driver = NetworkDriver.Create(settings);   // editor / desktop: plain UDP to Relay works too
#endif
            _pipe = _driver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
            Pump.Add(this);
        }

        static string ConnType
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return "wss";
#else
                return "dtls";
#endif
            }
        }

        static Unity.Services.Relay.Models.RelayServerEndpoint Pick(List<Unity.Services.Relay.Models.RelayServerEndpoint> eps)
        {
            foreach (var e in eps) if (e.ConnectionType == ConnType) return e;
            throw new Exception("Relay has no " + ConnType + " endpoint");
        }

        // ------------------------------------------------------------------------------------------------ host
        /// Open a room through Relay. Returns true at once; Current is Listening and JoinCode fills in when the
        /// allocation is ready, or Current goes Closed with Error.
        public bool Listen()
        {
            _state = State.Listening;
            HostAsync();
            return true;
        }

        async void HostAsync()
        {
            try
            {
                await SignIn();
                var alloc = await RelayService.Instance.CreateAllocationAsync(MaxPlayers);
                var code = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);
                if (_disposed) return;
                var ep = Pick(alloc.ServerEndpoints);
                var relay = new RelayServerData(ep.Host, (ushort)ep.Port, alloc.AllocationIdBytes, alloc.ConnectionData, alloc.ConnectionData, alloc.Key, ep.Secure, ep.ConnectionType == "wss" || ep.ConnectionType == "ws");
                MakeDriver(ref relay);
                if (_driver.Bind(NetworkEndpoint.AnyIpv4) != 0) throw new Exception("could not bind to Relay");
                _listenPending = true;                   // Listen once the Relay bind completes (Pump)
                JoinCode = code;
                Debug.Log("[OYM-COOP] relay room " + code);
            }
            catch (Exception e) { Fail("RELAY: " + Short(e)); }
        }

        // ------------------------------------------------------------------------------------------------ guest
        /// Join a room by its Relay join code (non-blocking; Connecting -> Connected or Closed with Error).
        public void Connect(string joinCode, int timeoutMs = 0)
        {
            _state = State.Connecting;
            JoinAsync(joinCode);
        }

        async void JoinAsync(string code)
        {
            try
            {
                await SignIn();
                var join = await RelayService.Instance.JoinAllocationAsync(code);
                if (_disposed) return;
                var ep = Pick(join.ServerEndpoints);
                var relay = new RelayServerData(ep.Host, (ushort)ep.Port, join.AllocationIdBytes, join.ConnectionData, join.HostConnectionData, join.Key, ep.Secure, ep.ConnectionType == "wss" || ep.ConnectionType == "ws");
                MakeDriver(ref relay);
                if (_driver.Bind(NetworkEndpoint.AnyIpv4) != 0) throw new Exception("could not bind to Relay");
                _conn = _driver.Connect(relay.Endpoint);
            }
            catch (Exception e) { Fail("RELAY: " + Short(e)); }
        }

        void Fail(string why)
        {
            if (_disposed) return;
            _error = why;
            _state = State.Closed;
            Debug.Log("[OYM-COOP] " + why);
        }

        // ------------------------------------------------------------------------------------------------ pump
        internal void Tick()
        {
            if (_disposed || !_driver.IsCreated) return;
            _driver.ScheduleUpdate().Complete();

            if (_listenPending && _driver.Bound)
            {
                _listenPending = false;
                if (_driver.Listen() != 0) { Fail("RELAY: could not open the room"); return; }
            }

            NetworkConnection c;
            while ((c = _driver.Accept()) != default)
            {
                if (_conn.IsCreated) { _driver.Disconnect(c); continue; }   // one guest per room
                _conn = c;
                Connected_();
            }

            if (!_conn.IsCreated) return;
            NetworkEvent.Type ev;
            while ((ev = _driver.PopEventForConnection(_conn, out var reader)) != NetworkEvent.Type.Empty)
            {
                switch (ev)
                {
                    case NetworkEvent.Type.Connect: Connected_(); break;
                    case NetworkEvent.Type.Data:
                        _lastRecv = Time.realtimeSinceStartup;
                        int n = reader.Length;
                        if (n <= 0) break;
                        var buf = new byte[n];
                        var na = new NativeArray<byte>(n, Allocator.Temp);
                        reader.ReadBytes(na);
                        na.CopyTo(buf);
                        na.Dispose();
                        if (n == 1 && buf[0] == CoopMsg.Ping) break;
                        _in.Enqueue(buf);
                        break;
                    case NetworkEvent.Type.Disconnect:
                        _conn = default;
                        if (!_closing) Fail(_error ?? "the other side closed the connection");
                        return;
                }
            }

            if (_state != State.Connected) return;
            while (_out.Count > 0)
            {
                if (!SendNow(_out.Peek())) break;        // the reliable window is full: try again next frame
                _out.Dequeue();
            }
            if (Time.realtimeSinceStartup - _lastSend > 1f) SendNow(new[] { CoopMsg.Ping });   // keep-alive
        }

        void Connected_()
        {
            if (_state == State.Connected) return;
            _lastRecv = Time.realtimeSinceStartup;
            _state = State.Connected;
        }

        bool SendNow(byte[] payload)
        {
            if (_driver.BeginSend(_pipe, _conn, out var w) != 0) return false;
            var na = new NativeArray<byte>(payload, Allocator.Temp);
            w.WriteBytes(na);
            na.Dispose();
            if (_driver.EndSend(w) < 0) return false;
            _lastSend = Time.realtimeSinceStartup;
            return true;
        }

        // ------------------------------------------------------------------------------------------------ main thread API
        public void Send(byte[] payload)
        {
            if (_state != State.Connected || payload == null || payload.Length == 0) return;
            if (payload.Length > MaxFrame) { Debug.LogWarning($"[OYM-COOP] message {payload[0]} too big ({payload.Length} B), dropped"); return; }
            if (_out.Count > 4096) return;
            _out.Enqueue(payload);
        }

        public bool Poll(out byte[] frame)
        {
            if (_in.Count > 0) { frame = _in.Dequeue(); return true; }
            frame = null; return false;
        }

        public void Shutdown(byte[] last)
        {
            if (_disposed) return;
            _closing = true;
            if (last != null && _state == State.Connected && _driver.IsCreated)
            {
                Tick();
                SendNow(last);
                _driver.ScheduleUpdate().Complete();
            }
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _closing = true;
            _state = State.Closed;
            Pump.Remove(this);
            if (_driver.IsCreated)
            {
                if (_conn.IsCreated) { _driver.Disconnect(_conn); _driver.ScheduleUpdate().Complete(); }
                _driver.Dispose();
            }
        }

        // ------------------------------------------------------------------------------------------------ frame pump
        sealed class Pump : MonoBehaviour
        {
            static Pump _it;
            static readonly List<CoopLink> Links = new List<CoopLink>();

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            static void ResetStatics() { _it = null; Links.Clear(); _signIn = null; }

            public static void Add(CoopLink l)
            {
                if (!_it)
                {
                    var go = new GameObject("Co-op Relay Pump") { hideFlags = HideFlags.HideInHierarchy };
                    DontDestroyOnLoad(go);
                    _it = go.AddComponent<Pump>();
                }
                if (!Links.Contains(l)) Links.Add(l);
            }

            public static void Remove(CoopLink l) => Links.Remove(l);

            void Update()
            {
                for (int i = Links.Count - 1; i >= 0; i--)
                {
                    try { Links[i].Tick(); }
                    catch (Exception e) { Debug.LogException(e); Links[i].Fail("RELAY: " + Short(e)); }
                }
            }

            void OnDestroy() { foreach (var l in Links.ToArray()) l.Dispose(); }
        }
    }
}
#endif
