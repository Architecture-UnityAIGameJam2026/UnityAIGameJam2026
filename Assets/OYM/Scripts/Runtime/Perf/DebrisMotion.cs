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
    /// ~1,800 script calls per physics step plus Rigidbody interpolation for ~1,300 bodies, and every moving rock was
    /// drawn again in HDRP's object motion-vector pass and re-synced to physics every step.
    /// Now: one transform job per frame (render-rate motion, so no interpolation needed), colliders follow at the next
    /// physics sync as before, and a mover is only touched as often as its motion is visible: a far or slow rock whose
    /// screen motion is under <see cref="MaxPixelsPerUpdate"/> per frame is updated every 2..<see cref="MaxStride"/>
    /// frames (staggered). Frames in which it does not move cost no motion-vector draw and no physics sync
    /// (GPU Resident Drawer only draws motion vectors for instances that moved).
    /// Non-kinematic bodies keep the old physics path (MoveRotation / MovePosition in FixedUpdate).
    [DefaultExecutionOrder(-50)]
    public sealed class DebrisMotion : MonoBehaviour
    {
        public const int MaxStride = 6;
        public const float MaxPixelsPerUpdate = 0.75f;

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
        NativeArray<float> _spinLast;                 // time of each spinner's last update
        NativeArray<byte> _spinStride, _driftStride;  // update every N frames
        JobHandle _handle;
        int _spinCursor, _driftCursor;

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

        /// bounding radius of everything the transform carries (for the spin's surface speed)
        static float Radius(Transform t)
        {
            float r = 0f;
            Vector3 c = t.position;
            foreach (var rend in t.GetComponentsInChildren<Renderer>(true))
            {
                var b = rend.bounds;
                r = Mathf.Max(r, (b.center - c).magnitude + b.extents.magnitude);
            }
            return r > 0f ? r : 1f;
        }

        internal static void Register(Spin s, Rigidbody rb)
        {
            var m = Instance; if (m == null || s.slot >= 0) return;
            if (IsPhysicsDriven(rb)) { s.physics = true; Add(m._physSpins, s, ref s.slot); return; }
            if (rb != null) rb.interpolation = RigidbodyInterpolation.None;
            if (s.radius <= 0f) s.radius = Radius(s.transform);
            s.physics = false; Add(m._spins, s, ref s.slot); m._dirty = true;
        }

        internal static void Unregister(Spin s)
        {
            var m = s_Instance; if (m == null || s.slot < 0) { s.slot = -1; return; }
            if (s.physics) { SwapRemove(m._physSpins, ref s.slot, (x, i) => x.slot = i); return; }
            SwapRemove(m._spins, ref s.slot, (x, i) => x.slot = i); m._dirty = true;
        }

        internal static void Register(Drift d, Rigidbody rb)
        {
            var m = Instance; if (m == null || d.slot >= 0) return;
            if (IsPhysicsDriven(rb)) { d.physics = true; Add(m._physDrifts, d, ref d.slot); return; }
            if (rb != null) rb.interpolation = RigidbodyInterpolation.None;
            d.physics = false; Add(m._drifts, d, ref d.slot); m._dirty = true;
        }

        internal static void Unregister(Drift d)
        {
            var m = s_Instance; if (m == null || d.slot < 0) { d.slot = -1; return; }
            if (d.physics) { SwapRemove(m._physDrifts, ref d.slot, (x, i) => x.slot = i); return; }
            SwapRemove(m._drifts, ref d.slot, (x, i) => x.slot = i); m._dirty = true;
        }

        void Rebuild()
        {
            _handle.Complete();
            DisposeNative();
            float now = Time.time;
            int ns = _spins.Count, nd = _drifts.Count;
            _spinTx = new TransformAccessArray(ns);
            _spinData = new NativeArray<SpinJob.Data>(ns, Allocator.Persistent);
            _spinLast = new NativeArray<float>(ns, Allocator.Persistent);
            _spinStride = new NativeArray<byte>(ns, Allocator.Persistent);
            for (int i = 0; i < ns; i++)
            {
                var s = _spins[i];
                _spinTx.Add(s.transform);
                _spinData[i] = new SpinJob.Data { axis = s.axis, degreesPerSecond = s.degreesPerSecond };
                _spinLast[i] = now;
                _spinStride[i] = 1;
            }
            _driftTx = new TransformAccessArray(nd);
            _driftData = new NativeArray<DriftJob.Data>(nd, Allocator.Persistent);
            _driftStride = new NativeArray<byte>(nd, Allocator.Persistent);
            for (int i = 0; i < nd; i++)
            {
                var d = _drifts[i];
                _driftTx.Add(d.transform);
                _driftData[i] = new DriftJob.Data
                {
                    center = d.center, axis = d.axis, degreesPerSecond = d.degreesPerSecond, r0 = d.R0,
                    phase = d.Phase, bobAmplitude = d.bobAmplitude, bobOmega = 6.2831853f / Mathf.Max(d.bobPeriod, 0.1f)
                };
                _driftStride[i] = 1;
            }
            _spinCursor = _driftCursor = 0;
            _dirty = false;
        }

        void Update()
        {
            if (_dirty) Rebuild();
            UpdateStrides();
            int frame = Time.frameCount;
            if (_spins.Count > 0)
                _handle = new SpinJob { data = _spinData, last = _spinLast, stride = _spinStride, now = Time.time, frame = frame }.Schedule(_spinTx);
            if (_drifts.Count > 0)
                _handle = new DriftJob { data = _driftData, stride = _driftStride, t = Time.time, frame = frame }.Schedule(_driftTx, _handle);
        }

        void LateUpdate() => _handle.Complete();

        void FixedUpdate()
        {
            for (int i = 0; i < _physSpins.Count; i++) _physSpins[i].PhysicsStep();
            for (int i = 0; i < _physDrifts.Count; i++) _physDrifts[i].PhysicsStep();
        }

        // screen-space speed -> update stride; a slice of movers is re-evaluated each frame (all within ~8 frames)
        void UpdateStrides()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 cp = cam.transform.position;
            // pixels per metre at 1 m distance, per frame at the current frame rate
            float pxPerRad = Screen.height / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            float fps = Mathf.Clamp(1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f), 20f, 240f);
            float k = pxPerRad / fps; // px per frame for 1 m/s at 1 m

            int n = _spins.Count, slice = Mathf.Max(32, n / 8);
            for (int j = 0; j < slice && n > 0; j++)
            {
                if (_spinCursor >= n) _spinCursor = 0;
                var s = _spins[_spinCursor];
                float dist = Mathf.Max(1f, (s.transform.position - cp).magnitude);
                float speed = Mathf.Abs(s.degreesPerSecond) * Mathf.Deg2Rad * s.radius;
                _spinStride[_spinCursor] = Stride(k * speed / dist);
                _spinCursor++;
            }
            n = _drifts.Count; slice = Mathf.Max(32, n / 8);
            for (int j = 0; j < slice && n > 0; j++)
            {
                if (_driftCursor >= n) _driftCursor = 0;
                var d = _driftData[_driftCursor];
                var tr = _drifts[_driftCursor].transform;
                float dist = Mathf.Max(1f, (tr.position - cp).magnitude);
                float speed = Mathf.Abs(d.degreesPerSecond) * Mathf.Deg2Rad * d.r0.magnitude + d.bobAmplitude * d.bobOmega;
                _driftStride[_driftCursor] = Stride(k * speed / dist);
                _driftCursor++;
            }
        }

        static byte Stride(float pxPerFrame)
        {
            if (pxPerFrame <= 1e-5f) return MaxStride;
            return (byte)Mathf.Clamp(Mathf.FloorToInt(MaxPixelsPerUpdate / pxPerFrame), 1, MaxStride);
        }

        void DisposeNative()
        {
            if (_spinTx.isCreated) _spinTx.Dispose();
            if (_driftTx.isCreated) _driftTx.Dispose();
            if (_spinData.IsCreated) _spinData.Dispose();
            if (_driftData.IsCreated) _driftData.Dispose();
            if (_spinLast.IsCreated) _spinLast.Dispose();
            if (_spinStride.IsCreated) _spinStride.Dispose();
            if (_driftStride.IsCreated) _driftStride.Dispose();
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
            [ReadOnly] public NativeArray<byte> stride;
            public NativeArray<float> last;
            public float now;
            public int frame;

            public void Execute(int i, TransformAccess t)
            {
                if ((frame + i) % stride[i] != 0) return; // staggered: not every far rock on the same frame
                float dt = now - last[i];
                last[i] = now;
                if (dt <= 0f) return; // paused / hit-stop
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
            [ReadOnly] public NativeArray<byte> stride;
            public float t;
            public int frame;

            public void Execute(int i, TransformAccess tr)
            {
                if ((frame + i) % stride[i] != 0) return;
                var d = data[i];
                tr.position = d.center + Quaternion.AngleAxis(d.degreesPerSecond * t, d.axis) * d.r0
                              + d.axis * (Mathf.Sin(t * d.bobOmega + d.phase) * d.bobAmplitude);
            }
        }
    }
}
