using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace OnYourMark
{
    /// <summary>The server restores the mission checkpoint after a double down and returns both pilots to the title after the final strike.</summary>
    public class GameFlow : NetworkBehaviour
    {
        enum Result { Playing, GameOver, MissionClear }

        [SerializeField] WaveManager waves;

        [Header("이벤트 채널")]
        [SerializeField] GameEvent allWavesCleared;
        [SerializeField] GameEvent roundReset;

        [Header("결과 화면")]
        [SerializeField] GameObject panel;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text detail;
        [SerializeField] Button restartButton;
        [SerializeField] TMP_Text restartLabel;

        readonly NetworkVariable<Result> result = new();
        readonly NetworkVariable<int> reachedWave = new();

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            result.Value = Result.Playing;
            allWavesCleared.Raised += OnAllWavesCleared;
        }

        public override void OnNetworkDespawn()
        {
            panel.SetActive(false);
            if (!IsServer) return;
            allWavesCleared.Raised -= OnAllWavesCleared;
        }

        void OnAllWavesCleared() => End(Result.MissionClear);

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer && result.Value == Result.Playing && EveryoneDown())
            {
                if (waves.HasMission) waves.RestoreCheckpoint();
                else End(Result.GameOver);
            }
            ShowResult();
        }

        static bool EveryoneDown()
        {
            if (Player.All.Count < 2) return false;
            foreach (var p in Player.All) if (!p.Health.IsDead) return false;
            return true;
        }

        void End(Result r)
        {
            reachedWave.Value = waves.WaveNumber;
            result.Value = r;
            Debug.Log($"[OnYourMark] Round ended: {r} at wave {waves.WaveNumber}");
            waves.Stop();
            foreach (var p in new System.Collections.Generic.List<Player>(Player.All)) p.NetworkObject.Despawn();
        }

        /// <summary>결과 화면의 RESTART 버튼 (Host만 누를 수 있음).</summary>
        public void Restart()
        {
            if (!IsServer || result.Value == Result.Playing) return;   // 끝난 판에서만
            if (waves.HasMission && result.Value == Result.MissionClear) { ReturnToTitleRpc(); return; }
            result.Value = Result.Playing;
            roundReset.Raise();
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ReturnToTitleRpc() => StartCoroutine(LeaveArena());

        System.Collections.IEnumerator LeaveArena()
        {
            // Give the host transport a tick to deliver the transition before shutting it down.
            yield return new WaitForSecondsRealtime(IsServer ? .35f : .1f);
            if (TitleMatch.Active) TitleMatch.Active.Close();
        }

        void ShowResult()
        {
            bool show = result.Value != Result.Playing;
            if (panel.activeSelf != show) panel.SetActive(show);
            if (!show) return;

            bool clear = result.Value == Result.MissionClear;
            title.text = result.Value switch
            {
                Result.MissionClear => "MISSION CLEAR",
                _ => "GAME OVER"
            };
            title.color = clear ? new Color(0.5f, 0.95f, 0.6f) : new Color(1f, 0.4f, 0.4f);
            detail.text = result.Value switch
            {
                Result.MissionClear => waves.HasMission ? "Upper deck secured" : $"All {waves.TotalWaves} waves cleared",
                _ => $"Reached wave {reachedWave.Value} / {waves.TotalWaves}"
            };
            restartButton.interactable = IsServer;
            restartLabel.text = IsServer ? (clear && waves.HasMission ? "RETURN TO TITLE" : "RESTART") : "WAITING FOR HOST";
        }
    }
}
