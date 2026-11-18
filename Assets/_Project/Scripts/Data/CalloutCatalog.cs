using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 콜아웃 대사 목록(lines.csv)과 Jev에게 보여줄 후보·사용 조건, 알림 기준값.
    /// </summary>
    [CreateAssetMenu(menuName = "On Your Mark/Callout Catalog")]
    public class CalloutCatalog : ScriptableObject
    {
        [Serializable]
        public class Candidate
        {
            public string id;
            [Tooltip("Jev에게 알려줄 사용 조건")]
            [TextArea] public string usage;
            [Tooltip("켜면 상황의 주인공이 말하고 파트너가 듣는다 (예: downed). 끄면 파트너가 주인공에게 말한다")]
            public bool spokenBySubject;
        }

        public struct Line
        {
            public string id, english, korean, category;
        }

        public TextAsset linesCsv;
        public Candidate[] candidates;

        [Header("상황 기준")]
        [Range(0, 1)] public float lowHealth = 0.4f;
        [Range(0, 1)] public float criticalHealth = 0.15f;
        [Range(0, 1)] public float lowAmmo = 0.25f;

        [Header("표시 · 호출 간격")]
        public float displaySeconds = 4f;
        [Tooltip("같은 플레이어에 대해 Jev를 다시 부르기까지의 최소 간격")]
        public float cooldownSeconds = 5f;

        Dictionary<string, Line> lines;

        public bool TryGetLine(string id, out Line line)
        {
            if (lines == null) Parse();
            return lines.TryGetValue(id, out line);
        }

        public Candidate FindCandidate(string id) => Array.Find(candidates, c => c.id == id);

        void OnValidate() => lines = null;

        void Parse()
        {
            lines = new Dictionary<string, Line>();
            if (linesCsv == null) return;
            var rows = SplitCsv(linesCsv.text.TrimStart('﻿'));
            if (rows.Count == 0) return;
            var header = rows[0];
            int Col(string name) => header.FindIndex(h => h.Trim() == name);
            int id = Col("id"), en = Col("english"), ko = Col("korean"), cat = Col("category");
            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row.Count <= Mathf.Max(id, en, ko, cat) || string.IsNullOrEmpty(row[id])) continue;
                lines[row[id]] = new Line { id = row[id], english = row[en], korean = row[ko], category = row[cat] };
            }
        }

        // 따옴표로 감싼 칸(쉼표 포함)을 처리하는 최소 CSV 파서
        static List<List<string>> SplitCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else cell.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(cell.ToString()); cell.Clear();
                    rows.Add(row); row = new List<string>();
                }
                else cell.Append(c);
            }
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }
    }
}
