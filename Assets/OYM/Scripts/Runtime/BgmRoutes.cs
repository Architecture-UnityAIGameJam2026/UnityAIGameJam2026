using UnityEngine;

namespace OYM
{
    /// Scene name -> background music. Lives in Resources/OYM_BgmRoutes (built by OYMMainTitle), read by BgmPlayer on
    /// every single-mode scene load, so no scene needs its own music object.
    [CreateAssetMenu(menuName = "OYM/BGM Routes", fileName = ResourceName)]
    public class BgmRoutes : ScriptableObject
    {
        public const string ResourceName = "OYM_BgmRoutes";

        [System.Serializable]
        public class Route
        {
            public string scene;
            public AudioClip clip;
            [Range(0f, 1f)] public float volume = 0.8f;
            [Tooltip("seconds; the previous track fades out over the same time")]
            public float fade = 1.5f;
        }

        public Route[] routes = new Route[0];

        public Route Find(string scene)
        {
            if (routes == null) return null;
            foreach (var r in routes) if (r != null && r.scene == scene) return r;
            return null;
        }
    }
}
