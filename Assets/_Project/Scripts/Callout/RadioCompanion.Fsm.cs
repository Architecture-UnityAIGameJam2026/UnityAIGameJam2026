using UnityEngine;
using UnityEngine.AI;

namespace OnYourMark
{
    public sealed partial class RadioCompanion
    {
        public enum CompanionState
        {
            Inactive, Guard, Pursue, MeleeAttack, FindFiringLane, Fire, Withdraw, Reload,
            Hold, TakeCover, Regroup, Dash, ApproachDownedPartner, Revive, UseTerminal
        }

        public CompanionState State { get; private set; } = CompanionState.Inactive;
        EnemyBase stateTarget;
        Vector3 tacticalGoal;
        float reconsiderLaneAt;
        const float RallyArrival = 1.5f;
        const float RetreatExit = GunSpacing + 3f;

        void Update()
        {
            if (!me.IsServer || !me.IsSpawned) return;
            var partner = me.Partner;
            bool active = partner && !me.Health.IsDead;
            if (!active || Time.time >= until) order = "";
            if (active && Time.time >= nextScan)
            {
                nextScan = Time.time + .35f;
                enemy = SelectTarget();
                if (!partner.Health.IsDead)
                    foreach (var part in SupportParts)
                        if (partner.Requests.Has(part)) support.BotGive(part);
            }

            bool visible = active && HasShot;
            CompanionState next = active ? DecideState(partner, visible) : CompanionState.Inactive;
            if (next != State || stateTarget != enemy)
                EnterState(next);

            me.Motor.BotDirection = Vector3.zero;
            me.Motor.BotHover = active && order == "hover";
            me.Motor.BotZoom = State == CompanionState.Hold && order == "scope";
            reviver.SetBotHolding(State == CompanionState.Revive);
            if (terminal) terminal.SetBotHolding(State == CompanionState.UseTerminal);
            if (!active) return;

            if (gun && wasReloading && !gun.Reloading && me.Ammo.IsFull) Record("reload completed");
            if (gun && me.Ammo.Current == 0 && !gun.Reloading && gun.BotReload()) Record("reload started");
            wasReloading = gun && gun.Reloading;
            TickState(partner, visible);
        }

        CompanionState DecideState(Player partner, bool visible)
        {
            if (partner.Health.IsDead)
            {
                order = "";
                return reviver.CanRevive(partner) ? CompanionState.Revive : CompanionState.ApproachDownedPartner;
            }

            switch (order)
            {
                case "return_hud":
                    if (terminal && terminal.IsAvailable && terminal.IsInside(me)) return CompanionState.UseTerminal;
                    order = "";
                    break;
                case "regroup":
                    if ((goal - transform.position).sqrMagnitude > RallyArrival * RallyArrival)
                        return CompanionState.Regroup;
                    order = "";
                    Record("regroup completed");
                    break;
                case "take_cover": return CompanionState.TakeCover;
                case "hold_position":
                case "scope": return CompanionState.Hold;
                case "dash": if (me.Motor.BotDash) return CompanionState.Dash; break;
            }

            bool retreatingFromTarget = State == CompanionState.Withdraw && stateTarget == enemy;
            if (gun && visible && Range < (retreatingFromTarget ? RetreatExit : GunSpacing))
                return CompanionState.Withdraw;
            if (gun && (gun.Reloading || me.Ammo.Current == 0)) return CompanionState.Reload;
            if (!enemy) return CompanionState.Guard;
            if (sword && !(enemy is HoverLaser))
                return visible && Range <= sword.Reach * .95f ? CompanionState.MeleeAttack : CompanionState.Pursue;
            float reach = gun ? gun.Range : bladeWave.Range;
            return visible && Range <= reach ? CompanionState.Fire : CompanionState.FindFiringLane;
        }

        void EnterState(CompanionState next)
        {
            State = next;
            stateTarget = enemy;
            nextPath = 0f;
            reconsiderLaneAt = 0f;
            waypoint = transform.position;
        }

        void TickState(Player partner, bool visible)
        {
            switch (State)
            {
                case CompanionState.ApproachDownedPartner:
                    MoveTo(partner.transform.position, .5f);
                    return;
                case CompanionState.Regroup:
                case CompanionState.TakeCover:
                    MoveTo(goal, .6f);
                    break;
                case CompanionState.Dash:
                    me.Motor.BotDirection = dashVector;
                    return;
                case CompanionState.Pursue:
                    MoveTo(enemy.transform.position, .5f);
                    break;
                case CompanionState.Withdraw:
                case CompanionState.FindFiringLane:
                    if (Time.time >= reconsiderLaneAt)
                    {
                        tacticalGoal = PickFiringPosition(State == CompanionState.Withdraw);
                        reconsiderLaneAt = Time.time + .75f;
                    }
                    MoveTo(tacticalGoal, .5f);
                    break;
                case CompanionState.Guard:
                case CompanionState.Revive:
                case CompanionState.UseTerminal:
                case CompanionState.Reload:
                    return;
            }

            // Holding a firing lane, a rally order, and a retreat all permit a shot of opportunity.
            if (!visible) return;
            var aim = (enemy.AimPoint - Eye).normalized;
            Face(aim);
            if (gun)
            {
                if (Range <= gun.Range && gun.BotShoot(aim)) Record("rail_shot fired");
            }
            else if (Range <= sword.Reach)
            {
                if (sword.BotSlash()) Record("slash started");
            }
            else if (enemy is HoverLaser && Range <= bladeWave.Range && bladeWave.BotLaunch(aim))
                Record("blade_wave fired");
        }

        // Sample firing positions around the target. A blocked or unreachable lane cannot become a goal.
        Vector3 PickFiringPosition(bool retreat)
        {
            Vector3 center = enemy.transform.position;
            center.y = transform.position.y;
            Vector3 outward = Vector3.ProjectOnPlane(transform.position - center, Vector3.up).normalized;
            if (outward.sqrMagnitude < .01f) outward = -transform.forward;
            Vector3 best = retreat ? transform.position : center;
            float bestCost = float.PositiveInfinity;
            for (int i = 0; i < 8; i++)
            {
                var offset = Quaternion.AngleAxis(i * 45f, Vector3.up) * outward * (RetreatExit + 1f);
                if (!TryReachable(center + offset, out var point)) continue;
                if (retreat && Vector3.Distance(point, center) < GunSpacing) continue;
                float cost = (point - transform.position).sqrMagnitude;
                bool clear = CanSeeFrom(point + Vector3.up * 1.1f, enemy);
                if (!clear && !retreat) continue;
                if (!clear) cost += 400f;
                if (cost >= bestCost) continue;
                bestCost = cost;
                best = point;
            }
            return best;
        }

        bool TryReachable(Vector3 position, out Vector3 point)
        {
            point = default;
            if (!NavMesh.SamplePosition(transform.position, out var start, 3, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(position, out var end, 3, NavMesh.AllAreas) ||
                !NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) ||
                path.status != NavMeshPathStatus.PathComplete) return false;
            point = end.position;
            return true;
        }
    }
}
