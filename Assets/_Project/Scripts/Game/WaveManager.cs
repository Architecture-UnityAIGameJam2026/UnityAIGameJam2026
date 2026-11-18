using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 웨이브 진행(서버). 두 플레이어가 스폰되면 시작해, 적을 전멸시킬 때마다 다음 웨이브로 넘어간다.
    /// 웨이브 사이(intermission) 동안 단말기가 켜져 파츠를 돌려받을 수 있다.
    /// 적은 NetworkObjectPool로 재사용한다 (첫 웨이브에서 만들어진 인스턴스를 다음 웨이브가 다시 쓴다).
    /// </summary>
    public class WaveManager : NetworkBehaviour
    {
        [SerializeField] WaveData[] waves;
        [SerializeField] float firstWaveDelay = 3f;
        [SerializeField] float intermission = 12f;

        [Header("이벤트 채널")]
        [SerializeField] GameEvent playersSpawned;
        [SerializeField] GameEvent waveCleared;
        [SerializeField] GameEvent allWavesCleared;
        [SerializeField] GameEvent roundReset;

        readonly NetworkVariable<int> waveIndex = new(-1);
        readonly NetworkVariable<int> alive = new();
        readonly NetworkVariable<bool> betweenWaves = new();
        readonly NetworkVariable<double> nextWaveAt = new();

        /// <summary>1부터 시작. 시작 전이면 0.</summary>
        public int WaveNumber => waveIndex.Value + 1;
        public int TotalWaves => waves.Length;
        public int Alive => alive.Value;
        public bool BetweenWaves => betweenWaves.Value;
        public bool AllCleared => betweenWaves.Value && WaveNumber == waves.Length;
        public float SecondsToNextWave => IsSpawned ? Mathf.Max(0f, (float)(nextWaveAt.Value - NetworkManager.ServerTime.Time)) : 0f;

        // 씬 오브젝트라 접속 전에 모든 피어에서 돈다: 적 스폰 메시지보다 먼저 풀 핸들러를 건다
        void Start() => RegisterEnemyPools();

        void RegisterEnemyPools()
        {
            if (waves == null) return;
            foreach (var wave in waves)
                if (wave != null && wave.groups != null)
                    foreach (var group in wave.groups)
                        if (group.enemy) NetworkObjectPool.Register(group.enemy.prefab);   // NavMeshAgent가 있어 미리 만들지 않는다
        }

        public override void OnNetworkSpawn()
        {
            RegisterEnemyPools();   // 모든 피어 (클라이언트도 풀에서 꺼내고 돌려놓는다)
            if (!IsServer) return;
            playersSpawned.Raised += Begin;
            roundReset.Raised += Stop;
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer) return;
            playersSpawned.Raised -= Begin;
            roundReset.Raised -= Stop;
        }

        /// <summary>서버: 진행 중인 웨이브를 멈추고 남은 적을 치운다 (게임 종료 / 재시작).</summary>
        public void Stop()
        {
            StopAllCoroutines();
            foreach (var enemy in FindObjectsByType<EnemyBase>(FindObjectsSortMode.None))
                if (enemy.IsSpawned) enemy.NetworkObject.Despawn();
            alive.Value = 0;
            waveIndex.Value = -1;
            betweenWaves.Value = false;
        }

        void Begin() => StartCoroutine(Run());

        IEnumerator Run()
        {
            yield return new WaitForSeconds(firstWaveDelay);
            for (int i = 0; i < waves.Length; i++)
            {
                betweenWaves.Value = false;
                waveIndex.Value = i;
                Debug.Log($"[OnYourMark] Wave {i + 1}/{waves.Length} started");
                yield return Spawn(waves[i]);
                yield return new WaitUntil(() => alive.Value == 0);

                betweenWaves.Value = true;
                waveCleared.Raise();
                if (i == waves.Length - 1) break;
                nextWaveAt.Value = NetworkManager.ServerTime.Time + intermission;
                yield return new WaitForSeconds(intermission);
            }
            allWavesCleared.Raise();
        }

        IEnumerator Spawn(WaveData wave)
        {
            // 먼저 전체 수를 올려 둬야, 다 나오기 전에 먼저 나온 적을 잡아도 클리어로 판정되지 않는다
            foreach (var group in wave.groups) alive.Value += group.count;

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
                    yield return new WaitForSeconds(wave.spawnInterval);
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
