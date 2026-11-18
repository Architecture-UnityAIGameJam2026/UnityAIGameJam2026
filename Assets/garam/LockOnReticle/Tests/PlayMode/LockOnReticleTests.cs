#if UNITY_EDITOR
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Garam.Reticles.Tests
{
    public class LockOnReticleTests
    {
        private GameObject root;
        private Camera camera;
        private ReticleTarget target;
        private LockOnReticle reticle;
        private float previousTimeScale;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            camera = new GameObject("Reticle test camera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.position = Vector3.zero;
            camera.transform.rotation = Quaternion.identity;
            camera.fieldOfView = 60f;
            var enemy = GameObject.CreatePrimitive(PrimitiveType.Cube);
            enemy.name = "Lock target";
            enemy.transform.position = new Vector3(0, 0, 8);
            enemy.transform.localScale = new Vector3(1, 2, 1);
            target = enemy.AddComponent<ReticleTarget>();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/garam/LockOnReticle/Prefabs/LockOnReticle.prefab");
            Assert.That(prefab, Is.Not.Null);
            root = Object.Instantiate(prefab);
            reticle = root.GetComponent<LockOnReticle>();
            reticle.worldCamera = camera;
            reticle.transitionDuration = 0f;
            reticle.acquireDuration = 0f;
            reticle.farPulse = 0f;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = previousTimeScale;
            if (target != null) Object.Destroy(target.gameObject);
            Object.Destroy(root);
            Object.Destroy(camera.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PrefabChangesFromSmallAimToEnclosingRingAndThreeArrowState()
        {
            Assert.That(reticle.State, Is.EqualTo(ReticleState.Idle));
            Assert.That(reticle.idle.alpha, Is.EqualTo(1f));
            Assert.That(reticle.near.alpha, Is.Zero);
            reticle.LockOn(target);
            yield return null;
            Assert.That(reticle.State, Is.EqualTo(ReticleState.Near));
            Assert.That(reticle.near.alpha, Is.EqualTo(1f));
            float size = reticle.nearRing.sizeDelta.x;
            target.transform.localScale *= 1.5f;
            yield return null;
            Assert.That(reticle.nearRing.sizeDelta.x, Is.GreaterThan(size));
            Assert.That(reticle.nearRing.sizeDelta.x, Is.EqualTo(reticle.nearRing.sizeDelta.y));

            target.transform.position = new Vector3(2, 0, 20);
            yield return null;
            Assert.That(reticle.State, Is.EqualTo(ReticleState.Far));
            Assert.That(reticle.far.alpha, Is.EqualTo(1f));
            Assert.That(reticle.farArrows.sizeDelta, Is.EqualTo(new Vector2(160, 160)));
            Vector2 expected = camera.WorldToScreenPoint(target.AimPosition);
            Vector2 actual = RectTransformUtility.WorldToScreenPoint(null, reticle.marker.position);
            Assert.That(Vector2.Distance(expected, actual), Is.LessThan(1f));
            Assert.That(reticle.visibility.blocksRaycasts, Is.False);

            reticle.ReleaseTarget();
            yield return null;
            Assert.That(reticle.State, Is.EqualTo(ReticleState.Idle));
            Assert.That(reticle.idle.alpha, Is.EqualTo(1f));
            actual = RectTransformUtility.WorldToScreenPoint(null, reticle.marker.position);
            Assert.That(Vector2.Distance(camera.pixelRect.center, actual), Is.LessThan(1f));
        }

        [UnityTest]
        public IEnumerator RangeHysteresisAndManualModesAvoidBoundaryFlicker()
        {
            reticle.LockOn(target);
            yield return null;
            target.transform.position = new Vector3(0, 0, 12.4f);
            yield return null;
            Assert.That(reticle.State, Is.EqualTo(ReticleState.Near));
            target.transform.position = new Vector3(0, 0, 13f);
            yield return null;
            Assert.That(reticle.State, Is.EqualTo(ReticleState.Far));
            target.transform.position = new Vector3(0, 0, 11.8f);
            yield return null;
            Assert.That(reticle.State, Is.EqualTo(ReticleState.Far));
            reticle.rangeMode = ReticleRangeMode.Near;
            yield return null;
            Assert.That(reticle.State, Is.EqualTo(ReticleState.Near));
            reticle.rangeMode = ReticleRangeMode.Far;
            yield return null;
            Assert.That(reticle.State, Is.EqualTo(ReticleState.Far));

            reticle.rangeMode = ReticleRangeMode.Automatic;
            reticle.distanceOrigin = target.transform;
            yield return null;
            Assert.That(reticle.State, Is.EqualTo(ReticleState.Near));
        }

        [UnityTest]
        public IEnumerator OffscreenTargetsHideAndDestroyedTargetsReleaseOnce()
        {
            int released = 0;
            reticle.onReleased.AddListener(() => released++);
            reticle.LockOn(target);
            target.transform.position = new Vector3(0, 0, -5);
            yield return null;
            Assert.That(reticle.visibility.alpha, Is.Zero);
            Assert.That(reticle.IsLocked, Is.True);
            target.transform.position = new Vector3(0, 0, 8);
            yield return null;
            Assert.That(reticle.visibility.alpha, Is.EqualTo(1f));
            Object.Destroy(target.gameObject);
            yield return null;
            yield return null;
            Assert.That(reticle.IsLocked, Is.False);
            Assert.That(reticle.State, Is.EqualTo(ReticleState.Idle));
            reticle.ReleaseTarget();
            Assert.That(released, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator FarArrowsConvergeOnThePivotEvenWhenTimeScaleIsZero()
        {
            reticle.rangeMode = ReticleRangeMode.Far;
            reticle.acquireDuration = 0.2f;
            Time.timeScale = 0f;
            reticle.LockOn(target);
            yield return null;
            Assert.That(reticle.farArrows.localScale.x, Is.GreaterThan(1f));
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(reticle.farArrows.localScale.x, Is.EqualTo(1f).Within(0.001f));
            reticle.ReleaseTarget();
            reticle.reducedMotion = true;
            reticle.LockOn(target);
            yield return null;
            Assert.That(reticle.farArrows.localScale.x, Is.EqualTo(1f));
        }

        [Test]
        public void FigmaSpritesContainTransparentBackgroundsAndVisibleArtwork()
        {
            foreach (string name in new[] { "idle", "near", "far" })
            {
                var texture = new Texture2D(2, 2);
                try
                {
                    string path = "Assets/garam/LockOnReticle/Art/reticle-" + name + ".png";
                    Assert.That(texture.LoadImage(File.ReadAllBytes(path)), Is.True);
                    Color32[] pixels = texture.GetPixels32();
                    Assert.That(System.Array.Exists(pixels, p => p.a == 0), Is.True, name + " must be transparent.");
                    Assert.That(System.Array.Exists(pixels, p => p.a > 0), Is.True, name + " must contain artwork.");
                }
                finally { Object.DestroyImmediate(texture); }
            }
            foreach (Image image in root.GetComponentsInChildren<Image>())
            {
                Assert.That(image.sprite, Is.Not.Null);
                Assert.That(image.raycastTarget, Is.False);
            }
        }
    }
}
#endif
