using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>S0 접속 화면: Host 시작 또는 IP로 Join. 내 클라이언트가 연결되면 패널을 닫는다.</summary>
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
            else if (LaunchArgs.Get("-join") is { } ip) { address.text = ip; Join(); }
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

        string joiningAddress;

        public void Join()
        {
            joiningAddress = string.IsNullOrWhiteSpace(address.text) ? "127.0.0.1" : address.text.Trim();
            Transport.SetConnectionData(joiningAddress, port);
            Show(Net.StartClient() ? $"Connecting to {joiningAddress}..." : "Could not start client.");
        }

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
                Show($"Could not reach {joiningAddress}:{port}.\nCheck the host is running, both PCs are on the same network,\nand the host's firewall allows the game.");
            else Show(string.IsNullOrEmpty(Net.DisconnectReason) ? "Disconnected." : Net.DisconnectReason);
            wasConnected = false;
        }

        void Show(string message) => status.text = message;
    }
}
