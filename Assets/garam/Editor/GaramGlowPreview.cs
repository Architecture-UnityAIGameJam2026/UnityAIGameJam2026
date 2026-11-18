using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace Garam.Editor
{
    // Batch-only visual check. It never opens or saves a user's working scene.
    public static class GaramGlowPreview
    {
        public static void Render()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run this preview in a separate batch Editor.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("PreviewCamera").AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 1.5f;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 100;
            camera.allowHDR = true;
            camera.transform.position = new Vector3(3.2f, 2.5f, -4f);
            camera.transform.LookAt(new Vector3(0f, .65f, 0f));
            var hdCamera = camera.gameObject.AddComponent<HDAdditionalCameraData>();
            hdCamera.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            hdCamera.backgroundColorHDR = new Color(.025f, .03f, .04f);
            hdCamera.volumeLayerMask = -1;
            var volume = new GameObject("PreviewVolume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            var exposure = volume.sharedProfile.Add<Exposure>(true);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(8f);
            var bloom = volume.sharedProfile.Add<Bloom>(true);
            bloom.intensity.Override(.15f);
            bloom.threshold.Override(1f);
            var environment = volume.sharedProfile.Add<VisualEnvironment>(true);
            environment.skyType.Override(3);
            var sky = volume.sharedProfile.Add<GradientSky>(true);
            sky.top.Override(new Color(.3f, .35f, .4f));
            sky.middle.Override(new Color(.2f, .22f, .25f));
            sky.bottom.Override(new Color(.12f, .12f, .12f));
            MakeLight("Key", new Vector3(45f, -35f, 0f), 9000f);
            MakeLight("Fill", new Vector3(30f, 145f, 0f), 3500f);
            var pack = JsonUtility.FromJson<GaramAssetPackBuilder.Pack>(File.ReadAllText("Assets/garam/Editor/asset-pack.json"));
            string[] names = { "01_medkit_closed", "02_medkit_open", "03_stim_injector", "18_debris_utility_box",
                "24_debris_beacon_caged", "25_debris_beacon_pole", "05_proximity_mine", "08_husk_small_drone" };
            Directory.CreateDirectory("Logs/garam-preview");
            var target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGBHalf);
            target.Create();
            camera.targetTexture = target;
            var request = new RenderPipeline.StandardRequest { destination = target };
            foreach (var name in names)
            {
                var spec = pack.assets.Single(a => a.name == name);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(spec.prefab));
                try
                {
                    var renderers = model.GetComponentsInChildren<Renderer>();
                    var bounds = renderers[0].bounds;
                    foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                    float scale = 2f / Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                    model.transform.localScale = Vector3.one * scale;
                    model.transform.position = new Vector3(0, .7f, 0) - bounds.center * scale;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        Material[] temporary = null;
                        if (pass == 1)
                        {
                            temporary = renderers.SelectMany(r => r.sharedMaterials).Distinct().Select(m => new Material(m)).ToArray();
                            var originals = renderers.SelectMany(r => r.sharedMaterials).Distinct().ToArray();
                            foreach (var m in temporary)
                            {
                                m.SetColor("_BaseColor", Color.black);
                                m.SetTexture("_BaseColorMap", null);
                                m.SetTexture("_MaskMap", null);
                                m.SetFloat("_Metallic", 0f);
                                m.SetFloat("_Smoothness", 0f);
                                m.SetFloat("_SpecularHighlights", 0f);
                                HDMaterial.ValidateMaterial(m);
                            }
                            foreach (var r in renderers)
                                r.sharedMaterials = r.sharedMaterials.Select(m => temporary[Array.IndexOf(originals, m)]).ToArray();
                        }
                        for (int warmup = 0; warmup < 3; warmup++)
                            RenderPipeline.SubmitRenderRequest(camera, request);
                        var previous = RenderTexture.active;
                        RenderTexture.active = target;
                        var image = new Texture2D(512, 512, TextureFormat.RGB24, false, false);
                        image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                        image.Apply();
                        File.WriteAllBytes("Logs/garam-preview/" + name + (pass == 0 ? "_lit.png" : "_emission.png"), image.EncodeToPNG());
                        RenderTexture.active = previous;
                        Object.DestroyImmediate(image);
                        if (temporary != null) foreach (var m in temporary) Object.DestroyImmediate(m);
                    }
                    Debug.Log("GARAM_PREVIEW " + name);
                }
                finally { Object.DestroyImmediate(model); }
            }
            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
            Debug.Log("GARAM_PREVIEW_COMPLETE");
        }

        static void MakeLight(string name, Vector3 rotation, float lux)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(rotation);
            var additional = light.gameObject.AddComponent<HDAdditionalLightData>();
            additional.SetIntensity(lux, LightUnit.Lux);
        }
    }
}
