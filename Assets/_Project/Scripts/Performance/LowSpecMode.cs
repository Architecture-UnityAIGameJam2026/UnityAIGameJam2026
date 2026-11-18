using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OnYourMark
{
    /// <summary>
    /// 저사양 모드. 게임이 시작되면 스스로 생겨 모든 씬(타이틀 · Arena)에 적용된다. 씬 오브젝트를 고치지 않고 카메라 기준으로만 줄인다:
    /// - 먼 잔해 · 운석: 레이어별 컬링 거리, LOD 일찍 낮추기
    /// - 그림자: 최대 거리 축소 (전역 볼륨 덮어쓰기)
    /// - 카메라 화면 효과: 반사 · SSGI · 볼류메트릭 · 접촉 그림자 · AO · 투과 · SSS · 모션 블러 · 피사계 심도 끄기
    /// 켜고 끄기와 수치는 Resources/LowSpecSettings.asset.
    /// </summary>
    public class LowSpecMode : MonoBehaviour
    {
        static readonly FrameSettingsField[] HeavyFields =
        {
            FrameSettingsField.SSR, FrameSettingsField.SSGI, FrameSettingsField.Volumetrics, FrameSettingsField.ReprojectionForVolumetrics,
            FrameSettingsField.ContactShadows, FrameSettingsField.SSAO, FrameSettingsField.Transmission, FrameSettingsField.SubsurfaceScattering,
            FrameSettingsField.MotionBlur, FrameSettingsField.DepthOfField,
        };

        public static bool Active { get; private set; }

        LowSpecSettings settings;
        Camera lastCamera;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var s = Resources.Load<LowSpecSettings>("LowSpecSettings");
            Active = s && (s.mode == LowSpecSettings.Mode.On
                           || (s.mode == LowSpecSettings.Mode.Auto && SystemInfo.systemMemorySize <= s.autoMemoryMB));
            if (!Active) return;

            var go = new GameObject("LowSpecMode");
            DontDestroyOnLoad(go);
            var mode = go.AddComponent<LowSpecMode>();
            mode.settings = s;
            mode.ApplyGlobal(go);
            Debug.Log($"[LowSpec] on (memory {SystemInfo.systemMemorySize} MB): cull {s.cullDistance} m, shadows {s.shadowDistance} m, LOD bias {s.lodBias}");
        }

        void ApplyGlobal(GameObject host)
        {
            QualitySettings.lodBias = settings.lodBias;
            if (settings.targetFrameRate > 0) Application.targetFrameRate = settings.targetFrameRate;

            // 그림자 거리: 우선순위가 높은 전역 볼륨으로 모든 씬의 볼륨 위에 덮어쓴다
            var volume = host.AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 1000f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var shadows = profile.Add<HDShadowSettings>(true);
            shadows.maxShadowDistance.Override(settings.shadowDistance);
            volume.sharedProfile = profile;
        }

        // 씬이 바뀌거나 카메라가 새로 생기면 그 카메라에 다시 적용한다
        void Update()
        {
            var cam = Camera.main;
            if (!cam || cam == lastCamera) return;
            lastCamera = cam;
            ApplyCamera(cam);
        }

        void ApplyCamera(Camera cam)
        {
            var distances = cam.layerCullDistances;
            for (int i = 0; i < 32; i++)
                if ((settings.sceneryLayers.value & (1 << i)) != 0) distances[i] = settings.cullDistance;
            cam.layerCullDistances = distances;
            cam.layerCullSpherical = true;

            if (!settings.disableHeavyEffects) return;
            var hd = cam.GetComponent<HDAdditionalCameraData>() ?? cam.gameObject.AddComponent<HDAdditionalCameraData>();
            hd.customRenderingSettings = true;
            var mask = hd.renderingPathCustomFrameSettingsOverrideMask;
            var fs = hd.renderingPathCustomFrameSettings;
            foreach (var f in HeavyFields) { mask.mask[(uint)f] = true; fs.SetEnabled(f, false); }
            hd.renderingPathCustomFrameSettingsOverrideMask = mask;
            hd.renderingPathCustomFrameSettings = fs;
        }
    }
}
