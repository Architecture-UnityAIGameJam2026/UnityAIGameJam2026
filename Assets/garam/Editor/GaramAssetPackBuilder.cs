using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace Garam.Editor
{
    /// <summary>Rebuilds the delivered prefabs using their committed FBX and texture inputs.</summary>
    public static class GaramAssetPackBuilder
    {
        const string Root = "Assets/garam";

        [Serializable] public class Pack { public string style; public AssetSpec[] assets; public MaterialSpec[] materials; }
        [Serializable] public class AssetSpec
        {
            public string id, category, name, model, prefab, method, sourceFbxSha256;
            public int triangles;
            public float[] dimensions;
            public bool greenGlow;
            public MaterialSlot[] materials;
        }
        [Serializable] public class MaterialSlot { public string sourceName, path; }
        [Serializable] public class MaterialSpec
        {
            public string sourceName, path, baseMap, maskMap, emissionMap;
            public float[] baseColor, emissionColor;
            public float metallic, roughness, emissionStrength, emissionPixelFraction;
            public bool doubleSided;
        }
        [Serializable] public class AssetResult
        {
            public string prefab;
            public int triangles, renderers, unmaskedEmissiveTriangles, maskedEmissiveMaterials;
            public Vector3 dimensions;
            public bool greenGlow;
        }
        [Serializable] public class Report
        {
            public bool passed;
            public string unityVersion, shader, style;
            public int prefabCount, triangles, greenGlowPrefabs, partialEmissionPrefabs;
            public List<AssetResult> assets = new List<AssetResult>();
        }

        static Pack ReadPack() => JsonUtility.FromJson<Pack>(File.ReadAllText(Root + "/Editor/asset-pack.json"));

        public static void BuildAndPreview()
        {
            Build();
            GaramGlowPreview.Render();
        }

        [MenuItem("Tools/Garam/Rebuild 64 Asset Prefabs")]
        public static void Build()
        {
            var pack = ReadPack();
            Require(pack.assets.Length == 64, "Expected 64 asset definitions.");
            var shader = Shader.Find("HDRP/Lit");
            Require(shader != null, "HDRP/Lit must be available.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureTextures(pack);
            foreach (var definition in pack.materials)
                CreateMaterial(definition, shader);
            AssetDatabase.SaveAssets();

            foreach (var asset in pack.assets)
            {
                var importer = AssetImporter.GetAtPath(asset.model) as ModelImporter;
                Require(importer != null, "Missing FBX importer: " + asset.model);
                importer.globalScale = 1f;
                importer.useFileScale = true;
                importer.bakeAxisConversion = true;
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.importCameras = false;
                importer.importLights = false;
                importer.addCollider = false;
                importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
                foreach (var slot in asset.materials)
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(slot.path);
                    Require(material != null, "Missing material: " + slot.path);
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slot.sourceName), material);
                }
                importer.SaveAndReimport();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(asset.model);
                Require(model != null, "Missing imported model: " + asset.model);
                var wrapper = new GameObject(asset.name);
                try
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    instance.transform.SetParent(wrapper.transform, false);
                    instance.name = "Model";
                    // No material guesses: every imported slot must resolve to the explicit remap.
                    var expected = new HashSet<string>(asset.materials.Select(m => m.path));
                    foreach (var renderer in wrapper.GetComponentsInChildren<Renderer>(true))
                        foreach (var material in renderer.sharedMaterials)
                            Require(material != null && expected.Contains(AssetDatabase.GetAssetPath(material)),
                                "Unmapped material on " + asset.id + "/" + renderer.name + ": " + (material ? material.name : "null"));
                    var renderers = wrapper.GetComponentsInChildren<Renderer>(true);
                    Require(renderers.Length > 0, "No renderers: " + asset.id);
                    var bounds = renderers[0].bounds;
                    foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    instance.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                    Directory.CreateDirectory(Path.GetDirectoryName(asset.prefab));
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    PrefabUtility.SaveAsPrefabAsset(wrapper, asset.prefab, out bool saved);
                    Require(saved, "Could not save prefab: " + asset.prefab);
                }
                finally { Object.DestroyImmediate(wrapper); }
                Debug.Log("GARAM_BUILT " + asset.id);
            }
            AssetDatabase.SaveAssets();
            Validate();
        }

        static void ConfigureTextures(Pack pack)
        {
            var linearMaps = new HashSet<string>(pack.materials.Select(m => m.maskMap).Where(p => !string.IsNullOrEmpty(p)));
            var paths = pack.materials.SelectMany(m => new[] { m.baseMap, m.maskMap, m.emissionMap })
                .Where(p => !string.IsNullOrEmpty(p)).Distinct();
            foreach (var path in paths)
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Require(importer != null, "Missing texture: " + path);
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = !linearMaps.Contains(path);
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        static Color ColorOf(float[] value, float alpha = 1f) => new Color(value[0], value[1], value[2], value.Length > 3 ? value[3] : alpha);

        static void CreateMaterial(MaterialSpec spec, Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(spec.path);
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(spec.path) };
                AssetDatabase.CreateAsset(material, spec.path);
            }
            material.shader = shader;
            // glTF factors are linear; Unity's non-HDR color property uses an sRGB UI value.
            material.SetColor("_BaseColor", ColorOf(spec.baseColor).gamma);
            material.SetFloat("_Metallic", spec.metallic);
            material.SetFloat("_Smoothness", 1f - spec.roughness);
            material.SetFloat("_MetallicRemapMin", 0f);
            material.SetFloat("_MetallicRemapMax", 1f);
            material.SetFloat("_SmoothnessRemapMin", 0f);
            material.SetFloat("_SmoothnessRemapMax", 1f);
            material.SetFloat("_AORemapMin", 0f);
            material.SetFloat("_AORemapMax", 1f);
            material.SetFloat("_SurfaceType", 0f);
            material.SetFloat("_DoubleSidedEnable", spec.doubleSided ? 1f : 0f);
            material.SetFloat("_DoubleSidedNormalMode", 1f);
            SetTexture(material, "_BaseColorMap", spec.baseMap);
            SetTexture(material, "_MaskMap", spec.maskMap);
            SetTexture(material, "_EmissiveColorMap", spec.emissionMap);
            var emission = ColorOf(spec.emissionColor);
            material.SetFloat("_UseEmissiveIntensity", 1f);
            material.SetFloat("_EmissiveIntensity", spec.emissionStrength);
            material.SetFloat("_EmissiveIntensityUnit", 0f); // Nits in the HDRP inspector.
            material.SetFloat("_EmissiveExposureWeight", 0f);
            material.SetColor("_EmissiveColorLDR", emission.gamma);
            material.SetColor("_EmissiveColor", emission * spec.emissionStrength);
            material.enableInstancing = true;
            material.globalIlluminationFlags = emission.maxColorComponent > 0f
                ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
        }

        static void SetTexture(Material material, string property, string path)
        {
            var texture = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Require(string.IsNullOrEmpty(path) || texture != null, "Missing texture: " + path);
            material.SetTexture(property, texture);
        }

        [MenuItem("Tools/Garam/Validate 64 Asset Prefabs")]
        public static void Validate()
        {
            var pack = ReadPack();
            var report = new Report { unityVersion = Application.unityVersion, shader = "HDRP/Lit", style = pack.style };
            foreach (var asset in pack.assets)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset.prefab);
                Require(prefab != null, "Missing prefab: " + asset.prefab);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    Require(instance.transform.localPosition == Vector3.zero && instance.transform.localScale == Vector3.one,
                        "Prefab root transform differs from meter scale: " + asset.id);
                    foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                        Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0, "Missing script: " + asset.id);
                    var renderers = instance.GetComponentsInChildren<Renderer>(true);
                    Require(renderers.Length > 0, "No renderers: " + asset.id);
                    var bounds = renderers[0].bounds;
                    foreach (var renderer in renderers)
                    {
                        bounds.Encapsulate(renderer.bounds);
                        foreach (var material in renderer.sharedMaterials)
                        {
                            Require(material != null && material.shader != null && material.shader.name == "HDRP/Lit", "Invalid shader: " + asset.id);
                            Require(AssetDatabase.GetAssetPath(material).StartsWith(Root + "/Materials/", StringComparison.Ordinal), "External material: " + asset.id);
                        }
                    }
                    var meshes = instance.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh)
                        .Concat(instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.sharedMesh)).ToArray();
                    Require(meshes.Length > 0 && meshes.All(m => m != null), "Missing mesh: " + asset.id);
                    int triangles = meshes.Sum(m => Enumerable.Range(0, m.subMeshCount).Sum(i => (int)m.GetIndexCount(i) / 3));
                    Require(triangles == asset.triangles, "Triangle mismatch: " + asset.id + " got " + triangles + " expected " + asset.triangles);
                    var expectedSize = new Vector3(asset.dimensions[0], asset.dimensions[1], asset.dimensions[2]);
                    float tolerance = Mathf.Max(0.001f, expectedSize.magnitude * 0.002f);
                    Require((bounds.size - expectedSize).magnitude < tolerance,
                        "Dimension mismatch: " + asset.id + " got " + bounds.size.ToString("F4") + " expected " + expectedSize.ToString("F4"));
                    Require(Mathf.Abs(bounds.min.y) < tolerance && new Vector2(bounds.center.x, bounds.center.z).magnitude < tolerance,
                        "Origin is not bottom-center: " + asset.id);
                    var mats = renderers.SelectMany(r => r.sharedMaterials).Distinct().ToArray();
                    int unmaskedEmission = 0;
                    var maskedEmission = new HashSet<Material>();
                    foreach (var renderer in renderers)
                    {
                        var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>().sharedMesh;
                        Require(mesh.subMeshCount == renderer.sharedMaterials.Length, "Submesh/material slot mismatch: " + asset.id);
                        for (int i = 0; i < mesh.subMeshCount; i++)
                        {
                            var material = renderer.sharedMaterials[i];
                            if (material.GetColor("_EmissiveColor").maxColorComponent <= 0.0001f) continue;
                            if (material.GetTexture("_EmissiveColorMap") != null)
                            {
                                var definition = pack.materials.Single(m => m.path == AssetDatabase.GetAssetPath(material));
                                Require(definition.emissionPixelFraction > 0f && definition.emissionPixelFraction < .25f,
                                    "Emission mask must isolate a small region: " + asset.id);
                                Require(material.IsKeywordEnabled("_EMISSIVE_COLOR_MAP"), "Emission mask shader keyword missing: " + asset.id);
                                maskedEmission.Add(material);
                            }
                            else unmaskedEmission += (int)mesh.GetIndexCount(i) / 3;
                        }
                    }
                    Require(unmaskedEmission < triangles * .25f, "Emission covers too much unmasked geometry: " + asset.id);
                    if (unmaskedEmission > 0 || maskedEmission.Count > 0) report.partialEmissionPrefabs++;
                    if (asset.greenGlow)
                    {
                        var green = mats.Single(m => m.name.StartsWith("OYM_HealthGreen_Emissive", StringComparison.Ordinal));
                        Require(Mathf.Abs(green.GetFloat("_EmissiveIntensity") - 2.5f) < 0.001f, "Green intensity mismatch: " + asset.id);
                        Require(green.GetColor("_EmissiveColor").g > 2.4f, "Green emission lost: " + asset.id);
                        report.greenGlowPrefabs++;
                    }
                    foreach (var path in AssetDatabase.GetDependencies(asset.prefab, true))
                        Require(!path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith(Root + "/", StringComparison.Ordinal),
                            "Prefab dependency outside garam: " + path);
                    report.assets.Add(new AssetResult { prefab = asset.prefab, triangles = triangles, renderers = renderers.Length, dimensions = bounds.size, greenGlow = asset.greenGlow,
                        unmaskedEmissiveTriangles = unmaskedEmission, maskedEmissiveMaterials = maskedEmission.Count });
                    report.triangles += triangles;
                }
                finally { Object.DestroyImmediate(instance); }
            }
            Require(report.assets.Count == 64 && report.greenGlowPrefabs == 3, "Incorrect prefab/glow count.");
            report.prefabCount = report.assets.Count;
            report.passed = true;
            File.WriteAllText(Root + "/Editor/validation-report.json", JsonUtility.ToJson(report, true) + "\n");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();
            Debug.Log("GARAM_VALIDATION_PASSED " + report.prefabCount + " prefabs, " + report.triangles + " triangles, " + report.greenGlowPrefabs + " green glow prefabs.");
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
