using System.Collections.Generic;
using UnityEngine;

namespace OYM
{
    /// PRISM's attack, as one timeline on the attack clock t (seconds since BeginCharge):
    ///   WAKE   0 .. 0.2      sensor slit flares, the iris snaps open (closed -> open model under the flash, petals
    ///                        spring out with the "쪼"), the body turns onto the target
    ///   DRAW   .. C - lock   the lens core lights and grows; motes stream in from all round and speed up; crackling
    ///                        arcs on the iris; a faint sighting beam + laser dot sweep onto the target (they lag:
    ///                        that is the dodge window); the lens light washes over the hull
    ///   LOCK   last `lock` s the core goes white-hot and compresses, the motes rush, the body draws back
    ///                        (anticipation), the sighting beam goes solid and strobes; a short dim right before the shot
    ///   FIRE   C .. C + beam blinding white-magenta flash + glint, a thick lance with a white-hot core to the hit point
    ///                        (looping laser-burn impact + hull hot spot), spark spray off the lens, recoil kick that
    ///                        settles with the snap; the lance thins to a hairline and cuts
    ///   VENT   .. + vent     the lens cools white -> magenta -> ember, embers drift off, the iris folds shut
    /// Gameplay drives it with BeginCharge / SetAim / Fire / Cancel (HoverLaser: charging NetworkVariable, aimPoint,
    /// ShowShotRpc). Only `pivot` (the visual) moves; the root belongs to the AI.
    public class PrismAttackFX : MonoBehaviour
    {
        [Header("Rig")]
        public Transform pivot;
        public GameObject closedModel, openModel;
        [Tooltip("lens centre / sensor slit, pivot space")] public Vector3 lensLocal = new Vector3(0f, 0.55f, 0.78f);
        public Vector3 sensorLocal = new Vector3(0f, 0.68f, 0.62f);
        public Light lensLight;
        public LineRenderer beamLine;
        public HuskFXMaterials materials = new HuskFXMaterials();

        [Header("Timing (s)")]
        public float chargeTime = 2.5f;
        public float lockTime = 0.45f;
        public float beamTime = 0.5f;
        public float ventTime = 1.5f;

        [Header("Look")]
        public float beamWidth = 0.3f;
        public float maxRange = 60f;
        public LayerMask beamMask = ~0;
        [Tooltip("open model X scale while the iris is shut (closed width / open width)")] public float irisShutScale = 0.74f;
        public float recoilDistance = 0.55f;
        [Tooltip("aim ahead of time: also auto-fire at the end of the charge when Fire() is not called")]
        public bool autoFire = true;

        /// fired once per shot: (lens, end, normal)
        public event System.Action<Vector3, Vector3, Vector3> Shot;

        // ---------------------------------------------------------------- state (everything Evaluate reads)
        float _t = -1f;                 // attack clock, < 0 idle
        float _fireAt = -1f;            // clock at the shot (C unless fired early / late)
        float _cancelAt = -1f;
        Vector3 _aim, _end, _endN;
        Collider _endCol;
        bool _hasAim, _shotDone;
        int _seed;

        HuskFXCanvas _fx;
        HuskFX.Emission _emOpen, _emClosed;
        HitFXInstance _burn;
        readonly List<HitFXInstance> _manual = new List<HitFXInstance>();
        Vector3 _openScale = Vector3.one, _pivotPos;
        Quaternion _pivotRot = Quaternion.identity;
        bool _inited;

        public bool Busy => _t >= 0f;
        public float Clock => _t;
        public Vector3 LensWorld => pivot ? pivot.TransformPoint(lensLocal) : transform.position;

        void Init()
        {
            if (_inited) return;
            _inited = true;
            if (!pivot) pivot = transform;
            if (openModel) _openScale = openModel.transform.localScale;
            _pivotPos = pivot == transform ? Vector3.zero : pivot.localPosition;
            _pivotRot = pivot == transform ? Quaternion.identity : pivot.localRotation;
            _emOpen = new HuskFX.Emission(openModel ? openModel.transform : null);
            _emClosed = new HuskFX.Emission(closedModel ? closedModel.transform : null);
            Idle();
        }

        void OnEnable() { Init(); }

        void OnDisable()
        {
            ReleaseBurn();
            HuskFX.ClearManual(_manual);
            if (_fx != null && _fx.Valid) { _fx.Begin(); _fx.End(); }   // destroying children while deactivating is not allowed
            if (beamLine) beamLine.enabled = false;
            if (lensLight) lensLight.enabled = false;
        }

        void OnDestroy() { if (_fx != null) { _fx.Destroy(); _fx = null; } }

        // ---------------------------------------------------------------- game API
        /// Start the charge; the shot lands `chargeTime` seconds later (HoverLaserData.chargeTime).
        public void BeginCharge(float charge, Vector3 aimPoint)
        {
            Init();
            chargeTime = Mathf.Max(lockTime + 0.3f, charge);
            _t = 0f; _fireAt = -1f; _cancelAt = -1f; _shotDone = false;
            _aim = aimPoint; _hasAim = true;
            _seed = Random.Range(0, 100000);
            _endCol = null;
            ReleaseBurn();
        }

        /// Where the sighting beam points (the AI's lagging aim). Call every frame while charging.
        public void SetAim(Vector3 aimPoint) { _aim = aimPoint; _hasAim = true; }

        /// The shot. `end` = hit point (or the end of the range), `normal` / `hit` optional (impact FX + hot spot).
        public void Fire(Vector3 end, Vector3 normal, Collider hit = null)
        {
            Init();
            if (_t < 0f) { BeginCharge(0f, end); _t = chargeTime; }
            _end = end; _endN = normal.sqrMagnitude > 1e-6f ? normal.normalized : (LensWorld - end).normalized; _endCol = hit;
            _fireAt = _t; _shotDone = false;
        }

        /// Fire along the current aim with a raycast (demo / autoFire).
        public void FireAtAim()
        {
            Vector3 o = LensWorld, d = (_aim - o).normalized;
            if (d.sqrMagnitude < 1e-6f) d = pivot.forward;
            if (Physics.Raycast(o + d * 0.4f, d, out var h, maxRange, beamMask, QueryTriggerInteraction.Ignore) && !h.collider.transform.IsChildOf(transform))
                Fire(h.point, h.normal, h.collider);
            else Fire(o + d * maxRange, -d, null);
        }

        /// Charge interrupted (staggered / killed): the core sputters out and the iris shuts.
        public void Cancel()
        {
            if (_t < 0f || _fireAt >= 0f) return;
            _cancelAt = _t;
        }

        /// Back to idle at once (pool reuse).
        public void ResetState()
        {
            Init();
            _t = -1f; _fireAt = -1f; _cancelAt = -1f; _shotDone = false; _hasAim = false;
            ReleaseBurn();
            HuskFX.ClearManual(_manual);
            Idle();
        }

        // ---------------------------------------------------------------- clock
        void Update() { if (Application.isPlaying) Tick(Time.deltaTime); }

        /// Advance the attack clock by dt and redraw (play mode: Update; batch capture: by hand).
        public void Tick(float dt)
        {
            Init();
            if (!Application.isPlaying) HuskFX.TickManual(_manual, dt);
            if (_t < 0f) { Idle(); if (_burn) TickBurn(dt, 0f); return; }
            float prev = _t;
            _t += dt;
            if (_fireAt < 0f && _cancelAt < 0f && autoFire && _t >= chargeTime) { _t = chargeTime; FireAtAim(); _t = prev + dt; }
            if (_fireAt >= 0f && !_shotDone && _t >= _fireAt) Shoot();
            float end = _cancelAt >= 0f ? _cancelAt + 0.35f + ventTime : (_fireAt >= 0f ? _fireAt + beamTime + ventTime : float.MaxValue);
            Evaluate(_t);
            TickBurn(dt, BeamEnvelope(_t));
            if (_t > end) { _t = -1f; Idle(); }
        }

        void Shoot()
        {
            _shotDone = true;
            Vector3 lens = LensWorld;
            Shot?.Invoke(lens, _end, _endN);
            // muzzle: kick + small shake for anyone close; the impact: body flash + camera kick of an energy hit
            // (its ripple-ring prefab is left out on purpose - the lance draws its own impact)
            if (Application.isPlaying) HitScreenFX.Impulse(lens, 0.18f, 30f, 0f, 0.08f);
            else if (HitScreenFX.Instance)
            {
                float d = Vector3.Distance(HitScreenFX.Instance.transform.position, lens);
                float k = Mathf.Clamp01(1f - d / 30f);
                HitScreenFX.Instance.AddTrauma(0.18f * k); HitScreenFX.Instance.AddFlash(0.08f * k);
            }
            HuskFX.HitReaction(HitKind.EnergyBolt, _end, _endCol, 1.2f, 0.35f);
        }

        // ---------------------------------------------------------------- evaluation (pure function of the clock)
        float FireT => _fireAt >= 0f ? _fireAt : chargeTime;

        float BeamEnvelope(float t)
        {
            if (_fireAt < 0f || t < _fireAt) return 0f;
            float f = t - _fireAt;
            if (f > beamTime) return 0f;
            return Mathf.Clamp01(f / 0.025f) * (1f - HuskFX.Smooth(beamTime - 0.14f, beamTime, f));
        }

        void Idle()
        {
            if (closedModel) closedModel.SetActive(true);
            if (openModel) { openModel.SetActive(false); openModel.transform.localScale = _openScale; }
            if (pivot && pivot != transform) pivot.SetLocalPositionAndRotation(_pivotPos, _pivotRot);
            _emOpen?.Set(1f); _emClosed?.Set(1f);
            if (beamLine) beamLine.enabled = false;
            HuskFX.SetLight(lensLight, 0f, HuskFX.LightMagenta);
            if (_fx != null && _fx.Valid) { _fx.Begin(); _fx.End(); }
        }

        /// Pose + FX at attack time t (s since BeginCharge).
        public void Evaluate(float t)
        {
            Init();
            if (_fx == null || !_fx.Valid) _fx = new HuskFXCanvas(transform, materials);
            float C = FireT, L = Mathf.Min(lockTime, C * 0.4f);
            bool fired = _fireAt >= 0f && t >= _fireAt;
            bool cancelled = _cancelAt >= 0f && t >= _cancelAt;
            float f = fired ? t - _fireAt : -1f;                         // time since the shot
            float vent = fired ? (f - beamTime) / ventTime : (cancelled ? (t - _cancelAt - 0.35f) / ventTime : -1f);

            // ---- heat of the lens (0..1) and how far the charge got
            float draw = HuskFX.Smooth(0.08f, C - L, t);                // 0..1 over the draw
            float lockK = cancelled ? 0f : Mathf.Clamp01((t - (C - L)) / L);
            float heat;
            if (fired) heat = f < beamTime ? 1f : Mathf.Lerp(0.95f, 0f, Mathf.Pow(Mathf.Clamp01(vent), 0.6f));
            else if (cancelled)
            {
                float c0 = 0.35f + 0.45f * HuskFX.Smooth(0.08f, C - L, _cancelAt);
                float s = t - _cancelAt;
                float sputter = s < 0.35f ? (HuskFX.H(Mathf.FloorToInt(t * 30f), _seed) > 0.45f ? 1f : 0.25f) : 1f;
                heat = Mathf.Lerp(c0, 0f, Mathf.Clamp01(s / (0.35f + ventTime * 0.6f))) * sputter;
            }
            else heat = 0.35f + 0.45f * draw + 0.2f * lockK;

            // ---- iris: open from 0.06 s, folds shut 55 % into the vent
            float openAt = 0.06f;
            float closeK = vent < 0f ? 0f : HuskFX.Smooth(0.45f, 0.62f, vent);
            bool open = t >= openAt && closeK < 1f;
            if (closedModel) closedModel.SetActive(!open);
            if (openModel)
            {
                openModel.SetActive(open);
                float spread = HuskFX.Snap((t - openAt) / 0.18f, 0.10f);           // petals spring out (the snap)
                float sx = Mathf.LerpUnclamped(irisShutScale, 1f, spread);
                sx = Mathf.Lerp(sx, irisShutScale, closeK);
                openModel.transform.localScale = new Vector3(_openScale.x * sx, _openScale.y, _openScale.z * Mathf.Lerp(1f, 0.96f, closeK));
            }

            // ---- body: turn onto the aim, draw back in LOCK, recoil on the shot, sputter shake on cancel
            Vector3 basePos = pivot == transform ? Vector3.zero : _pivotPos;
            Quaternion baseRot = pivot == transform ? Quaternion.identity : _pivotRot;
            Vector3 aimDirLocal = Vector3.forward;
            if (_hasAim && pivot != transform)
            {
                Vector3 lensW = transform.TransformPoint(basePos + baseRot * lensLocal);
                Vector3 target = fired ? _end : _aim;
                Vector3 dW = target - lensW;
                if (dW.sqrMagnitude > 1e-4f)
                {
                    Vector3 dl = Quaternion.Inverse(transform.rotation) * dW.normalized;
                    float yaw = Mathf.Atan2(dl.x, dl.z) * Mathf.Rad2Deg;
                    float pitch = -Mathf.Asin(Mathf.Clamp(dl.y, -1f, 1f)) * Mathf.Rad2Deg;
                    pitch = Mathf.Clamp(pitch, -35f, 35f);
                    float turn = HuskFX.Snap(t / 0.32f, 0.07f);
                    if (vent > 0f) turn *= 1f - HuskFX.Smooth(0.65f, 1f, vent);            // eases back to rest at the end
                    var look = Quaternion.Euler(pitch * turn, yaw * turn, 0f);
                    baseRot = look * baseRot;
                    aimDirLocal = look * Vector3.forward;
                }
            }
            Vector3 off = Vector3.zero;
            Quaternion kick = Quaternion.identity;
            if (!fired && !cancelled && lockK > 0f)
            {
                off -= aimDirLocal * (0.16f * HuskFX.Smooth(0f, 0.85f, lockK));            // draw back
                float sh = 0.006f * lockK;
                off += new Vector3(HuskFX.N(t * 40f, 3), HuskFX.N(t * 40f, 4), 0f) * sh;
            }
            if (fired)
            {
                float rise = Mathf.Clamp01(f / 0.035f);
                rise = 1f - (1f - rise) * (1f - rise) * (1f - rise);
                float decay = f < 0.035f ? 1f : Mathf.Exp(-(f - 0.035f) * 7f) * Mathf.Cos((f - 0.035f) * 15f);
                float back = recoilDistance * rise * decay;
                float pre = 0.16f * Mathf.Clamp01(1f - f / 0.05f);                          // release the draw-back
                off -= aimDirLocal * (back + pre);
                kick = Quaternion.AngleAxis(-7f * rise * decay, Vector3.right);
            }
            if (cancelled && t - _cancelAt < 0.4f)
            {
                float s = 1f - (t - _cancelAt) / 0.4f;
                off += new Vector3(HuskFX.N(t * 55f, 5), HuskFX.N(t * 55f, 6), HuskFX.N(t * 55f, 7)) * (0.03f * s);
            }
            if (pivot != transform) pivot.SetLocalPositionAndRotation(basePos + off, baseRot * kick);

            // ---- emission on the hull (lens light leaking through the iris)
            float emK = 1f + 1.8f * heat * heat * (open ? 1f : 0.4f);
            if (fired && f < 0.12f) emK += 2f * (1f - f / 0.12f);
            _emOpen?.Set(emK); _emClosed?.Set(t < openAt ? 1f + 2f * (t / openAt) : 1f);

            // ---- FX
            Vector3 lens = pivot.TransformPoint(lensLocal);
            Vector3 fwd = pivot.TransformDirection(Vector3.forward);
            Vector3 lensOut = HuskFX.TowardCam(lens + fwd * 0.05f, 0.06f);
            _fx.Begin();
            float lumen = 0f;
            int seed = _seed;

            // WAKE: sensor flare + pop under the iris swap
            float wake = Mathf.Exp(-Mathf.Max(0f, t - 0.02f) / 0.05f) * (t < 0.6f ? 1f : 0f);
            if (wake > 0.01f)
            {
                Vector3 sensor = pivot.TransformPoint(sensorLocal);
                _fx.dot.Sprite(sensor, 0.4f, HuskFX.Heat(0.85f) * (4f * wake));
                _fx.glow.Sprite(sensor, 1.6f, HuskFX.Heat(0.6f) * (0.7f * wake));
                _fx.glint.Flare(sensor, 2.2f, 0.16f, HuskFX.Heat(0.7f) * (1.2f * wake));
                lumen += 3000f * wake;
            }

            // LENS CORE (draw + lock + vent)
            if (open)
            {
                float flick = 1f + 0.12f * HuskFX.N(t * 23f, seed) + 0.06f * HuskFX.N(t * 61f, seed + 1);
                float press = fired ? 1f : 1f - 0.28f * HuskFX.Smooth(0.3f, 1f, lockK);   // compresses before the shot
                float inhale = (!fired && !cancelled && t > C - 0.07f && t < C) ? 0.35f : 1f;
                inhale *= 1f - closeK;                                                   // the iris covers it
                inhale *= HuskFX.Smooth(openAt, openAt + 0.12f, t);
                float core = (0.5f + 6f * heat * heat * heat) * flick * inhale;
                float size = (0.22f + 0.42f * heat) * press;
                if (!fired || f > beamTime)
                {
                    _fx.dot.Sprite(lensOut, size, HuskFX.Heat(heat * 0.92f) * core);
                    _fx.glow.Sprite(lensOut, (0.6f + 1.7f * heat) * press, HuskFX.Heat(0.55f + 0.1f * heat) * (0.15f + 0.65f * heat * heat) * flick * inhale);
                }
                lumen += (200f + 2200f * heat * heat * heat) * flick * inhale;
            }

            // DRAW / LOCK: motes streaming into the lens, arcs on the iris, sighting beam + laser dot
            if (!fired && !cancelled && t > 0.1f)
            {
                float density = HuskFX.Smooth(0.1f, 0.6f, t);
                float speed = 1f + 1.6f * lockK;
                const int Motes = 56;
                for (int i = 0; i < Motes; i++)
                {
                    float period = Mathf.Lerp(0.55f, 1.1f, HuskFX.H(i, seed + 11)) / speed;
                    float u = Mathf.Repeat(t / period + HuskFX.H(i, seed + 12), 1f);
                    Vector3 dir = HuskFX.Dir(i, seed + 13);
                    dir = (dir + fwd * 0.55f).normalized;                                // mostly from the front half
                    float R = Mathf.Lerp(1.6f, 4.2f, HuskFX.H(i, seed + 14));
                    float r = R * (1f - u) * (1f - u);                                     // accelerates in
                    float rTail = R * Mathf.Pow(1f - Mathf.Max(0f, u - 0.08f * speed), 2f);
                    Vector3 head = lensOut + dir * r, tail = lensOut + dir * rTail;
                    float a = density * Mathf.Clamp01(u * 5f) * Mathf.Clamp01((1f - u) * 12f);
                    Color c = HuskFX.Heat(0.5f + 0.45f * u) * (0.9f + 2.4f * u) * a;
                    _fx.streak.Streak(tail, head, 0.022f + 0.02f * u, c * 0.15f, c);
                }
                // arcs crawling over the iris
                float arcK = heat * heat;
                for (int k = 0; k < 3; k++)
                {
                    int frame = Mathf.FloorToInt(t * 17f + k * 0.37f);
                    if (HuskFX.H(frame, seed + 20 + k) > 0.35f + 0.4f * (1f - lockK)) continue;
                    Vector3 o = HuskFX.Dir(frame, seed + 30 + k) * 0.22f;
                    _fx.arc.Sprite(lensOut + o, 0.5f + 0.5f * HuskFX.H(frame, seed + 40 + k), HuskFX.Heat(0.75f) * (1.3f * arcK), HuskFX.H(frame, seed + 50 + k) * 360f);
                }
                // sighting beam: faint and loose while drawing, tight and solid in LOCK, strobing at the end
                if (_hasAim)
                {
                    Vector3 to = _aim - lensOut;
                    float dist = to.magnitude;
                    Vector3 aimEnd = _aim;
                    if (dist > 1e-3f && Physics.Raycast(lensOut + to / dist * 0.3f, to / dist, out var sh, Mathf.Min(maxRange, dist + 2f), beamMask, QueryTriggerInteraction.Ignore)
                        && !sh.collider.transform.IsChildOf(transform))
                        aimEnd = sh.point;
                    float on = HuskFX.Smooth(0.25f, 0.7f, t);
                    float strobe = lockK > 0.45f ? (Mathf.Repeat(t * 14f, 1f) < 0.55f ? 1.25f : 0.35f) : 1f;
                    float w = Mathf.Lerp(0.05f, 0.014f, Mathf.Max(draw * 0.7f, lockK));
                    float I = on * Mathf.Lerp(0.18f, 0.55f, draw) * (1f + 2.5f * lockK) * strobe * (1f + 0.15f * HuskFX.N(t * 31f, seed + 60));
                    Color bc = HuskFX.Heat(0.62f + 0.25f * lockK) * I;
                    _fx.streak.Streak(lensOut, aimEnd, w, bc, bc * 0.55f);
                    // laser dot where it lands (on the target's suit / the hull)
                    _fx.dot.Sprite(aimEnd, 0.06f + 0.05f * lockK, HuskFX.Heat(0.85f) * (2.5f * on * strobe * (0.5f + lockK)));
                    _fx.glow.Sprite(aimEnd, 0.5f + 0.4f * lockK, HuskFX.Heat(0.6f) * (0.35f * on * strobe * (0.4f + lockK)));
                }
            }

            // CANCEL: sputter pops
            if (cancelled && t - _cancelAt < 0.4f)
            {
                float s = t - _cancelAt;
                int frame = Mathf.FloorToInt(t * 24f);
                if (HuskFX.H(frame, seed + 70) > 0.5f)
                    _fx.dot.Sprite(lensOut + HuskFX.Dir(frame, seed + 71) * 0.12f, 0.35f, HuskFX.Heat(0.8f) * (3f * (1f - s / 0.4f)));
            }

            // FIRE
            if (beamLine) beamLine.enabled = false;
            if (fired)
            {
                float env = BeamEnvelope(t);
                float flash = Mathf.Exp(-f / 0.06f);
                // blinding flash at the lens + anamorphic glint
                _fx.dot.Sprite(lensOut, 0.7f + 1.1f * flash, HuskFX.Heat(0.92f) * (8f * flash + 2.5f * env));
                _fx.glow.Sprite(lensOut, 1.8f + 3.5f * flash, HuskFX.Heat(0.6f) * (0.45f * env + 1.3f * flash));
                _fx.glint.Flare(lensOut, 3f + 5f * flash, 0.25f + 0.25f * flash, HuskFX.Heat(0.75f) * (1f * env + 3.5f * flash));
                lumen += 16000f * Mathf.Exp(-f / 0.08f) + 3500f * env;

                if (env > 0.001f)
                {
                    float breathe = 1f + 0.08f * Mathf.Sin(f * 47f) + 0.05f * Mathf.Sin(f * 113f);
                    float burst = 1f + 0.8f * Mathf.Exp(-f / 0.05f);
                    float w = beamWidth * env * breathe * burst;
                    // sheath (FX_Beam material) + white-hot core streak
                    if (beamLine)
                    {
                        beamLine.enabled = true;
                        beamLine.positionCount = 2;
                        beamLine.SetPosition(0, lensOut);
                        beamLine.SetPosition(1, _end);
                        beamLine.widthMultiplier = w;
                        var c = new Color(1f, 1f, 1f, env);
                        beamLine.startColor = c; beamLine.endColor = c;
                    }
                    _fx.streak.Streak(lensOut, _end, w * 0.38f, HuskFX.Heat(0.9f) * (3.2f * env), HuskFX.Heat(0.85f) * (2.6f * env));
                    _fx.streak.Streak(lensOut, _end, w * 1.7f, HuskFX.Heat(0.58f) * (0.4f * env), HuskFX.Heat(0.55f) * (0.25f * env));
                    // where it lands
                    _fx.dot.Sprite(_end + _endN * 0.05f, 0.35f + 0.45f * flash, HuskFX.Heat(0.9f) * (2.5f * env + 5f * flash));
                    _fx.glow.Sprite(_end + _endN * 0.1f, 1.3f + 1.5f * flash, HuskFX.Heat(0.6f) * (0.45f * env + 0.9f * flash));
                }
                // spark spray thrown off the lens
                const int Sparks = 26;
                for (int i = 0; i < Sparks; i++)
                {
                    float life = Mathf.Lerp(0.18f, 0.45f, HuskFX.H(i, seed + 80));
                    float a = f / life;
                    if (a >= 1f) continue;
                    Vector3 d = (HuskFX.Dir(i, seed + 81) * 0.8f + fwd * 0.9f).normalized;
                    float v = Mathf.Lerp(6f, 17f, HuskFX.H(i, seed + 82));
                    float travel = v * life * (1f - Mathf.Exp(-3f * a)) / 3f;          // drag
                    Vector3 p = lensOut + d * travel;
                    Vector3 tail = p - d * Mathf.Min(travel, 0.25f + 0.25f * (1f - a));
                    Color c = HuskFX.Heat(1f - 0.55f * a) * (4f * (1f - a));
                    _fx.streak.Streak(tail, p, 0.02f, c * 0.1f, c);
                }
            }

            // VENT: embers drifting off the cooling lens
            if (vent > 0f && vent < 1f)
            {
                for (int i = 0; i < 10; i++)
                {
                    float u = Mathf.Repeat(vent * 2.2f + HuskFX.H(i, seed + 90), 1f);
                    Vector3 d = (HuskFX.Dir(i, seed + 91) + Vector3.up * 0.6f + fwd * 0.3f).normalized;
                    Vector3 p = lensOut + d * (0.1f + 0.9f * u);
                    float a = (1f - u) * (1f - vent);
                    _fx.dot.Sprite(p, 0.035f, HuskFX.Heat(0.55f) * (3f * a));
                }
            }

            _fx.End();
            HuskFX.SetLight(lensLight, lumen, Color.Lerp(HuskFX.LightMagenta, new Color(1f, 0.8f, 0.9f), Mathf.Clamp01(heat - 0.7f) / 0.3f));
            if (lensLight) lensLight.transform.position = lensOut + fwd * 0.25f;
        }

        // ---------------------------------------------------------------- held impact (laser burn)
        void TickBurn(float dt, float env)
        {
            var lib = HitFX.Library;
            var e = lib ? lib.Get(HitKind.LaserBurn) : null;
            if (env > 0.001f && e != null && e.prefab)
            {
                if (!_burn || !_burn.Playing)
                {
                    if (Application.isPlaying) _burn = HitFX.Spawn(e.prefab);
                    else { _burn = Instantiate(e.prefab); _burn.gameObject.hideFlags = HideFlags.DontSave; }
                    _burn.transform.SetPositionAndRotation(_end, Quaternion.LookRotation(_endN));
                    _burn.Play();
                }
                _burn.transform.SetPositionAndRotation(_end, Quaternion.LookRotation(_endN));
                _burn.SetSustain(env);
                if (_endCol)
                {
                    var hf = _endCol.GetComponentInParent<HitFlash>();
                    if (hf) hf.HoldHit(_end, e.overlayColor * (0.6f * env), e.overlaySize * 1.2f);
                }
            }
            else if (_burn && _burn.Playing) _burn.Stop();
            if (_burn && !Application.isPlaying)
            {
                _burn.Tick(dt);
                if (!_burn.Playing) { DestroyImmediate(_burn.gameObject); _burn = null; }
            }
        }

        void ReleaseBurn()
        {
            if (!_burn) return;
            if (Application.isPlaying) _burn.Stop();
            else DestroyImmediate(_burn.gameObject);
            _burn = null;
        }
    }
}
