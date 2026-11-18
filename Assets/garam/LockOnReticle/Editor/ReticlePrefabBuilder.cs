using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Garam.Reticles.Editor
{
    public static class ReticlePrefabBuilder
    {
        public const string Folder = "Assets/garam/LockOnReticle";
        public const string PrefabPath = Folder + "/Prefabs/LockOnReticle.prefab";

        [MenuItem("Tools/Garam/Lock-On Reticle/Rebuild Prefab")]
        public static void Build()
        {
            Sprite idleSprite = ImportSprite("reticle-idle.png");
            Sprite nearSprite = ImportSprite("reticle-near.png");
            Sprite farSprite = ImportSprite("reticle-far.png");
            if (!AssetDatabase.IsValidFolder(Folder + "/Prefabs"))
                AssetDatabase.CreateFolder(Folder, "Prefabs");

            var root = new GameObject("LockOnReticle", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.SetActive(false);
            try
            {
                Canvas canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 50;
                CanvasScaler scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;

                RectTransform marker = Rect("Marker", root.transform, new Vector2(256, 256));
                CanvasGroup visibility = marker.gameObject.AddComponent<CanvasGroup>();
                CanvasGroup idle = Group("Idle", marker, 1f);
                CanvasGroup near = Group("Near", marker, 0f);
                CanvasGroup far = Group("Far", marker, 0f);
                Art("Aim mark", idle.transform, idleSprite, new Vector2(32, 12));
                RectTransform nearRing = Art("Near ring", near.transform, nearSprite, new Vector2(256, 256));
                RectTransform farArrows = Art("Far arrows", far.transform, farSprite, new Vector2(160, 160));
                Art("Aim pivot", far.transform, idleSprite, new Vector2(32, 12));

                LockOnReticle controller = root.AddComponent<LockOnReticle>();
                controller.marker = marker;
                controller.visibility = visibility;
                controller.idle = idle;
                controller.near = near;
                controller.far = far;
                controller.nearRing = nearRing;
                controller.farArrows = farArrows;
                root.SetActive(true);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("Created Figma-authored lock-on reticle prefab: " + PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Sprite ImportSprite(string name)
        {
            string path = Folder + "/Art/" + name;
            if (!File.Exists(path))
                throw new FileNotFoundException("Download the Figma reticle export before building the prefab.", path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        private static CanvasGroup Group(string name, Transform parent, float alpha)
        {
            CanvasGroup group = Rect(name, parent, new Vector2(256, 256)).gameObject.AddComponent<CanvasGroup>();
            group.alpha = alpha;
            group.blocksRaycasts = false;
            group.interactable = false;
            return group;
        }

        private static RectTransform Art(string name, Transform parent, Sprite sprite, Vector2 size)
        {
            RectTransform rect = Rect(name, parent, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return rect;
        }
    }
}
