using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 3번 파츠 반환 각성 (P2) — 초고출력 해방.
    /// - 따발총 모드로 적을 처치하면 Q 킬 게이지가 2배(+2킬)로 찬다.
    /// - 레일건 모드에서 Q를 발동하면 조준한 표적을 끝까지 추적하는 유도 폭발 레이저 한 발:
    ///   표적에 laserDamage, 착탄 지점 반경 blastRadius 안의 적 모두에게 blastDamage.
    /// </summary>
    [CreateAssetMenu(menuName = "On Your Mark/Awakening/Rail Overdrive")]
    public class RailOverdriveAwakening : Awakening
    {
        [Header("따발총 모드")]
        public int arKillValue = 2;

        [Header("레일건 모드 Q")]
        public float laserDamage = 350f;
        public float blastRadius = 5f;
        public float blastDamage = 350f;

        public override int KillValue(AwakeningSet owner)
        {
            var modes = owner.Me.GetComponent<WeaponModes>();
            return modes != null && modes.Current.automatic ? arKillValue : 1;
        }
    }
}
