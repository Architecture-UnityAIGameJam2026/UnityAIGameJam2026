using UnityEngine;

namespace OnYourMark
{
    /// <summary>2번 파츠 반환 각성 (P1): 적을 처치할 때마다 대쉬 칸을 즉시 돌려받는다.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Awakening/Kill Dash Refund")]
    public class KillDashRefundAwakening : Awakening
    {
        public int stacks = 1;

        public override void OnKill(AwakeningSet owner) => owner.AddDashRpc(stacks);
    }
}
