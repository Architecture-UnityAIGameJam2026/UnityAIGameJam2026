using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OYM
{
    /// Scripted title run for checking (editor started with -oymTitleDemo, see OYMMainTitle.OpenAndPlay): opening shot,
    /// menu navigation, pilot swap, friend-code flyout (type + join), OPTIONS (music volume), quit dialog (cancel),
    /// swap back by clicking the pilot in the back, GAME START into the game scene. Every frame (UI included) goes to
    /// Screenshots/title/demo/frame_*.png at a fixed 20 fps, full-size stills of each screen to Screenshots/title/ui_*.png,
    /// then a GIF, then the editor quits. The player's own pilot / volume settings are put back afterwards.
    public class TitleDemo : MonoBehaviour
    {
        const int Fps = 20;
        int _frames;
        string _dir, _stillDir;
        bool _rec;
        int _pilot0;
        float _music0;

        void Start()
        {
            DontDestroyOnLoad(gameObject);
            _stillDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/title"));
            _dir = Path.Combine(_stillDir, "demo");
            Directory.CreateDirectory(_dir);
            foreach (var f in Directory.GetFiles(_dir, "frame_*.png")) File.Delete(f);
            StartCoroutine(Run());
            StartCoroutine(Record());
        }

        IEnumerator Wait(float s) { float t = 0f; while (t < s) { yield return null; t += Time.deltaTime; } }

        /// full-size still of this frame; the script waits two frames so the next action is not in the picture
        IEnumerator Still(string name)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(_stillDir, name + ".png"));
            Log("still " + name);
            yield return null; yield return null;
        }

        IEnumerator Run()
        {
            Time.captureFramerate = Fps;
            var d = FindFirstObjectByType<TitleDirector>();
            var ui = d.ui;
            d.scripted = true;
            ui.scripted = true;
            string title = SceneManager.GetActiveScene().name;
            _pilot0 = GameSettings.TitlePilot; _music0 = GameSettings.Music;
            _rec = true;
            yield return Wait(4.0f);
            Log($"main menu, pilot {d.Focus}, mode {ui.CurrentMode}"); yield return Still("ui_01_main");

            Log("menu: down, down, back up");
            ui.Navigate(0, 1); yield return Wait(0.5f);
            ui.Navigate(0, 1); yield return Wait(0.5f);
            ui.Navigate(0, -1); yield return Wait(0.35f);
            ui.Navigate(0, -1); yield return Wait(0.6f);

            Log("switch pilot (card)"); d.SetFocus(1 - d.Focus);
            yield return Wait(2.8f); yield return Still("ui_02_switch");

            Log("friend code: open, type, join");
            ui.ToggleFriend(); yield return Wait(0.9f);
            ui.SetFriendInput("7KQ4M2XD"); yield return Wait(0.5f);
            ui.Submit(); yield return Wait(0.4f); yield return Still("ui_03_friend");
            yield return Wait(1.6f); yield return Still("ui_03b_friend_status");
            ui.Cancel(); yield return Wait(0.7f);

            Log("options: music volume down 3 steps");
            ui.SelectMenuItem(1); yield return Wait(0.35f);
            if (ui.CurrentMode != TitleUI.Mode.Main || SceneManager.GetActiveScene().name != title) { Log("ABORT: not on the main menu before OPTION"); yield return Finish(); yield break; }
            ui.Submit(); yield return Wait(0.7f);
            Log($"mode after OPTION: {ui.CurrentMode}");
            ui.Navigate(0, 1); yield return Wait(0.3f);
            for (int i = 0; i < 3; i++) { ui.Navigate(-1, 0); yield return Wait(0.15f); }
            yield return Wait(0.4f); yield return Still("ui_04_options");
            ui.Cancel(); yield return Wait(0.7f);

            Log("exit dialog, cancel");
            ui.SelectMenuItem(2); yield return Wait(0.35f);
            ui.Submit(); yield return Wait(0.6f); Log($"mode after EXIT: {ui.CurrentMode}"); yield return Still("ui_05_exit");
            ui.Cancel(); yield return Wait(0.6f);

            Log("click the pilot in the back");
            d.scriptedHover = 1 - d.Focus; yield return Wait(0.6f);
            d.SetFocus(1 - d.Focus); d.scriptedHover = -1;
            yield return Wait(2.6f);
            ui.SelectMenuItem(0); yield return Wait(0.6f);
            Log($"GAME START as {d.pilots[d.Focus].displayName}");
            ui.Submit();
            float t = 0f;
            while (SceneManager.GetActiveScene().name == title && t < 30f) { yield return null; t += Time.unscaledDeltaTime; }
            t = 0f;
            while (t < 3f) { yield return null; t += Time.unscaledDeltaTime; }
            var net = Unity.Netcode.NetworkManager.Singleton;
            Log($"in {SceneManager.GetActiveScene().name}: hosting = {net && net.IsHost}, music = {(BgmPlayer.Current ? BgmPlayer.Current.name : "none")}");
            yield return Finish();
        }

        IEnumerator Finish()
        {
            _rec = false;
            Time.captureFramerate = 0;
            GameSettings.Music = _music0; GameSettings.TitlePilot = _pilot0; GameSettings.Save();
            yield return null;
            MakeGif();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#endif
        }

        IEnumerator Record()
        {
            var eof = new WaitForEndOfFrame();
            while (true)
            {
                yield return eof;
                if (!_rec) { if (_frames > 0) yield break; continue; }
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (!tex) continue;                                             // busy with a full-size still this frame
                var small = Shrink(tex, 1280);
                File.WriteAllBytes(Path.Combine(_dir, $"frame_{_frames:0000}.png"), small.EncodeToPNG());
                if (small != tex) Destroy(small);
                Destroy(tex);
                _frames++;
            }
        }

        // integer box downsample on the CPU (the game view can be 2560+ wide; full-size PNGs are slow and huge)
        static Texture2D Shrink(Texture2D src, int maxWidth)
        {
            int k = Mathf.CeilToInt(src.width / (float)maxWidth);
            if (k <= 1) return src;
            int w = src.width / k, h = src.height / k;
            var a = src.GetPixels32();
            var b = new Color32[w * h];
            int kk = k * k;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int r = 0, g = 0, bl = 0;
                    for (int dy = 0; dy < k; dy++)
                    {
                        int row = (y * k + dy) * src.width + x * k;
                        for (int dx = 0; dx < k; dx++) { var c = a[row + dx]; r += c.r; g += c.g; bl += c.b; }
                    }
                    b[y * w + x] = new Color32((byte)(r / kk), (byte)(g / kk), (byte)(bl / kk), 255);
                }
            var t = new Texture2D(w, h, TextureFormat.RGB24, false);
            t.SetPixels32(b); t.Apply(false);
            return t;
        }

        void MakeGif()
        {
            Log($"{_frames} frames -> {_dir}");
#if UNITY_EDITOR
            string tool = Path.GetFullPath(Path.Combine(Application.dataPath, "../../pipeline/tools/frames_to_gif.py"));
            if (!File.Exists(tool) || _frames < 2) return;
            var psi = new System.Diagnostics.ProcessStartInfo("python", $"-I \"{tool}\" \"{_dir}\" \"{_dir}.gif\" {Fps} 800")
            { UseShellExecute = false, CreateNoWindow = true };
            try { using (var p = System.Diagnostics.Process.Start(psi)) p.WaitForExit(240000); Log("gif " + _dir + ".gif"); }
            catch (System.Exception e) { Log("gif failed: " + e.Message); }
#endif
        }

        static void Log(string s) => Debug.Log("[OYM-Title] demo: " + s);
    }
}
