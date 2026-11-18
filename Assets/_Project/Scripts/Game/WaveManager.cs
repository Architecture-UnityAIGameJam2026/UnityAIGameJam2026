using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 웨이브 진행(서버). 두 플레이어가 스폰되면 시작해, 적을 전멸시킬 때마다 다음 웨이브로 넘어간다.
    /// 웨이브 사이(intermission) 동안 단말기가 켜져 파츠를 돌려받을 수 있다.
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

        public override void OnNetworkSpawn()
        {
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
                Spawn(waves[i]);
                yield return new WaitUntil(() => alive.Value == 0);

                betweenWaves.Value = true;
                waveCleared.Raise();
                if (i == waves.Length - 1) break;
                nextWaveAt.Value = NetworkManager.ServerTime.Time + intermission;
                yield return new WaitForSeconds(intermission);
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
                    var enemy = Instantiate(group.enemy.prefab, point.position, point.rotation);
                    enemy.GetComponent<Health>().Died += _ => alive.Value--;
                    enemy.Spawn(destroyWithScene: true);
                    alive.Value++;
                }
            }
        }
    }
}
