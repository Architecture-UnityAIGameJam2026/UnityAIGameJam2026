using UnityEngine;

namespace OYM
{
    /// ORBIT FRAME (RG-77 railgun) on the zero-g flight base (ZeroGFlight: WASD / Space / Ctrl fly, Shift dash).
    /// The rifle is always shouldered (UpperBody layer = AimIdle, legs in the hover pose). RMB = aim (zoom, slower thrust),
    /// LMB = fire: the coils charge from the rear to the muzzle, then the shot: beam to the crosshair, impact flash on the
    /// hit, a short hit-stop, camera kick + shake + FOV punch, and in zero-g the recoil shoves the shooter backward.
    public class OrbitFrameController : ZeroGFlight
    {
        public RailgunFX railgun;
        public RailgunFX fpRailgun;
        public float aimThrust = 0.6f, range = 400f, hitImpulse = 16f, recoilPush = 3.4f;
        public Color railTint = new Color(0.55f, 0.78f, 1f);
        [Tooltip("legacy (OYMGifCapture)")]
        public float walkSpeed = 1.6f, runSpeed = 4.5f, aimSpeed = 1.4f;

        [Header("Magazine")]
        [Tooltip("railgun rounds per magazine: one per shot, shown on the visor HUD (crossed: on the partner's screen)")]
        public int magazine = 8;
        [Tooltip("seconds to refill once the magazine runs dry (no shots meanwhile)")]
        public float reloadTime = 1.6f;

        /// rounds left in the magazine
        public int Ammo { get; private set; }
        public bool Reloading => _reloadAt >= 0f;
        float _reloadAt = -1f;

        static readonly int AimId = Animator.StringToHash("Aim");
        bool _aim;
        float _aimZoom;

        RailgunFX Active => firstPersonActive && fpRailgun ? fpRailgun : railgun;

        protected override void Awake()
        {
            base.Awake();
            Ammo = Mathf.Max(1, magazine);
            foreach (var r in new[] { railgun, fpRailgun })
            {
                if (!r) continue;
                r.aimProvider = AimPoint;
                r.Shot += OnShot;
            }
        }

        Vector3 AimPoint()
        {
            if (flightCam) return flightCam.AimPoint(range, transform, out _, out _);
            var g = Active; return g && g.muzzle ? g.muzzle.position + g.muzzle.forward * range : transform.position + transform.forward * range;
        }

        protected override void Tick(float dt, Quaternion aim)
        {
            var gun = Active;
            if (Ammo <= 0 && _reloadAt < 0f) _reloadAt = Time.time + reloadTime;
            if (_reloadAt >= 0f && Time.time >= _reloadAt) { _reloadAt = -1f; Ammo = Mathf.Max(1, magazine); }
            if (input.fire && gun && !gun.Charging && Ammo > 0)
            {
                Ammo--;
                gun.Fire();
                if (flightCam) flightCam.FovKick(-35f);                         // the view tightens while the coils charge
            }
            _aim = input.aim || (gun && gun.Charging);
            _aimZoom = Mathf.MoveTowards(_aimZoom, input.aim ? 1f : 0f, dt / 0.12f);
            if (flightCam && flightCam.target == transform) flightCam.SetAimZoom(_aimZoom);
            upperTarget = 1f;
            SetBool(AimId, true);
        }

        void OnShot(Vector3 muzzle, Vector3 end)
        {
            var cam = flightCam;
            Vector3 dir = (end - muzzle).normalized;
            bool hit = false;
            if (cam)
            {
                cam.AimPoint(range, transform, out var h, out bool did);
                if (did)
                {
                    var tgt = h.collider.GetComponentInParent<ZeroGTarget>();
                    if (tgt) { tgt.Hit(h.point, dir * hitImpulse, railTint); hit = true; }
                    if (ImpactFX.Main) ImpactFX.Main.Play(h.point, h.normal, tgt ? 1.5f : 1f, tgt ? 60 : 36);
                }
                cam.Kick(firstPersonActive ? 150f : 90f, Random.Range(-20f, 20f));
                cam.Shake(hit ? 0.5f : 0.36f);
                cam.FovKick(firstPersonActive ? 160f : 100f);
            }
            GameFeel.HitStop(hit ? 0.07f : 0.04f, hit ? 0.03f : 0.1f);
            velocity -= dir * recoilPush;                                       // zero-g: the shot shoves the shooter back
            leanVel.x -= 240f;
            if (fpRig && firstPersonActive) fpRig.Kick(1f);
        }

        protected override float ThrustScale() => input.aim ? aimThrust : 1f;
        protected override bool FaceCamera() => true;

        protected override void SyncViewmodel()
        {
            var f = fpRig ? fpRig.animator : null;
            if (f) f.SetBool(AimId, true);
        }

        protected override void OnViewChanged(bool fp)
        {
            if (!fp && flightCam) flightCam.SetAimZoom(0f);
        }
    }
}
