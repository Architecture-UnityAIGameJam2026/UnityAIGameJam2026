using UnityEngine;
using UnityEngine.UI;

namespace OnYourMark
{
    /// <summary>화면 오른쪽 위 콜아웃 표시. 말한 사람 → 들은 사람, 한국어 자막, 영어 원문을 잠깐 보여주고 사라진다.</summary>
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

        public void Show(string speaker, Color speakerColor, string listener, string koreanLine, string englishLine, float seconds)
        {
            header.text = $"<color=#{ColorUtility.ToHtmlStringRGB(speakerColor)}>{speaker}</color>  ▸  {listener}";
            korean.text = koreanLine;
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
