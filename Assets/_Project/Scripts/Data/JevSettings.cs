using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// OpenRouter Jev 호출 설정. API 키는 인스펙터에서 입력한다.
    /// 이 에셋은 Assets/_Project/Secrets/에 두며 git에 올리지 않는다 (.gitignore).
    /// 판단은 호스트만 하므로 키는 호스트 PC에만 있으면 된다.
    /// </summary>
    [CreateAssetMenu(menuName = "On Your Mark/Jev Settings")]
    public class JevSettings : ScriptableObject
    {
        [Tooltip("OpenRouter API 키 (sk-or-...)")]
        public string apiKey = "";
        public string endpoint = "https://openrouter.ai/api/v1/systemone";
        public string model = "jev-latest";
        public float timeoutSeconds = 8f;
        [Tooltip("이 점수보다 낮은 선택은 무시한다 (0이면 모두 사용)")]
        [Range(0, 1)] public float minConfidence = 0f;
        [Tooltip("요청·응답을 콘솔에 기록")]
        public bool logRequests = true;

        public bool HasKey => !string.IsNullOrWhiteSpace(apiKey);
    }
}
