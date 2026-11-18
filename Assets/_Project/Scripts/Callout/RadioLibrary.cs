using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace OnYourMark
{
    public sealed class RadioLine
    {
        public string id, speaker, recipient, category, english, korean, trigger, clipPath, action, actor, target, phase;
        public float seconds;
        public bool Commands => action.Length > 0 && (phase == "accepted" || phase == "started");
    }

    public sealed class RadioLibrary
    {
        [Serializable] class Mapping { public ActionDef[] actions; }
        [Serializable] class ActionDef { public string id; public Binding[] bindings; }
        [Serializable] class Binding { public string line_id, actor_ref, target_ref, speech_phase; }
        public readonly Dictionary<string, RadioLine> Lines = new();
        public GameObject RelayPrefab { get; }
        readonly Dictionary<string, AudioClip> clips;
        readonly Dictionary<string, AudioClip> statusClips;
        public AudioClip StatusClip(string id) => statusClips.TryGetValue(id,out var clip) ? clip : null;
        public AudioClip Clip(string id) => clips.TryGetValue(id, out var clip) ? clip : null;

        public RadioLibrary()
        {
            var pack = Resources.Load<RadioAssets>("TeamRadio");
            RelayPrefab = pack.relayPrefab;
            statusClips = pack.statusClips.ToDictionary(clip => clip.name.Substring(clip.name.IndexOf('_') + 1));
            clips = pack.clips.ToDictionary(clip => clip.name);
            var csv = pack.lines;
            var rows = CalloutCatalog.SplitCsv(csv.text.TrimStart('\ufeff'));
            var columns = rows[0].Select((name, col) => (name, col)).ToDictionary(x => x.name, x => x.col);
            foreach (var row in rows.Skip(1))
            {
                string C(string field) => row[columns[field]];
                var l = new RadioLine
                {
                    id = C("id"),
                    speaker = C("speaker"),
                    recipient = C("recipient"),
                    category = C("category"),
                    english = C("english"),
                    korean = C("korean"),
                    trigger = C("trigger_en"),
                    action = C("action_id"),
                    actor = C("actor_ref"),
                    target = C("target_ref"),
                    phase = C("speech_phase"),
                    seconds = float.Parse(C("seconds"), CultureInfo.InvariantCulture),
                    clipPath = "VoicePresets/" + System.IO.Path.ChangeExtension(C("file"), null)
                };
                Lines.Add(l.id, l);
            }
            var mapped = new HashSet<string>();
            var defs = JsonUtility.FromJson<Mapping>(pack.actions.text);
            foreach (var action in defs.actions)
                foreach (var binding in action.bindings)
                {
                    var l = Lines[binding.line_id];
                    if (!mapped.Add(l.id) || l.action != action.id || l.actor != binding.actor_ref ||
                        l.target != binding.target_ref || l.phase != binding.speech_phase)
                        throw new FormatException("Invalid radio action binding: " + l.id);
                }
            if (Lines.Values.Any(l => l.action.Length > 0 && !mapped.Contains(l.id)))
                throw new FormatException("Missing radio action binding");
        }
    }
}
