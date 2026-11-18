# OnYourMark

Unity **6000.3.28f1** / High Definition Render Pipeline (**HDRP 17.3.0**).

## 실행

1. Unity Hub에서 이 저장소 폴더를 프로젝트로 추가합니다.
2. Unity 6000.3.28f1로 열고 패키지 설치와 셰이더 임포트가 끝날 때까지 기다립니다.
3. `Assets/Scenes/SampleScene.unity`를 열고 Play를 누릅니다. HDRP 카메라, 태양, 하늘·안개 볼륨이 있는 빈 시작 씬입니다.

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
- 빌드 씬: `Arena`(0번, 그대로) 뒤에 `MainTitle`, `DebrisField`, `Showcase_DebrisField`가 붙어 있습니다. 데모의 시작 씬을 바꾸려면 Build Profiles에서 순서만 바꾸면 됩니다.
- ProjectSettings(중력, 레이어, 품질, HDRP High Fidelity, 입력)는 팀 설정을 그대로 씁니다. OYM 장애물은 `Environment`(10) 레이어입니다.
- 백업: OYM 병합 직전 `main`은 `backup/main-20261118-pre-oym`, `feature/prototype`(P7~P21) 병합 직전 `main`은 `backup/main-20261119-pre-prototype` 브랜치에 있습니다.
- `feature/prototype`(URP)은 HDRP로 맞춰 합쳤습니다: 새 FX 머티리얼 8종은 HDRP 투명 Lit, 다운 비네트 볼륨은 HDRP Vignette/ColorAdjustments, URP 카메라·라이트 데이터는 제거. `Arena`의 SenseLink HUD는 네트워크 교환에 연결된 prototype 쪽 하나만 남겼습니다.

### HUD 스왑 글리치 (`Assets/_Project/Scripts/UI/SwapGlitch/`)

교차 HUD 채널(0 생체 `Vitals` / 1 지도 `Spatial` / 2 소모품 `Reserves`)의 주인이 바뀌는 순간을 보여 주는 연출입니다.
화면에 있던 데이터(고스트)는 찢어지며 바깥쪽으로 빠져나가고, 새 데이터는 안쪽에서 띠별로 엇갈려 들어옵니다. 들어온 데이터는 '쪼'(살짝 넘어갔다가 딱 멈춤)로 고정된 뒤 새 소유자 색(내 것 라임 / 파트너 것 라벤더)으로 한 번 번쩍입니다. 전체 길이는 약 0.56초입니다.
파트너가 처음 접속하면 세 채널이 0.08초 간격으로 차례로 뒤바뀝니다(크로스 링크: 서로의 HUD를 들고 시작하는 순간).

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
