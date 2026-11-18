using UnityEngine;

namespace OYM
{
    /// Damped spring steps that stay stable at any frame rate: semi-implicit Euler, sub-stepped so that w*h <= 0.3.
    /// (A single explicit step blows up once w*dt approaches 2 - e.g. a 7.5 Hz recoil spring at 20 fps.)
    public static class Spring
    {
        static int Steps(float w, float dt) => Mathf.Clamp(Mathf.CeilToInt(w * dt / 0.3f), 1, 64);

        public static void Step(ref float x, ref float v, float target, float freq, float damping, float dt)
        {
            dt = Mathf.Clamp(dt, 0f, 0.1f);
            float w = 2f * Mathf.PI * freq; int n = Steps(w, dt); float h = dt / n;
            for (int i = 0; i < n; i++) { v += (w * w * (target - x) - 2f * damping * w * v) * h; x += v * h; }
        }

        public static void Step(ref Vector2 x, ref Vector2 v, Vector2 target, float freq, float damping, float dt)
        {
            dt = Mathf.Clamp(dt, 0f, 0.1f);
            float w = 2f * Mathf.PI * freq; int n = Steps(w, dt); float h = dt / n;
            for (int i = 0; i < n; i++) { v += (w * w * (target - x) - 2f * damping * w * v) * h; x += v * h; }
        }

        public static void Step(ref Vector3 x, ref Vector3 v, Vector3 target, float freq, float damping, float dt)
        {
            dt = Mathf.Clamp(dt, 0f, 0.1f);
            float w = 2f * Mathf.PI * freq; int n = Steps(w, dt); float h = dt / n;
            for (int i = 0; i < n; i++) { v += (w * w * (target - x) - 2f * damping * w * v) * h; x += v * h; }
        }
    }
}
