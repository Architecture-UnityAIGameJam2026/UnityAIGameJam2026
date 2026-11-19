using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 발사대. 밟은 플레이어를 target에 정확히 떨어지는 포물선으로 쏘아 올린다 (조작은 잠깐만 막는다).
    /// 이동은 소유자가 계산하므로, 밟은 캐릭터의 소유자 화면(AI 동료는 서버)에서만 발사한다.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class JumpPad : MonoBehaviour
    {
        public Transform target;
        [Min(.3f)] public float flightTime = 1.1f;
        [Tooltip("빛나는 표시 (맥동)")] public Renderer glow;
        float cooldownUntil;
        MaterialPropertyBlock block;

        void Awake() { GetComponent<Collider>().isTrigger = true; block = new MaterialPropertyBlock(); }

        void OnTriggerEnter(Collider other)
        {
            if (!target || Time.time < cooldownUntil) return;
            var player = other.GetComponentInParent<Player>();
            if (!player || !player.IsSpawned || !player.IsOwner || player.Health.IsDead) return;
            var motor = player.Motor;
            if (!motor || !motor.enabled) return;
            cooldownUntil = Time.time + .4f;
            motor.Leap(target.position + Vector3.up * .1f, flightTime);
            motor.RefillHover();
        }

        void Update()
        {
            if (!glow) return;
            glow.GetPropertyBlock(block);
            float pulse = 1.2f + Mathf.Sin(Time.time * 5f) * .6f;
            // 원판이 화면을 넓게 덮으면 블룸이 화면 전체로 번진다: 바위보다 확실히 밝은 정도로만 (노출 EV 11.9 기준)
            block.SetColor("_EmissiveColor", new Color(.3f, 1f, .85f) * pulse * 150f);
            glow.SetPropertyBlock(block);
        }

        void OnDrawGizmos()
        {
            if (!target) return;
            Gizmos.color = Color.cyan;
            Vector3 a = transform.position, b = target.position;
            Vector3 v = (b - a) / flightTime - .5f * -22f * flightTime * Vector3.up;
            Vector3 prev = a;
            for (int i = 1; i <= 20; i++)
            {
                float t = flightTime * i / 20f;
                var p = a + v * t + .5f * -22f * t * t * Vector3.up;
                Gizmos.DrawLine(prev, p); prev = p;
            }
        }
    }
}
