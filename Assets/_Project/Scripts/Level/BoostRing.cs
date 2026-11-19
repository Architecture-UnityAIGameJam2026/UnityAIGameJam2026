using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 부스트 링. 통과하면 링의 정면 방향으로 가속하고 추진 게이지가 다시 찬다 (비행 경로 · 지름길).
    /// 링은 빛나는 원(LineRenderer)과 트리거 구 하나로 되어 있다.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class BoostRing : MonoBehaviour
    {
        [Min(1f)] public float radius = 2.4f;
        [Min(1f)] public float boostSpeed = 22f;
        [Tooltip("통과하면 이 지점에 정확히 떨어지는 궤적으로 날린다 (없으면 정면으로 가속)")]
        public Transform target;
        [Min(.3f)] public float flightTime = .9f;
        public Color color = new(.45f, 1f, .88f);
        LineRenderer ring, inner;
        float cooldownUntil, hitAt = -9f;
        static Material material;

        void Awake()
        {
            var col = GetComponent<SphereCollider>();
            col.isTrigger = true; col.radius = radius;
            ring = MakeCircle("Ring", radius, .22f);
            inner = MakeCircle("Inner", radius * .72f, .08f);
        }

        LineRenderer MakeCircle(string name, float r, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            if (!material)
            {
                var shader = Shader.Find("OYM/FX/AdditiveHDR");
                material = new Material(shader ? shader : Shader.Find("Sprites/Default"));
                if (material.HasProperty("_Intensity")) material.SetFloat("_Intensity", 6f);
            }
            lr.sharedMaterial = material;
            lr.useWorldSpace = false; lr.loop = true; lr.positionCount = 40; lr.widthMultiplier = width;
            lr.startColor = lr.endColor = color;
            for (int i = 0; i < 40; i++)
            {
                float a = i / 40f * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            }
            return lr;
        }

        void OnTriggerEnter(Collider other)
        {
            if (Time.time < cooldownUntil) return;
            var player = other.GetComponentInParent<Player>();
            if (!player || !player.IsSpawned || !player.IsOwner || player.Health.IsDead) return;
            var motor = player.Motor;
            if (!motor || !motor.enabled) return;
            cooldownUntil = Time.time + .5f;
            hitAt = Time.time;
            // 들어온 쪽과 상관없이 링이 가리키는 쪽으로 (뒤에서 들어오면 반대로)
            if (target) motor.Leap(target.position + Vector3.up * .1f, flightTime);   // 다음 갑판에 정확히 내려앉는다
            else
            {
                Vector3 dir = Vector3.Dot(motor.Velocity, transform.forward) >= 0f ? transform.forward : -transform.forward;
                motor.Launch(dir * boostSpeed * .7f + Vector3.up * 2.5f, .12f);
            }
            motor.RefillHover();
        }

        void Update()
        {
            float k = Mathf.Clamp01((Time.time - hitAt) / .5f);
            float spin = Time.time * 40f;
            inner.transform.localRotation = Quaternion.Euler(0, 0, spin);
            var c = Color.Lerp(Color.white, color, k);
            ring.startColor = ring.endColor = c;
            ring.widthMultiplier = .22f + (1f - k) * .25f + Mathf.Sin(Time.time * 6f) * .03f;
            transform.localScale = Vector3.one * (1f + (1f - k) * .15f);
        }
    }
}
