using UnityEngine;

namespace OnYourMark
{
    /// <summary>3 소모품 지원: 탄창 / 검기 칸을 즉시 가득 채운다.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Support/Consumable")]
    public class ConsumableSupport : SupportEffect
    {
        public override void Apply(Player supporter, Player target) => target.Ammo.Fill();
    }
}
