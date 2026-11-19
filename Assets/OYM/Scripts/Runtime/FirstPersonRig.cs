using UnityEngine;

namespace OYM
{
    /// First-person viewmodel: a second copy of the character (arms-only skinned mesh + the weapon at full texture
    /// resolution, same Animator controller) that rides on the camera. After the Animator, the right arm is solved with
    /// two-bone IK so the weapon's grip socket sits at a framed spot in camera space (KESTREL: black blade across the lower
    /// right, ORBIT FRAME: RG-77 low right with the rails / coil lights in view); the left hand can follow the weapon.
    /// Sway (rotation lag), drift lag from the flight velocity and a recoil spring sit on top. `ikWeight` hands the arm
    /// back to the clip (KESTREL slashes) and is faded back in afterwards.
    [DefaultExecutionOrder(200)]
    public class FirstPersonRig : MonoBehaviour
    {
        public Animator animator;
        public Transform rUpper, rFore, rHand, lUpper, lFore, lHand;
        [Tooltip("GripSocket_R of this copy (child of the right hand)")]
        public Transform grip;
        [Tooltip("points on the weapon defining its main axis ((axisFrom or grip) -> axisTip) and its up / spine side (grip -> upRef)")]
        public Transform axisFrom, axisTip, upRef;
        [Tooltip("eye point in this rig's root space (rest pose)")]
        public Vector3 eyeLocal = new Vector3(0f, 1.62f, 0.1f);
        [Tooltip("extra camera-space offset of the whole body (moves the shoulders relative to the eye)")]
        public Vector3 bodyOffset = new Vector3(0f, 0f, 0f);

        [Header("Weapon framing (camera space)")]
        public Vector3 gripPos = new Vector3(0.2f, -0.25f, 0.4f);
        public Vector3 aimDir = new Vector3(0f, 0f, 1f);
        public Vector3 upDir = new Vector3(0f, 1f, 0f);
        public bool leftOnWeapon;
        public Vector3 rightPole = new Vector3(1f, -0.8f, -0.5f), leftPole = new Vector3(-1f, -0.8f, -0.5f);
        [Range(0, 1)] public float ikWeight = 1f;

        [Header("Feel")]
        public float swayAmount = 0.55f, swaySharpness = 11f, driftLag = 0.006f, floatBob = 0.004f;
        public float kickBack = 0.07f, kickPitch = 9f, kickFreq = 7.5f, kickDamping = 0.42f;

        [HideInInspector] public ZeroGFlight flight;
        public Transform ViewCamera { get; set; }

        Quaternion _lag = Quaternion.identity;
        Vector3 _drift, _kick, _kickV;          // kick: x = back (m), y = pitch (deg), z = roll (deg)
        Quaternion _axisFrame = Quaternion.identity;
        bool _axisReady, _lagInit;

        public void Kick(float strength, Vector3 localDir = default)
        {
            _kickV += new Vector3(kickBack * 30f, kickPitch * 22f, (localDir.x) * 140f) * strength;
        }

        void OnEnable() { _lagInit = false; }

        void ComputeAxes()
        {
            if (_axisReady || !grip || !axisTip) return;
            Vector3 a = grip.InverseTransformDirection(axisTip.position - (axisFrom ? axisFrom.position : grip.position)).normalized;
            Vector3 u = upRef ? grip.InverseTransformDirection(upRef.position - grip.position) : Vector3.up;
            u = Vector3.ProjectOnPlane(u, a).normalized;
            _axisFrame = Quaternion.LookRotation(a, u);
            _axisReady = true;
        }

        void LateUpdate()
        {
            Transform ct = ViewCamera ? ViewCamera : FlightCamera.Main ? FlightCamera.Main.transform : null;
            if (!ct || !animator) return;
            animator.SetLayerWeight(1, 1f);
            float dt = Mathf.Min(GameFeel.RealDt, 0.05f);
            Quaternion camRot = ct.rotation;

            // body rides on the camera
            transform.rotation = camRot;
            transform.position = ct.position + camRot * bodyOffset - camRot * eyeLocal;

            ComputeAxes();
            if (!grip || !rHand) return;

            // sway: the weapon lags behind the camera rotation; drift: lags the flight velocity; recoil spring
            if (!_lagInit) { _lag = camRot; _lagInit = true; }
            _lag = Quaternion.Slerp(_lag, camRot, 1f - Mathf.Exp(-swaySharpness * dt));
            Quaternion lagLocal = Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(camRot) * _lag, swayAmount);
            Vector3 v = flight ? Quaternion.Inverse(camRot) * flight.Velocity : Vector3.zero;
            _drift = Vector3.Lerp(_drift, Vector3.ClampMagnitude(-v * driftLag, 0.06f), 1f - Mathf.Exp(-6f * dt));
            Spring.Step(ref _kick, ref _kickV, Vector3.zero, kickFreq, kickDamping, dt);
            float bob = floatBob * Mathf.Sin(Time.time * 1.7f);

            Vector3 localPos = gripPos + _drift + new Vector3(0f, bob, -_kick.x);
            localPos = lagLocal * localPos;
            Quaternion localRot = lagLocal * Quaternion.Euler(-_kick.y, 0f, _kick.z) * Quaternion.LookRotation(aimDir.normalized, upDir.normalized);
            Vector3 gp = ct.TransformPoint(localPos);
            Quaternion gr = camRot * localRot * Quaternion.Inverse(_axisFrame);

            // left hand pose relative to the weapon, from the animated pose (before the right arm moves it)
            Vector3 lRelP = Vector3.zero; Quaternion lRelR = Quaternion.identity;
            bool left = leftOnWeapon && lHand && lFore && lUpper;
            if (left)
            {
                lRelR = Quaternion.Inverse(grip.rotation) * lHand.rotation;
                lRelP = Quaternion.Inverse(grip.rotation) * (lHand.position - grip.position);
            }

            SolveHandTo(rUpper, rFore, rHand, grip, gp, gr, ikWeight, ct.TransformDirection(rightPole));
            if (left)
            {
                Quaternion tr = grip.rotation * lRelR;
                Vector3 tp = grip.position + grip.rotation * lRelP;
                Vector3 hp = Vector3.Lerp(lHand.position, tp, ikWeight);
                Quaternion hr = Quaternion.Slerp(lHand.rotation, tr, ikWeight);
                TwoBone(lUpper, lFore, lHand, hp, ct.TransformDirection(leftPole));
                lHand.rotation = hr;
            }
        }

        /// Move `hand` (via upper / fore) so the child `socket` lands on (pos, rot), blended by w.
        static void SolveHandTo(Transform upper, Transform fore, Transform hand, Transform socket, Vector3 pos, Quaternion rot, float w, Vector3 pole)
        {
            if (!upper || !fore || w <= 0.001f) return;
            Quaternion oR = Quaternion.Inverse(hand.rotation) * socket.rotation;
            Vector3 oP = Quaternion.Inverse(hand.rotation) * (socket.position - hand.position);
            Quaternion hr = rot * Quaternion.Inverse(oR);
            Vector3 hp = pos - hr * oP;
            hp = Vector3.Lerp(hand.position, hp, w);
            hr = Quaternion.Slerp(hand.rotation, hr, w);
            TwoBone(upper, fore, hand, hp, pole);
            hand.rotation = hr;
        }

        public static void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 poleDir)
        {
            Vector3 A = a.position, B = b.position, C = c.position;
            float la = (B - A).magnitude, lb = (C - B).magnitude;
            Vector3 toT = target - A;
            float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(la - lb) + 1e-3f, la + lb - 1e-3f);
            Vector3 dir = toT.sqrMagnitude > 1e-8f ? toT.normalized : (C - A).normalized;
            Vector3 pole = Vector3.ProjectOnPlane(poleDir, dir);
            if (pole.sqrMagnitude < 1e-6f) pole = Vector3.ProjectOnPlane(B - A, dir);
            pole.Normalize();
            float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
            Vector3 Bn = A + dir * (la * cosA) + pole * (la * Mathf.Sqrt(1f - cosA * cosA));
            a.rotation = Quaternion.FromToRotation(B - A, Bn - A) * a.rotation;
            B = b.position; C = c.position;
            b.rotation = Quaternion.FromToRotation(C - B, (A + dir * d) - B) * b.rotation;
        }
    }
}
