using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// S0 접속 화면: Host 시작 또는 방 코드로 Join. 내 클라이언트가 연결되면 패널을 닫는다.
    /// 방 코드는 Host IP를 base64로 바꾼 값이다 (RoomCode). 화면에는 IP를 보여 주지 않는다.
    /// </summary>
    public class ConnectionMenu : MonoBehaviour
    {
        [SerializeField] GameObject panel;
        [SerializeField] TMP_InputField address;
        [SerializeField] TMP_Text status;
        [SerializeField] ushort port = 7777;

        static NetworkManager Net => NetworkManager.Singleton;
        static UnityTransport Transport => (UnityTransport)Net.NetworkConfig.NetworkTransport;

        void Start()
        {
            Net.OnClientConnectedCallback += OnConnected;
            Net.OnClientDisconnectCallback += OnDisconnected;

            if (LaunchArgs.Has("-host")) Host();
            else if (LaunchArgs.Get("-join") is { } code) { address.text = code; Join(); }
        }

        void OnDestroy()
        {
            if (Net == null) return;
            Net.OnClientConnectedCallback -= OnConnected;
            Net.OnClientDisconnectCallback -= OnDisconnected;
        }

        public void Host()
        {
            Transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            Show(Net.StartHost() ? "Hosting..." : "Could not start host. Is the port in use?");
        }

        string joiningCode;

        public void Join()
        {
            // 비워 두면 같은 PC의 Host로 접속 (테스트용)
            joiningCode = address.text.Trim();
            string ip = "127.0.0.1";
            if (joiningCode.Length > 0 && !RoomCode.TryDecode(joiningCode, out ip))
            {
                Show("That room code is not valid.\nCheck the code shown on the host's screen (case-sensitive).");
                return;
            }
            Transport.SetConnectionData(ip, port);
            Show(Net.StartClient() ? $"Connecting to room {Label}..." : "Could not start client.");
        }

        string Label => joiningCode.Length > 0 ? joiningCode : "on this PC";

        bool wasConnected;

        void OnConnected(ulong clientId)
        {
            if (clientId != Net.LocalClientId) return;
            wasConnected = true;
            panel.SetActive(false);
            Debug.Log($"[OnYourMark] Connected as client {clientId} ({(Net.IsHost ? "host" : "client")})");
        }

        void OnDisconnected(ulong clientId)
        {
            if (clientId != Net.LocalClientId && Net.IsServer) return;   // 호스트 입장에서 상대가 나간 경우
            panel.SetActive(true);
            Debug.Log($"[OnYourMark] Disconnected (connected before: {wasConnected}, reason: {Net.DisconnectReason})");
            if (!wasConnected)
                // 한 번도 연결되지 못함: 대부분 주소, 같은 네트워크 여부, Host 방화벽 문제
                Show($"Could not reach room {Label}.\nCheck the code, that the host is running, both PCs are on the same network,\nand the host's firewall allows the game.");
            else Show(string.IsNullOrEmpty(Net.DisconnectReason) ? "Disconnected." : Net.DisconnectReason);
            wasConnected = false;
        }

        void Show(string message) => status.text = message;
    }
}
