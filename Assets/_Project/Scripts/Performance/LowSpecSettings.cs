using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 저사양 모드 수치 (Resources/LowSpecSettings.asset). LowSpecMode가 게임 시작 때 읽어 모든 씬에 적용한다.
    /// </summary>
    [CreateAssetMenu(menuName = "On Your Mark/Low Spec Settings")]
    public class LowSpecSettings : ScriptableObject
    {
        public enum Mode { Auto, On, Off }

        [Tooltip("Auto: 메모리가 autoMemoryMB 이하인 기기에서만 켠다")]
        public Mode mode = Mode.Auto;
        public int autoMemoryMB = 12000;

        [Header("먼 배경")]
        [Tooltip("이 레이어의 오브젝트는 카메라에서 cullDistance보다 멀면 그리지 않는다 (잔해 · 운석)")]
        public LayerMask sceneryLayers;
        public float cullDistance = 700f;
        [Tooltip("LOD를 이만큼 일찍 낮춘다 (1 = 기본)")]
        public float lodBias = 0.5f;

        [Header("렌더 파이프라인")]
        [Tooltip("저사양일 때만 실행 중에 바꿔 끼우는 HDRP 에셋 (SSAO · 굴절 · 렌즈 플레어 끄기, 그림자 요청 32, 그림자 아틀라스 2048, 반사 캐시 1024). 프로젝트 품질 설정은 건드리지 않는다")]
        public UnityEngine.Rendering.RenderPipelineAsset pipeline;

        [Header("메모리")]
        [Tooltip("하늘 큐브맵 면 해상도 상한. 더 크면 실행 중에 작은 밉으로 복사해 바꿔 끼우고 원본은 내린다 (4096 → 2048: 약 256MB → 64MB). 0이면 끔")]
        public int skyMaxSize = 2048;
        [Tooltip("텍스처 해상도를 2^n분의 1로 (1 = 절반: 2048 → 1024). M1처럼 메모리를 GPU와 함께 쓰는 기기에서 효과가 크다")]
        [Range(0, 3)] public int textureMipLimit = 1;

        [Header("그림자 · 화면 효과")]
        [Tooltip("그림자를 그리는 최대 거리 (m)")]
        public float shadowDistance = 80f;
        [Tooltip("카메라에서 끄는 무거운 화면 효과: 반사 · SSGI · 볼류메트릭 · 접촉 그림자 · AO · 투과 · SSS · 모션 블러 · 피사계 심도")]
        public bool disableHeavyEffects = true;

        [Header("프레임")]
        [Tooltip("0이면 제한 없음")]
        public int targetFrameRate = 60;
    }
}
