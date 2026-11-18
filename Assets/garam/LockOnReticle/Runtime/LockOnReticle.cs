using UnityEngine;
using UnityEngine.Events;

namespace Garam.Reticles
{
    public enum ReticleState { Idle, Near, Far }
    public enum ReticleRangeMode { Automatic, Near, Far }

    /// <summary>
    /// Drives Figma-authored UI groups: small idle dot, enclosing near ring, three inward far arrows.
    /// Target selection and combat rules stay with the calling game code.
    /// </summary>
    [AddComponentMenu("Garam/Reticles/Lock On Reticle")]
    [DisallowMultipleComponent]
    public sealed class LockOnReticle : MonoBehaviour
    {
        [Header("Scene references")]
        public Camera worldCamera;
        [Tooltip("Optional player transform for range checks. Defaults to the camera when unassigned.")]
        public Transform distanceOrigin;
        [Tooltip("A direct child of a screen-space Canvas (or a full-screen UI container).")]
        public RectTransform marker;
        public CanvasGroup visibility;
        public CanvasGroup idle;
        public CanvasGroup near;
        public CanvasGroup far;
        [Tooltip("Near ring RectTransform; use centered anchors and pivot.")]
        public RectTransform nearRing;
        [Tooltip("Parent of the three inward-pointing arrows, centered on the target pivot.")]
        public RectTransform farArrows;

        [Header("Range selection")]
        public ReticleRangeMode rangeMode = ReticleRangeMode.Automatic;
        [Min(0f)] public float nearDistance = 12f;
        [Min(0f)] public float rangeHysteresis = 0.75f;

        [Header("Size in Canvas units")]
        [Min(0f)] public float nearPadding = 18f;
        [Min(1f)] public float minimumNearDiameter = 100f;
        [Min(1f)] public float maximumNearDiameter = 640f;
        [Min(1f)] public float farDiameter = 160f;

        [Header("Motion")]
        [Min(0f)] public float transitionDuration = 0.18f;
        [Min(0f)] public float acquireDuration = 0.3f;
        [Min(1f)] public float acquireScale = 1.35f;
        [Range(0f, 0.1f)] public float farPulse = 0.025f;
        public bool useUnscaledTime = true;
        public bool reducedMotion;

        public UnityEvent onLocked = new UnityEvent();
        public UnityEvent onReleased = new UnityEvent();

        public ReticleTarget Target => target;
        public ReticleState State { get; private set; } = ReticleState.Idle;
        public bool IsLocked => target != null;

        private ReticleTarget target;
        private bool hadTarget;
        private Canvas canvas;
        private float acquireAge;
        private float animationAge;
        private bool snapAppearance;

        private void OnEnable()
        {
            canvas = marker != null ? marker.GetComponentInParent<Canvas>() : null;
            snapAppearance = true;
            DisableRaycasts(visibility);
            DisableRaycasts(idle);
            DisableRaycasts(near);
            DisableRaycasts(far);
        }

        private static void DisableRaycasts(CanvasGroup group)
        {
            if (group == null)
                return;
            group.blocksRaycasts = false;
            group.interactable = false;
        }

        public void LockOn(ReticleTarget nextTarget)
        {
            if (nextTarget == null || !nextTarget.isActiveAndEnabled)
            {
                ReleaseTarget();
                return;
            }
            if (target == nextTarget)
                return;

            target = nextTarget;
            hadTarget = true;
            acquireAge = 0f;
            State = ReticleState.Idle; // Re-evaluate range without the previous target's hysteresis.
            onLocked.Invoke();
        }

        public void ReleaseTarget()
        {
            bool notify = hadTarget;
            target = null;
            hadTarget = false;
            State = ReticleState.Idle;
            acquireAge = 0f;
            if (notify)
                onReleased.Invoke();
        }

        private void LateUpdate()
        {
            if (marker == null || visibility == null || idle == null || near == null || far == null)
                return;
            if (worldCamera == null)
                worldCamera = Camera.main;
            if (canvas == null)
                canvas = marker.GetComponentInParent<Canvas>();
            var parent = marker.parent as RectTransform;
            if (canvas == null || parent == null)
                return;

            float delta = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            animationAge += delta;
            acquireAge += delta;
            if (hadTarget && (target == null || !target.isActiveAndEnabled))
                ReleaseTarget();

            Camera uiCamera = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : canvas.rootCanvas.worldCamera;
            Vector2 screenPoint = worldCamera != null
                ? worldCamera.pixelRect.center : new Vector2(Screen.width, Screen.height) * 0.5f;
            bool visible = true;
            float nearSize = minimumNearDiameter;

            if (target != null)
            {
                visible = worldCamera != null;
                if (worldCamera != null)
                {
                    Vector3 aim = target.AimPosition;
                    Vector3 viewport = worldCamera.WorldToViewportPoint(aim);
                    visible = viewport.z > worldCamera.nearClipPlane
                        && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;
                    screenPoint = worldCamera.WorldToScreenPoint(aim);
                    Vector3 origin = distanceOrigin != null ? distanceOrigin.position : worldCamera.transform.position;
                    ReticleState nextState = SelectState(Vector3.Distance(origin, aim));
                    if (nextState != State)
                        acquireAge = 0f;
                    State = nextState;

                    if (visible && State == ReticleState.Near
                        && TryProjectBounds(worldCamera, target.GetWorldBounds(), out Rect screenBounds))
                    {
                        screenPoint = screenBounds.center;
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenBounds.min, uiCamera, out Vector2 min);
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenBounds.max, uiCamera, out Vector2 max);
                        // The diagonal encloses the whole projected box, including its corners.
                        nearSize = Mathf.Clamp((max - min).magnitude + nearPadding * 2f,
                            minimumNearDiameter, Mathf.Max(minimumNearDiameter, maximumNearDiameter));
                    }
                }
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, uiCamera, out Vector2 localPoint))
                marker.localPosition = new Vector3(localPoint.x, localPoint.y, 0f);

            visibility.alpha = visible ? 1f : 0f;
            float step = snapAppearance || reducedMotion || transitionDuration <= 0f ? 1f : delta / transitionDuration;
            idle.alpha = Mathf.MoveTowards(idle.alpha, State == ReticleState.Idle ? 1f : 0f, step);
            near.alpha = Mathf.MoveTowards(near.alpha, State == ReticleState.Near ? 1f : 0f, step);
            far.alpha = Mathf.MoveTowards(far.alpha, State == ReticleState.Far ? 1f : 0f, step);
            snapAppearance = false;

            float acquire = reducedMotion || acquireDuration <= 0f ? 1f : Mathf.Clamp01(acquireAge / acquireDuration);
            float convergence = Mathf.Lerp(acquireScale, 1f, 1f - Mathf.Pow(1f - acquire, 3f));
            if (nearRing != null)
                nearRing.sizeDelta = Vector2.one * nearSize * convergence;
            if (farArrows != null)
            {
                float pulse = reducedMotion ? 1f : 1f + Mathf.Sin(animationAge * 4f) * farPulse * acquire;
                farArrows.sizeDelta = Vector2.one * Mathf.Max(1f, farDiameter);
                farArrows.localScale = Vector3.one * convergence * pulse;
            }
        }

        private ReticleState SelectState(float distance)
        {
            if (rangeMode == ReticleRangeMode.Near)
                return ReticleState.Near;
            if (rangeMode == ReticleRangeMode.Far)
                return ReticleState.Far;
            float threshold = nearDistance;
            if (State == ReticleState.Near)
                threshold += rangeHysteresis;
            else if (State == ReticleState.Far)
                threshold = Mathf.Max(0f, threshold - rangeHysteresis);
            return distance <= threshold ? ReticleState.Near : ReticleState.Far;
        }

        /// <summary>Projects all eight bounds corners, ignoring points clipped behind the camera.</summary>
        public static bool TryProjectBounds(Camera camera, Bounds bounds, out Rect screenBounds)
        {
            Vector2 min = Vector2.positiveInfinity;
            Vector2 max = Vector2.negativeInfinity;
            bool found = false;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 point = camera.WorldToScreenPoint(corner);
                if (point.z <= camera.nearClipPlane)
                    continue;
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
                found = true;
            }
            screenBounds = found ? Rect.MinMaxRect(min.x, min.y, max.x, max.y) : default;
            return found;
        }
    }
}
