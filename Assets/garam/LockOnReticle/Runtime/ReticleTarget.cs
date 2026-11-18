using UnityEngine;

namespace Garam.Reticles
{
    /// <summary>Put this on a lockable enemy; place aimPoint at its intended weak point.</summary>
    [DisallowMultipleComponent]
    public sealed class ReticleTarget : MonoBehaviour
    {
        public Transform aimPoint;
        public Renderer[] boundsRenderers;
        [Min(0.01f)] public float fallbackRadius = 1f;

        public Vector3 AimPosition => aimPoint != null ? aimPoint.position : transform.position;

        private void Awake()
        {
            if (boundsRenderers == null || boundsRenderers.Length == 0)
                boundsRenderers = GetComponentsInChildren<Renderer>();
        }

        private void Reset()
        {
            boundsRenderers = GetComponentsInChildren<Renderer>();
        }

        public Bounds GetWorldBounds()
        {
            var bounds = new Bounds(AimPosition, Vector3.one * Mathf.Max(0.01f, fallbackRadius) * 2f);
            bool found = false;
            if (boundsRenderers == null)
                return bounds;

            foreach (Renderer targetRenderer in boundsRenderers)
            {
                if (targetRenderer == null || !targetRenderer.enabled || !targetRenderer.gameObject.activeInHierarchy)
                    continue;
                if (!found)
                    bounds = targetRenderer.bounds;
                else
                    bounds.Encapsulate(targetRenderer.bounds);
                found = true;
            }
            return bounds;
        }
    }
}
