using OYM;
using OYM.Net;
using UnityEngine;

namespace OnYourMark
{
    // One pilot's footing and thruster budget. The other pilot can still be on the incoming transfer.
    internal sealed class ArenaFlightLimiter
    {
        public bool Active { get; private set; }

        PlayerStats stats;
        Transform ground, safeSurface;
        Vector3 safePoint;
        float verticalSpeed, fuelUsed, timeOnDeck;
        bool heldJump, hovering, bossReached;

        internal bool CheckSurface(ZeroGFlight pilot, HuskEncounter encounter)
        {
            var zones = HuskZones.Current;
            if (bossReached || !zones || !zones.arenaEntry || !encounter) return false;
            var game = CoopGame.Current;
            if (!(encounter.boss && encounter.boss.Playing) && ((encounter.Level && encounter.Level.BossFightStarted) ||
                (game && game.World && game.World.BossFightStarted)))
            {
                Active = false;
                bossReached = true;
                return false;
            }
            if (pilot.scripted || pilot.RidingArenaDevice || !encounter.Gate || encounter.hold) return Active;

            ground = null;
            var origin = pilot.transform.position + Vector3.up * .5f;
            if (verticalSpeed <= .1f && Physics.Raycast(origin, Vector3.down, out var hit, .88f, ~0, QueryTriggerInteraction.Ignore)
                && hit.normal.y > .55f && !encounter.IsPilotCollider(hit.collider) && !hit.collider.GetComponentInParent<HuskEnemy>())
            {
                if (!Active)
                {
                    if (!hit.transform.IsChildOf(zones.arenaEntry.root)) return false;
                    stats = pilot is KestrelController ? zones.kestrelMobility : zones.orbitMobility;
                    if (!stats) return false;
                    Active = true;
                    pilot.ResetMotion();
                }
                ground = safeSurface = hit.transform;
                safePoint = safeSurface.InverseTransformPoint(hit.point);
            }
            if (Active && safeSurface && pilot.transform.position.y < safeSurface.TransformPoint(safePoint).y - 22f)
            {
                // Falling off returns to the last solid footing, including a deck that has since moved.
                pilot.ResetMotion();
                pilot.transform.position = safeSurface.TransformPoint(safePoint) + Vector3.up * .25f;
                verticalSpeed = fuelUsed = 0f;
                hovering = heldJump = false;
                Physics.SyncTransforms();
            }
            return Active;
        }

        internal Vector3 Step(ZeroGFlight pilot, FlightInput input, Quaternion aim, Vector3 wish, Vector3 nativeVelocity, float dt)
        {
            bool up = input.move.y > .1f;
            bool pressed = up && !heldJump;
            heldJump = up;
            if (ground)
            {
                hovering = false;
                timeOnDeck += dt;
                if (timeOnDeck >= stats.hoverRechargeDelay) fuelUsed = 0f;
                if (pressed && AssistFieldJump(pilot, wish)) { verticalSpeed = 0f; return Vector3.zero; }
                verticalSpeed = pressed ? stats.JumpVelocity : -1f;
                if (pressed) ground = null;
            }
            else
            {
                timeOnDeck = 0f;
                hovering = up && fuelUsed < stats.hoverMaxTime && (hovering || verticalSpeed <= 0f || pressed);
                if (hovering)
                {
                    fuelUsed += dt;
                    verticalSpeed = 0f;
                    if (!pilot.Dashing) return aim * Vector3.forward * stats.hoverSpeed;
                }
                verticalSpeed = Mathf.Max(verticalSpeed + stats.gravity * dt, -32f);
            }
            var planar = pilot.Dashing ? Vector3.ProjectOnPlane(nativeVelocity, Vector3.up) : wish * stats.moveSpeed;
            return planar + Vector3.up * verticalSpeed;
        }

        // Main's stepping stones are further apart than arena decks. Assist only an aimed jump
        // between adjacent stones. Devices and these jumps all use the pilot's swept body.
        bool AssistFieldJump(ZeroGFlight pilot, Vector3 heading)
        {
            var path = HuskZones.Current.fieldStones;
            if (!path || ground.parent != path || heading.sqrMagnitude < .1f || !ground.name.StartsWith("PathStone_")) return false;
            int index = ground.GetSiblingIndex();
            for (int step = -1; step <= 1; step += 2)
            {
                int next = index + step;
                if (next < 0 || next >= path.childCount) continue;
                var target = path.GetChild(next);
                if (!target.name.StartsWith("PathStone_") || !target.TryGetComponent<Collider>(out var surface)) continue;
                var direction = Vector3.ProjectOnPlane(target.position - pilot.transform.position, Vector3.up);
                if (Vector3.Angle(direction, heading) > 40f) continue;
                var bounds = surface.bounds;
                var ray = new Ray(bounds.center + Vector3.up * (bounds.extents.y + 2f), Vector3.down);
                if (!surface.Raycast(ray, out var hit, bounds.size.y + 4f)) continue;
                pilot.RideArenaDevice(hit.point + Vector3.up * .2f, Mathf.Clamp(direction.magnitude / 14f, .6f, 2.5f), true);
                return true;
            }
            return false;
        }
    }
}
