using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace OYM.Signal
{
    /// One prerecorded radio line (Resources/OYM_Radio/catalog.csv, built by pipeline/jevmark/build_radio.py).
    public sealed class RadioLine
    {
        public string id, clipPath, speaker, english, korean, usage, role, partner;
        public string[] events;
        public float seconds, cooldown;
        public int priority;
        AudioClip _clip;
        bool _loaded;

        public bool Jev => speaker == "JEV";
        public AudioClip Clip
        {
            get
            {
                if (!_loaded) { _loaded = true; _clip = Resources.Load<AudioClip>(clipPath); if (!_clip) Debug.LogWarning("[OYM-RADIO] missing clip " + clipPath); }
                return _clip;
            }
        }

        public bool Has(string tag) { foreach (var e in events) if (e == tag) return true; return false; }
    }

    public sealed class RadioCatalog
    {
        public readonly List<RadioLine> Lines = new List<RadioLine>();
        public readonly Dictionary<string, RadioLine> ById = new Dictionary<string, RadioLine>();

        public static RadioCatalog Load()
        {
            var cat = new RadioCatalog();
            var ta = Resources.Load<TextAsset>("OYM_Radio/catalog");
            if (!ta) { Debug.LogWarning("[OYM-RADIO] Resources/OYM_Radio/catalog.csv missing"); return cat; }
            var rows = SplitCsv(ta.text.TrimStart('﻿'));
            if (rows.Count < 2) return cat;
            var head = rows[0];
            int C(string n) => head.FindIndex(h => h.Trim() == n);
            int id = C("id"), clip = C("clip"), sp = C("speaker"), en = C("english"), ko = C("korean"), sec = C("seconds"), ev = C("events"),
                role = C("role"), partner = C("partner"), cd = C("cooldown"), pri = C("priority"), use = C("usage");
            for (int i = 1; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r.Count < head.Count || string.IsNullOrEmpty(r[id])) continue;
                var l = new RadioLine
                {
                    id = r[id], clipPath = r[clip], speaker = r[sp], english = r[en], korean = r[ko], usage = r[use],
                    role = r[role], partner = r[partner],
                    events = r[ev].Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries),
                    seconds = F(r[sec]), cooldown = F(r[cd]), priority = (int)F(r[pri]),
                };
                cat.Lines.Add(l);
                cat.ById[l.id] = l;
            }
            return cat;
        }

        static float F(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;

        // quoted cells (commas, doubled quotes) and CRLF
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
