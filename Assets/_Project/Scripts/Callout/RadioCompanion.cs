using UnityEngine;
using UnityEngine.AI;

namespace OnYourMark
{
    // Only installed on the server-owned companion. Human input and cameras stay on PlayerObjects.
    public sealed partial class RadioCompanion : MonoBehaviour
    {
        Player me;
        Reviver reviver;
        UnlockTerminal terminal;
        PartExchange exchange;
        HitscanWeapon gun;
        MeleeWeapon sword;
        ProjectileWeapon bladeWave;
        SupportGiver support;
        EnemyBase enemy;
        Vector3 goal, waypoint;
        Vector3 dashVector;
        NavMeshPath path;
        float until, nextScan, nextPath;

        const float GunSpacing = 8f;
        static readonly HudPart[] SupportParts = { HudPart.Bio, HudPart.Consumable, HudPart.Map };
        string order = "";
        public string LastResult { get; private set; } = "";
        public float ResultTime { get; private set; }
        bool wasReloading;
        public string Status => $"state={State}, order={order}, target={(enemy ? enemy.GetType().Name : "none")}, reload={gun && gun.Reloading}, recent action={RecentResult}";
        public string RecentResult => Time.time - ResultTime < 8 ? LastResult : "";
        void Awake()
        {
            path = new NavMeshPath();
            me = GetComponent<Player>(); reviver = GetComponent<Reviver>(); gun = GetComponent<HitscanWeapon>();
            sword = GetComponent<MeleeWeapon>();
            bladeWave = GetComponent<ProjectileWeapon>();
            support = GetComponent<SupportGiver>();
            terminal = FindFirstObjectByType<UnlockTerminal>(); exchange = FindFirstObjectByType<PartExchange>();
            me.Motor.BotDriven = true; me.Motor.enabled = true;
        }
        void Record(string result) { LastResult = result; ResultTime = Time.time; }
        static HudPart Part(string id) => id.Contains("bio") || id.Contains("heal") || id.Contains("shield") ? HudPart.Bio :
            id.Contains("map") || id.Contains("scan") || id.Contains("mark") ? HudPart.Map : HudPart.Consumable;
        bool HasShot => enemy && CanSee(enemy);
        Vector3 Eye => transform.position + Vector3.up * 1.1f;
        bool CanSee(EnemyBase e) => CanSeeFrom(Eye, e);
        static bool CanSeeFrom(Vector3 origin, EnemyBase e)
        {
            var delta = e.AimPoint - origin;
            return Physics.Raycast(origin, delta.normalized, out var h, delta.magnitude + .1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && h.collider.GetComponentInParent<EnemyBase>() == e;
        }
        float Range => enemy ? Vector3.Distance(Eye, enemy.AimPoint) : float.PositiveInfinity;
        public bool Can(RadioLine l)
        {
            if (!me.IsBot || !me.IsServer || me.Health.IsDead || !me.Partner) return false;
            var p = me.Partner; var part = Part(l.id);
            var hud = FindFirstObjectByType<CrossHudState>();
            switch (l.action)
            {
                case "dash": return me.Motor.DashStacks > 0;
                case "jump": return me.Motor.Grounded;
                case "hover": return !me.Motor.Grounded && me.Motor.HoverLeft01 > 0;
                case "slash": return sword && Range <= sword.Reach && HasShot;
                case "blade_wave": return bladeWave && me.Ammo.Current > 0 && Range <= bladeWave.Range && HasShot;
                case "rail_shot": return gun && gun.CanBotFire && Range <= gun.Range && HasShot;
                case "scope": return gun && HasShot;
                case "reload": return gun && !gun.Reloading && !me.Ammo.IsFull;
                case "take_cover": return enemy && Cover(out _);
                case "regroup": return order != "regroup" && Vector3.Distance(transform.position, p.transform.position) > 2;
                case "hold_position": return true;
                case "request_support": return !me.Requests.Has(part) && !hud.IsReturned(part);
                case "share_map": part = HudPart.Map; goto case "grant_bio";
                case "grant_resource": part = HudPart.Consumable; goto case "grant_bio";
                case "grant_bio": return !p.Health.IsDead && p.Requests.Has(part) && !hud.IsReturned(part) && GetComponent<SupportGiver>().CooldownLeft(part) <= 0;
                case "request_exchange": return !exchange.HasOffer && !hud.IsReturned(part);
                case "confirm_exchange": return exchange.HasOffer && exchange.OfferFrom != RoleSelection.BotSlot && exchange.OfferPart == part;
                case "cancel_exchange": return exchange.HasOffer && exchange.OfferFrom == RoleSelection.BotSlot;
                case "return_hud": return terminal && terminal.IsAvailable && terminal.IsInside(me) && terminal.IsInside(p);
                case "revive": return reviver.CanRevive(p);
                default: return false;
            }
        }
        bool Cover(out Vector3 point)
        {
            point = default;
            if (!enemy) return false;
            var threat = enemy.transform.position + Vector3.up;
            foreach (var c in Physics.OverlapSphere(transform.position, 12, ~0, QueryTriggerInteraction.Ignore))
            {
                bool cover = false;
                for (var part = c.transform; part != null; part = part.parent)
                    if (part.name.IndexOf("Cover", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        part.name.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0) { cover = true; break; }
                if (!cover) continue;
                var away = c.bounds.center - threat; away.y = 0;
                var behind = c.ClosestPoint(c.bounds.center + away.normalized * 20) + away.normalized;
                if (NavMesh.SamplePosition(behind, out var hit, 2, NavMesh.AllAreas)) { point = hit.position; return true; }
            }
            return false;
        }
        void Face(Vector3 delta)
        {
            delta.y = 0;
            if (delta.sqrMagnitude > .001f) transform.rotation = Quaternion.LookRotation(delta);
        }
        public bool Execute(RadioLine l)
        {
            if (!Can(l)) return false;
            var dir = enemy ? (enemy.AimPoint - Eye).normalized : transform.forward;
            var part = Part(l.id);
            switch (l.action)
            {
                case "reload": if (!gun.BotReload()) return false; break;
                case "slash": Face(dir); if (!GetComponent<MeleeWeapon>().BotSlash()) return false; break;
                case "blade_wave": Face(dir); if (!GetComponent<ProjectileWeapon>().BotLaunch(dir)) return false; break;
                case "rail_shot": Face(dir); if (!gun.BotShoot(dir)) return false; break;
                case "dash": dashVector = (l.id.Contains("out") ? -dir : l.id.Contains("beam") || l.id.Contains("across") ? transform.right : dir); me.Motor.BotDirection = dashVector; me.Motor.BotDash = true; break;
                case "jump": me.Motor.BotJump = true; break;
                case "take_cover": if (!Cover(out goal)) return false; break;
                case "regroup": if (!TryReachable(me.Partner.transform.position, out goal)) return false; break;
                case "request_support": me.Requests.Request(part); break;
                case "grant_bio": if (!GetComponent<SupportGiver>().BotGive(HudPart.Bio)) return false; break;
                case "grant_resource": if (!GetComponent<SupportGiver>().BotGive(HudPart.Consumable)) return false; break;
                case "share_map": if (!GetComponent<SupportGiver>().BotGive(HudPart.Map)) return false; break;
                case "request_exchange": case "confirm_exchange": if (!exchange.BotOffer(part, l.action == "confirm_exchange")) return false; break;
                case "cancel_exchange": if (!exchange.BotCancel()) return false; break;
            }
            if (l.action is "dash" or "hover" or "take_cover" or "regroup" or "hold_position" or "scope" or "revive" or "return_hud")
            {
                order = l.action;
                until = Time.time + (l.action == "regroup" ? 15f : 4f);
                nextPath = 0;
            }
            if (l.action == "scope") Face(dir);
            Record(l.action + " started");
            return true;
        }
        EnemyBase SelectTarget()
        {
            EnemyBase best = null;
            float bestScore = float.PositiveInfinity;
            foreach (var candidate in FindObjectsByType<EnemyBase>(FindObjectsSortMode.None))
            {
                if (!candidate.IsSpawned || (sword && candidate is HoverLaser && me.Ammo.Current == 0)) continue;
                float score = Vector3.Distance(Eye, candidate.AimPoint);
                if (!CanSee(candidate)) score += 15f;
                if (sword && candidate is HoverLaser) score += 5f;
                if (candidate == enemy) score -= 1.5f;
                if (score >= bestScore) continue;
                best = candidate;
                bestScore = score;
            }
            return best;
        }

        void MoveTo(Vector3 destination, float stopDistance)
        {
            var offset = Vector3.ProjectOnPlane(destination - transform.position, Vector3.up);
            if (offset.sqrMagnitude <= stopDistance * stopDistance) return;
            if (Time.time >= nextPath)
            {
                nextPath = Time.time + .18f;
                waypoint = transform.position;
                if (NavMesh.SamplePosition(transform.position, out var start, 3, NavMesh.AllAreas) &&
                    NavMesh.SamplePosition(destination, out var end, 8, NavMesh.AllAreas) &&
                    NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path))
                {
                    foreach (var corner in path.corners)
                    {
                        if (Vector3.ProjectOnPlane(corner - transform.position, Vector3.up).sqrMagnitude < .09f) continue;
                        waypoint = corner;
                        break;
                    }
                }
            }
            var step = Vector3.ProjectOnPlane(waypoint - transform.position, Vector3.up);
            if (step.sqrMagnitude <= .04f) return;
            me.Motor.BotDirection = step.normalized;
            Face(step);
        }
        void OnDestroy() { if (terminal) terminal.SetBotHolding(false); }
    }
}
