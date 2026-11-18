using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>팀원 머리 위 요청 아이콘. 상대 화면에만 보인다.</summary>
    public class RequestIndicator : MonoBehaviour
    {
        [SerializeField] SupportRequests requests;
        [SerializeField] NetworkObject owner;
        [SerializeField] TMP_Text label;

        void LateUpdate()
        {
            string text = owner.IsOwner ? "" :
                $"{Icon(HudPart.Bio, "+HP", "7CFC9A")}{Icon(HudPart.Map, " MAP", "8FD3FF")}{Icon(HudPart.Consumable, " AMMO", "FFD166")}";
            label.text = text;
            if (text.Length > 0 && Camera.main) transform.forward = Camera.main.transform.forward;
        }

        string Icon(HudPart part, string text, string hex) => requests.Has(part) ? $"<color=#{hex}>{text}</color>" : "";
    }
}
