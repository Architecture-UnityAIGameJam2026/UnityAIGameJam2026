using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// R 재장전 · 2번 파츠(탄창 · 대쉬 수) 수치.
    /// 반환 전에는 파트너가 R로 채워 주면서 오버차지를 얹어 준다.
    /// </summary>
    [CreateAssetMenu(menuName = "On Your Mark/Support/Reload Config")]
    public class ReloadConfig : ScriptableObject
    {
        [Tooltip("R 재장전 쿨타임 (원격 · 자가 공통)")]
        public float cooldown = 5f;

        [Header("파트너가 R로 채워 줄 때 오버차지 (2번 파츠 반환 전)")]
        [Tooltip("P2 탄창을 장탄수의 이 배율만큼 채운다 (150%)")]
        public float magazineOverchargeRatio = 1.5f;
        [Tooltip("장탄수를 넘는 초과 탄환의 대미지 추가 비율 (+30%)")]
        public float overchargeDamageBonus = 0.3f;
        [Tooltip("P1 대쉬를 채운 뒤 이 시간 동안 대쉬가 칸을 쓰지 않는다 (무한 스텝)")]
        public float infiniteDashSeconds = 1f;
    }
}
