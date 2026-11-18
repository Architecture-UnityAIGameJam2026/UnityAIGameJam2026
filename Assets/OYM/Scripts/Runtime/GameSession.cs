using UnityEngine;
using UnityEngine.SceneManagement;

namespace OYM
{
    /// What the main title hands to the game: the chosen pilot. When a scene with a PlayTestDirector loads after a
    /// title selection, that pilot is moved to slot 0 of the director's arrays (its Start activates slot 0), so the
    /// game starts on the pilot picked on the title; Tab still cycles through all of them.
    public static class GameSession
    {
        public const string TitleScene = "MainTitle";
        public const string GameScene = "Showcase_DebrisField";
        const string SceneDir = "Assets/OYM/Scenes/";

        /// "KESTREL" / "ORBIT FRAME"; null when no title selection was made (scene opened directly).
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
            Debug.Log("[OYM-Title] game starts with " + pilot);
        }

        static void Swap<T>(T[] a, int i)
        {
            if (a == null || i >= a.Length) return;
            (a[0], a[i]) = (a[i], a[0]);
        }

        /// Load a scene of Assets/OYM/Scenes by name. In the editor this works without Build Settings entries.
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
