using UnityEngine;

namespace OnYourMark
{
    /// <summary>P1 생체 각성: 처치 시 잠깐 이동 속도 증가 + 대쉬 스택 즉시 충전.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Awakening/Kill Rush")]
    public class KillRushAwakening : Awakening
    {
        public float speedMultiplier = 1.3f;
        public float duration = 3f;

        public override void OnKill(AwakeningSet owner)
        {
            owner.Me.Buffs.Boost(speedMultiplier, duration);
            owner.ResetDashRpc();
        }
    }
}
