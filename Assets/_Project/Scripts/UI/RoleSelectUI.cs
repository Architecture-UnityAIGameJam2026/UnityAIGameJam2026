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
            bool show = selection.IsSpawned && net.IsConnectedClient && net.LocalClient.PlayerObject == null;
            if (panel.activeSelf != show) panel.SetActive(show);
            if (show) Refresh(net.LocalClientId);
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
        }
    }
}
