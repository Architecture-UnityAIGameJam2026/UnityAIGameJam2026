using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 웨이브 진행(서버). 두 플레이어가 스폰되면 시작해, 적을 전멸시킬 때마다 다음 웨이브로 넘어간다.
    /// 지금은 웨이브 사이를 시간으로 넘기고, P9에서 단말기 해금으로 바꾼다.
    /// 적은 NetworkObjectPool로 재사용한다 (첫 웨이브에서 만들어진 인스턴스를 다음 웨이브가 다시 쓴다).
    /// </summary>
    public class WaveManager : NetworkBehaviour
    {
        [SerializeField] WaveData[] waves;
        [SerializeField] float firstWaveDelay = 3f;
        [SerializeField] float intermission = 6f;

        [Header("이벤트 채널")]
        [SerializeField] GameEvent playersSpawned;
        [SerializeField] GameEvent waveCleared;
        [SerializeField] GameEvent allWavesCleared;

        readonly NetworkVariable<int> waveIndex = new(-1);
        readonly NetworkVariable<int> alive = new();

        /// <summary>1부터 시작. 시작 전이면 0.</summary>
        public int WaveNumber => waveIndex.Value + 1;
        public int TotalWaves => waves.Length;
        public int Alive => alive.Value;

        // 씬 오브젝트라 접속 전에 모든 피어에서 돈다: 적 스폰 메시지보다 먼저 풀 핸들러를 건다
        void Start() => RegisterEnemyPools();

        public override void OnNetworkSpawn()
        {
            RegisterEnemyPools();
            if (IsServer) playersSpawned.Raised += Begin;
        }

        void RegisterEnemyPools()
        {
            if (waves == null) return;
            foreach (var wave in waves)
                if (wave != null && wave.groups != null)
                    foreach (var group in wave.groups)
                        if (group.enemy) NetworkObjectPool.Register(group.enemy.prefab);   // NavMeshAgent가 있어 미리 만들지 않는다
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) playersSpawned.Raised -= Begin;
        }

        void Begin() => StartCoroutine(Run());

        IEnumerator Run()
        {
            yield return new WaitForSeconds(firstWaveDelay);
            for (int i = 0; i < waves.Length; i++)
            {
                waveIndex.Value = i;
                Spawn(waves[i]);
                yield return new WaitUntil(() => alive.Value == 0);

                waveCleared.Raise();
                if (i < waves.Length - 1) yield return new WaitForSeconds(intermission);
            }
            allWavesCleared.Raise();
        }

        void Spawn(WaveData wave)
        {
            foreach (var group in wave.groups)
            {
                var points = Spawner.For(group.enemy);
                for (int k = 0; k < group.count; k++)
                {
                    var point = points[k % points.Count];
                    var enemy = NetworkObjectPool.Get(group.enemy.prefab, point.position, point.rotation);
                    var health = enemy.GetComponent<Health>();
                    health.Died -= OnEnemyDied;   // 재사용된 인스턴스에 이전 구독이 남아 있으면 두 번 세지 않게
                    health.Died += OnEnemyDied;
                    enemy.Spawn(destroyWithScene: true);
                    alive.Value++;
                }
            }
        }

        void OnEnemyDied(Health health)
        {
            health.Died -= OnEnemyDied;
            alive.Value--;
        }
    }
}
