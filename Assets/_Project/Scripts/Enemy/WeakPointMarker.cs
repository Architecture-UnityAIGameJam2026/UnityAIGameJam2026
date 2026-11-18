using UnityEngine;

namespace OnYourMark
{
    /// <summary>지도 지원(약점 표식)을 받은 플레이어의 화면에서만 적 약점 표식을 켠다.</summary>
    public class WeakPointMarker : MonoBehaviour
    {
        [SerializeField] GameObject marker;

        void LateUpdate()
        {
            var local = Player.Local;
            marker.SetActive(local != null && local.Buffs.RevealsWeakPoints);
        }
    }
}
