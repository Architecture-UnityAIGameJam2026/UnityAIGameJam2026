using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 떠다니는 잔해 발판. 위치는 서버 시각으로 계산해 두 화면이 같은 자리를 본다 (동기화 메시지 없음).
    /// 위에 선 캐릭터는 PlayerMotor가 Delta만큼 함께 옮긴다.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class MovingPlatform : MonoBehaviour
    {
        public Vector3 travel = new(0, 0, 8);
        [Min(1f)] public float period = 7f;
        public float phase;
        public float spinDegrees;
        Vector3 origin;
        Quaternion baseRotation;
        public Vector3 Delta { get; private set; }

        void Awake() { origin = transform.position; baseRotation = transform.rotation; }

        static double Clock => NetworkManager.Singleton && NetworkManager.Singleton.IsListening
            ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;

        void Update()
        {
            float t = (float)(Clock % 100000.0);
            float k = Mathf.Sin((t / period + phase) * Mathf.PI * 2f) * .5f + .5f;
            var next = origin + travel * Mathf.SmoothStep(0f, 1f, k);
            Delta = next - transform.position;
            transform.SetPositionAndRotation(next, baseRotation * Quaternion.Euler(0, Mathf.Sin(t / period * Mathf.PI * 2f) * spinDegrees, 0));
        }

        /// <summary>이 콜라이더가 움직이는 발판의 일부면 그 발판.</summary>
        public static MovingPlatform Of(Collider c) => c ? c.GetComponentInParent<MovingPlatform>() : null;
    }
}
