using System;
using UnityEngine;
using UnityEngine.UI;

namespace OnYourMark
{
    /// <summary>
    /// 화면 오른쪽 위 콜아웃 표시. 캐릭터마다 칸을 하나씩 쓴다.
    /// - P1과 P2 알림이 겹치면 두 칸이 위아래로 함께 보인다 (먼저 뜬 쪽이 위).
    /// - 같은 캐릭터의 알림이 다시 오면 앞 알림을 minShowSeconds만 보여준 뒤 같은 칸에서 새 알림으로 바꾼다.
    /// 캐릭터 이름표와 색은 PlayerStats의 displayName · color를 쓴다.
    /// </summary>
    public class CalloutView : MonoBehaviour
    {
        [Serializable]
        class Slot
        {
            public RectTransform rect;
            public CanvasGroup group;
            public Text header, korean, english;

            [NonSerialized] public string subject;
            [NonSerialized] public float shownAt, firstShownAt, hideAt;
            [NonSerialized] public Message? pending;

            public bool Visible => subject != null;
        }

        struct Message
        {
            public string subject, speaker, listener, koreanLine, englishLine;
            public Color subjectColor;
            public float seconds;
        }

        [SerializeField] Slot[] slots;
        [SerializeField] float fadeSeconds = 0.4f;
        [Tooltip("같은 캐릭터의 다음 알림으로 바뀌기 전에 앞 알림을 보여주는 최소 시간")]
        [SerializeField] float minShowSeconds = 1f;
        [SerializeField] float slotSpacing = 8f;

        // 프로젝트에 한글 폰트가 없어 OS 한글 폰트를 쓴다
        static readonly string[] KoreanFonts = { "Apple SD Gothic Neo", "AppleGothic", "Malgun Gothic", "맑은 고딕", "Noto Sans CJK KR", "NanumGothic" };
        Vector2 topPosition;

        void Awake()
        {
            var font = Font.CreateDynamicFontFromOSFont(KoreanFonts, 32);
            topPosition = slots[0].rect.anchoredPosition;
            foreach (var s in slots)
            {
                if (font) foreach (var t in new[] { s.header, s.korean, s.english }) t.font = font;
                s.group.alpha = 0f;
            }
        }

        public void Show(string subject, Color subjectColor, string speaker, string listener,
            string koreanLine, string englishLine, float seconds)
        {
            var msg = new Message
            {
                subject = subject, subjectColor = subjectColor, speaker = speaker, listener = listener,
                koreanLine = koreanLine, englishLine = englishLine, seconds = seconds,
            };

            // 같은 캐릭터 칸이 떠 있으면: 앞 알림을 잠깐 보여준 뒤 교체
            var same = Array.Find(slots, s => s.Visible && s.subject == subject);
            if (same != null)
            {
                if (Time.unscaledTime - same.shownAt >= minShowSeconds) Apply(same, msg);
                else same.pending = msg;
                return;
            }

            // 다른 캐릭터면 빈 칸에 함께 띄운다 (빈 칸이 없으면 가장 오래된 칸을 쓴다)
            var free = Array.Find(slots, s => !s.Visible);
            if (free == null)
            {
                free = slots[0];
                foreach (var s in slots) if (s.firstShownAt < free.firstShownAt) free = s;
                free.subject = null;
            }
            free.firstShownAt = Time.unscaledTime;
            Apply(free, msg);
        }

        void Apply(Slot slot, Message m)
        {
            string tag = $"<color=#{ColorUtility.ToHtmlStringRGB(m.subjectColor)}>[{m.subject}]</color>";
            slot.header.text = $"상태 알림   {m.speaker} ▸ {m.listener}";
            slot.korean.text = $"{tag} {m.koreanLine}";
            slot.english.text = m.englishLine;
            slot.subject = m.subject;
            slot.pending = null;
            slot.shownAt = Time.unscaledTime;
            slot.hideAt = slot.shownAt + m.seconds;
            slot.group.alpha = 1f;
        }

        void Update()
        {
            float now = Time.unscaledTime;
            foreach (var s in slots)
            {
                if (!s.Visible) continue;
                if (s.pending.HasValue && now - s.shownAt >= minShowSeconds) { Apply(s, s.pending.Value); continue; }
                if (now > s.hideAt)
                {
                    s.group.alpha = Mathf.MoveTowards(s.group.alpha, 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
                    if (s.group.alpha <= 0f) s.subject = null;
                }
            }

            // 보이는 칸을 먼저 뜬 순서대로 위에서부터 쌓는다
            float y = topPosition.y;
            foreach (var s in Ordered())
            {
                if (!s.Visible) continue;
                s.rect.anchoredPosition = new Vector2(topPosition.x, y);
                y -= s.rect.sizeDelta.y + slotSpacing;
            }
        }

        Slot[] Ordered()
        {
            var copy = (Slot[])slots.Clone();
            Array.Sort(copy, (a, b) => a.firstShownAt.CompareTo(b.firstShownAt));
            return copy;
        }

        /// <summary>테스트용: 지금 보이는 칸들의 내용</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in Ordered())
                if (s.Visible) sb.Append($"[y={s.rect.anchoredPosition.y:0}] {s.subject}: {s.english.text}{(s.pending.HasValue ? " (next: " + s.pending.Value.englishLine + ")" : "")} | ");
            return sb.Length > 0 ? sb.ToString() : "(none)";
        }
    }
}
