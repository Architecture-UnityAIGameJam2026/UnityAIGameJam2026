using UnityEngine;
using UnityEngine.UI;

namespace OnYourMark
{
    /// <summary>
    /// 게임 진행 · UI 소리 (내 화면 전용): 웨이브 시작 · 클리어, 교환 제안 받음, 결과 징글, 버튼 클릭.
    /// 씬에 하나 (CrossHud 프리팹).
    /// </summary>
    public class GameSounds : MonoBehaviour
    {
        [SerializeField] WaveManager waves;
        [SerializeField] PartExchange exchange;
        [SerializeField] GameFlow flow;

        int lastWave;
        bool wasBetween, hadOffer, wasEnded;

        void Start()
        {
            // 같은 씬의 진행 오브젝트를 찾아 둔다 (프리팹끼리는 직접 참조할 수 없으므로)
            if (!waves) waves = FindFirstObjectByType<WaveManager>();
            if (!exchange) exchange = FindFirstObjectByType<PartExchange>();
            if (!flow) flow = FindFirstObjectByType<GameFlow>();

            // 씬의 모든 버튼(꺼져 있는 것 포함)에 클릭음
            foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                b.onClick.AddListener(() => Sfx.Play(SoundId.UiClick));
        }

        void Update()
        {
            if (waves && waves.IsSpawned)
            {
                int wave = waves.WaveNumber;
                bool between = waves.BetweenWaves;
                if ((wave > lastWave && wave > 0) || (between && !wasBetween && wave > 0)) Sfx.Play(SoundId.WaveAlert);
                lastWave = wave; wasBetween = between;
            }

            if (exchange && exchange.IsSpawned)
            {
                bool offer = exchange.HasOffer && Player.Local && exchange.OfferFrom != Player.Local.OwnerClientId;
                if (offer && !hadOffer) Sfx.Play(SoundId.TradeOffer);
                hadOffer = offer;
            }

            if (flow && flow.IsSpawned)
            {
                bool ended = flow.Ended;
                if (ended && !wasEnded) Sfx.Play(SoundId.ResultJingle);
                wasEnded = ended;
            }
        }
    }
}
