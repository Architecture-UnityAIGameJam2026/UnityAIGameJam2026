using Garam.Reticles;
using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 테스트용 자동 락온. 화면 중앙에 가장 가까운 ReticleTarget(적·더미)을 골라 LockOnReticle에 넘긴다.
    /// 실제 타겟팅 규칙이 정해지면 이 컴포넌트를 교체하면 된다.
    /// </summary>
    public class ReticleAutoLock : MonoBehaviour
    {
        [SerializeField] LockOnReticle reticle;
        [Tooltip("이 각도(도) 안에 들어온 적만 새로 락온한다.")]
        [SerializeField] float acquireAngle = 12f;
        [Tooltip("락온 중인 적이 이 각도를 벗어나면 해제한다.")]
        [SerializeField] float releaseAngle = 20f;
        [SerializeField] float maxDistance = 80f;
        [SerializeField] float scanInterval = 0.1f;

        ReticleTarget[] candidates = System.Array.Empty<ReticleTarget>();
        float nextScan;

        void Update()
        {
            if (!reticle) return;
            var cam = Camera.main;
            if (!cam) return;

            reticle.worldCamera = cam;
            if (!reticle.distanceOrigin) reticle.distanceOrigin = LocalPlayer();

            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + scanInterval;
                candidates = FindObjectsByType<ReticleTarget>(FindObjectsSortMode.None);
            }

            var current = reticle.Target;
            if (current && Score(cam, current, releaseAngle) < 0f) { reticle.ReleaseTarget(); current = null; }

            ReticleTarget best = null;
            float bestScore = float.MaxValue;
            foreach (var t in candidates)
            {
                if (!t || !t.isActiveAndEnabled) continue;
                float s = Score(cam, t, acquireAngle);
                if (s >= 0f && s < bestScore) { bestScore = s; best = t; }
            }

            if (best && best != current && (!current || bestScore + 2f < Score(cam, current, releaseAngle)))
                reticle.LockOn(best);
        }

        /// <summary>카메라 정면과의 각도. 범위 밖이면 -1.</summary>
        float Score(Camera cam, ReticleTarget t, float maxAngle)
        {
            Vector3 to = t.AimPosition - cam.transform.position;
            float dist = to.magnitude;
            if (dist > maxDistance || dist < 0.01f) return -1f;
            float angle = Vector3.Angle(cam.transform.forward, to);
            return angle <= maxAngle ? angle : -1f;
        }

        static Transform LocalPlayer()
        {
            var nm = NetworkManager.Singleton;
            var obj = nm && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
            return obj ? obj.transform : null;
        }
    }
}
