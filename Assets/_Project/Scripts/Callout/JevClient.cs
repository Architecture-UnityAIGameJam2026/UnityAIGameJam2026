using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace OnYourMark
{
    /// <summary>
    /// OpenRouter System One API로 Jev에게 "상황에 맞는 대사 ID 하나"를 고르게 한다.
    /// 요청: { model, state, questions: { clip: { type: "choice", instructions, criteria: { id: 설명, ... } } } }
    /// 응답: answers.clip.choice / answers.clip.confidence
    /// </summary>
    public static class JevClient
    {
        public struct Result
        {
            public bool ok;
            public string choice, model, error;
            public float confidence;
        }

        [Serializable] class Response { public string model; public Answers answers; }
        [Serializable] class Answers { public Clip clip; }
        [Serializable] class Clip { public string choice; public float confidence = -1f; }

        public static IEnumerator Choose(JevSettings settings, string state, string instructions,
            IReadOnlyDictionary<string, string> criteria, Action<Result> done)
        {
            var json = new StringBuilder();
            json.Append("{\"model\":").Append(Quote(settings.model))
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

            if (settings.logRequests) Debug.Log($"[Jev] 요청\n{state}");

            using var req = new UnityWebRequest(settings.endpoint, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json.ToString()));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + settings.apiKey.Trim());
            req.timeout = Mathf.CeilToInt(settings.timeoutSeconds);
            yield return req.SendWebRequest();

            var result = new Result();
            if (req.result != UnityWebRequest.Result.Success)
            {
                result.error = $"{req.responseCode} {req.error} {req.downloadHandler?.text}";
            }
            else
            {
                try
                {
                    var res = JsonUtility.FromJson<Response>(req.downloadHandler.text);
                    result.model = res?.model;
                    result.choice = res?.answers?.clip?.choice;
                    result.confidence = res?.answers?.clip?.confidence ?? -1f;
                    result.ok = !string.IsNullOrEmpty(result.choice);
                    if (!result.ok) result.error = "응답에 answers.clip.choice가 없음: " + req.downloadHandler.text;
                }
                catch (Exception e) { result.error = "응답 해석 실패: " + e.Message; }
            }

            if (settings.logRequests)
            {
                if (result.ok) Debug.Log($"[Jev] 선택 {result.choice} (점수 {result.confidence:0.00}, {result.model})");
                else Debug.LogWarning("[Jev] 실패: " + result.error);
            }
            done(result);
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
