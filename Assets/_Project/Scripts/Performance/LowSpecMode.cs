using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace OnYourMark
{
    /// <summary>
    /// 저사양 모드. 게임이 시작되면 스스로 생겨 모든 씬(타이틀 · Arena)에 적용된다. 씬 오브젝트를 고치지 않고 카메라 기준으로만 줄인다:
    /// - 먼 잔해 · 운석: 레이어별 컬링 거리, LOD 일찍 낮추기
    /// - 그림자: 최대 거리 축소 (전역 볼륨 덮어쓰기)
    /// - 카메라 화면 효과: 반사 · SSGI · 볼류메트릭 · 접촉 그림자 · AO · 투과 · SSS · 모션 블러 · 피사계 심도 끄기
    /// - 렌더 파이프라인: 저사양용 HDRP 에셋으로 바꿔 끼우기 (프로젝트 품질 설정은 다른 브랜치와 같게 둔다)
    /// - 하늘 큐브맵: 작은 밉으로 복사한 사본으로 바꾸고 원본을 메모리에서 내리기
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
        float savedLodBias; int savedMipLimit, savedFrameRate;
        RenderPipelineAsset savedPipeline; bool pipelineSwapped;
        readonly Dictionary<Cubemap, Cubemap> smallSkies = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            // HDRP only: the web build runs URP (it has its own budget in OYM.Web), and swapping in the HDRP LowSpec
            // asset there throws after the LOD / texture limits were already lowered
            if (!(GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset)) { Active = false; return; }
            var s = Resources.Load<LowSpecSettings>("LowSpecSettings");
            Active = s && (s.mode == LowSpecSettings.Mode.On
                           || (s.mode == LowSpecSettings.Mode.Auto && SystemInfo.systemMemorySize <= s.autoMemoryMB));
            if (!Active) return;

            var go = new GameObject("LowSpecMode");
            DontDestroyOnLoad(go);
            var mode = go.AddComponent<LowSpecMode>();
            mode.settings = s;
            mode.ApplyGlobal(go);
            Debug.Log($"[LowSpec] on (memory {SystemInfo.systemMemorySize} MB): cull {s.cullDistance} m, shadows {s.shadowDistance} m, LOD bias {s.lodBias}, texture 1/{1 << s.textureMipLimit}");
        }

        void ApplyGlobal(GameObject host)
        {
            savedLodBias = QualitySettings.lodBias; savedMipLimit = QualitySettings.globalTextureMipmapLimit; savedFrameRate = Application.targetFrameRate;
            QualitySettings.lodBias = settings.lodBias;
            QualitySettings.globalTextureMipmapLimit = settings.textureMipLimit;   // 텍스처 메모리 약 1/4
            if (settings.targetFrameRate > 0) Application.targetFrameRate = settings.targetFrameRate;

            // 품질 단계는 그대로 두고, 이 단계의 파이프라인 에셋만 실행 중에 바꾼다
            if (settings.pipeline)
            {
                savedPipeline = QualitySettings.renderPipeline;
                QualitySettings.renderPipeline = settings.pipeline;
                pipelineSwapped = true;
            }

            ShrinkSkies();
            SceneManager.sceneLoaded += OnSceneLoaded;

            // 그림자 거리: 우선순위가 높은 전역 볼륨으로 모든 씬의 볼륨 위에 덮어쓴다
            var volume = host.AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 1000f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var shadows = profile.Add<HDShadowSettings>(true);
            shadows.maxShadowDistance.Override(settings.shadowDistance);
            volume.sharedProfile = profile;
        }

        // 에디터 플레이가 끝나면 품질 설정을 되돌린다 (프로젝트 설정 파일에 저장되지 않게)
        void OnDestroy()
        {
            if (!settings) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (pipelineSwapped) QualitySettings.renderPipeline = savedPipeline;
            foreach (var small in smallSkies.Values) if (small) Destroy(small);
            smallSkies.Clear();
            QualitySettings.lodBias = savedLodBias;
            QualitySettings.globalTextureMipmapLimit = savedMipLimit;
            Application.targetFrameRate = savedFrameRate;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ShrinkSkies();

        // 하늘 큐브맵이 skyMaxSize보다 크면 그 볼륨의 프로필 사본(volume.profile)에 작은 사본을 넣는다. 에셋 파일은 건드리지 않는다
        void ShrinkSkies()
        {
            if (settings.skyMaxSize <= 0 || (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0) return;
            var replaced = new HashSet<Cubemap>();
            foreach (var volume in FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var profile = volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile;
                if (!profile || !profile.TryGet<HDRISky>(out var sky) || sky.hdriSky.value is not Cubemap big || big.width <= settings.skyMaxSize) continue;
                var small = Shrink(big);
                if (!small) continue;
                volume.profile.TryGet<HDRISky>(out var own);   // 처음 읽을 때 볼륨 전용 사본이 만들어진다
                own.hdriSky.value = small;
                replaced.Add(big);
            }
            // 이제 아무 볼륨도 원본을 쓰지 않는다. 다른 곳에서 다시 쓰면 자동으로 다시 올라온다
            foreach (var big in replaced) Resources.UnloadAsset(big);
        }

        Cubemap Shrink(Cubemap big)
        {
            if (smallSkies.TryGetValue(big, out var cached) && cached) return cached;
            int skip = 0, size = big.width;
            while (size > settings.skyMaxSize) { size >>= 1; skip++; }
            int mips = big.mipmapCount - skip;
            if (mips < 1) return null;
            var small = new Cubemap(size, big.graphicsFormat, mips > 1 ? TextureCreationFlags.MipChain : TextureCreationFlags.None, mips)
            {
                name = big.name + " (low spec)", wrapMode = big.wrapMode, filterMode = big.filterMode, anisoLevel = big.anisoLevel,
            };
            small.Apply(false, true);   // CPU 사본을 버리고 GPU에만 둔다. 내용은 아래에서 GPU끼리 복사
            for (int face = 0; face < 6; face++)
                for (int m = 0; m < mips; m++)
                    Graphics.CopyTexture(big, face, m + skip, small, face, m);
            smallSkies[big] = small;
            Debug.Log($"[LowSpec] sky {big.name}: {big.width} → {size}");
            return small;
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
