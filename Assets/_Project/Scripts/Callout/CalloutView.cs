using UnityEngine;
using UnityEngine.UI;

namespace OnYourMark
{
    /// <summary>
    /// 화면 오른쪽 위 콜아웃 표시. 누구의 상태인지(캐릭터 이름표) + 대사, 말한 사람 → 들은 사람, 영어 원문을 잠깐 보여주고 사라진다.
    /// 캐릭터 이름표와 색은 PlayerStats의 displayName · color를 쓴다.
    /// </summary>
    public class CalloutView : MonoBehaviour
    {
        [SerializeField] CanvasGroup group;
        [SerializeField] Text header, korean, english;
        [SerializeField] float fadeSeconds = 0.4f;

        // 프로젝트에 한글 폰트가 없어 OS 한글 폰트를 쓴다
        static readonly string[] KoreanFonts = { "Apple SD Gothic Neo", "AppleGothic", "Malgun Gothic", "맑은 고딕", "Noto Sans CJK KR", "NanumGothic" };
        float hideAt;

        void Awake()
        {
            var font = Font.CreateDynamicFontFromOSFont(KoreanFonts, 32);
            if (font) foreach (var t in new[] { header, korean, english }) t.font = font;
            group.alpha = 0f;
        }

        public void Show(string subject, Color subjectColor, string speaker, string listener,
            string koreanLine, string englishLine, float seconds)
        {
            string tag = $"<color=#{ColorUtility.ToHtmlStringRGB(subjectColor)}>[{subject}]</color>";
            header.text = $"상태 알림   {speaker} ▸ {listener}";
            korean.text = $"{tag} {koreanLine}";
            english.text = englishLine;
            group.alpha = 1f;
            hideAt = Time.unscaledTime + seconds;
        }

        void Update()
        {
            if (Time.unscaledTime > hideAt && group.alpha > 0f)
                group.alpha = Mathf.MoveTowards(group.alpha, 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
        }
    }
}
