using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace OnYourMark
{
    /// <summary>S1 포지션 선택 팝업. 접속은 됐지만 아직 내 캐릭터가 없을 때만 보인다.</summary>
    public class RoleSelectUI : MonoBehaviour
    {
        [SerializeField] RoleSelection selection;
        [SerializeField] GameObject panel;
        [SerializeField] Button[] buttons;      // roles와 같은 순서
        [SerializeField] TMP_Text[] labels;
        [SerializeField] TMP_Text status;

        public void Choose(int role) => selection.Choose(role);

        void Update()
        {
            var net = NetworkManager.Singleton;
            bool show = selection.IsSpawned && net.IsConnectedClient && net.LocalClient.PlayerObject == null && !selection.AllChosen;
            if (panel.activeSelf != show) panel.SetActive(show);
            if (!show) return;
            Refresh(net.LocalClientId);

            // The server still arbitrates ownership. An occupied title choice falls back to this picker.
            int? requested = OYM.GameSession.PreferredRole;
            if (requested.HasValue) OYM.GameSession.ClearDeployment();
            if (int.TryParse(LaunchArgs.Get("-role"), out int launchRole)) requested = launchRole;
            if (requested is int role && role >= 0 && role < buttons.Length && buttons[role].interactable)
                Choose(role);
        }

        void Refresh(ulong me)
        {
            bool chosen = selection.HasChosen(me);
            for (int i = 0; i < buttons.Length; i++)
            {
                var role = selection.Roles[i];
                ulong owner = selection.OwnerOf(i);
                string state = owner == me ? "YOU" : owner != RoleSelection.Nobody ? "TAKEN" : role.description;

                buttons[i].interactable = owner == RoleSelection.Nobody && !chosen;
                labels[i].text = $"{role.displayName}\n<size=60%>{state}</size>";
            }
            status.text = chosen ? "Waiting for your partner..." : "Choose your position";
            // Host는 상대가 Join할 방 코드를 알려 준다 (IP를 base64로 바꾼 값, IP는 화면에 보이지 않는다)
            if (NetworkManager.Singleton.IsServer)
                status.text += $"\n<size=60%>Room code: <color=#F08045>{RoomCode.Encode(LocalAddress.Get())}</color></size>";
        }
    }
}
