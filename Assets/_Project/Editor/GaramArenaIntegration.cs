using System.Collections.Generic;
using System.Linq;
using Garam.Reticles;
using Garam.Subtitles;
using OnYourMark.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace OnYourMark.EditorTools
{
    /// <summary>
    /// garam 브랜치 요소(3D 에셋 64종, SenseLink HUD, 락온 조준점, 외계어 자막)를 Arena 테스트 씬에 붙인다.
    /// 다시 실행하면 "Garam Integration" 루트를 지우고 새로 만든다.
    /// 배치모드: Unity.exe -batchmode -projectPath . -executeMethod OnYourMark.EditorTools.GaramArenaIntegration.Run -quit
    /// </summary>
    public static class GaramArenaIntegration
    {
        const string ScenePath = "Assets/_Project/Scenes/Arena.unity";
        const string RootName = "Garam Integration";
        const string GaramPrefabs = "Assets/garam/Prefabs";
        const string HudPrefab = "Assets/garam/UI/Prefabs/SenseLinkHUD.prefab";
        const string ReticlePrefab = "Assets/garam/LockOnReticle/Prefabs/LockOnReticle.prefab";
        const string FloorMaterial = "Assets/_Project/Materials/Grey_Floor.mat";
        const string PedestalMaterial = "Assets/_Project/Materials/Grey_Cover.mat";
        const string EventsDir = "Assets/_Project/Data/Events/";

        static readonly string[] TargetPrefabs =
        {
            "Assets/_Project/Prefabs/Enemy/Enemy_Rusher.prefab",
            "Assets/_Project/Prefabs/Enemy/Enemy_HoverLaser.prefab",
            "Assets/_Project/Prefabs/Arena/TrainingDummy.prefab",
        };
        const string PlayerPrefab = "Assets/_Project/Prefabs/Player/Player.prefab";

        // 이보다 큰 에셋(배경 소행성, 우주선 잔해)은 바닥 진열 대신 원경에 둔다.
        const float BackdropSize = 40f;

        [MenuItem("Tools/On Your Mark/Apply Garam Elements To Arena")]
        public static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run();
        }

        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var old in scene.GetRootGameObjects().Where(g => g.name == RootName).ToArray())
                Object.DestroyImmediate(old);

            var root = new GameObject(RootName);
            Bounds arena = ArenaBounds(scene, out float floorTop);

            int placed = BuildShowcase(root.transform, arena, floorTop);
            BuildHud(root.transform);
            BuildReticle(root.transform);
            BuildSubtitles(root.transform);
            int targets = PatchTargets();
            PatchPlayerCrosshair();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GaramIntegration] 완료: 에셋 {placed}개 배치, 락온 대상 프리팹 {targets}개, HUD·조준점·자막 추가. 아레나 bounds={arena}");
        }

        // ---------- 3D 에셋 진열 ----------

        static Bounds ArenaBounds(UnityEngine.SceneManagement.Scene scene, out float floorTop)
        {
            var arenaRoot = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Arena");
            var renderers = arenaRoot ? arenaRoot.GetComponentsInChildren<Renderer>() : new Renderer[0];
            var b = new Bounds(Vector3.zero, new Vector3(40, 1, 40));
            if (renderers.Length > 0)
            {
                b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
            }
            var floor = arenaRoot ? arenaRoot.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.StartsWith("Floor")) : null;
            var floorRenderer = floor ? floor.GetComponentInChildren<Renderer>() : null;
            floorTop = floorRenderer ? floorRenderer.bounds.max.y : 0f;
            return b;
        }

        static int BuildShowcase(Transform root, Bounds arena, float floorTop)
        {
            var showcase = new GameObject("Asset Showcase").transform;
            showcase.SetParent(root, false);
            var backdrop = new GameObject("Backdrop (large assets)").transform;
            backdrop.SetParent(root, false);

            var floorMat = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterial);
            var pedestalMat = AssetDatabase.LoadAssetAtPath<Material>(PedestalMaterial);

            var categories = AssetDatabase.GetSubFolders(GaramPrefabs).OrderBy(p => p).ToArray();
            float z = arena.max.z + 8f;
            float minX = float.MaxValue, maxX = float.MinValue;
            int count = 0, backdropIndex = 0;

            foreach (var category in categories)
            {
                var guids = AssetDatabase.FindAssets("t:Prefab", new[] { category });
                var prefabs = guids.Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p)
                    .Select(AssetDatabase.LoadAssetAtPath<GameObject>).Where(p => p).ToList();

                var row = new GameObject(System.IO.Path.GetFileName(category)).transform;
                row.SetParent(showcase, false);

                // 먼저 인스턴스화해서 실제 크기를 잰다
                var items = new List<(GameObject go, Bounds b)>();
                foreach (var prefab in prefabs)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, row);
                    var b = WorldBounds(go);
                    if (Mathf.Max(b.size.x, b.size.y, b.size.z) > BackdropSize)
                    {
                        go.transform.SetParent(backdrop, true);
                        PlaceBackdrop(go, b, arena, floorTop, backdropIndex++);
                        count++;
                        continue;
                    }
                    items.Add((go, b));
                }
                if (items.Count == 0) { Object.DestroyImmediate(row.gameObject); continue; }

                float rowDepth = items.Max(i => Mathf.Max(i.b.size.z, 1.2f));
                float totalWidth = items.Sum(i => Mathf.Max(i.b.size.x, 1.2f) + 3f) - 3f;
                float x = arena.center.x - totalWidth * 0.5f;
                float rowZ = z + rowDepth * 0.5f;

                foreach (var (go, b) in items)
                {
                    float w = Mathf.Max(b.size.x, 1.2f);
                    var center = new Vector3(x + w * 0.5f, 0f, rowZ);
                    float baseY = floorTop;

                    // 작은 픽업·파편은 받침대 위에 올려 눈높이 근처에서 보이게 한다
                    if (b.size.y < 0.6f)
                    {
                        var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        pedestal.name = go.name + " (pedestal)";
                        pedestal.transform.SetParent(go.transform.parent, false);
                        pedestal.transform.position = new Vector3(center.x, floorTop + 0.45f, center.z);
                        pedestal.transform.localScale = new Vector3(Mathf.Max(b.size.x, 0.6f) + 0.3f, 0.9f, Mathf.Max(b.size.z, 0.6f) + 0.3f);
                        if (pedestalMat) pedestal.GetComponent<Renderer>().sharedMaterial = pedestalMat;
                        baseY = floorTop + 0.9f;
                    }

                    go.transform.position += new Vector3(center.x - b.center.x, baseY - b.min.y, center.z - b.center.z);
                    AddColliders(go);
                    AddLabel(go.name, go.transform.parent, new Vector3(center.x, baseY + b.size.y + 0.5f, center.z - Mathf.Max(b.size.z, 1.2f) * 0.5f));

                    minX = Mathf.Min(minX, x);
                    maxX = Mathf.Max(maxX, x + w);
                    x += w + 3f;
                    count++;
                }
                z += rowDepth + 6f;
            }

            // 진열 구역 바닥: 아레나 가장자리부터 마지막 줄까지 이어서 걸어갈 수 있게 한다
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Showcase Floor";
            floor.transform.SetParent(showcase, false);
            float startZ = arena.max.z - 1f;
            minX = Mathf.Min(minX, arena.min.x);
            maxX = Mathf.Max(maxX, arena.max.x);
            floor.transform.position = new Vector3((minX + maxX) * 0.5f, floorTop - 0.5f, (startZ + z) * 0.5f);
            floor.transform.localScale = new Vector3(maxX - minX + 8f, 1f, z - startZ + 2f);
            if (floorMat) floor.GetComponent<Renderer>().sharedMaterial = floorMat;

            return count;
        }

        static void PlaceBackdrop(GameObject go, Bounds b, Bounds arena, float floorTop, int index)
        {
            // 큰 에셋은 아레나 바깥 원경에 띄워 둔다 (충돌 없음)
            var spots = new[]
            {
                new Vector3(arena.min.x - 180f, floorTop + 40f, arena.max.z + 220f),
                new Vector3(arena.max.x + 140f, floorTop + 10f, arena.max.z + 120f),
                new Vector3(arena.center.x, floorTop + 60f, arena.min.z - 220f),
            };
            var target = spots[index % spots.Length];
            go.transform.position += target - b.center;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        static void AddColliders(GameObject go)
        {
            if (go.GetComponentInChildren<Collider>()) return;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        }

        static void AddLabel(string text, Transform parent, Vector3 position)
        {
            var label = new GameObject(text + " (label)");
            label.transform.SetParent(parent, false);
            label.transform.position = position;
            var tm = label.AddComponent<TextMesh>();
            tm.text = text;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 48;
            tm.characterSize = 0.04f;
            tm.color = new Color(0.84f, 0.94f, 0.53f);
        }

        // ---------- UI ----------

        static void BuildHud(Transform root)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefab);
            if (!prefab) { Debug.LogError("[GaramIntegration] HUD 프리팹 없음: " + HudPrefab); return; }
            var hudGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);

            // HDRP 노출·후처리 영향을 받지 않도록 오버레이 캔버스로 띄운다
            var canvas = hudGo.GetComponent<Canvas>();
            if (canvas)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 10;
            }

            var hud = hudGo.GetComponentInChildren<OnYourMarkHud>();
            var bridge = hudGo.AddComponent<HudPlayerBridge>();
            SetRef(bridge, "hud", hud);
        }

        static void BuildReticle(Transform root)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ReticlePrefab);
            if (!prefab) { Debug.LogError("[GaramIntegration] 조준점 프리팹 없음: " + ReticlePrefab); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            var canvas = go.GetComponentInChildren<Canvas>();
            if (canvas) canvas.sortingOrder = 20;
            var reticle = go.GetComponentInChildren<LockOnReticle>();
            var auto = go.AddComponent<ReticleAutoLock>();
            SetRef(auto, "reticle", reticle);
        }

        static void BuildSubtitles(Transform root)
        {
            var canvasGo = new GameObject("Subtitle Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(root, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = new GameObject("Subtitle Panel", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            panel.transform.SetParent(canvasGo.transform, false);
            var pr = (RectTransform)panel.transform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0f);
            pr.pivot = new Vector2(0.5f, 0f);
            pr.anchoredPosition = new Vector2(0, 150);
            pr.sizeDelta = new Vector2(1200, 72);
            var bg = panel.GetComponent<Image>();
            bg.color = new Color(0.01f, 0.03f, 0.035f, 0.55f);
            bg.raycastTarget = false;
            var group = panel.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var textGo = new GameObject("Subtitle Text", typeof(RectTransform));
            textGo.transform.SetParent(panel.transform, false);
            var tr = (RectTransform)textGo.transform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(24, 6);
            tr.offsetMax = new Vector2(-24, -6);
            var text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 34;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color32(0xF3, 0xEE, 0xDD, 0xFF);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            // 디코더가 Shadow보다 먼저 메시를 바꿔야 하므로 효과를 먼저 붙인다
            var effect = textGo.AddComponent<AlienSubtitleEffect>();
            effect.playOnEnable = false;
            effect.duration = 0.9f;
            var shadow = textGo.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.6f);

            var director = canvasGo.AddComponent<SubtitleDirector>();
            SetRef(director, "subtitle", effect);
            SetRef(director, "label", text);
            SetRef(director, "group", group);
            SetRef(director, "playersSpawned", AssetDatabase.LoadAssetAtPath<GameEvent>(EventsDir + "Event_PlayersSpawned.asset"));
            SetRef(director, "waveCleared", AssetDatabase.LoadAssetAtPath<GameEvent>(EventsDir + "Event_WaveCleared.asset"));
            SetRef(director, "allWavesCleared", AssetDatabase.LoadAssetAtPath<GameEvent>(EventsDir + "Event_AllWavesCleared.asset"));
        }

        // ---------- 프리팹 패치 ----------

        /// <summary>적·훈련 더미에 ReticleTarget을 붙여 락온 대상으로 만든다.</summary>
        static int PatchTargets()
        {
            int patched = 0;
            foreach (var path in TargetPrefabs)
            {
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(path)) { Debug.LogWarning("[GaramIntegration] 없음: " + path); continue; }
                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var target = contents.GetComponent<ReticleTarget>();
                    if (!target) target = contents.AddComponent<ReticleTarget>();
                    target.boundsRenderers = contents.GetComponentsInChildren<MeshRenderer>(true)
                        .Where(r => !r.GetComponentInParent<Canvas>(true)).Cast<Renderer>().ToArray();
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    patched++;
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            return patched;
        }

        /// <summary>플레이어 프리팹의 기존 점 조준점을 끈다 (garam 조준점과 겹치지 않게).</summary>
        static void PatchPlayerCrosshair()
        {
            var contents = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                var crosshair = contents.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Crosshair");
                if (!crosshair) return;
                foreach (var g in crosshair.GetComponentsInChildren<Graphic>(true)) g.enabled = false;
                PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        static void SetRef(Object owner, string field, Object value)
        {
            var so = new SerializedObject(owner);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogError($"[GaramIntegration] {owner.GetType().Name}.{field} 필드 없음"); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
