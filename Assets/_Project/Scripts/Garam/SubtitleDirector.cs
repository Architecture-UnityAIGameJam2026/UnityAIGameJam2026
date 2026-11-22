using Garam.Subtitles;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OnYourMark
{
    /// <summary>
    /// garam 외계어 자막을 프로토타입에 붙이는 테스트용 연출.
    /// 게임 이벤트(플레이어 스폰, 웨이브 클리어, 전체 클리어)에 자막을 띄우고, T 키로 샘플 대사를 순서대로 재생한다.
    /// 프로젝트에 한글 폰트가 없어서 OS 한글 폰트(맑은 고딕 등)를 런타임에 연결한다.
    /// </summary>
    public class SubtitleDirector : MonoBehaviour
    {
        [SerializeField] AlienSubtitleEffect subtitle;
        [SerializeField] Text label;
        [SerializeField] CanvasGroup group;
        [SerializeField] float holdSeconds = 4f;
        [SerializeField] float fadeSeconds = 0.4f;

        [Header("게임 이벤트 (서버/호스트에서 Raise)")]
        [SerializeField] GameEvent playersSpawned;
        [SerializeField] GameEvent waveCleared;
        [SerializeField] GameEvent allWavesCleared;

        [TextArea] [SerializeField] string introLine = "외계 신호 수신. 번역을 시작합니다.";
        [TextArea] [SerializeField] string spawnLine = "감각 연결 완료. 서로의 시야를 확인하십시오.";
        [TextArea] [SerializeField] string waveClearedLine = "적 신호 소멸. 다음 파동이 접근 중입니다.";
        [TextArea] [SerializeField] string allClearedLine = "구역 확보. 생체 HUD를 반환할 수 있습니다.";
        [TextArea] [SerializeField] string[] sampleLines =
        {
            "외계 신호 수신. 번역을 시작합니다.",
            "왼쪽 잔해 뒤에 적 두 기. 엄호 바랍니다.",
            "실드 셀 확보. 파트너에게 전달하십시오.",
            "경고: 추진제 탱크 근처에서 교전하지 마십시오.",
        };

        static readonly string[] KoreanFonts = { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "NanumGothic" };

        int sampleIndex;
        float hideAt = float.MaxValue;

        void Awake()
        {
            if (label && (label.font == null || !label.font.HasCharacter('한')))
            {
                var os = OYM.Web.WebFonts.IsWeb ? OYM.Web.WebFonts.Korean : Font.CreateDynamicFontFromOSFont(KoreanFonts, label.fontSize);
                if (os) label.font = os;
            }
            if (group) group.alpha = 0f;
        }

        void OnEnable()
        {
            if (playersSpawned) playersSpawned.Raised += OnSpawned;
            if (waveCleared) waveCleared.Raised += OnWaveCleared;
            if (allWavesCleared) allWavesCleared.Raised += OnAllCleared;
        }

        void OnDisable()
        {
            if (playersSpawned) playersSpawned.Raised -= OnSpawned;
            if (waveCleared) waveCleared.Raised -= OnWaveCleared;
            if (allWavesCleared) allWavesCleared.Raised -= OnAllCleared;
        }

        void Start() => Show(introLine);

        void OnSpawned() => Show(spawnLine);
        void OnWaveCleared() => Show(waveClearedLine);
        void OnAllCleared() => Show(allClearedLine);

        public void Show(string line)
        {
            if (!subtitle || string.IsNullOrEmpty(line)) return;
            subtitle.Play(line);
            if (group) group.alpha = 1f;
            hideAt = Time.unscaledTime + subtitle.duration + holdSeconds;
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame && sampleLines.Length > 0)
            {
                Show(sampleLines[sampleIndex]);
                sampleIndex = (sampleIndex + 1) % sampleLines.Length;
            }

            if (group && Time.unscaledTime > hideAt)
                group.alpha = Mathf.MoveTowards(group.alpha, 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
        }
    }
}
