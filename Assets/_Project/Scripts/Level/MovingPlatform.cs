using OYM;
using OYM.Cine;
using UnityEngine;

namespace OnYourMark
{
    [DefaultExecutionOrder(-200)]
    public class MovingPlatform : MonoBehaviour
    {
        public Vector3 travel;
        [Min(1f)] public float period = 7f;
        public float phase;
        public float spinDegrees;

        Vector3 origin;
        Quaternion restRotation;
        Collider surface;
        float startTime;

        void Awake()
        {
            origin = transform.localPosition;
            restRotation = transform.localRotation;
            surface = GetComponent<Collider>();
        }

        void OnEnable()
        {
            StartClock();
            ArrivalCutscene.Finished += StartClock;
        }

        void OnDisable() => ArrivalCutscene.Finished -= StartClock;
        void StartClock() => startTime = Time.time;

        void Update()
        {
            float angle = (Time.time - startTime) / period * Mathf.PI * 2f;
            float offset = Mathf.Sin(angle + phase * Mathf.PI * 2f) * .5f + .5f;
            var pilot = Rider(surface);
            var previous = transform.position;

            transform.SetLocalPositionAndRotation(origin + travel * Mathf.SmoothStep(0f, 1f, offset),
                restRotation * Quaternion.Euler(0f, Mathf.Sin(angle) * spinDegrees, 0f));
            if (pilot) pilot.Move(transform.position - previous);
        }

        internal static ZeroGFlight Rider(Collider surface)
        {
            var camera = FlightCamera.Main;
            var pilot = camera && camera.target ? camera.target.GetComponent<ZeroGFlight>() : null;
            return surface && pilot && pilot.isActiveAndEnabled && !pilot.scripted &&
                   surface.Raycast(new Ray(pilot.transform.position + Vector3.up * .4f, Vector3.down), out _, .7f)
                ? pilot : null;
        }
    }
}
