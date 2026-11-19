using System.Collections.Generic;
using UnityEngine;

namespace OYM
{
    /// "Enemies around?" for ZeroGFlight's cruise boost (hold Shift: out of combat only). Team-repo version: reads the
    /// co-op enemies (OnYourMark.EnemyBase = Rusher / HoverLaser, spawned by WaveManager from NetworkObjectPool) through
    /// their public API only, the same way RadioCompanion / CalloutDirector look them up; nothing of theirs is edited.
    /// A threat = a spawned, active enemy that is not dead within the radius (measured to its AimPoint). Pooled enemies are
    /// inactive between lives and do not count. No enemies in the scene (the OYM test maps) = the cruise is always on.
    /// The art project's FlightThreat reads its HuskEncounter instead, which this repo does not have: keep this file when
    /// syncing ZeroGFlight from there.
    public static class FlightThreat
    {
        /// seconds between scene scans (FindObjectsByType); positions and life are read live on every call
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

        public static bool Near(Vector3 at, float radius)
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
