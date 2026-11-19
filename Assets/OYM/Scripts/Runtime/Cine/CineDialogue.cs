using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OYM.Cine
{
    /// One spoken line. `text` = what is said (the VO script, English), `sub` = the subtitle shown (Korean; empty = text).
    /// `voice` = Resources path of the VO clip (empty: timed from the text length). `shot` = camera for the line:
    /// "ots" (over the listener's shoulder, default), "two" (both from the side), "close" (tight on the speaker),
    /// "keep" (stay on the previous camera). `look` = where the speaker looks: "partner" (default), "ahead", "camera".
    [System.Serializable]
    public class DialogueLine
    {
        public string speaker;
        public string text;
        public string sub;
        public string voice;
        public string shot;
        public string look;
        [Tooltip("extra seconds after the line (pause before the next one)")]
        public float hold;
    }

    [System.Serializable]
    public class DialogueScript
    {
        public DialogueLine[] lines = new DialogueLine[0];

        /// Resources/OYM_Cine/<name>.json
        public static DialogueScript Load(string name)
        {
            var ta = Resources.Load<TextAsset>("OYM_Cine/" + name);
            if (!ta) { Debug.LogWarning("[OYM-CINE] no dialogue Resources/OYM_Cine/" + name); return new DialogueScript(); }
            var d = JsonUtility.FromJson<DialogueScript>(ta.text);
            return d ?? new DialogueScript();
        }
    }

    /// Plays a DialogueScript between CineActors: per line it cuts the CineCamera to the line's shot, turns the
    /// speaker's head to the listener (and the listener's to the speaker), shows the subtitle, plays the VO and waits
    /// for it (or for a time from the text length). Click / Space / Enter: finish typing, then next line.
    public class CineDialogue
    {
        public CineCamera cam;
        public CineOverlay overlay;
        public AudioSource voice;
        public System.Func<string, CineActor> actor;
        public System.Func<string, Color> accent;
        public bool useSubtitles = true;
        /// look point for "ahead" (e.g. the objective)
        public Vector3 aheadPoint;
        public float minLine = 1.8f, maxLine = 6f, perChar = 0.055f, gap = 0.25f;
        public bool skipped;
        public System.Func<bool> abort;
        /// false in recordings: real key presses must not advance lines
        public bool allowInput = true;
        /// (line index, line) as each line goes up on screen (the co-op HUD swaps on the "visor" line)
        public static event System.Action<int, DialogueLine> LineStarted;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { LineStarted = null; }

        public IEnumerator Play(DialogueScript script)
        {
            if (script?.lines == null) yield break;
            CineActor prevSpeaker = null;
            int ots = 0;
            for (int li = 0; li < script.lines.Length; li++)
            {
                if (abort != null && abort()) yield break;
                var line = script.lines[li];
                var s = actor(line.speaker);
                CineActor l = null;
                foreach (var name in Names(script)) { var a = actor(name); if (a && a != s) { l = a; break; } }

                // camera
                string shot = string.IsNullOrEmpty(line.shot) ? "ots" : line.shot;
                if (shot != "keep" && cam && s)
                {
                    CineShot cs;
                    if (shot == "two" && l) cs = CineShot.TwoShot(s.transform, l.transform, (ots & 1) == 0 ? 1f : -1f, 0.05f, 1.55f, 36f).Look(new Vector3(0f, 1.5f, 0f)).Lag(3f, 5f).Handheld(0.25f);
                    else if (shot == "close" || !l) cs = CineShot.Follow(s.transform, new Vector3(0.35f, 1.62f, 1.6f), FollowSpace.Yaw, 30f).Look(new Vector3(0f, 1.55f, 0f)).Lag(4f, 6f).Handheld(0.2f).Dof(1f);
                    else cs = CineShot.OverShoulder(l.transform, s.transform, Side(l, s), 0.16f, 1.15f, 32f).Look(new Vector3(0f, 1.52f, 0f)).Lag(3f, 5f).Handheld(0.22f);
                    cs.name = "line" + li;
                    // first line blends in from the establishing shot; then hard cuts (keep the same side per speaker)
                    cam.Play(cs, li == 0 ? 1.1f : (s == prevSpeaker ? 0.6f : 0f));
                    ots++;
                }
                // heads
                string look = string.IsNullOrEmpty(line.look) ? "partner" : line.look;
                if (s)
                {
                    if (look == "ahead") s.LookAt(aheadPoint, 1f);
                    else if (look == "camera" && cam) s.LookAt(cam.transform, 1f);
                    else if (l) s.LookAt(l.Head, 1f);
                    s.Talk(true);
                }
                if (l) l.LookAt(s ? s.Head : null, 0.9f);

                // subtitle + voice
                string shown = useSubtitles && !string.IsNullOrEmpty(line.sub) ? line.sub : line.text;
                Color acc = accent != null ? accent(line.speaker) : Color.white;
                if (overlay) overlay.Say(line.speaker, shown, acc);
                LineStarted?.Invoke(li, line);
                AudioClip clip = string.IsNullOrEmpty(line.voice) ? null : Resources.Load<AudioClip>(line.voice);
                float len = clip ? clip.length + 0.3f : Mathf.Clamp(0.9f + perChar * (line.text ?? shown ?? "").Length, minLine, maxLine);
                if (clip && voice) { voice.clip = clip; voice.Play(); }

                float t = 0f;
                bool advance = false;
                while (t < len && !advance)
                {
                    if (abort != null && abort()) { if (s) s.Talk(false); yield break; }
                    t += Time.deltaTime;
                    if (allowInput && Pressed())
                    {
                        if (overlay && overlay.Typing) overlay.FinishTyping();
                        else advance = true;
                    }
                    yield return null;
                }
                if (voice && advance) voice.Stop();
                if (s) s.Talk(false);
                if (overlay) overlay.ClearLine();
                float g = gap + Mathf.Max(0f, line.hold);
                while (g > 0f) { g -= Time.deltaTime; yield return null; }
                prevSpeaker = s;
            }
        }

        static IEnumerable<string> Names(DialogueScript d)
        {
            var seen = new HashSet<string>();
            foreach (var l in d.lines) if (l != null && !string.IsNullOrEmpty(l.speaker) && seen.Add(l.speaker)) yield return l.speaker;
        }

        /// Over-the-shoulder side: keep both cameras on the same side of the line between the two (180-degree rule).
        static float Side(CineActor listener, CineActor speaker)
        {
            // a fixed world order: the actor with the smaller instance id always gets its right shoulder used
            return listener.GetInstanceID() < speaker.GetInstanceID() ? 0.55f : -0.55f;
        }

        static bool Pressed()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current; var m = Mouse.current;
            return (k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame)) || (m != null && m.leftButton.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(0);
#endif
        }
    }
}
