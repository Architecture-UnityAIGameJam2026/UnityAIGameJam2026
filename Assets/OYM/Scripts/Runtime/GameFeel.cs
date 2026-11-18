using UnityEngine;

namespace OYM
{
    /// Hit-stop (short time-scale dip) for attacks. Durations are in real (unscaled) seconds and are measured with
    /// RealDt, which also holds while Time.captureFramerate drives the clock during recordings.
    [DefaultExecutionOrder(-1000)]
    public class GameFeel : MonoBehaviour
    {
        static GameFeel _i;
        float _left, _scale = 1f;

        /// Real seconds of the last frame (deltaTime with the time scale taken out).
        public static float RealDt => Time.timeScale > 1e-4f ? Time.deltaTime / Time.timeScale : Time.unscaledDeltaTime;

        static GameFeel I
        {
            get
            {
                if (!_i) { var go = new GameObject("GameFeel"); _i = go.AddComponent<GameFeel>(); }
                return _i;
            }
        }

        /// Freeze-frame: time runs at `scale` for `seconds` of real time (the stronger / longer one wins if they overlap).
        public static void HitStop(float seconds, float scale = 0.03f)
        {
            var g = I;
            g._left = Mathf.Max(g._left, seconds);
            g._scale = g._left > 0f && Time.timeScale < 0.999f ? Mathf.Min(g._scale, scale) : scale;
            Time.timeScale = g._scale;
        }

        public static bool Stopped => _i && _i._left > 0f;

        void Update()
        {
            if (_left <= 0f) return;
            _left -= RealDt;
            if (_left <= 0f) { _left = 0f; _scale = 1f; Time.timeScale = 1f; }
            else Time.timeScale = _scale;
        }

        void OnDisable() { Time.timeScale = 1f; }
    }
}
