using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.SafeArea.Tests
{
    /// <summary>
    /// Contract for ScreenManager's screen-entry signal (IScreenNavigator). The
    /// signal must fire exactly once per genuine screen change, not on a redundant
    /// re-request of the current screen, and must fire again on re-entry — this is
    /// the deterministic hook the Camera Test reset relies on. ScreenManager lives in
    /// Assembly-CSharp (an asmdef can't reference it), so it's created via reflection,
    /// the same lookup-by-name approach as SceneWiringTests.
    /// </summary>
    public class ScreenNavigatorTests
    {
        private GameObject _go;
        private object _screenManager;
        private MethodInfo _show;
        private Type _screenManagerType;

        [SetUp]
        public void SetUp()
        {
            _screenManagerType = Type.GetType("ScreenManager, Assembly-CSharp");
            Assert.IsNotNull(_screenManagerType, "ScreenManager type must resolve in Assembly-CSharp.");

            // Inactive so OnEnable (which needs a live UIDocument root) does not run;
            // we drive Show() directly to exercise the pure event logic.
            _go = new GameObject("nav-test");
            _go.SetActive(false);
            _go.AddComponent<UIDocument>();
            _screenManager = _go.AddComponent(_screenManagerType);

            _show = _screenManagerType.GetMethod("Show", new[] { typeof(string) });
            Assert.IsNotNull(_show, "ScreenManager must expose Show(string).");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                UnityEngine.Object.DestroyImmediate(_go);
        }

        private IScreenNavigator Nav => (IScreenNavigator)_screenManager;
        private void Show(string id) => _show.Invoke(_screenManager, new object[] { id });

        [Test]
        public void ScreenManager_ImplementsIScreenNavigator()
        {
            Assert.IsInstanceOf<IScreenNavigator>(_screenManager);
            Assert.IsNull(Nav.CurrentScreen, "CurrentScreen must be null before the first show.");
        }

        [Test]
        public void ScreenChanged_FiresOnceWithNewId_AndTracksCurrentScreen()
        {
            int count = 0;
            string last = null;
            Nav.ScreenChanged += id => { count++; last = id; };

            Show("camTest");

            Assert.AreEqual(1, count, "Entering a new screen must fire exactly once.");
            Assert.AreEqual("camTest", last);
            Assert.AreEqual("camTest", Nav.CurrentScreen);
        }

        [Test]
        public void ScreenChanged_DoesNotFire_WhenSameScreenReRequested()
        {
            int count = 0;
            Nav.ScreenChanged += _ => count++;

            Show("camTest");
            Show("camTest"); // redundant request while already active

            Assert.AreEqual(1, count, "A redundant re-request of the active screen must not re-fire.");
        }

        [Test]
        public void ScreenChanged_FiresAgain_OnReEntryAfterLeaving()
        {
            int camTestEntries = 0;
            Nav.ScreenChanged += id => { if (id == "camTest") camTestEntries++; };

            Show("camTest"); // enter
            Show("combine"); // leave
            Show("camTest"); // re-enter

            Assert.AreEqual(2, camTestEntries, "Re-entering camTest after leaving must fire again.");
        }

        [Test]
        public void OtherScreens_FireWithTheirOwnId()
        {
            string last = null;
            Nav.ScreenChanged += id => last = id;

            Show("camTest");
            Show("combine");
            Assert.AreEqual("combine", last);
            Show("menu");
            Assert.AreEqual("menu", last);
        }

        [Test]
        public void Unsubscribe_StopsCallbacks_NoLeak()
        {
            int count = 0;
            Action<string> handler = _ => count++;
            Nav.ScreenChanged += handler;

            Show("camTest");
            Assert.AreEqual(1, count);

            Nav.ScreenChanged -= handler;
            Show("combine");
            Show("camTest");

            Assert.AreEqual(1, count, "No callbacks may fire after unsubscribing (no leak).");
        }

        [Test]
        public void NavigatorBindings_AreUnregisteredOnDisable()
        {
            string source = File.ReadAllText("Assets/UI/ScreenManager.cs");
            StringAssert.Contains("_navigatorBindings", source,
                "ScreenManager must retain navigator callback bindings for teardown.");
            StringAssert.Contains("private void OnDisable()", source,
                "ScreenManager must implement OnDisable to remove navigator callbacks.");
            StringAssert.Contains("_navigatorBindings[i].Unbind();", source,
                "ScreenManager.OnDisable must unregister each navigator callback.");
            StringAssert.Contains("_navigatorBindings.Clear();", source,
                "ScreenManager.OnDisable must clear callback binding storage.");
            StringAssert.Contains("button.clicked -= _buttonCallback", source,
                "Button navigators must remove their previously-added clicked callback.");
            StringAssert.Contains("UnregisterCallback(_elementCallback)", source,
                "VisualElement navigators must remove their previously-registered ClickEvent callback.");
        }
    }
}
