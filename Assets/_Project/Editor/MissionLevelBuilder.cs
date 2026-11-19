using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OnYourMark.EditorTools
{
    /// <summary>
    /// Arena의 협동 미션 레벨 보강을 한 번에 만든다 (다시 실행하면 지우고 새로 만든다).
    /// - 파쿠르 장치: 긴 틈의 발사대, 비행 지름길의 부스트 링, 떠다니는 발판, 상승 구간의 선체 판 다리
    /// 위치는 MissionRoute의 갑판 · 레인 · 링크 지점에서 계산한다.
    /// </summary>
    public static class MissionLevelBuilder
    {
        const string Root = "Mission Upgrade";
        const string MatDir = "Assets/_Project/Materials/Mission";

        [MenuItem("On Your Mark/Build Mission Upgrade")]
        public static void Build()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/_Project/Scenes/Arena.unity")
            {
                Debug.LogWarning("Open Arena before building the mission layout.");
                return;
            }
            var roots = scene.GetRootGameObjects();
            var route = roots.SelectMany(go => go.GetComponentsInChildren<MissionRoute>()).FirstOrDefault();
            if (!route) return;
            var old = roots.FirstOrDefault(go => go.name == Root);
            if (old) Object.DestroyImmediate(old);
            var root = new GameObject(Root).transform;

            var glow = Mat("Mission_Glow", new Color(.05f, .12f, .12f), new Color(.3f, 1f, .85f), 900f);
            var hull = Mat("Mission_Hull", new Color(.16f, .18f, .2f), Color.black, 0f, .55f, .7f);

            var previousRandomState = Random.state;
            try
            {
                Random.InitState(1207);   // 다시 만들어도 같은 배치
                BuildParkour(route, root, glow, hull);
                BuildLessons(route, root, glow, hull);
                BuildGuides(route, root);
                BuildCover(route, root);
                BuildDressing(route, root);
            }
            finally
            {
                Random.state = previousRandomState;
            }

            EditorSceneManager.MarkSceneDirty(route.gameObject.scene);
            EditorSceneManager.SaveScene(route.gameObject.scene);
        }

        // ---------------- parkour props ----------------

        // 틈마다 건너는 방법이 다르다 (같은 점프만 반복하지 않게). 구간별 구성:
        //  1페이즈(배우기): 징검다리 점프 → 부스트 링 비행 → 트러스 옆 대쉬 점프
        //  상부 레인(P1): 짚라인 → 링 → 내리막 대쉬 점프 → 발사대 · 링 · 링 연속 비행
        //  하부 레인(P2): 좁은 트러스를 달리고 양끝 갑판의 엄폐물에서 위쪽 P1을 엄호
        //  재합류: 옆으로 떠다니는 판 → 짚라인 → 오르내리는 판
        //  상승: 떠 있는 화물 상자 타고 오르기 → 발사대 → 발사대 · 링 2단 → 컨테이너 등반으로 최상단 갑판
        static void BuildParkour(MissionRoute route, Transform root, Material glow, Material hull)
        {
            var group = new GameObject("Parkour").transform;
            group.SetParent(root, false);
            var gaps = Gaps(route).ToDictionary(g => g.name);
            ClearTrussBlockers(route.gameObject.scene, gaps.Values);
            Physics.SyncTransforms();
            var violet = new Color(.75f, .5f, 1f);
            var amber = new Color(1f, .7f, .3f);

            // 발사대 한 번에 건너편으로
            foreach (var (name, time) in new[] { ("Deck crossing 13", 1.1f) })
                if (gaps.TryGetValue(name, out var g)) Pad(group, name + " pad", g.a - g.dir * 1.4f, g.b + g.dir * 1.6f, time, glow, hull);

            // 부스트 링 한 개: 틈 위를 날아 건너편 착지점으로 (착지점 바로 앞에 다음 장치가 없는 틈만: 하부 6은 착지하자마자 7번 발사대에 끌려가 뺐다)
            foreach (var name in new[] { "Deck crossing 1", "Upper crossing 2" })
                if (gaps.TryGetValue(name, out var g))
                    Ring(group, name + " boost ring", Vector3.Lerp(g.a, g.b, .5f) + Vector3.up * 3.2f, g.b - g.a, Landing(group, name, g), .9f, null);

            // 연속 비행 (상부 4): 발사대가 첫 링으로 쏘아 올리고, 링이 다음 링으로, 마지막 링이 착지점으로
            if (gaps.TryGetValue("Upper crossing 4", out var u4))
            {
                Vector3 side = Vector3.Cross(Vector3.up, u4.dir);
                var landing = Landing(group, u4.name, u4);
                var ringB = Ring(group, "Upper crossing 4 chain ring B", Vector3.Lerp(u4.a, u4.b, .68f) - side * 3f + Vector3.up * 5f, u4.b - u4.a, landing, .6f, violet);
                var ringA = Ring(group, "Upper crossing 4 chain ring A", Vector3.Lerp(u4.a, u4.b, .32f) + side * 3.5f + Vector3.up * 4f, u4.b - u4.a, ringB, .55f, violet);
                Pad(group, "Upper crossing 4 pad", u4.a - u4.dir * 1.4f, ringA.position, .6f, glow, hull);
            }
            // 발사대 · 링 2단 (갑판 14): 위로 쏘아 올린 뒤 링이 앞으로 밀어 준다
            if (gaps.TryGetValue("Deck crossing 14", out var d14))
            {
                var ring = Ring(group, "Deck crossing 14 sky ring", Vector3.Lerp(d14.a, d14.b, .5f) + Vector3.up * 5.5f, d14.b - d14.a, Landing(group, d14.name, d14), .65f, amber);
                Pad(group, "Deck crossing 14 pad", d14.a - d14.dir * 1.4f, ring.position, .7f, glow, hull);
            }

            // 떠다니는 선체 판: 옆으로 흐르는 판 (갑판 9), 오르내리는 판 (갑판 11)
            foreach (var (name, travelSide, period) in new[] { ("Deck crossing 9", true, 6.5f), ("Deck crossing 11", false, 5.5f) })
            {
                if (!gaps.TryGetValue(name, out var g)) continue;
                Vector3 side = Vector3.Cross(Vector3.up, g.dir);
                var plate = Slab(group, name + " drifting plate", Vector3.Lerp(g.a, g.b, .5f) - Vector3.up * .35f, Quaternion.LookRotation(g.dir), new Vector3(3.2f, .35f, 3.6f), hull, glow);
                var mp = plate.AddComponent<MovingPlatform>();
                mp.travel = travelSide ? side * 5f : Vector3.up * 2.2f;
                mp.period = period; mp.phase = Random.value; mp.spinDegrees = 4f;
            }

            BuildLowerLaneCover(group, gaps);

            // 짚라인: 오르막 (상부 1), 수평 (갑판 10). 도착점과 다음 장치 사이가 넉넉한 틈에만 (도착하자마자 다른 장치에 끌려가지 않게)
            foreach (var name in new[] { "Upper crossing 1", "Deck crossing 10" })
                if (gaps.TryGetValue(name, out var g)) Zip(group, name, g, hull, glow);

            // 오르기: 틈 사이에 떠 있는 화물을 밟고 한 층씩 올라선다 (점프 어시스트는 꺼진다: Foothold)
            const string C = "Assets/OYM/Props/Prefabs/Cover/";
            if (gaps.TryGetValue("Deck crossing 12", out var d12))
                Foothold(group, "Foothold crate 12", C + "cover_crate_stack.prefab", d12, .5f, 1.45f, 2.0f);
            if (gaps.TryGetValue("Deck crossing 15", out var d15))
                Foothold(group, "Foothold container 15", C + "cover_container.prefab", d15, .45f, 1.9f, 2.1f);

            // 상승 구간 옆길: 끊어진 갑판 사이를 잇는 기울어진 선체 판과 맨틀용 턱
            for (int i = 12; i < 16; i++)
            {
                Vector3 a = route.deck[i].position, b = route.deck[i + 1].position;
                Vector3 along = b - a;
                Vector3 side = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(along, Vector3.up).normalized);
                Vector3 mid = Vector3.Lerp(a, b, .5f) + side * 4.5f;
                float pitch = -Mathf.Atan2(along.y, Vector3.ProjectOnPlane(along, Vector3.up).magnitude) * Mathf.Rad2Deg * .6f;
                Slab(group, $"Hull bridge {i}", mid - Vector3.up * .4f, Quaternion.LookRotation(Vector3.ProjectOnPlane(along, Vector3.up).normalized) * Quaternion.Euler(pitch, 0, 0),
                    new Vector3(2.6f, .4f, Vector3.ProjectOnPlane(along, Vector3.up).magnitude * .45f), hull, glow);
                Slab(group, $"Ledge {i}", b - Vector3.ProjectOnPlane(along, Vector3.up).normalized * 2.5f + side * 4.5f + Vector3.up * .3f, Quaternion.LookRotation(Vector3.ProjectOnPlane(along, Vector3.up).normalized),
                    new Vector3(2.6f, 1.4f, 1.2f), hull, glow);
            }
        }

        static void BuildLowerLaneCover(Transform group, System.Collections.Generic.Dictionary<string, Gap> gaps)
        {
            // along은 다리 입구에서 뒤로(-) 또는 출구에서 앞으로(+) 잰 거리다.
            // 엄폐물은 넓은 갑판에 놓고 가운데 트러스와 다음 다리로 꺾는 자리를 비운다.
            var placements = new[]
            {
                ("Deck crossing 4", "cover_barricade_low", -3f, -3.2f),
                ("Deck crossing 5", "cover_crate_stack",    3f,  4f),
                ("Deck crossing 6", "cover_barricade_low",  2f, -3.2f)
            };
            foreach (var (name, prefabName, along, side) in placements)
            {
                if (!gaps.TryGetValue(name, out var g) || MissionRoute.Classify(g.a, g.b) != MissionRoute.GapKind.Walkway) continue;
                var label = prefabName + " vault @" + name;
                var vault = group.Find(label);
                var spot = (along < 0f ? g.a : g.b) + g.dir * along + Vector3.Cross(Vector3.up, g.dir) * side;
                var h = Physics.RaycastAll(spot + Vector3.up * 2f, Vector3.down, 5f, Mask, QueryTriggerInteraction.Ignore)
                    .Where(hit => hit.normal.y > .7f && (!vault || !hit.transform.IsChildOf(vault)))
                    .OrderBy(hit => hit.distance).FirstOrDefault();
                if (!h.collider) continue;
                if (!vault)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OYM/Props/Prefabs/Cover/" + prefabName + ".prefab");
                    if (!asset) continue;
                    vault = ((GameObject)PrefabUtility.InstantiatePrefab(asset, group)).transform;
                    vault.name = label;
                }
                vault.SetPositionAndRotation(h.point - h.normal * .05f, Quaternion.FromToRotation(Vector3.up, h.normal) * Quaternion.LookRotation(g.dir));
                PrefabUtility.RecordPrefabInstancePropertyModifications(vault);
                if (!vault.TryGetComponent<UnityEngine.AI.NavMeshObstacle>(out var obstacle))
                    obstacle = vault.gameObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();
                obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
                obstacle.center = vault.InverseTransformPoint(RenderBounds(vault.gameObject).center);
                obstacle.size = prefabName == "cover_crate_stack" ? new Vector3(3.2f, 2f, 1.7f) : new Vector3(3f, 1f, 1.2f);
                obstacle.carving = true; obstacle.carveOnlyStationary = true;
            }
        }

        // 틈의 착지점 (건너편 갑판 안쪽 1.6m)
        static Transform Landing(Transform parent, string name, Gap g)
        {
            var t = new GameObject(name + " landing").transform;
            t.SetParent(parent, false);
            t.position = g.b + g.dir * 1.6f;
            return t;
        }

        static Transform Ring(Transform parent, string name, Vector3 at, Vector3 forward, Transform target, float time, Color? color)
        {
            var ring = new GameObject(name);
            ring.transform.SetParent(parent, false);
            ring.transform.position = at;
            ring.transform.rotation = Quaternion.LookRotation(forward.normalized + Vector3.up * .15f);
            ring.AddComponent<SphereCollider>();
            var br = ring.AddComponent<BoostRing>();
            br.target = target;
            br.flightTime = time;
            if (color.HasValue) br.color = color.Value;
            return ring.transform;
        }

        // 짚라인: 출발 갑판 가장자리의 기둥 → 건너편 갑판의 기둥. 줄은 기둥 꼭대기 사이, 몸은 그 아래로 매달려 간다
        static void Zip(Transform parent, string name, Gap g, Material hull, Material glow)
        {
            if (!Edge(g.a, -g.dir, out var from) || !Edge(g.b, g.dir, out var to)) return;
            Vector3 start = from.point - g.dir * .6f, end = to.point + g.dir * 1.8f;
            const float hang = 2.9f;
            var zip = new GameObject(name + " zipline");
            zip.transform.SetParent(parent, false);
            zip.transform.position = start + Vector3.up * 1.1f;
            var trigger = zip.AddComponent<SphereCollider>();
            trigger.radius = 1.3f;
            var endPoint = new GameObject("End").transform;
            endPoint.SetParent(zip.transform, true);
            endPoint.position = end + Vector3.up * .5f;   // 착지 쪽 턱 위로 도착해 살짝 내려앉는다
            var z = zip.AddComponent<Zipline>();
            z.end = endPoint;
            // 양 끝: 길 양옆의 기둥 둘 + 가로대 (문 모양). 사람은 가운데로 지나가고, 줄은 가로대 가운데에 걸린다
            Vector3 side = Vector3.Cross(Vector3.up, g.dir);
            float top = hang + .55f;
            var anchors = new Vector3[2];
            int n = 0;
            foreach (var (p, label) in new[] { (start - g.dir * .5f, "Start"), (end + g.dir * .5f, "End") })
            {
                foreach (float sgn in new[] { -1f, 1f })
                {
                    var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    pole.name = label + " pole";
                    Object.DestroyImmediate(pole.GetComponent<Collider>());
                    pole.transform.SetParent(zip.transform, true);
                    pole.transform.position = p + side * (1.35f * sgn) + Vector3.up * (top * .5f);
                    pole.transform.localScale = new Vector3(.2f, top * .5f, .2f);
                    pole.GetComponent<Renderer>().sharedMaterial = hull;
                }
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = label + " crossbar";
                Object.DestroyImmediate(bar.GetComponent<Collider>());
                bar.transform.SetParent(zip.transform, true);
                bar.transform.SetPositionAndRotation(p + Vector3.up * top, Quaternion.LookRotation(g.dir));
                bar.transform.localScale = new Vector3(2.9f, .16f, .16f);
                bar.GetComponent<Renderer>().sharedMaterial = hull;
                var pulley = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pulley.name = label + " pulley";
                Object.DestroyImmediate(pulley.GetComponent<Collider>());
                pulley.transform.SetParent(zip.transform, true);
                pulley.transform.position = p + Vector3.up * (top - .12f);
                pulley.transform.localScale = Vector3.one * .34f;
                pulley.GetComponent<Renderer>().sharedMaterial = glow;
                anchors[n++] = p + Vector3.up * (top - .12f);
            }
            // 줄: 어두운 강선 + 발광선 (멀리서도 길이 읽히게)
            foreach (var (mat, width, lineName) in new[] { (hull, .1f, "Cable"), (glow, .05f, "Cable glow") })
            {
                var lr = new GameObject(lineName).AddComponent<LineRenderer>();
                lr.transform.SetParent(zip.transform, false);
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.SetPosition(0, anchors[0]);
                lr.SetPosition(1, anchors[1]);
                lr.widthMultiplier = width;
                lr.sharedMaterial = mat;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // 타고 가는 길(몸 캡슐)이 비어 있는지 확인: 막히면 경고만 남긴다 (빌드 결과에서 확인)
            Vector3 c0 = start + Vector3.up * (.9f + 1.2f), c1 = endPoint.position + Vector3.up * .9f;   // 손잡이를 잡은 높이에서 출발 (Zipline.grabLift)
            if (Physics.CapsuleCast(c0 + Vector3.up * .5f, c0 - Vector3.up * .5f, .35f, (c1 - c0).normalized, (c1 - c0).magnitude - .5f, Mask, QueryTriggerInteraction.Ignore))
                Debug.LogWarning("[MissionLevelBuilder] zipline path blocked: " + name);
        }

        // 틈 사이에 떠 있는 발판 하나 (화물 상자 · 컨테이너). top: 출발 갑판보다 몇 m 위가 윗면인지
        static void Foothold(Transform parent, string name, string prefabPath, Gap g, float at, float top, float height)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (!prefab) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            var p = Vector3.Lerp(g.a, g.b, at);
            p.y = g.a.y + top - height;   // 프리팹 기준점은 바닥
            go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(g.dir) * Quaternion.Euler(0, 6f, 0));
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.gameObject.name = name;   // MissionRoute가 이름으로 디딤판을 알아본다
        }

        // 팀이 틈마다 걸쳐 놓은 트러스 중 틈의 출발 · 착지 자리나 건너는 길을 막는 것 (출발점에서 멈추거나 도약 중에 부딪혀 떨어지던 원인):
        //  - 하부 트러스 다리의 겉모습(Lower truss crossing의 자식): 보이게 두고 충돌만 끈다. 바닥은 부모의 박스 콜라이더
        //  - 진짜 빈 틈에 걸친 장식: 숨긴다 (새 장치와 겹쳐 보이지 않게). 빌드할 때마다 되돌린 뒤 다시 판단한다
        static int ClearTrussBlockers(Scene scene, System.Collections.Generic.IEnumerable<Gap> gaps)
        {
            int hidden = 0, ghosted = 0;
            var list = gaps.ToArray();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.gameObject.scene == scene && t.name == "wreck_truss_long" && !t.gameObject.activeSelf && PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) t.gameObject.SetActive(true);
            Physics.SyncTransforms();
            foreach (var c in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (c.gameObject.scene != scene || !c.enabled || !c.name.Contains("truss")) continue;
                var b = c.bounds;
                b.Expand(1.2f);
                if (!list.Any(g => Enumerable.Range(0, 11).Any(k => b.Contains(Vector3.Lerp(g.a - g.dir * 1.5f, g.b + g.dir * 1.5f, k / 10f) + Vector3.up)))) continue;
                var instance = PrefabUtility.GetNearestPrefabInstanceRoot(c.gameObject) ?? c.gameObject;
                if (instance.transform.parent && instance.transform.parent.name.StartsWith("Lower truss crossing"))
                {
                    foreach (var mc in instance.GetComponentsInChildren<MeshCollider>())
                    {
                        Undo.RecordObject(mc, "Truss collider off");
                        mc.enabled = false;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(mc);
                    }
                    ghosted++;
                }
                else
                {
                    Undo.RecordObject(instance, "Hide truss blocker");
                    instance.SetActive(false);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
                    hidden++;
                }
            }
            Debug.Log($"[MissionLevelBuilder] route trusses: hidden {hidden}, walkway visuals made non-blocking {ghosted}");
            return hidden + ghosted;
        }

        static void Pad(Transform parent, string name, Vector3 at, Vector3 target, float time, Material glow, Material hull)
        {
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = name;
            pad.transform.SetParent(parent, false);
            pad.transform.position = at + Vector3.up * .05f;
            pad.transform.localScale = new Vector3(2.2f, .06f, 2.2f);
            pad.GetComponent<Renderer>().sharedMaterial = glow;
            Object.DestroyImmediate(pad.GetComponent<Collider>());
            var trigger = pad.AddComponent<BoxCollider>();
            trigger.size = new Vector3(1f, 30f, 1f);
            trigger.center = new Vector3(0, 15f, 0);
            var t = new GameObject("Target").transform;
            t.SetParent(parent, false);
            t.position = target;
            t.name = name + " target";
            var jp = pad.AddComponent<JumpPad>();
            jp.target = t; jp.flightTime = time; jp.glow = pad.GetComponent<Renderer>();
            BuildPadSurface(jp, hull);
        }

        static void BuildPadSurface(JumpPad pad, Material hull)
        {
            // 발밑에서 디스크 전체가 빛나면 조준 방향이 묻힌다. 바깥 테두리만 맥동시키고 금속 면에 방향을 표시한다.
            var face = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            face.name = "Tread plate";
            Object.DestroyImmediate(face.GetComponent<Collider>());
            face.transform.SetParent(pad.transform, false);
            face.transform.localPosition = Vector3.up * .82f;
            face.transform.localScale = new Vector3(.9f, .45f, .9f);
            face.GetComponent<Renderer>().sharedMaterial = hull;

            var direction = Vector3.ProjectOnPlane(pad.target.position - pad.transform.position, Vector3.up).normalized;
            var arrows = new GameObject("Launch direction").transform;
            arrows.position = face.transform.TransformPoint(Vector3.up);
            arrows.SetParent(pad.transform, true);
            foreach (float offset in new[] { -.08f, .55f })
                Chevron(arrows, arrows.position + direction * offset, direction, Vector3.up, pad.glow.sharedMaterial);
        }

        static GameObject Slab(Transform parent, string name, Vector3 at, Quaternion rot, Vector3 size, Material hull, Material glow)
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = name;
            slab.layer = LayerMask.NameToLayer("Environment");
            slab.transform.SetParent(parent, false);
            slab.transform.SetPositionAndRotation(at, rot);
            slab.transform.localScale = size;
            slab.GetComponent<Renderer>().sharedMaterial = hull;
            var trim = GameObject.CreatePrimitive(PrimitiveType.Cube);
            trim.name = "Trim";
            Object.DestroyImmediate(trim.GetComponent<Collider>());
            trim.transform.SetParent(slab.transform, false);
            trim.transform.localPosition = new Vector3(0, .5f, .5f);
            trim.transform.localScale = new Vector3(1.02f, .12f, .04f);
            trim.GetComponent<Renderer>().sharedMaterial = glow;
            return slab;
        }

        // ---------------- level design pass ----------------

        static int Mask => LayerMask.GetMask("Environment", "Default");

        static bool Ground(Vector3 p, out RaycastHit hit, float up = 2f, float down = 6f) =>
            Physics.Raycast(p + Vector3.up * up, Vector3.down, out hit, up + down, Mask, QueryTriggerInteraction.Ignore);

        readonly struct Gap
        {
            public readonly string name; public readonly Vector3 a, b, dir; public readonly float length;
            public Gap(string name, Vector3 a, Vector3 b)
            {
                this.name = name; this.a = a; this.b = b;
                var flat = Vector3.ProjectOnPlane(b - a, Vector3.up);
                length = flat.magnitude; dir = length > .01f ? flat / length : Vector3.forward;
            }
        }

        static Gap[] Gaps(MissionRoute route) => route.GetComponentsInChildren<Unity.AI.Navigation.NavMeshLink>()
            .Select(l => new Gap(l.name, l.transform.TransformPoint(l.startPoint), l.transform.TransformPoint(l.endPoint))).ToArray();

        // 1페이즈 첫 틈: 징검다리 둘 (점프를 배우는 자리). 디딤판이 있는 틈은 점프 어시스트가 꺼진다 (MissionRoute가 감지)
        static void BuildLessons(MissionRoute route, Transform root, Material glow, Material hull)
        {
            var group = new GameObject("Lessons").transform;
            group.SetParent(root, false);
            var first = Gaps(route).FirstOrDefault(l => l.name == "Deck crossing 0");
            if (first.name == null) return;
            Vector3 side = Vector3.Cross(Vector3.up, first.dir);
            float[] at = { .3f, .7f }, lateral = { .9f, -.9f }, yaw = { 8f, -6f };
            for (int i = 0; i < 2; i++)
            {
                var p = Vector3.Lerp(first.a, first.b, at[i]) + side * lateral[i];
                Slab(group, $"Foothold stone {i}", p - Vector3.up * .3f, Quaternion.LookRotation(first.dir) * Quaternion.Euler(0, yaw[i], 0),
                    new Vector3(3.2f, .55f, 2.8f), hull, glow);
            }
        }

        // 러너 가이드: 모든 틈의 출발선 · 착지선과 진행 방향 쐐기, 착지 쪽 신호기.
        // 색과 쐐기 수가 건너는 방법을 알려 준다: 청록 1개 = 점프, 주황 2개 = 대쉬 점프, 보라 3개 = 비행 · 발사대 · 링
        static void BuildGuides(MissionRoute route, Transform root)
        {
            Physics.SyncTransforms();   // 앞 단계에서 만든 디딤판 · 신호기를 물리 검사에 넣는다
            var group = new GameObject("Runner guides").transform;
            group.SetParent(root, false);
            var mats = new[]
            {
                Mat("Mission_GuideJump", new Color(.02f, .05f, .05f), new Color(.3f, 1f, .85f), 1400f),
                Mat("Mission_GuideDash", new Color(.06f, .04f, .01f), new Color(1f, .62f, .18f), 1400f),
                Mat("Mission_GuideFly", new Color(.04f, .02f, .06f), new Color(.72f, .45f, 1f), 1400f),
            };
            var post = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OYM/Props/Prefabs/Wreckage/debris_beacon_post.prefab");
            foreach (var l in Gaps(route))
            {
                // 바닥이 이어진 통로는 틈이 아니다: 표시하지 않는다. 디딤판이 놓인 틈은 길어도 점프 칸이다
                var kind = MissionRoute.Classify(l.a, l.b);
                if (kind == MissionRoute.GapKind.Walkway) continue;
                bool stones = kind == MissionRoute.GapKind.Foothold;
                // 짚라인 · 페리는 달려들거나 올라타면 되는 자리: 점프 표시
                bool ride = root.Find($"Parkour/{l.name} zipline") || root.Find($"Parkour/{l.name} ferry");
                int tier = stones || ride || l.length <= 8f ? 0 : l.length <= 12.5f ? 1 : 2;
                var mat = mats[tier];
                var g = new GameObject(l.name + " guide").transform;
                g.SetParent(group, false);
                if (Edge(l.a, -l.dir, out var takeoff))
                {
                    Bar(g, "Takeoff line", takeoff.point, l.dir, takeoff.normal, new Vector3(3.2f, .05f, .2f), mat);
                    for (int k = 0; k <= tier; k++)
                        if (Ground(takeoff.point - l.dir * (1.1f + k * .8f), out var c, 1f, 2f) && c.normal.y > .8f)
                            Chevron(g, c.point, l.dir, c.normal, mat);
                }
                if (!Edge(l.b, l.dir, out var landing)) continue;
                Bar(g, "Landing line", landing.point, l.dir, landing.normal, new Vector3(3.2f, .05f, .12f), mat);
                if (!post) continue;
                Vector3 side = Vector3.Cross(Vector3.up, l.dir);
                foreach (float s in new[] { 1f, -1f })
                {
                    var spot = landing.point + l.dir * 1.6f + side * (2.6f * s);
                    if (!Ground(spot, out var gh, 1.5f, 3f) || gh.normal.y < .85f || Mathf.Abs(gh.point.y - landing.point.y) > 1.2f) continue;
                    var beacon = (GameObject)PrefabUtility.InstantiatePrefab(post, g);
                    beacon.name = "Landing beacon";
                    beacon.transform.SetPositionAndRotation(gh.point + Vector3.up * 1.6f, Quaternion.LookRotation(l.dir));
                    beacon.transform.localScale = new Vector3(2.2f, 4f, 2.2f);
                    var obstacle = beacon.AddComponent<UnityEngine.AI.NavMeshObstacle>();
                    obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Capsule; obstacle.radius = .18f; obstacle.height = .8f; obstacle.carving = true;
                    var cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    cap.name = "Beacon cap";
                    Object.DestroyImmediate(cap.GetComponent<Collider>());
                    cap.transform.SetParent(g, false);
                    cap.transform.position = gh.point + Vector3.up * 3.35f;
                    cap.transform.localScale = Vector3.one * .32f;
                    cap.GetComponent<Renderer>().sharedMaterial = mat;
                    break;
                }
            }
        }

        // 틈 끝점에서 갑판 안쪽으로 조금씩 들어가며 실제 가장자리 바닥을 찾는다
        static bool Edge(Vector3 p, Vector3 inward, out RaycastHit hit)
        {
            for (float d = 0f; d <= 2.4f; d += .3f)
                if (Ground(p + inward * d, out hit, 1.2f, 2f) && hit.normal.y > .8f && Mathf.Abs(hit.point.y - p.y) < 1.5f) return true;
            hit = default;
            return false;
        }

        static void Bar(Transform parent, string name, Vector3 at, Vector3 dir, Vector3 normal, Vector3 size, Material mat)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = name;
            Object.DestroyImmediate(bar.GetComponent<Collider>());
            bar.transform.SetParent(parent, false);
            bar.transform.SetPositionAndRotation(at + normal * .03f, Quaternion.LookRotation(Vector3.ProjectOnPlane(dir, normal).normalized, normal));
            bar.transform.localScale = size;
            bar.GetComponent<Renderer>().sharedMaterial = mat;
            bar.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // 진행 방향을 가리키는 ">" 모양 바닥 표식
        static void Chevron(Transform parent, Vector3 at, Vector3 dir, Vector3 normal, Material mat)
        {
            var c = new GameObject("Chevron").transform;
            c.SetParent(parent, false);
            c.SetPositionAndRotation(at + normal * .03f, Quaternion.LookRotation(Vector3.ProjectOnPlane(dir, normal).normalized, normal));
            foreach (float s in new[] { -1f, 1f })
            {
                var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
                arm.name = "Arm";
                Object.DestroyImmediate(arm.GetComponent<Collider>());
                arm.transform.SetParent(c, false);
                arm.transform.localPosition = new Vector3(.3f * s, 0f, -.25f);
                arm.transform.localRotation = Quaternion.Euler(0f, -50f * s, 0f);
                arm.transform.localScale = new Vector3(.14f, .04f, .8f);
                arm.GetComponent<Renderer>().sharedMaterial = mat;
                arm.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        // 전투 갑판의 엄폐물: 조우가 열리는 넓은 갑판에 2~3개. 동선 · 틈 끝 · 스폰 중심 · 체크포인트를 비켜 놓고, 적 AI가 돌아가게 NavMesh를 판다
        static void BuildCover(MissionRoute route, Transform root)
        {
            Physics.SyncTransforms();   // 앞 단계에서 만든 디딤판 · 신호기를 물리 검사에 넣는다
            var group = new GameObject("Combat cover").transform;
            group.SetParent(root, false);
            const string C = "Assets/OYM/Props/Prefabs/Cover/";
            var props = new[] { "cover_barricade_low", "cover_crate_stack", "cover_barricade_high", "cover_container" }
                .Select(n => AssetDatabase.LoadAssetAtPath<GameObject>(C + n + ".prefab")).Where(x => x).ToArray();
            if (props.Length == 0) return;
            var gaps = Gaps(route);
            var keepClear = gaps.SelectMany(l => new[] { l.a, l.b }).ToList();
            for (int p = 2; p <= 4; p++) { keepClear.Add(route.Checkpoint(p, true)); keepClear.Add(route.Checkpoint(p, false)); }
            var arenas = new (Transform t, int count)[]
            {
                (route.deck[1], 3), (route.deck[2], 3), (route.deck[10], 3), (route.deck[11], 2),
                (route.deck[13], 3), (route.deck[15], 2), (route.upperLane[2], 2), (route.upperLane[4], 2),
            };
            int pick = 0;
            foreach (var (t, count) in arenas)
            {
                Vector3 center = t.position;
                if (!Ground(center, out var floor)) continue;
                // 이 갑판으로 들어오는 틈과 나가는 틈: 그 사이 동선은 비운다
                var inGap = gaps.OrderBy(l => (l.b - center).sqrMagnitude).First();
                var outGap = gaps.OrderBy(l => (l.a - center).sqrMagnitude).First();
                Vector3 travel = Vector3.ProjectOnPlane(outGap.a - inGap.b, Vector3.up).normalized;
                if (travel.sqrMagnitude < .1f) travel = outGap.dir;
                var chosen = new System.Collections.Generic.List<Vector3>();
                for (int ring = 0; ring < 2 && chosen.Count < count; ring++)
                    for (int k = 0; k < 12 && chosen.Count < count; k++)
                    {
                        Vector3 offset = Quaternion.AngleAxis(k * 30f + ring * 15f, Vector3.up) * travel * (ring == 0 ? 6f : 8f);
                        if (!Ground(center + offset, out var h, 2f, 4f) || h.collider != floor.collider || h.normal.y < .9f) continue;
                        if (keepClear.Any(x => Flat(x - h.point) < 5f)) continue;
                        if (Flat(h.point - center) < 4.5f || DistanceToSegment(h.point, inGap.b, outGap.a) < 3f) continue;
                        if (chosen.Any(x => Flat(x - h.point) < 5f)) continue;
                        var prefab = props[pick % props.Length];
                        var rot = Quaternion.FromToRotation(Vector3.up, h.normal) * Quaternion.LookRotation(-travel) * Quaternion.Euler(0, Random.Range(-18f, 18f), 0);
                        // 놓을 자리에 다른 물체가 없어야 한다 (바닥 자신은 제외)
                        if (Physics.OverlapBox(h.point + h.normal * 1.25f, new Vector3(2.1f, 1.1f, 1.2f), rot, Mask, QueryTriggerInteraction.Ignore).Any(c => c != floor.collider)) continue;
                        var cover = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
                        cover.name = prefab.name + " @" + t.name;
                        cover.transform.SetPositionAndRotation(h.point - h.normal * .05f, rot);
                        var b = RenderBounds(cover);
                        var obstacle = cover.AddComponent<UnityEngine.AI.NavMeshObstacle>();
                        obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
                        obstacle.center = cover.transform.InverseTransformPoint(b.center);
                        obstacle.size = prefab.name.Contains("container") ? new Vector3(4f, 2.1f, 2.2f) : prefab.name.Contains("crate") ? new Vector3(3.2f, 2f, 1.7f) :
                            prefab.name.Contains("high") ? new Vector3(3f, 1.9f, 1.6f) : new Vector3(3f, 1f, 1.2f);
                        obstacle.carving = true; obstacle.carveOnlyStationary = true;
                        chosen.Add(h.point);
                        pick++;
                    }
            }
        }

        // 경로 둘레의 큰 잔해: 멀리서도 보이는 랜드마크와, 지나갈 때 스쳐 가는 시차로 속도감을 준다.
        // 닿을 수 없는 거리에 두고 충돌체를 뺀다 (엉뚱한 곳에 올라서지 않게)
        static void BuildDressing(MissionRoute route, Transform root)
        {
            Physics.SyncTransforms();   // 앞 단계에서 만든 디딤판 · 신호기를 물리 검사에 넣는다
            var group = new GameObject("Set dressing").transform;
            group.SetParent(root, false);
            const string W = "Assets/OYM/Props/Prefabs/Wreckage/";
            var routePoints = route.deck.Select(d => d.position).Concat(route.upperLane.Select(u => u.position)).Concat(route.lowerLane.Select(u => u.position)).ToArray();
            var items = new (string prefab, int deck, float side, float height, float scale, Vector3 euler)[]
            {
                ("wreck_ship_bow", 6, -1f, -18f, 1f, new Vector3(8f, 100f, -6f)),     // 중반의 거대한 함선 잔해: 어디서든 보이는 랜드마크
                ("wreck_solar_wing", 1, 1f, -3f, 1.6f, new Vector3(20f, 60f, 75f)),
                ("wreck_hab_module", 3, 1f, -9f, 2f, new Vector3(15f, 30f, 10f)),
                ("wreck_solar_wing", 5, 1f, 3f, 1.8f, new Vector3(-10f, 120f, 60f)),
                ("wreck_truss_long", 10, 1f, -6f, 2.5f, new Vector3(0f, 70f, 25f)),
                ("wreck_hull_large", 11, -1f, -2f, 3f, new Vector3(10f, 200f, -20f)),
                ("wreck_hab_module", 12, 1f, -10f, 2.2f, new Vector3(-20f, 80f, 5f)),
                ("wreck_truss_long", 13, -1f, 5f, 2.5f, new Vector3(40f, 10f, 70f)),
                ("wreck_hull_large", 14, 1f, -4f, 3.2f, new Vector3(-15f, 150f, 30f)),
            };
            foreach (var it in items)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(W + it.prefab + ".prefab");
                if (!prefab) continue;
                var d = route.deck;
                Vector3 heading = Vector3.ProjectOnPlane(d[Mathf.Min(it.deck + 1, d.Length - 1)].position - d[Mathf.Max(it.deck - 1, 0)].position, Vector3.up).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, heading) * it.side;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
                go.name = it.prefab + " (dressing)";
                go.transform.rotation = Quaternion.LookRotation(heading) * Quaternion.Euler(it.euler);
                go.transform.localScale = Vector3.one * it.scale;
                foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                // 경로(갑판 · 레인)에서 충분히 떨어질 때까지 바깥으로 민다
                float lateral = 20f;
                for (int i = 0; i < 30; i++)
                {
                    go.transform.position = d[it.deck].position + side * lateral + Vector3.up * it.height;
                    var b = RenderBounds(go);
                    float clearance = 14f + b.extents.magnitude;
                    if (routePoints.All(p => Vector3.Distance(p, b.center) > clearance)) break;
                    lateral += 4f;
                }
            }
        }

        static Bounds RenderBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static float Flat(Vector3 v) => Vector3.ProjectOnPlane(v, Vector3.up).magnitude;

        static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = Vector3.ProjectOnPlane(b - a, Vector3.up), ap = Vector3.ProjectOnPlane(p - a, Vector3.up);
            float t = ab.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp01(Vector3.Dot(ap, ab) / ab.sqrMagnitude);
            return (ap - ab * t).magnitude;
        }

        // ---------------- assets ----------------

        static Material Mat(string name, Color baseColor, Color emissive, float intensity, float metallic = .2f, float smooth = .5f)
        {
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/_Project/Materials", "Mission");
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_BaseColor", baseColor);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smooth);
            mat.SetColor("_EmissiveColor", emissive * intensity);
            mat.SetColor("_EmissiveColorLDR", emissive);
            mat.SetFloat("_EmissiveIntensity", intensity);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
            return mat;
        }
    }
}
