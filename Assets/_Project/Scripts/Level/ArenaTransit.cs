using OYM;
using UnityEngine;

namespace OnYourMark
{
    // Showcase pilots use swept flight bodies, so they don't generate Rigidbody trigger events.
    public abstract class ArenaTransit : MonoBehaviour
    {
        Collider volume;
        ZeroGFlight occupant;

        protected virtual void Awake()
        {
            volume = GetComponent<Collider>();
            volume.isTrigger = true;
        }

        void LateUpdate()
        {
            var camera = FlightCamera.Main;
            var target = camera ? camera.target : null;
            var encounter = HuskEncounter.Current;
            if (!target || !target.TryGetComponent<ZeroGFlight>(out var pilot) || !pilot.isActiveAndEnabled)
            {
                occupant = null;
                return;
            }
            if (pilot.scripted || (encounter && (!encounter.Gate || !encounter.PlayerAlive))) return;

            var chest = target.position + Vector3.up * pilot.bodyCenter;
            if ((volume.ClosestPoint(chest) - chest).sqrMagnitude > pilot.bodyRadius * pilot.bodyRadius)
            {
                occupant = null;
                return;
            }
            if (occupant == pilot) return;
            occupant = pilot;
            Ride(pilot);
        }

        protected abstract void Ride(ZeroGFlight pilot);
    }
}
