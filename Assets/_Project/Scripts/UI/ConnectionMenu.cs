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

        public void Join()
        {
            string ip = string.IsNullOrWhiteSpace(address.text) ? "127.0.0.1" : address.text.Trim();
            Transport.SetConnectionData(ip, port);
            Show(Net.StartClient() ? $"Connecting to {ip}..." : "Could not start client.");
        }

        void OnConnected(ulong clientId)
        {
            if (clientId == Net.LocalClientId) panel.SetActive(false);
        }

        void OnDisconnected(ulong clientId)
        {
            if (clientId != Net.LocalClientId && Net.IsServer) return;   // 호스트 입장에서 상대가 나간 경우
            panel.SetActive(true);
            Show(string.IsNullOrEmpty(Net.DisconnectReason) ? "Disconnected." : Net.DisconnectReason);
        }

        void Show(string message) => status.text = message;
    }
}
