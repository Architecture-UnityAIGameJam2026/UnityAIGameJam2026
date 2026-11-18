using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 2 지도 지원. 받는 쪽에 따라 효과가 다르다:
    /// P2가 받으면 적 약점 표식 공유, P1이 받으면 이동 속도 가속.
    /// </summary>
    [CreateAssetMenu(menuName = "On Your Mark/Support/Map")]
    public class MapSupport : SupportEffect
    {
        public enum Kind { WeakPointMark, SpeedRail }

        public Kind kind;
        public float duration = 5f;
        [Tooltip("SpeedRail일 때 이동 속도 배율")]
        public float speedMultiplier = 1.6f;

        public override void Apply(Player supporter, Player target)
        {
            if (kind == Kind.SpeedRail) target.Buffs.Boost(speedMultiplier, duration);
            else target.Buffs.RevealWeakPoints(duration);
        }
    }
}
