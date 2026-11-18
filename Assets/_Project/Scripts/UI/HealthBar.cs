using UnityEngine;
using UnityEngine.UI;

namespace OnYourMark
{
    /// <summary>월드 공간 체력바. 카메라를 향하고 Health 비율만큼 채운다.</summary>
    public class HealthBar : MonoBehaviour
    {
        [SerializeField] Health health;
        [SerializeField] Image fill;

        void LateUpdate()
        {
            fill.fillAmount = health.Ratio;
            if (Camera.main) transform.forward = Camera.main.transform.forward;
        }
    }
}
