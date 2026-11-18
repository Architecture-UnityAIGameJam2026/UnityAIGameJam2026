using UnityEngine;
using UnityEngine.SceneManagement;

namespace OYM
{
    /// One deployment from the title to the network lobby. Opening Arena directly keeps its connection menu.
    public static class GameSession
    {
        public const string TitleScene = "MainTitle";
        public const string GameScene = "Arena";

        public static int? PreferredRole { get; private set; }
        public static string JoinCode { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ClearDeployment()
        {
            PreferredRole = null;
            JoinCode = null;
        }

        public static void Deploy(int role, string joinCode)
        {
            PreferredRole = role;
            JoinCode = joinCode;
            LoadArena();
        }

        public static void LoadArena() => SceneManager.LoadSceneAsync(GameScene);
    }
}
