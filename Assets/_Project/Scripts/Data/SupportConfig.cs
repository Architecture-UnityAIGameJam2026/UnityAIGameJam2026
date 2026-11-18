using UnityEngine;

namespace OnYourMark
{
    /// <summary>원격 체력 지원 · 월드 핑 공통 수치.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Support/Config")]
    public class SupportConfig : ScriptableObject
    {
        [Header("지원 판정")]
        [Tooltip("조준선과 팀원 사이 거리가 이 이내면 팀원을 조준한 것으로 본다 (소프트 락온)")]
        public float softLockRadius = 1.5f;
        public float supportRange = 60f;

        [Header("월드 핑")]
        public GameObject pingPrefab;
        public float pingDuration = 3f;
        public LayerMask pingMask;
    }
}
