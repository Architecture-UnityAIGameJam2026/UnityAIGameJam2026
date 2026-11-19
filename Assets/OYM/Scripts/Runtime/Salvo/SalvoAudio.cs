using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OYM
{
    /// Sounds of the ORBIT FRAME E salvo (owned by OrbitSalvo).
    ///   lock tones   synthesized here: short sine blips with a little 3rd harmonic (electronic, not harsh), exact
    ///                pitches - seeker open (up-sweep), seek tick, lock (two-tone, higher per lock), full (triple), lost
    ///                (down-sweep), deny (low double buzz), ready (soft rising pair). 2D.
    ///   clips        Resources/OYM_Salvo, from the team's game sound collection: salvo_launch = MissileToAir1 (the user's
    ///                pick for the team's smart lock-on fire), salvo_pop = WU_Missile per missile, salvo_impact_a / _b =
    ///                AR / WU MissileDamage per blast (half 3D, heard far off).
    public sealed class SalvoAudio
    {
        const string Dir = "OYM_Salvo/";
        const int Rate = 44100;
        static AudioClip s_open, s_tick, s_lock, s_full, s_lost, s_deny, s_ready;
        static AudioClip s_launch, s_pop, s_blastA, s_blastB;
        static bool s_loaded;

        readonly GameObject _go, _blastGo;
        readonly AudioSource _ui;
        readonly AudioSource[] _body = new AudioSource[3];
        readonly List<AudioSource> _blasts = new List<AudioSource>(6);
        int _nextBody, _nextBlast;
        float _lastTick = -1f, _tone = 0.7f, _launch = 0.85f, _blast = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_loaded = false; }

        public SalvoAudio(Transform pilot)
        {
            Load();
            _go = new GameObject("Salvo audio");
            _go.transform.SetParent(pilot, false);
            _go.transform.localPosition = Vector3.up * 1.5f;
            _ui = Source(_go, 0f);
            _ui.priority = 40;
            for (int i = 0; i < _body.Length; i++) { _body[i] = Source(_go, 0.35f); _body[i].minDistance = 6f; _body[i].maxDistance = 300f; }
            // blasts happen far from the pilot: their own sources, moved to each blast
            _blastGo = new GameObject("Salvo blasts (audio)");
            SceneManager.MoveGameObjectToScene(_blastGo, pilot.gameObject.scene);
            for (int i = 0; i < 6; i++)
            {
                var g = new GameObject("blast " + i);
                g.transform.SetParent(_blastGo.transform, false);
                var s = Source(g, 0.6f);
                s.minDistance = 20f; s.maxDistance = 800f;
                _blasts.Add(s);
            }
        }

        public void Dispose()
        {
            if (_blastGo) Object.Destroy(_blastGo);
        }

        static AudioSource Source(GameObject g, float spatial)
        {
            var s = g.AddComponent<AudioSource>();
            s.playOnAwake = false; s.loop = false; s.spatialBlend = spatial; s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            return s;
        }

        public void Volumes(float tone, float launch, float blast) { _tone = tone; _launch = launch; _blast = blast; }

        // ------------------------------------------------------------------------------------------------ events
        public void Open() => Ui(s_open, 1f, 0.8f);
        public void Seek()
        {
            if (Time.unscaledTime - _lastTick < 0.07f) return;              // a seeker hopping between two targets: no buzz
            _lastTick = Time.unscaledTime;
            Ui(s_tick, 1f, 0.8f);
        }
        public void Lock(int number, bool full) => Ui(full ? s_full : s_lock, 1f + 0.06f * Mathf.Max(0, number - 1), 1f);
        public void Lost() => Ui(s_lost, 1f, 0.8f);
        public void Deny() => Ui(s_deny, 1f, 1f);
        public void Ready() => Ui(s_ready, 1f, 0.75f);

        public void Launch()
        {
            if (!s_launch) return;
            var s = Body();
            s.pitch = 1f;
            s.PlayOneShot(s_launch, _launch * 0.9f);
        }

        public void Pop(Vector3 at, int i)
        {
            if (!s_pop) return;
            var s = Body();
            s.pitch = 1.04f + 0.1f * Mathf.Repeat(i * 0.618f, 1f);
            s.PlayOneShot(s_pop, _launch * 0.6f);
        }

        public void Blast(Vector3 at)
        {
            var c = (_nextBlast & 1) == 0 ? s_blastA : s_blastB;
            if (!c) c = s_blastA ? s_blastA : s_blastB;
            if (!c || _blasts.Count == 0) return;
            var s = _blasts[_nextBlast++ % _blasts.Count];
            if (!s) return;
            s.transform.position = at;
            s.pitch = 0.92f + 0.16f * Random.value;
            s.PlayOneShot(c, _blast);
        }

        AudioSource Body() => _body[_nextBody++ % _body.Length];

        void Ui(AudioClip c, float pitch, float vol)
        {
            if (!c || !_ui) return;
            _ui.pitch = pitch;
            _ui.PlayOneShot(c, _tone * vol);
        }

        // ------------------------------------------------------------------------------------------------ clips
        static void Load()
        {
            if (s_loaded && s_tick) return;
            s_loaded = true;
            s_launch = Resources.Load<AudioClip>(Dir + "salvo_launch");
            s_pop = Resources.Load<AudioClip>(Dir + "salvo_pop");
            s_blastA = Resources.Load<AudioClip>(Dir + "salvo_impact_a");
            s_blastB = Resources.Load<AudioClip>(Dir + "salvo_impact_b");
            if (!s_launch || !s_pop || !s_blastA) Debug.LogWarning("[OYM-SALVO] Resources/OYM_Salvo clips missing: launch / blast sounds off");

            s_open = Synth("open", 0.085f, t => Sweep(t, 0f, 0.08f, 650f, 1450f, 0.16f));
            s_tick = Synth("tick", 0.03f, t => Blip(t, 0f, 0.026f, 1250f, 0.22f));
            s_lock = Synth("lock", 0.12f, t => Blip(t, 0f, 0.045f, 1900f, 0.3f) + Blip(t, 0.06f, 0.055f, 2500f, 0.3f));
            s_full = Synth("full", 0.24f, t => Blip(t, 0f, 0.035f, 2700f, 0.28f) + Blip(t, 0.05f, 0.035f, 2700f, 0.28f) + Blip(t, 0.1f, 0.13f, 2700f, 0.28f));
            s_lost = Synth("lost", 0.1f, t => Sweep(t, 0f, 0.09f, 1500f, 700f, 0.2f));
            s_deny = Synth("deny", 0.17f, t => Blip(t, 0f, 0.055f, 330f, 0.34f) + Blip(t, 0.1f, 0.055f, 330f, 0.34f));
            s_ready = Synth("ready", 0.13f, t => Blip(t, 0f, 0.04f, 1100f, 0.2f) + Blip(t, 0.055f, 0.07f, 1650f, 0.2f));
        }

        static AudioClip Synth(string name, float seconds, System.Func<float, float> f)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Rate * seconds));
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(f(i / (float)Rate), -1f, 1f);
            var c = AudioClip.Create("salvo " + name, n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        /// a blip: 2 ms attack, 12 ms release
        static float Blip(float t, float start, float len, float hz, float amp)
        {
            float x = t - start;
            if (x < 0f || x > len) return 0f;
            float env = Mathf.Min(1f, x / 0.002f) * Mathf.Min(1f, (len - x) / 0.012f);
            float ph = 2f * Mathf.PI * hz * x;
            return amp * env * (Mathf.Sin(ph) + 0.22f * Mathf.Sin(3f * ph));
        }

        /// a linear frequency sweep f0 -> f1 over len
        static float Sweep(float t, float start, float len, float f0, float f1, float amp)
        {
            float x = t - start;
            if (x < 0f || x > len) return 0f;
            float env = Mathf.Min(1f, x / 0.003f) * Mathf.Min(1f, (len - x) / 0.015f);
            float ph = 2f * Mathf.PI * (f0 * x + (f1 - f0) * x * x / (2f * len));
            return amp * env * (Mathf.Sin(ph) + 0.18f * Mathf.Sin(3f * ph));
        }
    }
}
