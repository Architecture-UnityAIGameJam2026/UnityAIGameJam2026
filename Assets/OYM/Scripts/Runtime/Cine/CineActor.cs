using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace OYM.Cine
{
    /// Cutscene puppet for a play-test pilot. Begin() takes the character away from its ZeroGFlight controller (disabled)
    /// and its Animator controller (swapped for an own PlayableGraph), End() hands both back.
    /// While it is on, the director drives it:
    ///   SetBody(root, yaw, tilt)   root position (feet), body yaw, and a lean around the pivot ~1 m up
    ///                              (tilt.x = pitch, + = nose down; tilt.y = roll), same pivot as ZeroGFlight
    ///   Play("DashLoop", fade)     crossfade to a clip of its own controller (name match: Hover, DashLoop, DashStop, Idle...)
    ///   LookAt(point / transform)  neck + head turn toward a point after the animation (clamped, damped)
    ///   Talk(on)                   a little head motion while its line plays
    [DefaultExecutionOrder(-30)]
    public class CineActor : MonoBehaviour
    {
        public string displayName;
        public Animator animator;
        public JetpackFX jetpack;
        public ZeroGFlight flight;
        public Color accent = Color.white;
        [Header("Look")]
        public float maxYaw = 70f, maxPitch = 35f, lookSharpness = 5f;
        [Range(0f, 1f)] public float neckShare = 0.4f;

        bool _on;
        RuntimeAnimatorController _ctl;
        PlayableGraph _graph;
        AnimationMixerPlayable _mix;
        AnimationClipPlayable[] _clips;
        AnimationClip[] _src;
        float[] _w;
        int _cur = -1;
        float _fade = 0.25f;
        Transform _pivot, _head, _neck;
        float _pivotH = 1f;
        Vector3 _lookPt; Transform _lookT; float _lookW, _lookWT;
        Quaternion _lookCur = Quaternion.identity;
        bool _talk; float _talkW, _seed;

        public bool On => _on;
        public Vector3 HeadPosition => _head ? _head.position : transform.position + Vector3.up * 1.6f;
        public Transform Head => _head ? _head : transform;
        public Transform Pivot => _pivot;

        public static CineActor For(ZeroGFlight f, string name)
        {
            var a = f.GetComponent<CineActor>();
            if (!a) a = f.gameObject.AddComponent<CineActor>();
            a.flight = f; a.displayName = name;
            a.Init();
            return a;
        }

        void Init()
        {
            if (!flight) flight = GetComponent<ZeroGFlight>();
            if (!animator) animator = flight && flight.animator ? flight.animator : GetComponentInChildren<Animator>();
            if (!jetpack) jetpack = flight && flight.jetpack ? flight.jetpack : GetComponentInChildren<JetpackFX>();
            if (!_pivot) _pivot = transform.Find("LeanPivot");
            if (flight) _pivotH = flight.pivotHeight;
            if (animator && animator.isHuman)
            {
                _head = animator.GetBoneTransform(HumanBodyBones.Head);
                _neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            }
            _seed = (name.GetHashCode() & 1023) * 0.013f;
        }

        // ------------------------------------------------------------------------------------------------ take over / hand back
        public void Begin()
        {
            if (_on) return;
            Init();
            if (flight) { flight.ResetMotion(); flight.scriptedInput = default; flight.enabled = false; }
            if (animator)
            {
                _ctl = animator.runtimeAnimatorController;
                _src = _ctl ? _ctl.animationClips : new AnimationClip[0];
                animator.runtimeAnimatorController = null;           // the graph is the only animation source
                _graph = PlayableGraph.Create(name + " (cine)");
                _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(_graph, "pose", animator);
                _mix = AnimationMixerPlayable.Create(_graph, _src.Length);
                _clips = new AnimationClipPlayable[_src.Length];
                _w = new float[_src.Length];
                for (int i = 0; i < _src.Length; i++)
                {
                    _clips[i] = AnimationClipPlayable.Create(_graph, _src[i]);
                    _clips[i].SetApplyFootIK(false);
                    _graph.Connect(_clips[i], 0, _mix, i);
                    _mix.SetInputWeight(i, 0f);
                }
                output.SetSourcePlayable(_mix);
                _cur = -1;
            }
            _on = true;
            Play("Hover", 0f);
        }

        public void End()
        {
            if (!_on) return;
            _on = false;
            if (_graph.IsValid()) _graph.Destroy();
            if (animator && _ctl) animator.runtimeAnimatorController = _ctl;
            if (_pivot) { _pivot.localPosition = Vector3.up * _pivotH; _pivot.localRotation = Quaternion.identity; }
            if (flight) { flight.ResetMotion(); flight.scriptedInput = default; flight.enabled = true; }
            _lookT = null; _lookW = _lookWT = 0f; _talk = false;
        }

        void OnDestroy() { if (_graph.IsValid()) _graph.Destroy(); }

        // ------------------------------------------------------------------------------------------------ drive
        public void SetBody(Vector3 root, float yaw, Vector2 tilt)
        {
            transform.SetPositionAndRotation(root, Quaternion.Euler(0f, yaw, 0f));
            if (!_pivot) return;
            float ang = tilt.magnitude;
            _pivot.localPosition = Vector3.up * _pivotH;
            _pivot.localRotation = ang > 1e-3f ? Quaternion.AngleAxis(ang, new Vector3(tilt.x, 0f, tilt.y) / ang) : Quaternion.identity;
        }

        int Find(string clip)
        {
            if (_src == null) return -1;
            for (int i = 0; i < _src.Length; i++) if (_src[i] && _src[i].name.EndsWith(clip)) return i;
            for (int i = 0; i < _src.Length; i++) if (_src[i] && _src[i].name.Contains(clip)) return i;
            return -1;
        }

        public bool Has(string clip) => Find(clip) >= 0;

        /// Crossfade to a clip (first one that exists of a "A|B|C" list), restarted from `from` seconds.
        public void Play(string clips, float fade, float from = 0f, float speed = 1f)
        {
            if (!_on || _clips == null) return;
            int i = -1;
            foreach (var c in clips.Split('|')) { i = Find(c); if (i >= 0) break; }
            if (i < 0 || i == _cur) return;
            _cur = i; _fade = Mathf.Max(0.001f, fade);
            _clips[i].SetTime(from); _clips[i].SetTime(from);
            _clips[i].SetSpeed(speed);
            if (fade <= 0f) for (int k = 0; k < _w.Length; k++) _w[k] = k == i ? 1f : 0f;
        }

        public void LookAt(Transform t, float weight = 1f) { _lookT = t; _lookWT = weight; }
        public void LookAt(Vector3 p, float weight = 1f) { _lookT = null; _lookPt = p; _lookWT = weight; }
        public void LookAway() { _lookWT = 0f; }
        public void Talk(bool on) { _talk = on; }

        void Update()
        {
            if (!_on || !_graph.IsValid()) return;
            float dt = Time.deltaTime;
            for (int k = 0; k < _w.Length; k++)
            {
                _w[k] = Mathf.MoveTowards(_w[k], k == _cur ? 1f : 0f, dt / _fade);
                _mix.SetInputWeight(k, _w[k]);
            }
            _graph.Evaluate(dt);
        }

        // ------------------------------------------------------------------------------------------------ head look
        void LateUpdate()
        {
            if (!_on) return;
            float dt = Time.deltaTime;
            _lookW = Mathf.MoveTowards(_lookW, _lookWT, dt / 0.5f);
            _talkW = Mathf.MoveTowards(_talkW, _talk ? 1f : 0f, dt / 0.25f);
            if (!_head) return;
            Vector3 target = _lookT ? (_lookT == transform ? HeadPosition : _lookT.position) : _lookPt;
            // body frame: the lean pivot's facing (upright in dialogue)
            Transform body = _pivot ? _pivot : transform;
            Quaternion want = Quaternion.identity;
            if (_lookW > 1e-3f)
            {
                Vector3 d = body.InverseTransformDirection(target - _head.position);
                if (d.sqrMagnitude > 1e-4f)
                {
                    float yaw = Mathf.Clamp(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -maxYaw, maxYaw);
                    float pitch = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -maxPitch, maxPitch);
                    want = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(pitch, yaw, 0f), _lookW);
                }
            }
            if (_talkW > 1e-3f)
            {
                float t = Time.time;
                float nod = (Mathf.PerlinNoise(_seed, t * 2.6f) - 0.5f) * 7f + Mathf.Sin(t * 5.1f + _seed) * 1.2f;
                float tilt = (Mathf.PerlinNoise(_seed + 5f, t * 1.3f) - 0.5f) * 5f;
                want *= Quaternion.Euler(nod * _talkW, tilt * 0.5f * _talkW, tilt * _talkW);
            }
            _lookCur = Quaternion.Slerp(_lookCur, want, 1f - Mathf.Exp(-lookSharpness * dt));
            // in world terms, about the head: body-space rotation -> world delta
            Quaternion delta = body.rotation * _lookCur * Quaternion.Inverse(body.rotation);
            if (_neck)
            {
                Quaternion qn = Quaternion.Slerp(Quaternion.identity, delta, neckShare);
                _neck.rotation = qn * _neck.rotation;
                _head.rotation = (delta * Quaternion.Inverse(qn)) * _head.rotation;
            }
            else _head.rotation = delta * _head.rotation;
        }
    }
}
