using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// Q 궁극기 하나 (전략 패턴). 킬 게이지가 차면 발동해 duration 동안 효과가 켜진다.
    /// 게이지를 누가 채우고 누가 발동하는지는 3번 파츠 상태에 따라 Ultimate가 정한다.
    /// 훅은 모두 서버에서 호출된다. 능력치 배율은 무기 · 이동이 읽는다.
    /// </summary>
    public abstract class UltimateData : ScriptableObject
    {
        public string displayName = "ULTIMATE";
        public float duration = 8f;
        [Tooltip("충전에 필요한 처치 수")]
        public int killsRequired = 15;
        [Tooltip("3번(스킬) 파츠를 돌려받은 뒤 필요한 처치 수 (같으면 변화 없음)")]
        public int killsRequiredAwakened = 15;

        public virtual float DamageMultiplier => 1f;
        public virtual float AttackSpeedMultiplier => 1f;
        public virtual float MoveSpeedMultiplier => 1f;

        public virtual void Begin(Ultimate u) { }
        public virtual void Tick(Ultimate u, float dt) { }
        public virtual void End(Ultimate u) { }
    }
}
