using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 원격 지원 효과 하나 (전략 패턴). 에셋 하나가 효과 하나이고, 각 포지션의 PlayerStats가
    /// "상대가 내 파츠를 들고 있을 때 나에게 해 줄 수 있는 지원"으로 이 에셋들을 가진다.
    /// </summary>
    public abstract class SupportEffect : ScriptableObject
    {
        public HudPart part;
        public float cooldown = 6f;

        /// <summary>서버에서 호출.</summary>
        public abstract void Apply(Player supporter, Player target);
    }
}
