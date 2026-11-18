using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace OnYourMark
{
    /// <summary>
    /// 라운드 결과(S7). 두 플레이어가 동시에 쓰러지면 게임 오버, 마지막 웨이브를 클리어하면 미션 클리어.
    /// 끝나면 웨이브를 멈추고 캐릭터를 디스폰한다. Host가 재시작하면 RoundReset을 Raise해
    /// 각 시스템이 자기 상태를 되돌리고, 다시 포지션 선택(S1)부터 시작한다.
    /// </summary>
    public class GameFlow : NetworkBehaviour
    {
        enum Result { Playing, GameOver, MissionClear, PartnerLeft }

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
            if (IsServer && result.Value == Result.Playing && EveryoneDown()) End(Result.GameOver);
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
            result.Value = Result.Playing;
            roundReset.Raise();
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
                Result.PartnerLeft => "PARTNER DISCONNECTED",
                _ => "GAME OVER"
            };
            title.color = clear ? new Color(0.5f, 0.95f, 0.6f) : new Color(1f, 0.4f, 0.4f);
            detail.text = result.Value switch
            {
                Result.MissionClear => $"All {waves.TotalWaves} waves cleared",
                Result.PartnerLeft => "Restart to choose roles and wait for a partner.",
                _ => $"Reached wave {reachedWave.Value} / {waves.TotalWaves}"
            };
            restartButton.interactable = IsServer;
            restartLabel.text = IsServer ? "RESTART" : "WAITING FOR HOST";
        }
    }
}
