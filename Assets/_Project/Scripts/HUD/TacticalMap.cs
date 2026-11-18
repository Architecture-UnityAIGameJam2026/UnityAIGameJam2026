using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>
    /// Tab 확대 전술 지도. Tab을 누르고 있는 동안 화면 중앙에 미니맵을 크게 띄운다 (불투명도 80%).
    /// 보여 주는 지도는 교차 HUD 1번 파츠의 미니맵과 같다 (반환 전에는 파트너 주변).
    /// </summary>
    public class TacticalMap : MonoBehaviour
    {
        [SerializeField] InputActionReference toggle;
        [SerializeField] CanvasGroup panel;
        [Range(0, 1)] [SerializeField] float opacity = 0.8f;

        void Update()
        {
            bool show = Player.Local != null && CursorLock.IsLocked && toggle.action.IsPressed();
            panel.alpha = show ? opacity : 0f;
        }
    }
}
