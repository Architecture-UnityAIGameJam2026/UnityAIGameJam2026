using UnityEngine;
using UnityEngine.SceneManagement;

namespace OYM.Net
{
    /// Team repo port: the local title calls CoopSession.AutoStart itself, the team title does not. With -coopHost /
    /// -coopJoin <code> (and -coopPilot 0 / 1) on the command line this starts the showcase LAN co-op from the title
    /// scene; the team title has no panel for it, so this is the only way in there.
    public sealed class CoopBoot : MonoBehaviour
    {
        float _t;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { SceneManager.sceneLoaded -= OnLoaded; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            var a = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(a, "-coopHost") < 0 && System.Array.IndexOf(a, "-coopJoin") < 0) return;
            SceneManager.sceneLoaded += OnLoaded;
        }

        static void OnLoaded(Scene s, LoadSceneMode mode)
        {
            if (s.name != GameSession.TitleScene) return;
            var go = new GameObject("Co-op boot (-coopHost / -coopJoin)");
            SceneManager.MoveGameObjectToScene(go, s);
            go.AddComponent<CoopBoot>();
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            var title = FindAnyObjectByType<TitleDirector>();
            if (title && title.ui && _t > 0.5f) { CoopSession.AutoStart(title); Destroy(gameObject); }   // AutoStart runs once per run
            else if (_t > 20f) { Debug.LogWarning("[OYM-COOP] -coopHost / -coopJoin: no TitleDirector in the title scene"); Destroy(gameObject); }
        }
    }
}
