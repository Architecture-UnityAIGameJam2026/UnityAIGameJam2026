using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace OnYourMark
{
    /// <summary>
    /// 로컬 연출용 GameObject 풀 (폭발 · 섬광처럼 잠깐 켰다 끄는 것). Instantiate + Destroy(fx, t) 대신
    /// FxPool.Play(template, pos, rot, scale, lifetime)를 부르면 수명이 끝날 때 비활성화해서 다음에 다시 쓴다.
    /// 수명 처리는 코루틴 없이 Update 한 곳에서 하므로 재생할 때 GC 할당이 없다.
    /// 풀은 현재 씬에 붙어 있어 씬이 바뀌면 같이 정리된다.
    /// </summary>
    public sealed class FxPool : MonoBehaviour
    {
        struct Live { public GameObject go; public ObjectPool<GameObject> pool; public float until; }

        static FxPool s_Instance;

        readonly Dictionary<GameObject, ObjectPool<GameObject>> pools = new();
        readonly List<Live> live = new(32);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Instance = null;

        static FxPool Instance
        {
            get
            {
                if (!s_Instance) s_Instance = new GameObject("[FxPool]").AddComponent<FxPool>();
                return s_Instance;
            }
        }

        /// <summary>template 복제본을 켜서 lifetime초 뒤 풀로 돌려보낸다. template은 비활성 자식 템플릿이어도 된다.</summary>
        public static GameObject Play(GameObject template, Vector3 position, Quaternion rotation, Vector3 scale, float lifetime)
        {
            if (!template) return null;
            var self = Instance;
            var pool = self.PoolFor(template);
            var go = pool.Get();
            var t = go.transform;
            t.SetPositionAndRotation(position, rotation);
            t.localScale = scale;
            go.SetActive(true);
            self.live.Add(new Live { go = go, pool = pool, until = Time.time + lifetime });
            return go;
        }

        /// <summary>미리 count개를 만들어 둔다 (첫 재생 프레임에 Instantiate가 몰리지 않게).</summary>
        public static void Prewarm(GameObject template, int count)
        {
            if (!template || count <= 0) return;
            var pool = Instance.PoolFor(template);
            var tmp = ListPool<GameObject>.Get();
            for (int i = 0; i < count; i++) tmp.Add(pool.Get());
            for (int i = 0; i < tmp.Count; i++) pool.Release(tmp[i]);
            ListPool<GameObject>.Release(tmp);
        }

        ObjectPool<GameObject> PoolFor(GameObject template)
        {
            if (pools.TryGetValue(template, out var pool)) return pool;
            var root = transform;
            pool = new ObjectPool<GameObject>(
                createFunc: () =>
                {
                    var go = Instantiate(template, root);
                    go.name = template.name;
                    go.SetActive(false);
                    return go;
                },
                actionOnGet: null,
                actionOnRelease: go => { if (go) go.SetActive(false); },
                actionOnDestroy: go => { if (go) Destroy(go); },
                collectionCheck: false, defaultCapacity: 4, maxSize: 64);
            pools.Add(template, pool);
            return pool;
        }

        void Update()
        {
            float now = Time.time;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var e = live[i];
                if (e.go && now < e.until) continue;
                if (e.go) e.pool.Release(e.go);
                int last = live.Count - 1;
                live[i] = live[last];
                live.RemoveAt(last);
            }
        }

        void OnDestroy()
        {
            foreach (var p in pools.Values) p.Dispose();
            pools.Clear();
            live.Clear();
            if (s_Instance == this) s_Instance = null;
        }
    }
}
