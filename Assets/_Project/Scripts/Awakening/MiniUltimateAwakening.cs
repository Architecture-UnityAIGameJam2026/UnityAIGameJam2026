using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 3번 파츠 반환 각성 (P1): E 패링으로 투사체 · 레이저 반사에 성공하면
    /// Q(나노 강화제) 효과가 짧게 미니 발동한다 (킬 게이지는 쓰지 않는다).
    /// </summary>
    [CreateAssetMenu(menuName = "On Your Mark/Awakening/Mini Ultimate On Reflect")]
    public class MiniUltimateAwakening : Awakening
    {
        public float seconds = 2.5f;

        public override void OnReflect(AwakeningSet owner) => owner.Me.Ultimate.ActivateFree(seconds);
    }
}
