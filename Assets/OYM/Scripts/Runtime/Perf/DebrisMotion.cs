using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;

namespace OYM
{
    /// Runs every Spin and Drift from one place (the debris field has ~1,800 of them).
    ///
    /// Before: each component moved its kinematic, interpolated Rigidbody in its own FixedUpdate, so every frame paid
    /// ~1,800 script calls per physics step plus Rigidbody interpolation for ~1,300 bodies, and every drifting rock
    /// was drawn a third time in HDRP's object motion vector pass.
    /// Now: one transform job per frame (render-rate motion, so no interpolation needed), colliders follow at the
    /// next physics sync as before, and movers farther than <see cref="ObjectMotionRange"/> from the camera use
    /// camera-only motion vectors (their screen motion there is ~1 px/frame, TAA/DLSS cannot tell).
    /// Non-kinematic bodies keep the old physics path (MoveRotation / MovePosition in FixedUpdate).
    [DefaultExecutionOrder(-50)]
    public sealed class DebrisMotion : MonoBehaviour
    {
        public const float ObjectMotionRange = 150f;

        static DebrisMotion s_Instance;
        static bool s_Quitting;

        readonly List<Spin> _spins = new();
        readonly List<Drift> _drifts = new();
        readonly List<Spin> _physSpins = new();
        readonly List<Drift> _physDrifts = new();
        bool _dirty = true;

        TransformAccessArray _spinTx, _driftTx;
        NativeArray<SpinJob.Data> _spinData;
        NativeArray<DriftJob.Data> _driftData;
        JobHandle _handle;

        // motion-vector LOD: a slice of movers is re-checked every frame
        readonly List<Mover> _movers = new();
        readonly Dictionary<Transform, int> _moverIndex = new();
        int _mvCursor;

        struct Mover
        {
            public Transform t;
            public Renderer[] renderers;
            public bool objectMotion;
            public int refs;
        }

        static DebrisMotion Instance
        {
            get
            {
                if (s_Instance == null && !s_Quitting && Application.isPlaying)
                {
                    var go = new GameObject("~DebrisMotion") { hideFlags = HideFlags.DontSave };
                    DontDestroyOnLoad(go);
                    s_Instance = go.AddComponent<DebrisMotion>();
                }
                return s_Instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_Instance = null; s_Quitting = false; }

        void OnApplicationQuit() => s_Quitting = true;

        static bool IsPhysicsDriven(Rigidbody rb) => rb != null && !rb.isKinematic;

        // O(1) add / swap-remove; the slot index lives on the component
        static void Add<T>(List<T> list, T item, ref int slot) { slot = list.Count; list.Add(item); }

        static void SwapRemove<T>(List<T> list, ref int slot, System.Action<T, int> setSlot)
        {
            int i = slot, last = list.Count - 1;
            if (i < 0 || i > last) { slot = -1; return; }
            if (i != last) { list[i] = list[last]; setSlot(list[i], i); }
            list.RemoveAt(last);
            slot = -1;
        }

        internal static void Register(Spin s, Rigidbody rb)
        {
            var m = Instance; if (m == null || s.slot >= 0) return;
            if (IsPhysicsDriven(rb)) { s.physics = true; Add(m._physSpins, s, ref s.slot); return; }
            if (rb != null) rb.interpolation = RigidbodyInterpolation.None;
            s.physics = false; Add(m._spins, s, ref s.slot); m._dirty = true; m.AddMover(s.transform);
        }

        internal static void Unregister(Spin s)
        {
            var m = s_Instance; if (m == null || s.slot < 0) { s.slot = -1; return; }
            if (s.physics) { SwapRemove(m._physSpins, ref s.slot, (x, i) => x.slot = i); return; }
            SwapRemove(m._spins, ref s.slot, (x, i) => x.slot = i); m._dirty = true; m.RemoveMover(s.transform);
        }

        internal static void Register(Drift d, Rigidbody rb)
        {
            var m = Instance; if (m == null || d.slot >= 0) return;
            if (IsPhysicsDriven(rb)) { d.physics = true; Add(m._physDrifts, d, ref d.slot); return; }
            if (rb != null) rb.interpolation = RigidbodyInterpolation.None;
            d.physics = false; Add(m._drifts, d, ref d.slot); m._dirty = true; m.AddMover(d.transform);
        }

        internal static void Unregister(Drift d)
        {
            var m = s_Instance; if (m == null || d.slot < 0) { d.slot = -1; return; }
            if (d.physics) { SwapRemove(m._physDrifts, ref d.slot, (x, i) => x.slot = i); return; }
            SwapRemove(m._drifts, ref d.slot, (x, i) => x.slot = i); m._dirty = true; m.RemoveMover(d.transform);
        }

        void AddMover(Transform t)
        {
            if (_moverIndex.TryGetValue(t, out int i)) { var m = _movers[i]; m.refs++; _movers[i] = m; return; } // Spin + Drift on one object
            _moverIndex[t] = _movers.Count;
            _movers.Add(new Mover { t = t, renderers = t.GetComponentsInChildren<Renderer>(true), objectMotion = true, refs = 1 });
        }

        void RemoveMover(Transform t)
        {
            if (!_moverIndex.TryGetValue(t, out int i)) return;
            var m = _movers[i];
            if (--m.refs > 0) { _movers[i] = m; return; }
            // leave the renderers as they were authored
            if (!m.objectMotion)
                foreach (var r in m.renderers) if (r != null) r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            int last = _movers.Count - 1;
            if (i != last) { _movers[i] = _movers[last]; _moverIndex[_movers[i].t] = i; }
            _movers.RemoveAt(last);
            _moverIndex.Remove(t);
        }

        void Rebuild()
        {
            _handle.Complete();
            DisposeNative();
            _spinTx = new TransformAccessArray(_spins.Count);
            _spinData = new NativeArray<SpinJob.Data>(_spins.Count, Allocator.Persistent);
            for (int i = 0; i < _spins.Count; i++)
            {
                _spinTx.Add(_spins[i].transform);
                _spinData[i] = new SpinJob.Data { axis = _spins[i].axis, degreesPerSecond = _spins[i].degreesPerSecond };
            }
            _driftTx = new TransformAccessArray(_drifts.Count);
            _driftData = new NativeArray<DriftJob.Data>(_drifts.Count, Allocator.Persistent);
            for (int i = 0; i < _drifts.Count; i++)
            {
                var d = _drifts[i];
                _driftTx.Add(d.transform);
                _driftData[i] = new DriftJob.Data
                {
                    center = d.center, axis = d.axis, degreesPerSecond = d.degreesPerSecond, r0 = d.R0,
                    phase = d.Phase, bobAmplitude = d.bobAmplitude, bobOmega = 6.2831853f / Mathf.Max(d.bobPeriod, 0.1f)
                };
            }
            _dirty = false;
        }

        void Update()
        {
            if (_dirty) Rebuild();
            float dt = Time.deltaTime;
            if (_spins.Count > 0 && dt > 0f)
                _handle = new SpinJob { data = _spinData, dt = dt }.Schedule(_spinTx);
            if (_drifts.Count > 0)
                _handle = new DriftJob { data = _driftData, t = Time.time }.Schedule(_driftTx, _handle);
            UpdateMotionVectorModes();
        }

        void LateUpdate() => _handle.Complete();

        void FixedUpdate()
        {
            for (int i = 0; i < _physSpins.Count; i++) _physSpins[i].PhysicsStep();
            for (int i = 0; i < _physDrifts.Count; i++) _physDrifts[i].PhysicsStep();
        }

        void UpdateMotionVectorModes()
        {
            int n = _movers.Count;
            if (n == 0) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 cp = cam.transform.position;
            float r2 = ObjectMotionRange * ObjectMotionRange;
            int slice = Mathf.Max(32, n / 8); // every mover re-checked about every 8 frames
            for (int k = 0; k < slice && k < n; k++)
            {
                if (_mvCursor >= n) _mvCursor = 0;
                var m = _movers[_mvCursor];
                if (m.t != null)
                {
                    bool near = (m.t.position - cp).sqrMagnitude < r2;
                    if (near != m.objectMotion)
                    {
                        var mode = near ? MotionVectorGenerationMode.Object : MotionVectorGenerationMode.Camera;
                        foreach (var r in m.renderers) if (r != null) r.motionVectorGenerationMode = mode;
                        m.objectMotion = near;
                        _movers[_mvCursor] = m;
                    }
                }
                _mvCursor++;
            }
        }

        void DisposeNative()
        {
            if (_spinTx.isCreated) _spinTx.Dispose();
            if (_driftTx.isCreated) _driftTx.Dispose();
            if (_spinData.IsCreated) _spinData.Dispose();
            if (_driftData.IsCreated) _driftData.Dispose();
        }

        void OnDestroy()
        {
            _handle.Complete();
            DisposeNative();
            if (s_Instance == this) s_Instance = null;
        }

        struct SpinJob : IJobParallelForTransform
        {
            public struct Data { public Vector3 axis; public float degreesPerSecond; }
            [ReadOnly] public NativeArray<Data> data;
            public float dt;

            public void Execute(int i, TransformAccess t)
            {
                var d = data[i];
                t.rotation = Quaternion.AngleAxis(d.degreesPerSecond * dt, d.axis) * t.rotation;
            }
        }

        struct DriftJob : IJobParallelForTransform
        {
            public struct Data
            {
                public Vector3 center, axis, r0;
                public float degreesPerSecond, phase, bobAmplitude, bobOmega;
            }
            [ReadOnly] public NativeArray<Data> data;
            public float t;

            public void Execute(int i, TransformAccess tr)
            {
                var d = data[i];
                tr.position = d.center + Quaternion.AngleAxis(d.degreesPerSecond * t, d.axis) * d.r0
                              + d.axis * (Mathf.Sin(t * d.bobOmega + d.phase) * d.bobAmplitude);
            }
        }
    }
}
