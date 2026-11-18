using UnityEngine;
using UnityEngine.SceneManagement;

namespace OYM
{
    public static class GameSession
    {
        public const string TitleScene = "MainTitle";
        public const string GameScene = "Arena";
        public static string ReturnMessage { get; set; }
        public static bool LaunchArgsUsed { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            ReturnMessage = null;
            LaunchArgsUsed = false;
        }

        public static void ReturnToTitle() => SceneManager.LoadSceneAsync(TitleScene);
    }
}
