using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OnYourMark.EditorTools
{
    /// <summary>
    /// 메모리가 적은 기기용 2인 테스트: 복제 에디터(Multiplayer Play Mode) 대신
    /// macOS 빌드 하나를 Host로 띄우고, 에디터에서 Play 후 Join(방 코드 비움 = 같은 PC)으로 들어간다.
    /// </summary>
    public static class LocalTestBuild
    {
        const string AppPath = "Builds/OnYourMark.app";

        [MenuItem("Tools/On Your Mark/Build and Run Host (macOS)")]
        static void BuildAndRun()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) { UnityEngine.Debug.LogError("[LocalTestBuild] Build Settings에 씬이 없습니다."); return; }

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = AppPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development,
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                UnityEngine.Debug.LogError($"[LocalTestBuild] 빌드 실패: {report.summary.result}");
                return;
            }
            RunHost();
        }

        [MenuItem("Tools/On Your Mark/Run Last Build as Host")]
        static void RunHost()
        {
            if (!Directory.Exists(AppPath)) { UnityEngine.Debug.LogError($"[LocalTestBuild] {AppPath}가 없습니다. 먼저 빌드하세요."); return; }
            // 창 모드 · 작은 해상도로 띄워 메모리와 GPU 사용을 줄인다. 포지션은 P1(검)으로 자동 선택
            Process.Start("open", $"-n \"{AppPath}\" --args -host -role 0 -screen-fullscreen 0 -screen-width 960 -screen-height 540");
            UnityEngine.Debug.Log("[LocalTestBuild] Host 빌드 실행. 에디터에서 Play 후 같은 PC의 방으로 참가하세요 (타이틀 흐름은 -host · -join · -role 인자를 따른다).");
        }
    }
}
