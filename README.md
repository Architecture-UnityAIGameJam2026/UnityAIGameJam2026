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
