#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OYM.Tests
{
    public class TitleMatchMenuTests
    {
        [Test]
        public void RoomPanelKeepsTheOriginalCopyAndJoinControls()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/OYM/Title/MainTitle.uxml").CloneTree();
            var room = tree.Q("friend-panel");
            CollectionAssert.AreEqual(new[] { "btn-copy", "btn-join", "btn-back" },
                room.Query<VisualElement>().ToList().Select(e => e.name).Where(n => n != null && n.StartsWith("btn-")));
            Assert.That(room.Q<TextField>("fp-field").maxLength, Is.EqualTo(6));
        }

        [UnityTest]
        public IEnumerator GameStartOpensModeSelectionAndBackRestoresTheTitleMenu()
        {
            yield return SceneManager.LoadSceneAsync("MainTitle");
            var root = UnityEngine.Object.FindFirstObjectByType<UIDocument>().rootVisualElement;
            var menu = root.Q("menu");
            yield return WaitFor(() => root.Q("menu-cursor").ClassListContains("menu-cursor--on"));
            // The title enables the main menu at the end of the intro animation.
            yield return new WaitForSecondsRealtime(.3f);
            using (var click = ClickEvent.GetPooled())
            {
                var start = menu.Q("mi-0");
                click.target = start;
                start.SendEvent(click);
            }
            yield return WaitFor(() => menu.worldBound.xMax < 0 && root.Q("play-modes").worldBound.xMin >= 0);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("MainTitle"));
            var modes = root.Q("play-modes");
            CollectionAssert.AreEqual(new[] { "SINGLE PLAYER", "MULTIPLAYER", "BACK" },
                modes.Query<Button>().ToList().Select(button => button.text));
            Assert.That(modes.worldBound.xMin, Is.GreaterThanOrEqualTo(0));
            var back = modes.Q<Button>("btn-modes-back");
            back.Focus();
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = back;
                back.SendEvent(submit);
            }
            yield return WaitFor(() => menu.worldBound.xMin >= 0 && modes.worldBound.xMax < 0);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("MainTitle"));
        }

        static IEnumerator WaitFor(Func<bool> ready)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 12;
            while (!ready())
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), "Title transition timed out");
                yield return null;
            }
        }
    }
}
#endif
