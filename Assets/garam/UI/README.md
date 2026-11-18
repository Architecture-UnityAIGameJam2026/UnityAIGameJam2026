# ON YOUR MARK / SENSELINK

Notion 게임 기획의 **서로의 감각을 대신 보는 협동 HUD**를 구현한 1920×1080 Unity UGUI 시안입니다.

- 기획: https://www.notion.so/41c6d6248d16807a86c9cae370dabff0
- 편집 가능한 Figma: https://www.figma.com/design/XRi3tZ8GwuovH3Jt4qKWHV/UnityAIGameJam2026?node-id=9-577
- 실행 씬: `Scenes/HudShowcase.unity`
- 재사용 Canvas: `Prefabs/SenseLinkHUD.prefab`
- 검증 환경: Unity 6000.3.28f1, URP 17.3, Input System, UGUI.

## 브랜치와 렌더 파이프라인

이 UI 전달 브랜치는 검증한 URP 작업 기준으로 만들었습니다. 저장소 `main`은 이후 HDRP 17.3으로 전환되었습니다. Showcase의 공간 셰이더·후처리 코드는 URP용이며, HDRP `main`에 병합할 때는 해당 부분의 변환이 필요합니다. UI 에셋은 `Assets/garam/UI`에 모여 있습니다.

## 바로 보기

`HudShowcase` 씬을 열고 Play를 누릅니다. Game View를 1920×1080 또는 16:9로 선택하고 포커스를 줍니다.
Edit mode에서 `unity command script eval --file tools/export-hud.cs --project-path .`로 UnityPackage를 생성합니다. 출력은 `.gameharness/artifacts/OnYourMark-SenseLink-UI.unitypackage`이며 다른 프로젝트로 가져올 수 있습니다. Git에는 UI 원본과 `.meta`를 포함합니다.
패키지는 이 UI 폴더만 포함하며 대상 프로젝트에 URP, Input System, UGUI가 설치되어 있어야 합니다.

| 입력 | 동작 |
| --- | --- |
| 양방향 화살표 클릭 / Tab | 교환 채널 메뉴 열기·닫기 |
| 십자 방패 클릭 | 지원 메뉴 열기·닫기 |
| 왼쪽 아래 세 번째 / 네 번째 슬롯 | 실드 / 스캔 바로 요청 |
| 메뉴 카드 클릭 | 교환 또는 지원 요청; 지원 후 2.5초 피드백 |
| × 클릭 | 메뉴 닫기 / 교환 요청 취소 |
| F1 / F2 / F3 / F4 | 기본 / 교환 대기 / 파트너 위기 / 각성 시연 |
| 1 / 2 / 3 | 생체 / 공간 / 소모품 정보 교환 요청 |
| Enter | 같은 채널에 대한 파트너 동의 시뮬레이션 |
| Esc | 메뉴 닫기 및 교환 요청 취소 |
| Q | 빠른 지원 요청 |
| Q 누른 채 1 / 2 / 3 | 실드 / 스캔 / 보급 요청 선택 |
| 마우스 오른쪽 | 위기 상태에서 지원과 회복 시뮬레이션 |
| E | 목표 완료 후 생체 HUD 반환; 완료 전에는 차단 |
| R | UI 전환·게이지·카메라·회전 모션 줄이기 |

교환 요청은 10초 후 만료됩니다. 생명력 15% 이하 또는 잔여 탄약 1/8 이하이면 위기 알림을 표시합니다.

## 기존 씬에 적용

1. `SenseLinkHUD.prefab`을 씬에 추가합니다. 내부 Graphic과 Text는 개별 편집할 수 있습니다.
2. Canvas는 `Screen Space - Camera`, 기준 해상도 `1920×1080`, CanvasScaler Match `0.5`입니다. 렌더 카메라를 지정하거나 `MainCamera` 태그를 사용합니다. `Awake`에서 카메라가 비어 있으면 `Camera.main`을 연결합니다.
3. 카메라의 HDR와 Post Processing을 켭니다. Global Volume에 `Materials/Showcase Post FX.asset`을 연결하면 Bloom, Vignette, ACES, 색 보정, Film Grain이 적용됩니다.
4. 클릭 입력에는 씬의 `EventSystem`과 `InputSystemUIInputModule`이 필요합니다. UI 모듈의 Point/Click 액션을 연결합니다. Showcase 씬에는 설정되어 있습니다.
5. 위기 비네트·카메라 모션까지 사용하려면 카메라에 `HudSceneMotion`을 추가하고 HUD와 Volume을 연결합니다. `reactor`는 선택 사항입니다. 글자 가독성을 위해 색수차는 사용하지 않습니다.
6. 실제 게임 연결 시 `OnYourMarkHud.demoInput`을 끕니다. 클릭과 이벤트 바인딩은 유지됩니다. 씬의 레벨·파트너 마네킹·고정 미니맵은 시연용입니다.

```csharp
hud.demoInput = false;
hud.SetPartnerTelemetry(partnerHealth01, partnerAmmo, partnerCapacity, partnerResourceIsDash);
hud.localHealth = localHealth01;
hud.localAmmo = localAmmo;
hud.localAmmoCapacity = localCapacity;
hud.localResourceIsDash = localResourceIsDash;

// 게임/네트워크 계층에서 이벤트를 구독합니다.
hud.ExchangeRequested += SendExchangeRequest;
hud.SupportRequested += SendSupportRequest;
hud.HudReturned += ApplyAwakening;

// 두 플레이어가 동일 채널에 동의한 것을 서버가 검증한 뒤 호출합니다.
hud.ConfirmExchange(channel); // 0: 생체, 1: 공간, 2: 소모품

// 게임의 목표 시스템에서만 설정합니다.
hud.sectorComplete = true;
hud.ReturnHud(0);
```

컨트롤러는 로컬 UI 상태와 이벤트를 제공합니다. 게임 연결은 아래의 `HudPlayerBridge`가 담당하며, Showcase에서는 기존 시연 입력을 사용할 수 있습니다.
구독한 이벤트는 연결한 게임 컴포넌트의 수명에 맞춰 해제하세요.
현재 HUD의 텍스트는 체력·탄약·거리 숫자 3개뿐입니다. 설명 문구를 추가하거나 한국어로 현지화할 경우 한글 폰트를 연결하세요.

### feature/prototype의 Arena 연결

- `main`의 SenseLink 프리팹과 HUD 연결 코드를 바탕으로 Arena에 적용했습니다. 렌더링은 이 브랜치의 URP 설정을 사용합니다.
- `Assets/_Project/Scripts/Garam/HudPlayerBridge.cs`가 실제 플레이어, `CrossHudState`, `PartExchange`를 연결합니다. Arena에서는 시연 입력을 끄고, 플레이어가 생성된 동안만 HUD를 표시합니다.
- 검 화면은 레일건 잔탄(최대 20발), 레일건 화면은 검 대시(최대 3회)를 표시합니다. 숫자는 실제 남은 수량이며, 아래 눈금은 용량에 비례합니다. 대시는 3칸을 사용합니다.
- 교환 버튼은 서버의 기존 제안·동의 흐름으로 연결되고, 취소는 제안자만 할 수 있습니다. 지원 버튼은 실제 지원 요청을 보냅니다. 키보드 1/2/3, Q, 조준 후 지원 동작도 유지됩니다.
- Esc로 커서를 푼 뒤 왼쪽 아래 HUD 버튼을 클릭합니다. HUD 밖을 클릭하면 다시 1인칭 조작으로 돌아갑니다.
- 지도에는 기존 교차 HUD의 실제 RenderTexture를 연결했습니다. 웨이브, 호버, 교환·소생·단말기 안내는 기존 게임 표시를 함께 사용합니다.
- HUD 반환 및 라운드 재시작은 서버 상태를 따릅니다. 기존 `SetPartnerTelemetry(health, ammo)` 호출은 8칸 시연용으로 계속 사용할 수 있습니다.

## 디자인과 효과

- Soft Signal 팔레트: 라벤더 `#B7A6F6`는 기본 정보, 아이보리 `#F3EEDD`는 숫자·무기, 라임 `#D5EF86`은 경로·교환·반환·호버, 코랄 `#F78E72`는 위험 상태입니다. 런타임 상태 색도 공통 JSON 팔레트를 읽습니다.
- 승인된 네 모서리 배치를 유지하고 색감·윤곽을 다듬었습니다. 미니맵·슬롯·메뉴는 둥근 모서리, 체력 수치는 열린 브래킷입니다. 왼쪽 위는 가로 체력 바 `(48, 36)` / `428×76`, 오른쪽 위는 미니맵 `(1632, 40)` / `240×250`, 지도 아래는 목표 `(1568, 328)`입니다.
- 왼쪽 아래 `(48, 948)`부터 교환·지원·실드·스캔 슬롯 4개를 배치했습니다. 교환·지원 메뉴는 슬롯 바로 위 `(48, 768)`에 열립니다. 중앙 하단에는 버튼이나 메뉴가 없습니다.
- 기본 텍스트 16개 → 숫자 3개(체력·탄약·거리). 제목·상태·설명·버튼 문구는 모두 아이콘으로 교체했습니다. 목표 진행도는 원형 점 3개, 소유권은 라벤더 두 사람 / 라임 한 사람으로 표시합니다.
- 무기·탄약은 오른쪽 아래 `(1552, 960)` / `320×80`에 있습니다. 총은 `160×51.2`, 검은 `80×19.2`, 수류탄은 `21×28`입니다. 표시 무기는 시연용이며 실제 인벤토리 연동은 별도입니다.
- 아이보리 탄약 숫자를 왼쪽, 아이보리 무기 선화를 오른쪽에 배치하고, 내부 분할선·총열·탄창이 보이도록 면 채우기를 없앴습니다. 무기 영역에는 배경 박스·외곽선·모서리 장식이 없습니다. 주무기 아래 짧은 라임 발광선과 탄약 칸만 유지합니다.
- 체력은 수치에 따라 길이가 바뀌는 가로 바입니다. 파트너 체력은 라벤더, 반환받은 내 체력은 라임, 위험 상태는 코랄로 표시합니다. 그 아래 짧은 라벤더 눈금 8개는 시연용 연결 상태 표시입니다.
- 전체 네이티브 UGUI Graphic 176개. 교환·지원 메뉴는 `304×152`이며 한 번에 하나만 표시합니다. 슬롯은 `64×76`, 최소 버튼 클릭 영역은 `44×44`입니다.
- 21개 벡터 아이콘을 공통 레이아웃 JSON에서 Unity와 Figma에 적용합니다. 심장/접힌 지도/탄환은 교환 채널, 방패/레이더/상자는 지원 선택, 체크는 요청 완료를 뜻합니다. 무기 아이콘은 종횡비를 유지한 1.25px 선과 제한적인 발광을 사용합니다.
- `HudActionButton`: 클릭, 호버 배경·발광 애니메이션, 키보드 Submit 이벤트. 숨긴 메뉴는 레이캐스트를 가로채지 않습니다.
- `HologramUI.shader`: 투명 합성, HDR 발광, 미세 스캔라인, UGUI 스텐실/클리핑 지원.
- 본문 머티리얼 발광 1.1, 강조선 머티리얼 발광 3.2. URP Bloom intensity 0.85 / threshold 1.1 / scatter 0.65로 강조선과 조명 중심의 빛 번짐을 만듭니다.
- `IonSurface.shader`: HDR 발광과 안개가 적용되는 공간 조명 스트립.
- 등장 페이드, 메뉴 확대/페이드, 체력 보간, 위기 비네트, 코어 회전. 지원 알림은 2.5초, 각성 알림은 4초 후 사라집니다.
- 폰트: Rajdhani SemiBold / IBM Plex Mono Regular. SIL OFL 라이선스를 `Fonts`에 포함했습니다.
- 배경은 Unity 기본 메시로 만든 편집 가능한 시연 공간입니다. 레퍼런스 이미지를 UI 텍스처로 복사하지 않았습니다.

## Figma 및 원본

Figma에는 1920×1080 화면 9개, 기본/호버 변형의 인터랙티브 컴포넌트 세트 13개, 클릭 연결 48개가 있습니다. 텍스트·벡터·색상 변수를 유지하며 이미지로 합쳐진 UI는 없습니다. 호버는 0.16초 Smart Animate와 Glow를 사용합니다.

| 상태 | Figma 노드 |
| --- | --- |
| 기본 | 9:577 |
| 생체 교환 대기 | 9:994 |
| 위기 | 9:1423 |
| 각성 | 9:1852 |
| 교환 메뉴 | 15:1538 |
| 지원 메뉴 | 15:1628 |
| 지도 교환 대기 | 18:615 |
| 소모품 교환 대기 | 18:814 |
| 지원 전송 피드백 | 18:1013 |

루트 `tools/generate-hud-layout.mjs`가 공통 레이아웃과 아이콘 JSON을 생성합니다. `Design/create-figma.js`는 초기 제작 소스, `Design/refine-figma.js`, `Design/wire-figma-prototype.js`, `Design/complete-figma-prototype.js`는 개정 2 마이그레이션입니다. `Design/icon-figma.js`는 개정 3의 크기·아이콘·무기 실루엣, `Design/frameless-weapons-figma.js`는 개정 4의 무기 박스 제거를 적용합니다. 초기 마이그레이션에는 재실행 방지 장치가 있으며 실시간 양방향 동기화는 아닙니다.
최신 개정 7의 색상·윤곽은 `Design/soft-signal-figma.js`가 적용합니다. 기존 9개 프레임 ID, 위치·크기, 인터랙션을 보존하고 색상 변수도 갱신합니다. `Design/cyberpunk-layout-figma.js`는 개정 6의 배치 이력입니다. Figma 지원 완료 화면에서 즉시 지원 슬롯을 다시 누르면 기본 화면으로 돌아갑니다.

Unity에서 레이아웃 JSON 변경을 반영하려면 Edit mode의 `HudShowcase`에서 `HudShowcaseBuilder.RebuildInteractiveHud()`를 실행합니다. 씬의 HUD 자식과 프리팹을 재생성하므로 개별 수정 사항을 먼저 보관하세요. 초기 제작용 `Create()`, `LightChamber()`는 최신 HUD 갱신용이 아닙니다.

## 수행한 검증

- C# 컴파일: 오류 0 / 경고 0.
- `HudShowcaseBuilder.Validate()`: Canvas 설정, 폰트, 교차 소유권, 일치/불일치 동의, 목표 게이트, 체력/탄약 임계값, 각성 통과.
- `tools/validate-hud-interaction.cs`: 네 모서리 배치, 가로 체력 바의 수치·소유권 반영, 즉시 실드/스캔 슬롯, 이동한 메뉴의 UGUI 레이캐스트·포인터 이벤트, 호버, 피드백, 메뉴 중복 방지, 숨긴 메뉴 입력 차단 통과. 숫자 3개, 최소 44px 클릭 영역, 요청 채널·소유권 아이콘 전환, 무기 종횡비도 확인했습니다.
- 이번 개정의 OS 마우스·키보드 입력 검증은 완료하지 못했습니다. `Application.isFocused == false`에서 입력 주입 검증이 실패했으며 원인은 확정하지 않았습니다. `tools/validate-hud-input.cs`는 전경 포커스 사전 조건을 추가했으나 이번 버전의 성공 결과는 없습니다.
- 실제 Game View에서 기본·교환 메뉴·지원 메뉴·교환 대기·위기·각성의 1920×1080 렌더 확인. 이미지: `.gameharness/artifacts/unity-*.png`.
- Figma 화면 및 프로토타입 연결 구조 확인. 프레젠테이션 모드의 수동 클릭 검증은 수행하지 않았습니다.
- 별도 플레이어 실행 파일 빌드 및 다른 프로젝트로 재임포트 검증은 수행하지 않았습니다.

```powershell
unity recompile --project-path . --format json
# HudShowcase가 열린 Play mode에서:
unity command script eval --code 'OnYourMark.UI.Editor.HudShowcaseBuilder.Validate()' --project-path . --format json
unity command script run --file tools/validate-hud-interaction.cs --entry HudInteractionValidation.Run --project-path . --format json
```
