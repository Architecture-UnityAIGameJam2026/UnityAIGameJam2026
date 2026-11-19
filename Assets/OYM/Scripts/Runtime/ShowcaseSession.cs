using UnityEngine;
using UnityEngine.SceneManagement;

namespace OYM
{
    /// Team repo port of what the local art project's GameSession carried for the showcase flow (the team's GameSession
    /// plays the co-op Arena and dropped these):
    ///   SelectedPilot  "KESTREL" / "ORBIT FRAME", or null. When a scene with a PlayTestDirector loads while it is set,
    ///                  that pilot is moved to slot 0 (the director starts on slot 0), like the local title selection did.
    ///                  The team title does not fill it, so in the team build the arrival cutscene plays from F8 or
    ///                  -oymCineDemo, and the showcase co-op (OYM.Net) sets it per seat.
    ///   LoadScene      loads a scene of Assets/OYM/Scenes by name (in the editor even without a Build Settings entry).
    public static class ShowcaseSession
    {
        const string SceneDir = "Assets/OYM/Scenes/";

        public static string SelectedPilot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            SelectedPilot = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { SceneManager.sceneLoaded += OnSceneLoaded; }

        // sceneLoaded runs after the new scene's Awake / OnEnable and before its Start
        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (string.IsNullOrEmpty(SelectedPilot)) return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var d in root.GetComponentsInChildren<PlayTestDirector>(true))
                    PutFirst(d, SelectedPilot);
        }

        static void PutFirst(PlayTestDirector d, string pilot)
        {
            if (d.names == null || d.controllers == null) return;
            int i = System.Array.IndexOf(d.names, pilot);
            if (i <= 0 || i >= d.controllers.Length) return;
            Swap(d.controllers, i); Swap(d.names, i); Swap(d.pivotOffsets, i); Swap(d.eyeOffsets, i);
            Debug.Log("[OYM-Showcase] game starts with " + pilot);
        }

        static void Swap<T>(T[] a, int i)
        {
            if (a == null || i >= a.Length) return;
            (a[0], a[i]) = (a[i], a[0]);
        }

        public static AsyncOperation LoadScene(string name)
        {
#if UNITY_EDITOR
            string path = SceneDir + name + ".unity";
            if (System.IO.File.Exists(path))
                return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#endif
            return SceneManager.LoadSceneAsync(name, LoadSceneMode.Single);
        }
    }
}
