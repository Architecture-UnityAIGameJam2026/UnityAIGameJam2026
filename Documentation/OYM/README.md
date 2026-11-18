# OnYourMarkUnity — 데브리 필드 아레나 (Unity 6 HDRP)

> **팀 저장소 병합 메모 (2026-11-19, 데모 v0.1)**
> 이 문서는 아트 작업용 로컬 프로젝트(`OnYourMark/OnYourMarkUnity`)의 README를 옮긴 것입니다. 팀 저장소에서는 아래가 다릅니다.
> - 에셋 위치는 같습니다(`Assets/OYM/`). 문서 속 `Tools/`, `../skybox`, `../meshy`, `../pipeline`, `Screenshots/` 경로는 아트 작업 PC에만 있는 원본·파이프라인 폴더라 저장소에는 없습니다.
> - **ProjectSettings는 팀 설정을 씁니다**: 중력 -9.81(무중력은 오브젝트별 `useGravity = false` / kinematic), 레이어 8 Player · 9 Enemy · 10 Environment · 11 Terminal, 기본 파이프라인 `Assets/Settings/HDRP High Fidelity`. `OYM_HDRP.asset`은 참고용으로만 남아 있고 기본값으로 지정되지 않습니다(그래서 SSR·셰도마스크 같은 일부 기능은 팀 HDRP 에셋 설정을 따릅니다).
> - `OYM ▸ Rebuild Everything` / `1. Configure Project`는 이제 **프로젝트 설정을 건드리지 않습니다**(중력·레이어·그래픽 API·기본 파이프라인·빌드 씬 덮어쓰기 제거). 빌드 씬은 없을 때 뒤에 추가만 하고, 0번 씬은 팀 결정(현재 `Arena`)을 유지합니다.
> - 잔해·운석·프랍 같은 장애물은 `Obstacle`(8) 대신 팀의 **`Environment`(10)** 레이어에 있습니다(`OYMSetup.ObstacleLayer`). 기존 씬·프리팹의 레이어도 같이 옮겼습니다.
> - 스카이박스: 16K 원본(439 MB)은 GitHub 파일 한도(100 MB)를 넘어서 **7680×3840**(`Generated/Sky/skybox_equirect_7680_nosun.hdr`, GUID 동일)으로 줄였습니다. 임포트 설정이 큐브맵 면 2048(Standalone)이라 런타임 결과는 같습니다. 16K 원본은 아트 PC의 `skybox/` 출력에 있습니다.
> - `BossIntro.unity`: 행성·대기 캡 메쉬(약 140만 정점)가 씬에 직렬화돼 128 MB였던 것을 `OYM.ProceduralCapMesh`가 로드 때 만들도록 바꿨습니다(씬 79 KB, 메쉬는 참조 카운트로 공유·해제).

부서진 전함 잔해 799조각과 운석 526개가 떠 있는 무중력 아레나입니다.
배경은 `../skybox/`에서 시뮬레이션으로 구운 16K HDR 스카이박스이고, 잔해·운석 메쉬와 배치도 같은 시뮬레이션에서 나옵니다.

- **Unity 6000.3.28f1**, HDRP 17.3, Input System 1.20, D3D12 (6000.6.9f1은 Hub 설치가 "Validation Failed"로 실패해서 이 버전 사용)
- 씬: `Assets/OYM/Scenes/DebrisField.unity` (메인), `DebrisField_EnemyTest.unity` (HUSK 적 배치 테스트용 사본),
  `Showcase_DebrisField.unity` (데브리 필드 + 적 + 플레이 캐릭터 KESTREL / ORBIT FRAME),
  `DebrisField_PropTest.unity` (환경 프랍·픽업 44종 배치 테스트용 사본)
- 룩뎁 스크린샷: `Screenshots/01_spawn_wreck.png` ~ `07_flythrough.png`, 적 테스트: `Screenshots/enemies/`, 프랍 테스트: `Screenshots/props/`, 쇼케이스: `Screenshots/showcase/`

## 여는 법

1. Unity Hub → **Add** → `OnYourMarkUnity` 폴더 선택 → 에디터 6000.3.28f1로 열기
2. `Assets/OYM/Scenes/DebrisField.unity` 열고 Play
3. Main Camera의 `FreeFlyCamera`: WASD 이동, Space/Ctrl 상하, Q/E 롤, Shift 가속, 우클릭 드래그로 시점

메뉴 **OYM**에 빌드 도구가 있습니다(아래 표). 씬과 머티리얼, 볼륨 프로필은 스크립트가 만드니 **손으로 고치기보다 `OYMSetup.cs` 상수를 바꾸고 다시 빌드**하는 쪽이 안전합니다.

## 스크립트

| 파일 | 메뉴 / 배치 진입점 | 역할 |
|---|---|---|
| `Scripts/Editor/OYMSetup.cs` | OYM ▸ Rebuild Everything / `OYMSetup.BatchAll` | HDRP 에셋·프로젝트 설정(Linear, DX12, 무중력, 레이어), 머티리얼, 렌즈 플레어, 3D 안개 노이즈, 볼륨 프로필, 씬 전체(태양·행성광·카메라·잔해·운석 LOD·코어 라이트·국소 안개·행성 자리표시)를 다시 만듦 |
| `Scripts/Editor/OYMCapture.cs` | OYM ▸ 3. Capture Look-dev Shots / `OYMCapture.CaptureAll` | 고정 카메라 7곳에서 1920×1080 캡처 → `Screenshots/` (첫 샷 전 90프레임 워밍업) |
| `Scripts/Editor/OYMMeshImporter.cs` | (자동) | `.oymmesh`(skybox/export_assets.py 출력) 임포터. 서브메쉬 3개: 외부 선체 / 내부 / 고온 단면 |
| `Scripts/Editor/OYMDebug.cs` | OYM ▸ Debug ▸ Lighting Diagnostic / `OYMDebug.LightTest`, `FirstFrameNoSSGI`, `FirstFrameNoVolumetrics` | 조명·캡처 경로 진단(아래 "디버그") |
| `Scripts/Editor/OYMEnemies.cs` | OYM ▸ Enemies ▸ … / `OYMEnemies.BuildAndCapture` | 게임용 HUSK 적 6종(보스 포함) 임포트 → HDR 발광 HDRP 머티리얼·LODGroup 프리팹 → 테스트 씬 → 캡처 |
| `Scripts/Editor/OYMProps.cs` | OYM ▸ Props ▸ … / `OYMProps.BuildAndCapture` | 게임용 환경 프랍·픽업 44종 임포트 → HDRP 머티리얼·LODGroup·충돌체 프리팹(`Props/Prefabs/<분류>/`) → 테스트 씬(운석 위 엄폐물 레이캐스트 배치 + 분류별 라인업) → 캡처 |
| `Scripts/Editor/OYMShowcase.cs` | OYM ▸ Showcase ▸ … / `OYMShowcase.BuildAndCapture`, `RefreshAll`, `OpenAndPlay` | 적 테스트 씬 + 캐릭터 세션의 `CharacterPlayTest.unity`를 합친 쇼케이스 씬 빌드·스틸 캡처·Play 실행 |
| `Scripts/Editor/OYMMainTitle.cs` | OYM ▸ Title ▸ … / `OYMMainTitle.BuildAndCapture`, `OpenAndPlay` | 메인 타이틀 씬(`MainTitle.unity`) 빌드, 음악 임포트(스트리밍 Vorbis), BGM 라우트, 빌드 세팅 첫 씬 등록, 스틸 캡처(아래 "메인 타이틀") |
| `Scripts/Runtime/TitleDirector.cs`, `TitleCharacter.cs`, `TitleUI.cs`, `TitleIcons.cs`, `GameSettings.cs` | — | 타이틀 연출: 오프닝 → 선택 캐릭터 포커스·교대, 카메라 스프링·마우스 패럴랙스, 심도, UI Toolkit 오버레이(`Title/MainTitle.uxml/.uss`: 로고·메뉴·옵션·파일럿 카드·친구 코드·Discord), 설정 저장 |
| `Scripts/Runtime/BgmPlayer.cs`, `BgmRoutes.cs` | — | 씬 이름 → BGM(`Resources/OYM_BgmRoutes.asset`), 씬 전환 시 크로스페이드, 음소거 기억 |
| `Scripts/Runtime/GameSession.cs` | — | 타이틀에서 고른 캐릭터를 게임 씬의 PlayTestDirector 0번 슬롯으로 옮김 |
| `Scripts/Editor/OYMDepthFade.cs` | OYM ▸ Look ▸ … / `OYMDepthFade.BatchCompare` | 원거리 감쇠(`Runtime/DepthFocusFade.cs` 글로벌 커스텀 패스 + `Shaders/DepthFocusFade.shader`)를 볼륨 프로필에 넣음, 끔/켬 비교 캡처 |
| `Scripts/Runtime/FreeFlyCamera.cs` | — | 무중력 미리보기 카메라 |
| `Scripts/Runtime/Spin.cs` | — | 잔해·운석의 일정 각속도 회전 |
| `Tools/sync_assets.py` | — | `assets`: skybox 출력 → `Assets/OYM/Generated`, `scripts`: C# → `skybox/unity_src` 백업 |
| `Tools/prep_enemies.py` | — | `meshy/enemies/game` → `Assets/OYM/Enemies` (FBX 복사, 텍스처를 HDRP 마스크맵으로 변환, GLB에서 발광 세기 읽기) |
| `Tools/prep_props.py` | — | `meshy/props/game` → `Assets/OYM/Props` (FBX·분리 바위 조각 FBX 복사, 텍스처를 HDRP 마스크맵으로 변환, 발광 세기는 `game_props.py` 표에서) |
| `Tools/dbg_check.py` | — | `OYMDebug` 렌더를 숫자로 검사(초록 과잉, 밝은 면 방향, 언릿 패치 중립성) |

배치 실행 예(같은 프로젝트를 연 에디터가 없어야 함):

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.28f1/Editor/Unity.exe" -batchmode -projectPath "C:/Users/ROG STRIX/OnYourMark/OnYourMarkUnity" -executeMethod OYM.EditorTools.OYMSetup.BatchAll -quit -logFile "C:/Users/ROG STRIX/OnYourMark/OnYourMarkUnity/Logs/oym_setup.log"
```

`BatchAll` 약 30초, `CaptureAll` 약 5분, `OYMDebug.LightTest` 약 1분 걸립니다.

## 처음부터 다시 만드는 순서

```bash
cd skybox
python bake.py --eq 16384 --spp 4 --nosun --tag _nosun   # 스카이 (수십 분)
python export_assets.py                                  # 잔해·운석·텍스처·매니페스트
cd ../OnYourMarkUnity
python Tools/sync_assets.py assets                       # Generated/ 갱신 (에디터 닫고)
```

그다음 Unity에서 **OYM ▸ Rebuild Everything** → **OYM ▸ 3. Capture Look-dev Shots** (또는 위 배치 명령으로 `BatchAll`, `CaptureAll`).
스크립트를 고쳤으면 `python Tools/sync_assets.py scripts`로 `skybox/unity_src` 사본도 맞춰 두세요.

## 룩 조정 포인트 (`OYMSetup.cs` 상단 상수)

| 상수 | 값 | 의미 |
|---|---|---|
| `SunLux` | 30000 | 태양 조도. 색은 매니페스트 `sun_color`(스카이 베이크와 같은 값) |
| `CameraEV100` / `SkyEV` | 13.0 / 13.2 | 고정 노출 / HDRI 하늘 밝기 |
| `PlanetshineLux`, `PlanetshineColor` | 4000, 청회색 | 거대 행성 반사광(그림자 없는 보조광). 물리값은 약 500 lux지만 역광 시야에서 형태를 읽히게 올림 |
| `IndirectDiffuseBoost` | 5 | 하늘(청록 성운) 앰비언트 배수. SSGI를 껐기 때문에 피드백 없이 안전 |
| `HotMinTemp`, `HotNits` | 1150 K, 110000 | 이 온도 이상 조각만 찢긴 단면이 발광. 밝기 = `HotNits` × 흑체 상대휘도(매니페스트 `hot_palette.rel`) |

## 원거리 감쇠 (Depth Focus Fade)

멀리 있는 잔해가 화면을 어지럽히지 않고 앞의 적이 먼저 읽히도록, 카메라에서 멀수록 화면을 죽이는 깊이 기반 처리입니다.
`OYM_SpaceVolume` 프로필(DebrisField, 적·프랍 테스트, Showcase 씬)에 들어 있습니다.

- 파일: `Scripts/Runtime/DepthFocusFade.cs`(볼륨 컴포넌트 = 설정값, `DepthFocusFadePass` = 렌더링), `Shaders/DepthFocusFade.shader`, `Scripts/Editor/OYMDepthFade.cs`(프로필 적용·비교 캡처)
- 렌더링은 HDRP **글로벌 커스텀 패스**(`CustomPassVolume.RegisterUniqueGlobalCustomPass`, **Before Pre-Refraction**)입니다. 에디터 로드·Play·플레이어 시작 때 스스로 등록되므로 씬 오브젝트나 HDRP Global Settings 항목이 필요 없습니다. 값은 각 카메라의 볼륨 스택에서 읽으니 볼륨 블렌딩이 그대로 됩니다.
- 이 지점은 불투명·하늘·**볼류메트릭 안개 합성 뒤**, 투명 오브젝트 전입니다. 그래서 먼 코어 글로우(안개 산란)까지 같이 죽고, 피격 FX·빔·추진 화염 같은 투명 FX는 거리와 상관없이 그대로 보입니다. 블룸보다 앞이라 먼 발광 단면은 번지지도 않습니다.
  (처음 버전은 커스텀 포스트프로세스 After Opaque And Sky였는데, 안개가 그 뒤에 합성돼서 코어 글로우가 남았습니다.)
- 처리 순서(거리에 따른 양 `t`, `시작~끝` 사이 로그 램프): 하이라이트 눌러 담기 → 채도 빼기 → 어둡게 → 헤이즈 색으로 녹이기. 하늘(별·성운)은 `skyWeight`만큼만.
- 프로필 추가는 Library당 한 번 자동으로 하고(`Library/OYM_DepthFade_v1.applied`), `Rebuild Everything`은 `OYMSetup.CreateVolumeProfile`에서 다시 넣습니다.
- 다른 프로필(`BossIntro_Volume`, 캐릭터 테스트 씬들)에는 넣지 않았습니다. 원거리 사건을 보여 주는 시네마틱은 자기 볼륨에서 `intensity`를 0으로 덮거나 거리를 늘리면 됩니다.
- 메뉴: **OYM ▸ Look ▸ Apply / Reset Depth Focus Fade**, **Capture Depth Fade Compare**(`Screenshots/depthfade/<샷>_off|on|mask.png`, 배치 `OYMDepthFade.BatchCompare`)
- 튜닝은 Play 중 볼륨 인스펙터에서 바로 보면서 하면 됩니다. `debugView`를 켜면 감쇠량이 흑백(검정 = 그대로, 흰색 = 최대)으로 나옵니다.

| 파라미터 (`OYMDepthFade` 상수) | 기본값 | 의미 |
|---|---|---|
| `startDistance` / `endDistance` | 80 m / 1600 m | 이 안쪽은 그대로, 여기서 최대. 전투 거리는 수십 m, 난파 잔해는 0.9~2.6 km |
| `skyWeight` | 0.5 | 하늘 픽셀에 주는 감쇠 비율 |
| `desaturation` | 0.75 | 최대 거리에서 빼는 채도 |
| `darkenEV` | 1.5 | 최대 거리에서 빼는 노출(스톱) |
| `highlightLimit`, `highlightCompress` | 0.5, 1 | 먼 하이라이트를 이 밝기(프리익스포즈 기준, 1 ≈ 흰색)로 부드럽게 눌러 담음. 고온 단면(2~11)의 글로우를 죽이는 핵심 |
| `hazeColor`, `haze` | 청회색 (sRGB 0.19, 0.22, 0.26), 0.4 | 먼 지오메트리가 녹아드는 색과 비율. 검정을 살짝 들어서 실루엣을 배경에 묻음. 스와치는 머티리얼 색처럼 sRGB(내부에서 선형 변환) |

## 조명 디버깅에서 확인된 것 (2026-11-18)

1. **태양이 3.6%로 약해진 원인 = 높이 안개.** HDRP는 디렉셔널 라이트를 높이 안개 투과율로 감쇠합니다
   (`LightEvaluation.hlsl`의 `TransmittanceHeightFog`). 전역 Fog의 base height가 +60 km여서 y≈0의 아레나는
   두꺼운 안개층 바닥에 있었고, 고도 25°의 태양빛이 약 150 km를 통과했습니다. 지금은 안개층을 아레나 100 km 아래로
   내려 두었습니다(전역 Fog는 국소 볼류메트릭 안개를 켜기 위해서만 ON).
2. **초록색 = SSGI.** 새로 시작한 Unity 프로세스의 첫 ~40프레임 동안 SSGI가 초기화 안 된 히스토리를 읽어
   초록 캐스트를 더합니다(`OYMDebug.FirstFrameNoSSGI`로 분리 확인). 태양이 3.6%였을 때는 이게 조명보다 커서
   "초록 태양"으로 보였고, 이후 albedo가 1을 넘는 얼음 운석에서는 SSGI 피드백이 초록을 계속 붙잡아 두었습니다.
   그래서 SSGI는 끄고, 얼음 albedo는 1 이하로 묶었습니다.
3. **방향 불일치**는 2의 초록 잔상 때문이었습니다. 측면·위 조명 테스트에서 밝은 면 방향이 설정과 일치합니다.
4. **캡처 경로는 정상.** 언릿 회색 패치가 그레이딩 없이 (103,103,103) 중립으로 읽힙니다.

## 디버그 (`OYMDebug.cs`)

저장하지 않는 임시 씬에 구체·운석(프로젝트 머티리얼)과 HDRP/Unlit 기준 패치를 놓고 변형을 렌더해
`Screenshots/dbg/*.png` + `probes.json`을 씁니다. 볼륨 프로필은 메모리 사본만 바꿉니다.

```bash
python Tools/dbg_check.py        # 각 렌더의 초록 과잉, 밝은 면 중심, 언릿 패치 RGB 출력 + sheet.png
```

- `a0_*`: 고친 설정으로 프로세스 첫 프레임 / `b0_*`: 예전 안개(+60 km) / `b1`: 안개 OFF / `c0`: 그레이딩 OFF(캡처 경로 확인) / `c1`: 볼륨 없음
- `d0`, `d1`: 오른쪽·위에서 비추는 방향 확인 / `z_first_*`: 효과 하나씩 끈 상태의 프로세스 첫 프레임
- `dbg/old_session/`: 이전 세션의 초록 재현 이미지

## HUSK 적 테스트 (`OYMEnemies.cs`)

게임용 에셋은 meshy 검수 세션이 `meshy/enemies/game/<이름>/`에 만듭니다: LOD0~2, 미터 단위, 앞이 +Z.
`meshy/enemies/out*`의 원본(0.3M~3.3M 삼각형)은 쓰지 않습니다.

```bash
python Tools/prep_enemies.py      # 에디터 닫고 실행. meshy/enemies/game 은 읽기만 함
```

Unity에서 **OYM ▸ Enemies ▸ Build + Capture Enemy Test** (배치: `OYMEnemies.BuildAndCapture`).

| 모델 | 크기 | LOD0 삼각형 | 발광 |
|---|---|---|---|
| wick_body / wick_open | 1.1 m | 12k / 25k | ×4 |
| prism_body / prism_charge | 3.5 m | 25k / 25k | ×4 / ×8 |
| citadel_dart | 2.0 m | 6k | ×8 |
| citadel_boss | 123 × 91 × 301 m | 520k (부위 12 + 손상 런처 2, 부위마다 LODGroup) | 머리·집게·손상 런처 ×8 |

- **HDR 발광**: `_EmissiveColorMap` = `<set>_emission.png`, Use Emission Intensity ON, 단위 EV100.
  값은 카메라 EV(13) + log2(GLB의 `KHR_materials_emissive_strength`) + `EmissiveEvBias`(2)입니다. 그래서 ×4는 EV100 17, ×8은 18.
  bias를 2로 둔 이유: 저작값(+2/+3 스톱)만으로는 그림자 쪽에서 빨간 칠처럼 보였고, 블룸(볼륨의 Bloom: 고품질, 강도 0.25, scatter 0.72)을 넘지 못했습니다.
- **보스 파괴**: `citadel_launcher_L/R_damaged`는 프리팹에서 꺼 두었습니다. 파괴되면 같은 자리의 `citadel_launcher_L/R`을 끄고 손상판을 켜면 됩니다(캡처 `i_citadel_boss_launcherL_destroyed`).
- **캡처** `Screenshots/enemies/`:
  - `e0*`: 맥락 샷
  - `i_*`: 태양 쪽 3/4 뷰
  - `g_*`: 그림자 쪽. 발광만으로 읽히는지 확인용
  - 단독 샷은 대상만 별도 레이어(30)로 옮기고 카메라 culling mask로 렌더합니다. 렌더러 수천 개의 `enabled`를 매 샷 바꾸는 방식은 배치 프로세스가 19 GB까지 커져 스왑에 걸렸습니다.
- 테스트 씬은 `DebrisField.unity`를 열어 적을 배치한 뒤 **다른 이름으로** 저장하므로, 원본 씬은 바뀌지 않습니다.

## 쇼케이스 (`OYMShowcase.cs`)

데브리 필드 + HUSK 적 + 캐릭터 세션의 플레이 테스트(KESTREL / ORBIT FRAME, Tab으로 전환, 마우스로 팔로우 카메라 회전)를 한 씬에 띄웁니다.

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.28f1/Editor/Unity.exe" -projectPath "C:/Users/ROG STRIX/OnYourMark/OnYourMarkUnity" -executeMethod OYM.EditorTools.OYMShowcase.OpenAndPlay
```

- 빌드 방식: `OYMEnemies.BuildTestScene()`으로 적을 배치한 데브리 필드에 `Scenes/CharacterPlayTest.unity`(OYMPlayTest)를 병합합니다. 그 씬에서는 캐릭터·카메라·PlayTest 디렉터만 남기고, 어두운 테스트용 볼륨·조명·바닥·기둥은 버립니다.
- 캐릭터 코드에 컴파일 의존성이 없어서, 캐릭터 세션이 씬을 고치면 다시 빌드만 하면 반영됩니다.
- 바닥 판은 없습니다(사용자 피드백 11-18). 캐릭터는 무중력으로 떠서 **실제 잔해 조각** 위에 있고, 조각들이 징검다리처럼 난파선 쪽으로 이어집니다(`OYMDebrisPath.cs`).
  - 조각 고르기: 시뮬레이션 잔해(`Generated/Wreck/*.oymmesh`) 가운데 PCA로 가장 납작한 것을 골라, 가장 얇은 축이 위를 향하게 눕힙니다. 스폰 조각은 약 31 m, 징검다리 16개, 길이 약 400 m입니다.
  - 징검다리는 몇 도씩 기울어 있고 `OYM.Drift`로 천천히 오르내립니다. 텍스처는 가까이서 패턴 반복이 보이지 않도록 전용 머티리얼(`Path_Hull_*`)을 써서 타일을 8배 크게 했습니다(`PathTileScale`).
  - 프랍(`Assets/OYM/Props/Prefabs`, Meshy 프랍 세션)이 있으면 함께 씁니다. 트러스 빔이 조각 사이를 잇고, 엄폐물과 작은 잔해가 조각 위에 놓입니다. 프랍이 없으면 이 부분은 건너뜁니다.
- 운석: 스폰 근처의 작은·중간 운석과 프랍 운석(ast_ice/metal/husk 24 m), 작은 돌 64개가 `OYM.Drift`(느린 궤도 + 흔들림)와 `OYM.Spin`으로 길 주변을 돕니다. 움직임은 Play 모드에서만 보입니다.
- 적 눈 발광은 사용자 피드백으로 +1.5 EV 더 올렸습니다(`OYMEnemies.EyeExtraEv`). 열린 내부 전체가 발광하는 wick_open은 제외했습니다.
- 스틸 `Screenshots/showcase/s0*.png`는 에디트 모드에서 각 캐릭터를 자기 컨트롤러의 Idle 클립으로 포즈해서 찍습니다.

## 메인 타이틀 (`OYMMainTitle.cs`)

`DebrisField.unity`를 배경으로 KESTREL과 ORBIT FRAME이 등을 맞대고 떠 있는 컷으로 열리고, 0.9초 뒤 지난번에 고른 캐릭터(PlayerPrefs, 처음엔 ORBIT FRAME)가 부스터를 켜고 화면 오른쪽 앞으로 나옵니다. 다른 캐릭터는 왼쪽 뒤에서 심도로 흐려집니다. 그 주위에 사용자 스케치(11-18)에 그린 것만 들어옵니다. 작은 글씨(키 안내, 이름표, 스탯, 버전 표기)는 넣지 않습니다.

| 위치 | 내용 |
|---|---|
| 왼쪽 위 | 이미지젠 로고(`Title/Logo/oym_logo_b.png`, 스텐실 2단 + 해저드 스트라이프). 와이프 + 스캔 라인으로 등장 |
| 왼쪽 아래 | GAME START / OPTION / EXIT. 마우스 호버나 W·S로 커서 바가 스냅, 선택 항목이 오른쪽으로 밀려남 |
| 오른쪽 아래 | 파일럿 박스(이름만). 선택한 캐릭터가 위에 크게, 다른 캐릭터는 아래에 작게 오른쪽으로 붙음. 클릭하면 교대 |
| 오른쪽 | CO-OP 원형 버튼(F) → 친구 코드 패널: 내 코드(8자) + COPY, 친구 코드 입력 + JOIN |
| 오른쪽 위 | Discord 버튼(`OYMMainTitle.DiscordUrl`, 비어 있으면 "링크 미설정" 토스트) |
| 아래 가운데 | 가는 선 + 마크(장식) |

| 입력 | 동작 |
|---|---|
| A·D·←→ / Tab / 카드 클릭 / 뒤 캐릭터 클릭 | 캐릭터 교대(점화 플래시, 도착 시 스냅). UI 강조색이 KESTREL 시안 ↔ ORBIT FRAME 앰버로 바뀜 |
| W·S·↑↓ + Enter/Space | 메뉴. GAME START = 선택 캐릭터 출격 → `Showcase_DebrisField` 로드, 그 캐릭터로 시작 |
| OPTION | 마스터 볼륨, 음악 볼륨, 음악 켜기/끄기, 전체 화면, 마우스 감도(W·S 선택, A·D 조절, 드래그). `GameSettings`(PlayerPrefs)에 저장 |
| Esc | 열린 패널 닫기 / 메인에서는 종료 확인 창(기본 선택 CANCEL) |
| 마우스 이동 | 카메라가 살짝 따라 움직이는 패럴랙스 |

- **로고**: `design/prompts/title/`의 프롬프트로 Codex 이미지젠 4안(`design/title/logo_a~d.png`) → `pipeline/title/logo_alpha.py`로 검정 배경을 알파로 → `Title/Logo/`. 바꾸려면 `OYMMainTitle.LogoVariant`.
- **CO-OP**: 친구 코드는 설치마다 한 번 만들어 PlayerPrefs에 둡니다(0/O/1/I 제외 32자). 네트워킹 패키지가 없어서 JOIN은 형식만 검사하고 "CO-OP SERVICE IS NOT CONNECTED" 상태를 보여 줍니다.
- **연출** (`TitleCharacter` 이벤트 + `TitleFX`, 카메라 오프셋은 디렉터 뒤 LateUpdate에서 얹음):
  - 오프닝: 카메라가 3 m 뒤 우주에서 2.2초 동안 밀고 들어옴
  - 교대: 들어오는 캐릭터는 바깥쪽으로 휘는 궤적으로 부스트(점화 HDR 플래시), 카메라가 그쪽으로 휩(요·롤 펀치, FOV가 벌어졌다 스냅), 모션 블러·블룸·색수차·렌즈 왜곡 펄스. 나가는 캐릭터는 역추진 + 살짝 돌며 뒤로 밀림
  - 스트라이크("쪼"): KESTREL은 Slash_Left 0.3→0.75초를 휘두르고(슬래시 아크) 칼을 왼쪽 아래로 뻗은 자세로 고정(팔을 화면 평면에서 18° 더 내림, 얼굴을 가리지 않게), ORBIT FRAME은 RG-77 충전 후 한 발 발사. 그 순간 히트스톱(0.08초) + 임팩트 셰이크 + FOV 펀치 인 + 히어로 라이트 플래시 + 역추진 퍼프
  - 출격: 큰 FOV 워프·블러·블룸·왜곡
  - 포즈 후보는 `OYMMainTitle.PoseSheet` → `python -I ../pipeline/title/pose_sheet.py Screenshots/title/poses Screenshots/title/pose_sheet.png`로 실제 포커스 카메라에서 비교해 고릅니다. 배치 스틸은 `ForceSkin()`으로 렌더마다 스키닝을 다시 계산합니다(안 하면 몸은 첫 포즈에 멈추고 손에 붙은 칼만 움직여 "칼이 손에 없는" 것처럼 보임).
- **BGM**: `Resources/OYM_BgmRoutes.asset` — `MainTitle` → `bgm_main_theme_01`, `Showcase_DebrisField`·`DebrisField` → `bgm_battle_01`. 씬이 바뀌면 크로스페이드(실시간 기준이라 히트스톱 영향 없음), 라우트가 없는 씬은 지금 곡을 유지합니다. 음악 켜기/끄기와 볼륨은 OPTION에서 바꾸고 PlayerPrefs에 기억합니다.
- 음악 파일은 스트리밍 + Vorbis 70%로 임포트합니다(2~3분 WAV를 메모리에 풀지 않음).
- 빌드 세팅 0번 = `MainTitle`, 게임 씬이 없으면 추가합니다. `OYMSetup.BuildScene`은 빌드 세팅을 `DebrisField`만으로 덮어쓰니 그 뒤에는 타이틀을 다시 빌드하세요.
- UI는 버튼 컨트롤 없이 `pick` 클래스 요소 + 클릭 콜백으로 만듭니다(런타임 테마 스타일 간섭 방지). 아이콘(Discord, CO-OP)은 `TitleIcons.cs`가 Painter2D로 그립니다. UI 문구는 영어(폰트: OS의 Bahnschrift → Segoe UI → 내장 폰트 순).
- 배치 빌드: `bash ../pipeline/title/run_title_batch.sh` (락이 풀릴 때까지 기다렸다가 `BuildAndCapture` → `Screenshots/title/t*.png`, UI 없는 스틸)
- 플레이 확인: `bash ../pipeline/title/run_title_ui.sh demo` → 메뉴 이동·교대·친구 코드·옵션·종료 창·출격까지 녹화(`Screenshots/title/demo.gif`, 화면별 스틸 `Screenshots/title/ui_*.png`) 후 종료. 데모가 바꾼 설정은 끝에 되돌립니다.

## CITADEL 등장 시네마틱 (`OYMBossIntro.cs`)

10.5초, 30 fps, 1920×1080 프레임을 오프라인으로 렌더합니다. 샷 구성은 `meshy/enemies/cinematic/BOSS_INTRO_SHOTS.md`, 사운드는 6.8초에 히트가 오는 `citadel_intro_rumble.wav`입니다.

```bash
python Tools/prep_cinematic.py      # 런처 발사관·엔진 배기용 빨강 발광 마스크(시네마틱 전용)
"/c/Program Files/Unity/Hub/Editor/6000.3.28f1/Editor/Unity.exe" -batchmode -projectPath "C:/Users/ROG STRIX/OnYourMark/OnYourMarkUnity" -executeMethod OYM.EditorTools.OYMBossIntro.Preview -quit -logFile "C:/Users/ROG STRIX/OnYourMark/OnYourMarkUnity/Logs/oym_bossintro.log"
```

- `Preview`: 키 프레임 10장(960×540)을 `Screenshots/boss_intro_preview/`에 렌더합니다.
- `RenderChunk -oymFrom 0 -oymTo 105`: 본 렌더 프레임을 `Screenshots/boss_intro/frame_0000.png`부터 씁니다. 배치 모드 `Camera.Render()`는 프레임마다 메모리가 쌓이므로 3구간(0-105, 105-210, 210-315)으로 나눠 실행하세요.
- 모든 요소가 시간 t의 함수(`Evaluate(t)`)여서, 어느 프레임을 어느 프로세스에서 렌더해도 결과가 같습니다.
- 장면 구성:
  - 하늘은 아레나와 같은 HDRI 별하늘입니다.
  - 행성은 실제 지구 각도를 1/10 축척으로 만든 것으로, 반지름 637 km에 카메라 고도 40 km입니다. 보이는 부분만 촘촘한 캡 메쉬로 만들어 테두리가 매끄럽습니다.
  - 대기는 `OYM/Cine/AtmosphereRim` 셰이더입니다. 시선이 행성 중심에 가장 가깝게 지나가는 고도를 픽셀마다 계산해, 그 고도의 공기 밀도 exp(-고도/H)만큼 빛나게 합니다. 해 쪽은 주황으로 산란합니다.
  - 해는 테두리 바로 위에 있어 보스가 역광 실루엣이 되고, 약한 청색 보조광을 더했습니다.
- 연출:
  - 보스는 4.5 km에서 행성 테두리 선 아래에 있다가 빛나는 테두리를 가로질러 떠오르고, 6.8초에 약 1 km까지 다가온 뒤 낮은 카메라 위로 지나갑니다.
  - 집게가 펴지고 런처가 조준합니다. 센서 발광은 EV 14→16으로 오르고 히트 때 19까지 번쩍입니다. 발사관이 점화되고, 블룸은 0.3→0.6, 카메라가 흔들립니다. 부스터 불빛 점들이 크기를 보여 줍니다.
- **v2(사용자 검토 반영, 출력 `Screenshots/boss_intro_v2/`):**
  - 접근: 2.95 km에서 시작해 내내 빛나는 행성 테두리 위에 실루엣으로 서 있게 했습니다(3/4 측면, 진행 방향에서 35° 비껴 옴). 히트 때 약 350 m입니다.
  - 히트: 센서 EV100 20, 발사관 점화, 블룸 1.0, 노출 +1 EV 3프레임, 흔들림 약 2.2°.
  - 조명: 따뜻한 보조광 1만 lux와 차가운 보조광 3천 lux에, 오른쪽 뒤에서 들어오는 2.5만 lux 테두리광을 더했습니다.
  - 7.3–9.3 s 머리 위 통과(집게 → 배 쪽 빨간 선 → 엔진), 10.0–10.5 s 검게 페이드합니다.
- HDRP `PhysicallyBasedSky`는 배치 렌더에서 사전 계산된 빛이 0으로 남아(해발 0 m에 해를 높이 띄워도 땅과 대기가 검음) 쓰지 않습니다.

## 성능 — 45 fps 방어 (2026-11-19, 브랜치 `perf/fps45`)

목표: 메인 타이틀과 게임 중 45 fps 이상(RTX 4060 노트북, 2560×1600 기준). 씬·프리팹은 건드리지 않고 임포트 설정, 파이프라인 에셋, 런타임 컴포넌트로만 처리했습니다.

| 항목 | 내용 |
|---|---|
| 텍스처 | 무압축이던 130장(garam BaseColor/Mask/Emission, 히트 FX, 레티클, 팀 로고) → BC7/BC5. 4K 소품 6장 → 2K. 1인칭 무기·스카이 큐브맵은 유지. 4의 배수가 아닌 타이틀 로고·아이콘과 그라데이션 램프(`T_Ramp_*`)는 압축 안 함 |
| 동적 해상도 | HDRP 에셋 3종에서 켬(하드웨어 DRS 50–100 %). 업스케일러는 **DLSS(RTX) → STP** 순, 폴백 FSR1. `com.unity.modules.nvidia` 추가 |
| `FrameRateGuard` | 런타임에 모든 게임 카메라의 `allowDynamicResolution`을 켜고, GPU 시간으로 렌더 스케일을 조절(예산 18.9 ms, 60 Hz 수직동기 화면이면 그 주기). 스케일 50 %에서도 45 fps 아래면 볼륨으로 포그 품질 Low·접촉 그림자 끔(1단계), SSAO 끔(2단계), 여유가 생기면 되돌림. CPU 병목일 때는 해상도를 내리지 않음 |
| `DebrisMotion` | `Spin`·`Drift` 1,800여 개를 Transform 잡 하나로 움직임(개별 FixedUpdate·리지드바디 보간 제거). 카메라 150 m 밖 데브리는 카메라 모션벡터만 써서 오브젝트 모션벡터 패스에서 빠짐 |
| `ShadowThrottle` | 반경 60 m 이상 포인트/스팟 그림자(코어 라이트 900 m)를 HDRP 캐시 그림자로 바꾸고 큐브 6면 중 프레임당 2면씩 갱신. 요청하지 않은 면은 HDRP가 컬링도 건너뜀 |
| HDRP High Fidelity | GPU Resident Drawer 작은 메시 컬링 0.3 %(화면 높이 약 5 px 미만 메시 생략, 원거리 잔해), 데칼 지원 끔(프로젝트에 데칼 없음, 켜져 있으면 데칼을 받는 불투명 전부를 뎁스 프리패스로 한 번 더 그림), SSGI 지원 끔(모든 볼륨에서 꺼져 있음), 컬러 버퍼 R11G11B10, PCSS 그림자 필터 → Medium |
| 볼륨·설정 | `OYM_SpaceVolume` 태양 그림자 거리 2600 → 1200 m(`OYMSetup` 상수도 같이), `Maximum Allowed Timestep` 0.333 → 0.1, Frame Timing Stats 켬 |

명령줄: `-oymPerfHud`(fps·GPU·스케일 오버레이, **F8**로 토글), `-oymPerfLog <파일>`(초당 CSV), `-oymNoGuard`(가드 끔), `-oymNoShadowThrottle`.
데칼을 쓰게 되면 `HDRP High Fidelity`의 Decals를 다시 켜야 합니다(뎁스 프리패스 비용이 같이 돌아옵니다).
잔해 799조각(820만 삼각형)에는 여전히 LOD가 없습니다. Unity 6.3 `MeshLodUtility.GenerateMeshLods`로 임포터에서 자동 생성해 봤지만 `frag_0090`에서 에디터가 네이티브 크래시(`MeshLod::BuildRedirect`)해서 넣지 않았습니다. LOD가 필요하면 `skybox/export_assets.py` 쪽에서 단순화 메시를 같이 내보내는 게 안전합니다.

## 알려진 한계

- 스폰 시야(+Z)가 태양(앞 오른쪽)과 행성(앞 왼쪽)을 함께 마주 봐서 대부분 실루엣·림 라이트로 보입니다. 의도한 역광 구도지만 전투 가독성이 더 필요하면 스폰 방향이나 `PlanetshineLux`를 조정하세요.
- 찢긴 단면이 얇은 선체의 가는 띠라서 발광이 "윤곽선"처럼 보입니다. 단면 안쪽으로 식어 가는 그라데이션이 있으면 더 자연스럽습니다.
- 거대 행성은 아직 매끈한 청회색 구 자리표시입니다(Meshy 행성으로 교체 예정, `design/GIANT_PLANET_MESHY.md`).
