# 외계어 → 한국어 자막 효과

`UnityEngine.UI.Text`에 붙여 쓰는 독립 모듈입니다. 한국어 문장 전체가 외계 기호로 보이다가 왼쪽부터 원문으로 해독됩니다. 실제 번역 API를 호출하는 기능이 아니라 **이미 준비된 한국어 자막을 드러내는 시각 효과**입니다.

PlanetGame의 [`OblationTerminalText`](https://github.com/kgr0831/PlanetGame/blob/d1c2f6430e98d31c0ec3b510762b64c16065f504/Assets/Scripts/OblationTerminalText.cs)를 바탕으로 게임 매니저·오디오 매니저 의존성을 제거했습니다. 원본처럼 `Text.text`, 글자 위치, 줄바꿈을 유지하고 **UV와 색상만** 바꿉니다. HDRP/URP/Built-in 렌더 파이프라인과 무관하게 Canvas에서 동작합니다.

## 설치

1. Canvas 아래 `UI > Legacy > Text`를 만들거나 기존 `Text` 자막을 선택합니다.
2. 한국어를 지원하는 폰트 에셋을 `Text > Font`에 지정합니다. 빌드에서도 사용할 폰트를 프로젝트에 포함하세요.
3. 같은 오브젝트에 `Garam > Subtitles > Alien Subtitle Effect` 컴포넌트를 추가합니다.
4. `Text`에 한국어 문장을 넣고 Play 모드로 실행합니다. 기본값은 0.6초 동안 초당 18회 기호를 갱신합니다.

현재 모듈은 PlanetGame 및 garam UI와 같은 **Legacy UI Text용**입니다. TextMeshPro/TMP_Text에 붙이는 컴포넌트는 아닙니다. `Shadow`/`Outline`을 함께 쓰면 Inspector에서 **Alien Subtitle Effect를 그 컴포넌트보다 위에** 두세요. 디코더는 Text의 4정점 글자 메시에 먼저 적용되어야 합니다.

다른 프로젝트에서는 `SubtitleEffect` 폴더 전체를 `Assets` 안으로 복사하면 됩니다. 런타임에 필요한 것은 `Runtime` 폴더와 Unity UI 패키지뿐이며, 이 저장소에는 필요한 `com.unity.ugui`가 이미 설치되어 있습니다. 테스트 폴더는 Unity Test Framework가 있는 프로젝트에서만 사용하세요.

## 호출 예제

```csharp
using Garam.Subtitles;
using UnityEngine;

public sealed class DialogueSubtitle : MonoBehaviour
{
    [SerializeField] private AlienSubtitleEffect subtitle;

    public void OnDialogueLine(string koreanLine)
    {
        subtitle.Play(koreanLine);
    }

    public void SkipDecode()
    {
        subtitle.Complete();
    }
}
```

```csharp
subtitle.Play("외계 신호 수신. 번역을 시작합니다.");
subtitle.Replay();                      // 같은 문장을 처음부터 다시 해독
subtitle.Complete();                    // 원문을 즉시 표시
subtitle.duration = 1.2f;               // 해독 시간
subtitle.reducedMotion = true;          // 애니메이션 없이 원문 표시
subtitle.onCompleted.AddListener(OnSubtitleDecoded);
```

`Play`, `Replay`, `Complete`는 Unity UI Button의 On Click에도 연결할 수 있습니다. 인스펙터의 `On Decode Tick`에 짧은 효과음을 연결하면 기호가 바뀔 때 소리를 낼 수 있습니다. 오디오 시스템이나 특정 게임 클래스는 필요하지 않습니다.

## 동작과 옵션

| 옵션/API | 기본값 / 동작 |
| --- | --- |
| `duration` | 0.6초. 0 이하면 즉시 원문 표시 |
| `scrambleRate` | 초당 18회 기호 변경 |
| `playOnEnable` | 켜짐. 오브젝트/컴포넌트 활성화 시 재생 |
| `useUnscaledTime` | 켜짐. `Time.timeScale = 0`에서도 해독 진행 |
| `reducedMotion` | 꺼짐. 켜면 원문 즉시 표시 |
| `ignoreNumericChanges` | 켜짐. 같은 길이에서 숫자끼리만 바뀌면 재시작하지 않음 |
| `alienSymbols` | `△▽◇○□⊙⊕∴∵×`. 폰트가 지원하는 기호만 사용 |
| `alienTint` | 밝은 청록색에서 원래 글자색으로 복원. 원래 알파값 유지 |
| `Progress` | 0~1. 정지/완료/즉시 표시 상태는 1 |
| `IsPlaying` | 현재 해독 애니메이션 진행 여부 |
| `onCompleted` | 재생 완료 또는 `Complete()` 시 한 번 호출. 즉시 표시 재생은 다음 LateUpdate에 호출 |

- `Text.text`를 직접 바꾸어도 자동으로 재생합니다. **같은 문장**을 다시 재생하려면 `Play` 또는 `Replay`를 호출하세요.
- `playOnEnable = false`는 활성화 시 자동 재생만 끕니다. 이후 `Play`/`Replay`나 원문 변경은 재생됩니다.
- `null`/빈 문장은 자막을 비우고 재생하지 않습니다. 완료 이벤트도 발생하지 않습니다.
- 컴포넌트를 비활성화하면 원문 메시가 복구되고 완료 이벤트 없이 재생이 취소됩니다. 비활성 상태의 `Play`는 원문만 설정하며, 다음 활성화 시 `playOnEnable` 설정을 따릅니다.
- 공백·줄바꿈·리치 텍스트 태그는 유지됩니다. 외계 기호는 원문 글자의 사각형 안에 맞춰지므로 원문과 폭이 다른 기호는 늘어나 보일 수 있습니다.
- 지원되는 기호가 없으면 폰트에 있는 `<>+*/=|`를 사용합니다. 대체 기호도 없거나 폰트가 없으면 원문을 그대로 둡니다. 원문 한글 자체가 네모로 보이면 폰트의 한글 지원을 확인하세요.

## 검증

Unity 6000.3.28f1에서 PlayMode 테스트를 실행합니다. 한국어 렌더링 검증에는 OS에 `Malgun Gothic`, `Apple SD Gothic Neo`, `Noto Sans CJK KR` 중 하나가 필요합니다. 게임 런타임은 OS 폰트를 자동으로 선택하지 않습니다.

```powershell
unity test . --mode PlayMode --filter Garam.Subtitles.Tests --output Logs/subtitle-tests.xml --timeout 600 -- -logFile Logs/subtitle-tests.log
```

테스트는 외계 기호 → 부분 해독 → 원문 복원, 한국어·리치 텍스트의 위치/UV/알파 보존, 동일 문장 재생, 비활성화 복구, 숫자 갱신, 일시정지, 즉시 완료, 폰트 변경/기호 대체, Best Fit 및 Outline 조합을 확인합니다.
