using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Garam.Subtitles
{
    /// <summary>
    /// Decodes alien glyphs into the existing Text content without changing its layout.
    /// Based on PlanetGame's OblationTerminalText; no game or audio manager is required.
    /// </summary>
    [AddComponentMenu("Garam/Subtitles/Alien Subtitle Effect")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Text))]
    public sealed class AlienSubtitleEffect : BaseMeshEffect
    {
        public const string DefaultSymbols = "△▽◇○□⊙⊕∴∵×";
        private const string FallbackSymbols = "<>+*/=|";

        [Header("Playback")]
        [Min(0f)] public float duration = 0.6f;
        [Range(1f, 60f)] public float scrambleRate = 18f;
        public bool playOnEnable = true;
        public bool useUnscaledTime = true;
        public bool reducedMotion;
        [Tooltip("Updating only digits (for example, a counter) does not restart decoding.")]
        public bool ignoreNumericChanges = true;

        [Header("Appearance")]
        [Tooltip("Only symbols present in the Text font are used. ASCII is the fallback.")]
        public string alienSymbols = DefaultSymbols;
        public Color alienTint = new Color32(155, 235, 248, 255);

        [Header("Optional feedback")]
        public UnityEvent onDecodeTick = new UnityEvent();
        public UnityEvent onCompleted = new UnityEvent();

        private Text label;
        private string message;
        private string supportedSymbols = string.Empty;
        private Font cachedFont;
        private string cachedSymbols;
        private int glyphSize;
        private float elapsed;
        private int phase = -1;
        private bool playing;

        public float Progress => !playing || reducedMotion || duration <= 0f
            ? 1f : Mathf.Clamp01(elapsed / duration);
        public bool IsPlaying => playing && Progress < 1f;

        protected override void Awake()
        {
            base.Awake();
            label = GetComponent<Text>();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureLabel();
            message = label.text;
            Restart(playOnEnable && Application.isPlaying);
        }

        protected override void OnDisable()
        {
            playing = false;
            base.OnDisable(); // Rebuild the original mesh when the effect is removed.
        }

        /// <summary>Sets the Korean/source subtitle and restarts, even for an identical line.</summary>
        public void Play(string subtitle)
        {
            EnsureLabel();
            label.text = subtitle ?? string.Empty;
            Replay();
        }

        /// <summary>Restarts decoding the current Text content.</summary>
        [ContextMenu("Replay Subtitle")]
        public void Replay()
        {
            EnsureLabel();
            message = label.text;
            Restart(isActiveAndEnabled);
        }

        /// <summary>Immediately reveals the source subtitle and completes an active playback.</summary>
        [ContextMenu("Complete Subtitle")]
        public void Complete()
        {
            bool notify = playing;
            playing = false;
            phase = -1;
            if (graphic != null)
                graphic.SetVerticesDirty();
            if (notify)
                onCompleted.Invoke();
        }

        private void EnsureLabel()
        {
            if (label == null)
                label = GetComponent<Text>();
        }

        private void Restart(bool animate, bool dirtyMesh = true)
        {
            elapsed = 0f;
            phase = -1;
            // Instant playback is completed in LateUpdate, outside a mesh rebuild.
            playing = animate && !string.IsNullOrEmpty(message);
            if (playing && Progress < 1f)
                WarmSymbols();
            if (dirtyMesh && graphic != null)
                graphic.SetVerticesDirty();
        }

        private void SynchronizeText(bool dirtyMesh = true)
        {
            if (message == label.text)
                return;

            string next = label.text;
            bool onlyDigits = ignoreNumericChanges && message != null && next.Length == message.Length;
            if (onlyDigits)
            {
                for (int i = 0; i < next.Length; i++)
                {
                    if (next[i] != message[i] && !(char.IsDigit(next[i]) && char.IsDigit(message[i])))
                    {
                        onlyDigits = false;
                        break;
                    }
                }
            }

            message = next;
            if (!onlyDigits)
                Restart(Application.isPlaying && isActiveAndEnabled, dirtyMesh);
        }

        private void LateUpdate()
        {
            SynchronizeText();
            if (!playing)
                return;

            elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (Progress >= 1f)
            {
                Complete();
                return;
            }

            int nextPhase = Mathf.FloorToInt(elapsed * Mathf.Max(1f, scrambleRate));
            if (nextPhase == phase)
                return;

            phase = nextPhase;
            WarmSymbols();
            graphic.SetVerticesDirty();
            if (supportedSymbols.Length > 0)
                onDecodeTick.Invoke();
        }

        private void WarmSymbols()
        {
            if (label.font == null)
            {
                supportedSymbols = string.Empty;
                cachedFont = null;
                return;
            }

            if (cachedFont != label.font || cachedSymbols != alienSymbols)
            {
                cachedFont = label.font;
                cachedSymbols = alienSymbols;
                var supported = new StringBuilder();
                AddSupportedSymbols(supported, alienSymbols);
                if (supported.Length == 0)
                    AddSupportedSymbols(supported, FallbackSymbols);
                supportedSymbols = supported.ToString();
            }

            glyphSize = label.resizeTextForBestFit ? label.cachedTextGenerator.fontSizeUsedForBestFit : label.fontSize;
            if (glyphSize <= 0)
                glyphSize = label.fontSize;
            if (label.font.dynamic && supportedSymbols.Length > 0)
                label.font.RequestCharactersInTexture(supportedSymbols, glyphSize, label.fontStyle);
        }

        private void AddSupportedSymbols(StringBuilder target, string candidates)
        {
            if (string.IsNullOrEmpty(candidates))
                return;
            foreach (char symbol in candidates)
            {
                if (!char.IsWhiteSpace(symbol) && !char.IsSurrogate(symbol) && !char.IsControl(symbol)
                    && label.font.HasCharacter(symbol))
                    target.Append(symbol);
            }
        }

        public override void ModifyMesh(VertexHelper vertices)
        {
            if (!IsActive())
                return;

            EnsureLabel();
            SynchronizeText(false);
            float progress = Progress;
            if (progress >= 1f || label.font == null)
                return;

            WarmSymbols();
            if (supportedSymbols.Length == 0)
                return;

            int quadCount = vertices.currentVertCount / 4;
            int visibleCount = 0;
            for (int quad = 0; quad < quadCount; quad++)
                if (IsVisibleQuad(vertices, quad * 4))
                    visibleCount++;

            int resolved = Mathf.FloorToInt(progress * visibleCount);
            int visibleIndex = 0;
            for (int quad = 0; quad < quadCount; quad++)
            {
                int firstVertex = quad * 4;
                if (!IsVisibleQuad(vertices, firstVertex))
                    continue;

                int index = visibleIndex++;
                if (index < resolved)
                    continue;

                char symbol = supportedSymbols[(index * 17 + Mathf.Max(0, phase)) % supportedSymbols.Length];
                if (!label.font.GetCharacterInfo(symbol, out CharacterInfo character, glyphSize, label.fontStyle))
                    continue;

                for (int corner = 0; corner < 4; corner++)
                {
                    var vertex = new UIVertex();
                    vertices.PopulateUIVertex(ref vertex, firstVertex + corner);
                    vertex.uv0 = corner == 0 ? character.uvTopLeft
                        : corner == 1 ? character.uvTopRight
                        : corner == 2 ? character.uvBottomRight : character.uvBottomLeft;
                    Color tint = Color.Lerp(alienTint, vertex.color, progress);
                    tint.a = vertex.color.a / 255f; // Preserve fades, rich-text alpha and CanvasGroup behavior.
                    vertex.color = tint;
                    vertices.SetUIVertex(vertex, firstVertex + corner);
                }
            }
        }

        private static bool IsVisibleQuad(VertexHelper vertices, int firstVertex)
        {
            var first = new UIVertex();
            var opposite = new UIVertex();
            vertices.PopulateUIVertex(ref first, firstVertex);
            vertices.PopulateUIVertex(ref opposite, firstVertex + 2);
            // Spaces and line breaks must not consume reveal time or become alien glyphs.
            return !Mathf.Approximately(first.position.x, opposite.position.x)
                && !Mathf.Approximately(first.position.y, opposite.position.y);
        }
    }
}
