using System.Collections.Generic;
using OYM.Signal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace OYM.Flow
{
    /// The clock of the dodge gimmicks: the same phase on both co-op PCs. The host's is its own Time.time; the guest
    /// gets an offset from LevelFlow's state message, so a debris band or a beam sweep is where the partner sees it.
    public static class FlowClock
    {
        public static float Offset;
        public static float Now => Time.time + Offset;
    }

    /// Shared bits of the dodge gimmicks (DebrisStream, SweepBeam, NavBeacons).
    static class Hazard
    {
        static HuskFXMaterials s_mats;
        static bool s_looked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_mats = null; s_looked = false; }

        /// This PC's pilot when a gimmick may hurt it: engaged (first contact happened), up, and the player has the
        /// controls (no arrival cutscene, level cutaway or boss entrance). Each PC checks only its own pilot (the HP is
        /// its own); the partner's PC runs the same motion (FlowClock) and checks theirs.
        public static bool Pilot(out HuskEncounter e, out Vector3 chest)
        {
            e = HuskEncounter.Current; chest = default;
            if (!e || !e.Engaged || !e.PlayerAlive || !e.Player) return false;
            if (!SignalInstall.PlayerView(e)) return false;
            chest = e.PlayerChest;
            return true;
        }

        /// the additive FX materials of the HUSK rigs (glow / dot / streak sprites), from the encounter's dart prefab
        public static HuskFXMaterials Materials()
        {
            if (!s_looked)
            {
                s_looked = true;
                var cfg = Resources.Load<HuskEncounterConfig>("OYM_HuskEncounter");
                var m = cfg && cfg.dartPrefab ? cfg.dartPrefab.materials : null;
                s_mats = m != null && m.glow && m.dot && m.streak && m.arc ? m : null;
            }
            return s_mats;
        }

        public static AudioClip Clip(string name) => Resources.Load<AudioClip>("OYM_GameSfx/" + name);

        public static Light MakeLight(Transform parent, string name, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var hd = go.AddHDLight(LightType.Point);
            var l = go.GetComponent<Light>();
            l.lightUnit = LightUnit.Lumen; l.intensity = 0f; l.range = range;
            l.shadows = LightShadows.None; l.enabled = false;
            if (hd) hd.affectsVolumetric = false;
            return l;
        }

        /// the closest point to p on the segment a-b
        public static Vector3 OnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return a + ab * t;
        }

        public static void PlayAt(AudioClip c, Vector3 p, float volume, float pitch, float minDist)
        {
            if (!c) return;
            var go = new GameObject("[flow sfx] " + c.name);
            go.transform.position = p;
            var s = go.AddComponent<AudioSource>();
            s.clip = c; s.volume = volume; s.pitch = pitch; s.spatialBlend = 1f; s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Logarithmic; s.minDistance = minDist; s.maxDistance = minDist * 40f;
            s.Play();
            Object.Destroy(go, c.length / Mathf.Max(0.1f, pitch) + 0.2f);
        }
    }
}
