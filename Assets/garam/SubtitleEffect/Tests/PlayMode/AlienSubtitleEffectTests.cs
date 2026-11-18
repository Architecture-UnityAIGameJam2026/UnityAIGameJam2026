using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Garam.Subtitles.Tests
{
    public class AlienSubtitleEffectTests
    {
        private GameObject canvasObject;
        private Font font;
        private Text label;
        private AlienSubtitleEffect effect;
        private float previousTimeScale;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            canvasObject = new GameObject("Subtitle test canvas", typeof(Canvas));
            canvasObject.SetActive(false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var textObject = new GameObject("Subtitle", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(canvasObject.transform, false);
            label = textObject.GetComponent<Text>();
            font = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 32);
            label.font = font;
            label.fontSize = 32;
            label.rectTransform.sizeDelta = new Vector2(900, 240);
            label.color = new Color(0.9f, 0.8f, 0.7f, 0.6f);
            label.supportRichText = true;
            label.text = "SIGNAL 10\nDecode this message.";
            effect = textObject.AddComponent<AlienSubtitleEffect>();
            effect.playOnEnable = false;
            effect.duration = 0.6f;
            canvasObject.SetActive(true);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = previousTimeScale;
            Object.Destroy(canvasObject);
            Object.Destroy(font);
            yield return null;
        }

        [UnityTest]
        public IEnumerator KoreanDecodesLeftToRightWithoutChangingTextLayoutOrAlpha()
        {
            Assert.That(font.HasCharacter('한'), Is.True, "Install a Korean font to run this rendering test.");
            const string source = "<color=#FFA04080>외계 신호</color> 수신!\n한국어로 번역 중입니다.";
            label.text = source;
            // Warm the atlas before taking the original UV snapshot.
            effect.Replay();
            effect.Complete();
            Snapshot original = Capture();
            int completed = 0;
            effect.onCompleted.AddListener(() => completed++);

            effect.Replay();
            Snapshot alien = Capture();
            Assert.That(alien.positions.Length, Is.GreaterThan(0));
            CollectionAssert.AreEqual(original.positions, alien.positions);
            Assert.That(CountDifferentQuads(original, alien), Is.GreaterThan(0));
            for (int i = 0; i < alien.colors.Length; i++)
                Assert.That(alien.colors[i].a, Is.EqualTo(original.colors[i].a));
            Assert.That(label.text, Is.EqualTo(source));

            yield return new WaitForSecondsRealtime(0.3f);
            Snapshot partial = Capture();
            Assert.That(effect.Progress, Is.InRange(0.2f, 0.95f));
            int changed = CountDifferentQuads(original, partial);
            Assert.That(changed, Is.GreaterThan(0).And.LessThan(CountDifferentQuads(original, alien)));
            for (int i = 0; i < 4; i++)
                Assert.That(partial.uvs[i], Is.EqualTo(original.uvs[i]), "The first glyph resolves first.");
            CollectionAssert.AreEqual(original.positions, partial.positions);

            yield return new WaitForSecondsRealtime(0.45f);
            Snapshot decoded = Capture();
            CollectionAssert.AreEqual(original.positions, decoded.positions);
            CollectionAssert.AreEqual(original.uvs, decoded.uvs);
            CollectionAssert.AreEqual(original.colors, decoded.colors);
            Assert.That(label.text, Is.EqualTo(source));
            Assert.That(effect.IsPlaying, Is.False);
            Assert.That(completed, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CompleteAndDisableRestoreTheMeshAndIdenticalLinesCanReplay()
        {
            effect.Replay();
            effect.Complete();
            Snapshot original = Capture();
            int completed = 0;
            effect.onCompleted.AddListener(() => completed++);
            effect.Play(label.text);
            Assert.That(CountDifferentQuads(original, Capture()), Is.GreaterThan(0));
            effect.Complete();
            effect.Complete();
            CollectionAssert.AreEqual(original.uvs, Capture().uvs);
            Assert.That(completed, Is.EqualTo(1));

            effect.Replay();
            effect.enabled = false;
            CollectionAssert.AreEqual(original.uvs, Capture().uvs);
            Assert.That(effect.IsPlaying, Is.False);
            effect.enabled = true;
            Assert.That(effect.IsPlaying, Is.False);
            effect.playOnEnable = true;
            effect.enabled = false;
            effect.enabled = true;
            Assert.That(effect.IsPlaying, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DirectTextChangesRestartButDigitOnlyChangesCanKeepTheirProgress()
        {
            label.text = "SIGNAL 20";
            Capture();
            Assert.That(effect.IsPlaying, Is.True);
            effect.Complete();
            label.text = "SIGNAL 21";
            Capture();
            Assert.That(effect.IsPlaying, Is.False);
            effect.ignoreNumericChanges = false;
            label.text = "SIGNAL 22";
            Capture();
            Assert.That(effect.IsPlaying, Is.True);
            effect.Complete();
            label.text = "NEW SIGNAL";
            Capture();
            Assert.That(effect.IsPlaying, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator UnscaledPlaybackCompletesWhileTheGameIsPaused()
        {
            effect.duration = 0.15f;
            Time.timeScale = 0f;
            effect.Play("Paused subtitle");
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(effect.Progress, Is.EqualTo(1f));

            effect.useUnscaledTime = false;
            effect.Replay();
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(effect.Progress, Is.EqualTo(0f));
            Time.timeScale = 1f;
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(effect.IsPlaying, Is.False);
        }

        [UnityTest]
        public IEnumerator ZeroDurationReducedMotionAndEmptyInputNeverLeaveScrambledText()
        {
            effect.Replay();
            effect.Complete();
            Snapshot original = Capture();
            int completed = 0;
            effect.onCompleted.AddListener(() => completed++);
            effect.duration = 0f;
            effect.Replay();
            CollectionAssert.AreEqual(original.uvs, Capture().uvs);
            yield return null;
            Assert.That(completed, Is.EqualTo(1));

            effect.duration = 1f;
            effect.reducedMotion = true;
            effect.Replay();
            CollectionAssert.AreEqual(original.uvs, Capture().uvs);
            yield return null;
            Assert.That(completed, Is.EqualTo(2));
            effect.Play(null);
            Assert.That(label.text, Is.Empty);
            Assert.That(effect.IsPlaying, Is.False);
        }

        [UnityTest]
        public IEnumerator UnsupportedSymbolsFallBackAndFontChangesRemainUsable()
        {
            effect.alienSymbols = "\n\t\ud83d\udef8"; // Whitespace and surrogate pairs cannot be legacy glyphs.
            effect.Replay();
            effect.Complete();
            Snapshot original = Capture();
            effect.Replay();
            Assert.That(CountDifferentQuads(original, Capture()), Is.GreaterThan(0));
            label.font = null;
            yield return null;
            label.font = font;
            effect.Replay();
            Assert.That(Capture().positions.Length, Is.GreaterThan(0));
            effect.Complete();
            Assert.That(effect.Progress, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator BestFitAndOutlineKeepTheSameGeometryAfterDecoding()
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 16;
            label.resizeTextMaxSize = 40;
            label.rectTransform.sizeDelta = new Vector2(250, 100);
            label.gameObject.AddComponent<Outline>(); // The decoder must precede geometry-expanding effects.
            effect.Replay();
            effect.Complete();
            Snapshot original = Capture();
            effect.Replay();
            Snapshot alien = Capture();
            CollectionAssert.AreEqual(original.positions, alien.positions);
            Assert.That(CountDifferentQuads(original, alien), Is.GreaterThan(0));
            effect.Complete();
            CollectionAssert.AreEqual(original.uvs, Capture().uvs);
            yield return null;
        }

        private Snapshot Capture()
        {
            Canvas.ForceUpdateCanvases();
            Mesh mesh = label.canvasRenderer.GetMesh();
            return new Snapshot { positions = mesh.vertices, uvs = mesh.uv, colors = mesh.colors32 };
        }

        private static int CountDifferentQuads(Snapshot first, Snapshot second)
        {
            Assert.That(second.uvs.Length, Is.EqualTo(first.uvs.Length));
            int count = 0;
            for (int i = 0; i < first.uvs.Length; i += 4)
            {
                for (int j = i; j < Mathf.Min(i + 4, first.uvs.Length); j++)
                {
                    if (first.uvs[j] == second.uvs[j])
                        continue;
                    count++;
                    break;
                }
            }
            return count;
        }

        private struct Snapshot
        {
            public Vector3[] positions;
            public Vector2[] uvs;
            public Color32[] colors;
        }
    }
}
