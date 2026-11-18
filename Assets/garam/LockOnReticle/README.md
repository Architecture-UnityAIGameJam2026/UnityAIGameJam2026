# 락온 조준점 모듈

평소에는 `( · )` 모양의 작은 조준점, 근거리 락온에서는 적을 감싸는 원, 원거리 락온에서는 중심을 향하는 화살표 3개로 전환됩니다. Figma에서 만든 투명 PNG를 Unity UGUI 프리팹에 연결했습니다. 입력·적 탐색·공격 로직과 독립적으로 사용할 수 있습니다.

## Figma 원본

- [세 상태 미리보기](https://www.figma.com/design/XRi3tZ8GwuovH3Jt4qKWHV/UnityAIGameJam2026?node-id=33-2002)
- [HUD / LockOnReticle 컴포넌트 세트](https://www.figma.com/design/XRi3tZ8GwuovH3Jt4qKWHV/UnityAIGameJam2026?node-id=33-2001)
- 기본 조준점은 [HUD / BracketAim](https://www.figma.com/design/XRi3tZ8GwuovH3Jt4qKWHV/UnityAIGameJam2026?node-id=36-2007)이며, 둥근 괄호 사이에 작은 점을 둡니다. 원거리 조준점의 중심에도 같은 모양을 사용합니다. 기존 `primary`, `accent`, `white`, `dim`, `ink` 색상 변수를 재사용했습니다.
- `Art/figma-source.json`에 에셋별 노드와 원래 크기를 기록했습니다. 임시 다운로드 URL이나 인증 정보는 저장하지 않습니다.

## 바로 사용

1. `Prefabs/LockOnReticle.prefab`을 씬에 드래그합니다. 자체 Overlay Canvas가 포함되어 있습니다.
2. `LockOnReticle > World Camera`에 플레이어 카메라를 지정합니다. 비어 있으면 `Camera.main`을 사용합니다.
3. 적 루트에 `ReticleTarget`을 추가합니다. 필요하면 `Aim Point`에 가슴·머리·약점 Transform을 지정합니다.
4. 근거리 원에 포함할 몸체 Renderer를 `Bounds Renderers`에 지정합니다. 비어 있으면 Awake에서 자식 Renderer를 찾습니다. 이펙트·무기까지 원에 포함시키고 싶지 않다면 몸체만 지정하세요.
5. 기존 타겟 선택 로직에서 `LockOn(target)`과 `ReleaseTarget()`을 호출합니다.

```csharp
using Garam.Reticles;
using UnityEngine;

public sealed class PlayerTargeting : MonoBehaviour
{
    [SerializeField] private LockOnReticle reticle;

    public void SelectEnemy(ReticleTarget enemy)
    {
        reticle.LockOn(enemy);
    }

    public void ClearEnemy()
    {
        reticle.ReleaseTarget();
    }
}
```

```csharp
reticle.rangeMode = ReticleRangeMode.Automatic; // 거리별 Near/Far 자동 선택
reticle.nearDistance = 12f;
reticle.distanceOrigin = player.transform;    // 3인칭에서는 플레이어 기준 거리 권장

reticle.rangeMode = ReticleRangeMode.Near;     // 근접 무기 등에서 근거리 형태 강제
reticle.rangeMode = ReticleRangeMode.Far;      // 원거리 무기 형태 강제
reticle.reducedMotion = true;                 // 수렴·맥동·크로스페이드 없이 표시
```

## 상태별 동작

| 상태 | 위치와 크기 | 움직임 |
| --- | --- | --- |
| Idle | 카메라 화면 중심, 32×12 Canvas 단위 | 둥근 괄호와 중심점 `( · )` |
| Near | 적 Renderer bounds의 화면상 중심, 크기에 맞춘 원 | 전체 투영 bounds의 대각선을 감싸고 여백 추가 |
| Far | `Aim Point`의 정확한 투영 위치, 160×160 Canvas 단위 | 120° 간격 화살표 3개가 1.35배에서 1배로 0.3초 동안 수렴 |

- 기본 전환 시간은 0.18초이며, 원거리에는 약한 맥동이 있습니다. `farPulse = 0`으로 끌 수 있습니다.
- 자동 전환은 기본 12m, 히스테리시스 0.75m입니다. 경계 근처를 오갈 때 깜박이지 않도록 Near→Far는 12.75m 초과, Far→Near는 11.25m 이하에서 바뀝니다.
- 거리는 `Distance Origin`이 있으면 해당 Transform, 없으면 카메라를 기준으로 합니다. 새 적을 선택하면 기존 대상의 거리 상태를 이어받지 않습니다.
- 원 크기는 기본 100~640 Canvas 단위로 제한됩니다. 매우 크거나 아주 가까운 적은 최대 크기에 걸리므로 Inspector에서 조정하세요.
- Renderer가 없으면 `ReticleTarget.fallbackRadius`의 월드 bounds를 사용합니다.
- 대상이 카메라 뒤/화면 밖이면 조준점을 숨기되 락온은 유지합니다. 대상이 삭제·비활성화되면 자동 해제하고 Idle로 돌아갑니다.
- `onLocked`는 새 대상을 선택할 때, `onReleased`는 락온을 해제할 때 한 번 발생합니다. `Target`, `State`, `IsLocked`로 현재 상태를 조회할 수 있습니다.
- 기본적으로 일시정지 중에도 움직입니다. `Use Unscaled Time`을 끄면 게임 시간에 맞춥니다.
- 타겟 가림 판정, 공격 가능 거리, 입력, 적 탐색은 게임에서 처리하고 필요한 시점에 `ReleaseTarget()`을 호출하세요.

## 기존 Canvas에 합치기

프리팹의 `Marker`를 기존 Screen Space Overlay/Camera Canvas의 컨테이너로 옮기고, 컨트롤러의 참조를 연결하면 됩니다. `Marker`는 그 컨테이너의 직접 자식이어야 하며 부모에 회전이나 비균일 스케일을 주지 마세요. Screen Space Camera라면 Canvas의 `Render Camera`도 지정합니다. World Space Canvas는 이 모듈의 지원 대상이 아닙니다.

프리팹은 `CanvasScaler` 기준 해상도 1920×1080을 사용하며 조준점 이미지가 마우스 클릭을 가로채지 않습니다. `Near ring`과 `Far arrows`는 중심 앵커/피벗, 각 그룹은 같은 중심을 유지하세요.

## 에셋 수정과 재생성

Figma 컴포넌트의 링과 화살표는 편집 가능한 벡터입니다. 해당 에셋 노드의 PNG 내보내기 설정은 4배이며, `Art`의 같은 파일명을 교체하면 기존 프리팹 연결이 유지됩니다. PNG는 Figma 원본의 스냅샷이므로 이후 Figma 변수 변경이 게임에 자동 반영되지는 않습니다.

`Tools > Garam > Lock-On Reticle > Rebuild Prefab` 메뉴는 제공된 프리팹을 다시 생성합니다. 생성 프리팹을 직접 수정했다면 별도 프리팹으로 저장한 뒤 사용하세요. Sprite Import 설정도 함께 적용합니다. 런타임에 필요한 것은 `Runtime`, `Art`, `Prefabs` 폴더와 Unity UI 패키지입니다.

## 검증

```powershell
unity test . --mode PlayMode --filter Garam.Reticles.Tests --output Logs/reticle-tests.xml --timeout 300 -- -logFile Logs/reticle-tests.log
```

실제 프리팹으로 Idle/Near/Far 전환, 적 크기 대응, Canvas 화면 좌표 추적, 거리 경계, 수동 전환, 대상 소멸·화면 밖 처리, 일시정지 중 수렴, Figma PNG 투명도 및 Sprite 연결을 확인합니다.
