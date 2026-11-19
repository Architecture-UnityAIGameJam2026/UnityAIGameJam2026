using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace OYM.Signal
{
    /// Jev on OpenRouter System One (the team's AI mediation model, see OYM_git garam/AI_CALLOUT_ARCHITECTURE.md):
    /// one "choice" question whose options are prerecorded radio line ids + "none". Jev only picks an id; the game
    /// validates it and plays the local recording. Only the game state text and the line texts are sent.
    /// Key (never in the project): env OPENROUTER_API_KEY, or a file named by env OPENROUTER_KEY_FILE, or
    /// %USERPROFILE%\.qwer\openrouter_api_key.txt. No key = the mediator picks locally (RadioMediator).
    public static class JevLink
    {
        public const string Endpoint = "https://openrouter.ai/api/v1/systemone";
        public const string Model = "jev-latest";

        public struct Result
        {
            public bool ok;
            public long code;
            public string choice, model, error;
            public float confidence, seconds;
        }

        [Serializable] class Response { public string model; public Answers answers; }
        [Serializable] class Answers { public Clip clip; }
        [Serializable] class Clip { public string choice; public float confidence = -1f; }

        static string s_key;
        static bool s_looked;
        public static string KeySource { get; private set; } = "none";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_key = null; s_looked = false; KeySource = "none"; }

        public static bool HasKey { get { Look(); return !string.IsNullOrEmpty(s_key); } }

        static string ReadKeyFile(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                var k = File.ReadAllText(path).Trim();
                return k.Length > 8 && k.IndexOfAny(new[] { '\r', '\n', ' ', '\t' }) < 0 ? k : null;
            }
            catch (Exception) { return null; }
        }

        static void Look()
        {
            if (s_looked) return;
            s_looked = true;
            var env = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
            if (!string.IsNullOrWhiteSpace(env) && env.Trim().IndexOfAny(new[] { ' ', '\r', '\n' }) < 0) { s_key = env.Trim(); KeySource = "OPENROUTER_API_KEY"; return; }
            if ((s_key = ReadKeyFile(Environment.GetEnvironmentVariable("OPENROUTER_KEY_FILE"))) != null) { KeySource = "OPENROUTER_KEY_FILE"; return; }
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if ((s_key = ReadKeyFile(Path.Combine(home, ".qwer", "openrouter_api_key.txt"))) != null) { KeySource = "~/.qwer/openrouter_api_key.txt"; return; }
            KeySource = "none";
        }

        public static IEnumerator Choose(string state, string instructions, IReadOnlyDictionary<string, string> criteria, float timeout, Action<Result> done)
        {
            Look();
            if (string.IsNullOrEmpty(s_key)) { done(new Result { error = "no OpenRouter key" }); yield break; }
            var json = new StringBuilder(2048);
            json.Append("{\"model\":").Append(Quote(Model))
                .Append(",\"state\":").Append(Quote(state))
                .Append(",\"questions\":{\"clip\":{\"type\":\"choice\",\"instructions\":").Append(Quote(instructions))
                .Append(",\"criteria\":{");
            bool first = true;
            foreach (var kv in criteria)
            {
                if (!first) json.Append(',');
                json.Append(Quote(kv.Key)).Append(':').Append(Quote(kv.Value));
                first = false;
            }
            json.Append("}}}}");

            float t0 = Time.realtimeSinceStartup;
            using var req = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json.ToString()));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + s_key);
            req.timeout = Mathf.Max(1, Mathf.CeilToInt(timeout));
            yield return req.SendWebRequest();

            var r = new Result { code = req.responseCode, seconds = Time.realtimeSinceStartup - t0 };
            if (req.result != UnityWebRequest.Result.Success) r.error = $"{req.responseCode} {req.error}";
            else
            {
                try
                {
                    var res = JsonUtility.FromJson<Response>(req.downloadHandler.text);
                    r.model = res?.model;
                    r.choice = res?.answers?.clip?.choice;
                    r.confidence = res?.answers?.clip?.confidence ?? -1f;
                    r.ok = !string.IsNullOrEmpty(r.choice);
                    if (!r.ok) r.error = "no answers.clip.choice in the response";
                }
                catch (Exception e) { r.error = "bad response: " + e.Message; }
            }
            done(r);
        }

        static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
