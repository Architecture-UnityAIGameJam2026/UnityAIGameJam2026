using System.IO;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// OpenRouter Jev 호출 설정. 호스트의 키 파일 경로나 기존 인스펙터 키를 사용한다.
    /// 이 에셋은 Assets/_Project/Secrets/에 두며 git에 올리지 않는다 (.gitignore).
    /// 판단은 호스트만 하므로 키는 호스트 PC에만 있으면 된다.
    /// </summary>
    [CreateAssetMenu(menuName = "On Your Mark/Jev Settings")]
    public class JevSettings : ScriptableObject
    {
        [Tooltip("OpenRouter API 키 (sk-or-...)")]
        public string apiKey = "";
        [Tooltip("호스트 PC의 OpenRouter 키 파일 경로. 지정하면 호출 시 파일에서 읽습니다.")]
        public string apiKeyFile = "";
        public string endpoint = "https://openrouter.ai/api/v1/systemone";
        public string model = "jev-latest";
        public float timeoutSeconds = 8f;
        [Tooltip("이 점수보다 낮은 선택은 무시한다 (0이면 모두 사용)")]
        [Range(0, 1)] public float minConfidence = 0f;
        [Tooltip("요청·응답을 콘솔에 기록")]
        public bool logRequests = true;

        string JevPath => string.IsNullOrWhiteSpace(apiKeyFile) ? System.Environment.GetEnvironmentVariable("OPENROUTER_KEY_FILE") : apiKeyFile;
        string JevKey => string.IsNullOrWhiteSpace(apiKey) ? System.Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") : apiKey;
        public bool HasKey => string.IsNullOrWhiteSpace(JevPath) ? !string.IsNullOrWhiteSpace(JevKey) : File.Exists(JevPath);

        public string speechKeyFile = "";
        public bool TryGetSpeechKey(out string key)
        {
            ReadKey(string.IsNullOrWhiteSpace(speechKeyFile) ? System.Environment.GetEnvironmentVariable("ELEVENLABS_KEY_FILE") : speechKeyFile, System.Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY"), out key);
            if (string.IsNullOrWhiteSpace(key)) return false;
            var matches = System.Text.RegularExpressions.Regex.Matches(key, @"(?<![A-Za-z0-9_])sk_[A-Za-z0-9_-]+");
            key = matches.Count == 1 ? matches[0].Value : null;
            return key != null;
        }
        public bool TryGetApiKey(out string key) => ReadKey(JevPath, JevKey, out key);
        static bool ReadKey(string path,string fallback,out string key)
        {
            key = null;
            try
            {
                key = string.IsNullOrWhiteSpace(path) ? fallback?.Trim() : File.ReadAllText(path).Trim();
                return !string.IsNullOrWhiteSpace(key) && key.IndexOfAny(new[] { '\r', '\n' }) < 0;
            }
            catch (IOException) { return false; }
            catch (System.UnauthorizedAccessException) { return false; }
        }
    }
}
