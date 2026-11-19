using System.Collections.Generic;
using UnityEngine;

namespace OYM
{
    /// "Enemies around?" for ZeroGFlight's cruise boost (hold Shift: out of combat only). Reads both kinds of enemy through
    /// their public API only (nothing of theirs is edited); either one near = no cruise:
    ///   showcase  the live HUSK (HuskEncounter): an awake HUSK within the radius, a homing dart in the air, or the CITADEL
    ///             fight (HuskLevel beat Boss). Dormant sleepers do not count: cruising into a zone wakes it, and the first
    ///             HUSK to wake drops you out of the cruise. Enemies switched off with F10 = no HUSK threat.
    ///   co-op     the team's enemies (OnYourMark.EnemyBase = Rusher / HoverLaser, spawned by WaveManager from
    ///             NetworkObjectPool), looked up the same way RadioCompanion / CalloutDirector do: a spawned, active enemy
    ///             that is not dead within the radius (measured to its AimPoint). Pooled enemies are inactive between
    ///             lives and do not count.
    /// Neither in the scene (the OYM test maps, -oymNoHusk, recorders) = the cruise is always on.
    public static class FlightThreat
    {
        /// seconds between scene scans for the team's enemies (FindObjectsByType); positions and life are read live
        const float Rescan = 0.25f;

        static readonly List<global::OnYourMark.EnemyBase> s_enemies = new List<global::OnYourMark.EnemyBase>();
        static readonly List<global::OnYourMark.Health> s_health = new List<global::OnYourMark.Health>();
        static float s_nextScan = float.NegativeInfinity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Install()
        {
            s_enemies.Clear(); s_health.Clear();
            s_nextScan = float.NegativeInfinity;
            ZeroGFlight.EnemiesNear = Near;
        }

        public static bool Near(Vector3 at, float radius) => HuskNear(at, radius) || TeamNear(at, radius);

        static bool HuskNear(Vector3 at, float radius)
        {
            var e = HuskEncounter.Current;
            if (!e || !e.isActiveAndEnabled || !e.hostileEnabled) return false;
            var level = e.Level;
            if (level && level.beat == HuskLevel.Beat.Boss) return true;
            float r2 = radius * radius;
            var enemies = e.Enemies;
            for (int i = 0; i < enemies.Count; i++)
            {
                var h = enemies[i];
                if (h && h.IsAwake && (h.transform.position - at).sqrMagnitude <= r2) return true;
            }
            var darts = e.Darts;
            if (darts != null)
                for (int i = 0; i < darts.Count; i++)
                {
                    var d = darts[i];
                    if (d && d.Busy) return true;
                }
            return false;
        }

        static bool TeamNear(Vector3 at, float radius)
        {
            float now = Time.unscaledTime;
            if (now >= s_nextScan)
            {
                s_nextScan = now + Rescan;
                s_enemies.Clear(); s_health.Clear();
                foreach (var e in Object.FindObjectsByType<global::OnYourMark.EnemyBase>(FindObjectsSortMode.None))
                {
                    s_enemies.Add(e);
                    s_health.Add(e.GetComponent<global::OnYourMark.Health>());
                }
            }
            float r2 = radius * radius;
            for (int i = 0; i < s_enemies.Count; i++)
            {
                var e = s_enemies[i];
                if (!e || !e.isActiveAndEnabled || !e.IsSpawned) continue;      // back in the pool / not spawned
                var h = s_health[i];
                if (h && h.IsDead) continue;
                if ((e.AimPoint - at).sqrMagnitude <= r2) return true;
            }
            return false;
        }
    }
}
