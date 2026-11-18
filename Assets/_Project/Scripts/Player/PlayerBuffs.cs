using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>지원으로 받는 시간제 효과. 끝나는 시각을 서버 시간으로 동기화한다.</summary>
    public class PlayerBuffs : NetworkBehaviour
    {
        [SerializeField] GameObject speedEffect;

        readonly NetworkVariable<double> speedUntil = new();
        readonly NetworkVariable<float> speedMultiplier = new(1f);
        readonly NetworkVariable<double> weakPointsUntil = new();

        double Now => IsSpawned ? NetworkManager.ServerTime.Time : 0;

        public float SpeedMultiplier => Now < speedUntil.Value ? speedMultiplier.Value : 1f;
        public bool RevealsWeakPoints => Now < weakPointsUntil.Value;

        // 서버 전용
        public void Boost(float multiplier, float duration)
        {
            speedMultiplier.Value = multiplier;
            speedUntil.Value = Now + duration;
        }

        public void RevealWeakPoints(float duration) => weakPointsUntil.Value = Now + duration;

        void Update() => speedEffect.SetActive(SpeedMultiplier > 1f);
    }
}
