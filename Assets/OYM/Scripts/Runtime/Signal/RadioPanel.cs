using UnityEngine;
using UnityEngine.UIElements;

namespace OYM.Signal
{
    /// Radio line on screen (bottom left, clear of the cine subtitles and letterbox): a signal meter driven by the
    /// actual voice level, the callsign in the speaker's colour, the Korean line typed on, the English line under it.
    public class RadioPanel : MonoBehaviour
    {
        public float charsPerSecond = 42f, fadeIn = 0.12f, fadeOut = 0.45f, linger = 1.1f;

        UIDocument _doc;
        VisualElement _box, _accent;
        readonly VisualElement[] _bars = new VisualElement[6];
        Label _call, _ko, _en;
        string _full = "";
        float _typed, _alpha, _hideAt = -1f, _level;
        AudioSource _src;
        readonly float[] _buf = new float[256];

        void Awake()
        {
            _doc = SignalUI.Document(transform, "radio panel", 2f);
            var root = _doc.rootVisualElement;
            _box = new VisualElement { pickingMode = PickingMode.Ignore };
            _box.style.position = Position.Absolute;
            _box.style.left = 64; _box.style.bottom = 168; _box.style.width = 760;
            _box.style.flexDirection = FlexDirection.Row;
            _box.style.opacity = 0f;
            _accent = new VisualElement { pickingMode = PickingMode.Ignore };
            _accent.style.width = 3; _accent.style.marginRight = 14; _accent.style.alignSelf = Align.Stretch;
            _box.Add(_accent);

            var col = new VisualElement { pickingMode = PickingMode.Ignore };
            col.style.flexDirection = FlexDirection.Column; col.style.flexShrink = 1;
            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.style.flexDirection = FlexDirection.Row; head.style.alignItems = Align.FlexEnd; head.style.marginBottom = 6;
            var meter = new VisualElement { pickingMode = PickingMode.Ignore };
            meter.style.flexDirection = FlexDirection.Row; meter.style.alignItems = Align.FlexEnd; meter.style.height = 16; meter.style.marginRight = 10;
            for (int i = 0; i < _bars.Length; i++)
            {
                var b = new VisualElement { pickingMode = PickingMode.Ignore };
                b.style.width = 3; b.style.height = 3; b.style.marginRight = 2;
                _bars[i] = b; meter.Add(b);
            }
            head.Add(meter);
            _call = SignalUI.Text(17f, Color.white, 5, true);
            head.Add(_call);
            col.Add(head);
            _ko = SignalUI.Text(27f, new Color(0.95f, 0.96f, 0.97f), 1);
            _ko.style.whiteSpace = WhiteSpace.Normal; _ko.style.maxWidth = 740;
            col.Add(_ko);
            _en = SignalUI.Text(16f, new Color(1f, 1f, 1f, 0.55f), 1);
            _en.style.whiteSpace = WhiteSpace.Normal; _en.style.maxWidth = 740; _en.style.marginTop = 3;
            col.Add(_en);
            _box.Add(col);
            root.Add(_box);
        }

        public void Show(string callsign, Color accent, string korean, string english, float seconds, AudioSource src)
        {
            _call.text = callsign;
            _call.style.color = accent;
            _accent.style.backgroundColor = new Color(accent.r, accent.g, accent.b, 0.9f);
            foreach (var b in _bars) b.style.backgroundColor = accent;
            _full = korean ?? "";
            _typed = 0f;
            _ko.text = "";
            _en.text = english ?? "";
            _src = src;
            _hideAt = Time.unscaledTime + Mathf.Max(1.2f, seconds) + linger;
        }

        public void Hide() { _hideAt = Time.unscaledTime; }
        public bool Showing => _hideAt > Time.unscaledTime;

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            bool on = Time.unscaledTime < _hideAt;
            _alpha = Mathf.MoveTowards(_alpha, on ? 1f : 0f, dt / (on ? fadeIn : fadeOut));
            _box.style.opacity = _alpha;
            if (_alpha <= 0f) return;
            if (_typed < _full.Length)
            {
                _typed = Mathf.Min(_full.Length, _typed + charsPerSecond * dt);
                _ko.text = _full.Substring(0, (int)_typed);
            }
            // signal meter: the voice's level (the clips are radio-processed, so it reads as a transmission)
            float rms = 0f;
            if (_src && _src.isPlaying)
            {
                _src.GetOutputData(_buf, 0);
                for (int i = 0; i < _buf.Length; i++) rms += _buf[i] * _buf[i];
                rms = Mathf.Sqrt(rms / _buf.Length);
            }
            _level = Mathf.Lerp(_level, Mathf.Clamp01(rms * 7f), 1f - Mathf.Exp(-18f * dt));
            float t = Time.unscaledTime;
            for (int i = 0; i < _bars.Length; i++)
            {
                float shape = 0.55f + 0.45f * Mathf.PerlinNoise(t * 9f, i * 1.7f);
                float h = 3f + 13f * Mathf.Clamp01(_level * shape * (0.6f + 0.1f * i));
                _bars[i].style.height = h;
            }
        }
    }
}
