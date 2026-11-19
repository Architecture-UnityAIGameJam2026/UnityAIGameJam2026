using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 부스트 링을 통과한 파일럿을 연결된 착지 지점으로 보낸다.
    /// 링은 빛나는 원(LineRenderer)과 트리거 구 하나로 되어 있다.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class BoostRing : ArenaTransit
    {
        [Min(1f)] public float radius = 2.4f;
        [Min(1f)] public float boostSpeed = 22f;
        [Tooltip("통과하면 이 지점에 정확히 떨어지는 궤적으로 날린다 (없으면 정면으로 가속)")]
        public Transform target;
        [Min(.3f)] public float flightTime = .9f;
        public Color color = new(.45f, 1f, .88f);
        LineRenderer ring, inner;
        float hitAt = -9f;
        static Material material;

        protected override void Awake()
        {
            base.Awake();
            var col = GetComponent<SphereCollider>();
            col.radius = radius;
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

        protected override void Ride(OYM.ZeroGFlight pilot)
        {
            hitAt = Time.time;
            var direction = Vector3.Dot(pilot.Velocity, transform.forward) >= 0 ? transform.forward : -transform.forward;
            var landing = target ? target.position : pilot.transform.position + direction * (boostSpeed * flightTime);
            pilot.RideArenaDevice(landing + Vector3.up * .1f, flightTime, true);
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
