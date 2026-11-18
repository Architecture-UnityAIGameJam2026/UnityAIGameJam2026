# OnYourMark

Unity **6000.3.28f1** / High Definition Render Pipeline (**HDRP 17.3.0**).

## 실행

1. Unity Hub에서 이 저장소 폴더를 프로젝트로 추가합니다.
2. Unity 6000.3.28f1로 열고 패키지 설치와 셰이더 임포트가 끝날 때까지 기다립니다.
3. `Assets/OYM/Scenes/MainTitle.unity`를 열고 Play를 누릅니다. 빌드도 이 타이틀에서 시작합니다.
4. 파일럿을 고른 뒤 **GAME START**를 누릅니다. 기존 타이틀 메뉴가 왼쪽으로 밀려나고 같은 자리에 **SINGLE PLAYER / MULTIPLAYER / BACK**이 나타납니다.
5. **SINGLE PLAYER**는 AI 동료와 바로 출격합니다. **MULTIPLAYER**는 타이틀에서 LAN 방을 엽니다. 방장은 위쪽 **COPY**를 눌러 내 6글자 코드를 공유합니다. 참가자는 같은 Wi-Fi 또는 LAN에서 아래쪽에 상대 코드를 입력하고 **JOIN**이나 Enter를 누릅니다. 대소문자를 구분합니다.
6. 두 사람이 연결되면 같은 아레나로 함께 이동합니다. 같은 파일럿을 골랐다면 방장이 선택한 역할을 맡고 참가자는 다른 역할로 출격합니다. **BACK**은 이전 선택으로 돌아갑니다.

## 협동 미션

플레이 씬은 `Assets/_Project/Scenes/Arena.unity`입니다. 화면 위에 각 파일럿의 목표와 방향·거리가 표시됩니다.

| 구간 | 두 파일럿이 할 일 |
|---|---|
| 1 · 첫 도약 | P2가 엄호하는 동안 P1이 첫 잔해로 건넙니다. 뒤따르는 P2의 점프에 맞춰 각자 **좌클릭**으로 동료에게 향하는 파편을 막습니다. |
| 2 · 슈트 고장 | 레이저를 지나면 HUD와 궁극기 권한이 교차합니다. P1은 위쪽 선체, P2는 아래쪽 트러스의 집결점을 차례로 지납니다. |
| 3 · 재회 | 링이 겹치는 순간 **Space**를 누릅니다. P1이 먼저, P2가 한 박자 뒤입니다. 같은 갑판에 착지한 뒤 적을 정리하고 함께 전진합니다. |
| 4 · 상갑판 | P1이 검으로 사선을 열고 P2가 레일건으로 끝냅니다. 한 명이 그로기라면 그 파일럿이 **Q**로 동료의 스킬을 열고, 서 있는 파일럿이 마무리합니다. 결과 화면에서 호스트가 타이틀로 돌아갑니다. |

피격 시 자기 화면 가장자리가 붉게 반응합니다. 그로기 중 **C**로 동료의 1인칭을 보거나 자기 시점으로 돌아옵니다. **F**를 길게 누르면 가까운 동료를 소생시킵니다. 한 명이 쓰러진 상태로 다음 구간에 진입하면 HP 50%로 복귀합니다. 둘 다 쓰러지면 마지막 구간 체크포인트에서 다시 시작합니다.

전투 중 **1 / 2 / 3**으로 파츠 교환을 제안·수락하고, **R**로 현재 권한에 맞는 자원을 채웁니다. **V**를 누른 채 말하고 놓으면 팀 무전을 전송합니다. 미션 안내와 상태 콜아웃은 녹음 음성과 한영 자막을 사용합니다. Jev의 상황 선택에는 호스트의 OpenRouter 설정이, 마이크 음성 인식에는 ElevenLabs 설정이 필요합니다. 마지막 일격에서는 음악과 중재자 무전이 멎고 슈트 호흡과 열린 무전음이 남습니다.

## 렌더링 설정

- Graphics의 기본 파이프라인과 Quality의 High Fidelity / Balanced / Performant 설정 모두 HDRP를 사용합니다.
- 파이프라인과 볼륨 에셋은 `Assets/Settings/`에 있습니다. 기본 품질은 High Fidelity입니다.
- Linear 색 공간을 사용하며, 데스크톱을 대상으로 합니다. HDRP는 Android, iOS, WebGL을 지원하지 않습니다.
- 새 머티리얼은 `HDRP/Lit` 등 HDRP 셰이더로 만듭니다. URP용 셰이더와 머티리얼은 그대로 사용할 수 없습니다.
- 기존 Input Actions와 샘플 씬 경로/GUID를 유지했습니다.

HDRP 설정과 시작 씬은 Unity 공식 `com.unity.template.hdrp-blank` 17.1.1에서 가져왔습니다. 해당 에셋의 라이선스는 [Unity HDRP Template License](Documentation/UnityHDRPTemplateLicense.md)를 참고하세요.

## 데모 v0.1 — OYM 아트 통합 · HUD 스왑 글리치 · 풀링

### 구성

- `Assets/OYM/`: OnYourMark 아트·연출(데브리 필드 아레나, KESTREL / ORBIT FRAME, HUSK 적 6종 + CITADEL, 프랍 44종, 타격 FX, 메인 타이틀, CITADEL 시네마틱, BGM). 자세한 내용과 병합 때 바뀐 점은 [Documentation/OYM/README.md](Documentation/OYM/README.md)에 있습니다.
- 협동 맵은 HDRP `Showcase_DebrisField`의 시작 선체·징검다리·부유 잔해를 사용하며 높은 선체 길과 낮은 트러스 길이 합류합니다. 에디터에서 `Arena`를 직접 재생해도 타이틀의 모드 선택을 거칩니다. 원본 아트 씬도 개별적으로 열 수 있습니다.
- ProjectSettings(중력, 레이어, 품질, HDRP High Fidelity, 입력)는 팀 설정을 그대로 씁니다. OYM 장애물은 `Environment`(10) 레이어입니다.
- 백업: OYM 병합 직전 `main`은 `backup/main-20261118-pre-oym`, `feature/prototype`(P7~P21) 병합 직전 `main`은 `backup/main-20261119-pre-prototype` 브랜치에 있습니다.
- `feature/prototype`(URP)은 HDRP로 맞춰 합쳤습니다: 새 FX 머티리얼 8종은 HDRP 투명 Lit, 다운 비네트 볼륨은 HDRP Vignette/ColorAdjustments, URP 카메라·라이트 데이터는 제거. `Arena`의 SenseLink HUD는 네트워크 교환에 연결된 prototype 쪽 하나만 남겼습니다.

### HUD 스왑 글리치 (`Assets/_Project/Scripts/UI/SwapGlitch/`)

교차 HUD 채널(0 생체 `Vitals` / 1 지도 `Spatial` / 2 소모품 `Reserves`)의 주인이 바뀌는 순간을 보여 주는 연출입니다.
화면에 있던 데이터(고스트)는 찢어지며 바깥쪽으로 빠져나가고, 새 데이터는 안쪽에서 띠별로 엇갈려 들어옵니다. 들어온 데이터는 '쪼'(살짝 넘어갔다가 딱 멈춤)로 고정된 뒤 새 소유자 색(내 것 라임 / 파트너 것 라벤더)으로 한 번 번쩍입니다. 전체 길이는 약 0.56초입니다.
협동 미션은 자기 HUD로 시작합니다. 페이즈 2에서 레이저가 슈트를 손상시키면 HUD 소유권이 바뀌고 교차 연출이 재생됩니다.

- `SenseLinkHUD` 프리팹 루트에 `HudSwapGlitch`가 붙어 있어 `Arena`·`HudShowcase`에 자동으로 들어갑니다.
- 자동 연결: `OnYourMarkHud.ChannelSwapping(channel, toSelf)`. 네트워크 게임에서는 `HudPlayerBridge`가 매 프레임 `SetGameState`로 서버 소유권(`CrossHudState`)을 넣는데, 값이 바뀌는 그 프레임에 **바뀌기 직전** 이벤트가 나가서 5초 합의 교환·단말기 반환·라운드 리셋 모두 글리치가 재생됩니다(1번 파츠는 생체·지도 두 모듈이 함께). 시연 모드의 `ConfirmExchange` / `ReturnHud`도 같은 이벤트를 씁니다.
- 직접 호출: `glitch.Play(channel, toSelf)`, `glitch.PlayCrossLink()`. 데이터가 바뀌기 **직전에** 불러야 고스트에 이전 값이 남습니다.
- 시연(`demoInput`): `1`/`2`/`3`으로 교환 요청 → `Enter`(파트너 동의) → 해당 채널 스왑, `F4` 반환, `F7` 크로스 링크 다시 보기. `reducedMotion`(R)이면 찢김·이동 없이 페이드와 플래시만 남습니다.
- 조정값(Inspector): `duration`, `exitTime`, `lockTime`, `travel`, `tear`, `split`, `overshoot`, `bandHeight`, `noiseBlocks`. `swapClip`을 넣으면 스왑마다 한 번 재생합니다.
- 성능: 대기 중 비용 0(메쉬 이펙트는 재생 중에만 동작), 고스트와 노이즈 블록은 처음 한 번 만든 뒤 재사용, 연출 레이어는 중첩 Canvas라 메인 HUD 배칭을 다시 하지 않습니다.

### 풀링과 최적화

- `NetworkObjectPool`(`Assets/_Project/Scripts/Pooling/`): NGO 프리팹 핸들러로 투사체·적을 서버와 클라이언트 모두에서 재사용합니다. 서버는 `NetworkObjectPool.Get(prefab, pos, rot)` 다음에 `Spawn()`을 부르고, 반환은 평소처럼 `Despawn()`으로 합니다.
  - 새 네트워크 프리팹을 풀에 넣을 때는 **모든 피어에서** 첫 스폰 전에 `Register(prefab)`를 부르세요(`Start`/`OnNetworkSpawn`).
  - 재사용된 인스턴스는 필드 값을 그대로 가지고 옵니다. 상태는 `OnNetworkSpawn`에서 초기화하고, 이벤트 구독은 `OnNetworkDespawn`에서 해제하세요(`EnemyBase`, `Rusher`, `HoverLaser`, `Projectile`, `WaveManager` 참고).
- `FxPool.Play(template, pos, rot, scale, lifetime)`: 잠깐 켜는 로컬 연출용입니다. `Instantiate` + `Destroy(fx, t)` 대신 씁니다(Rusher 폭발, 각성 이펙트, 월드 핑, 궁극기 폭발·탄도선).
- 매 프레임 생기던 할당 제거: HUD 숫자 문자열 캐시, 숫자키 배열, `HudPlayerBridge` 컴포넌트 캐시, `ReticleAutoLock`의 0.1초마다 `FindObjectsByType` → `ReticleTarget.Active` 등록부.
