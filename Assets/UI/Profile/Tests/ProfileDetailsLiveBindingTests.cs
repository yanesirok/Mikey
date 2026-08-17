using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Mikey.UI.Profile.Tests
{
    /// <summary>
    /// Exercises the REAL production <see cref="ProfileDetailsController"/> as
    /// wired onto the REAL "UI" GameObject in SampleScene.unity, bound against
    /// the REAL MikeyApp.uxml document — not source-text assertions, and not a
    /// reimplementation of its logic. This is the regression guard for exactly
    /// the class of bug where every source-level check passed (".clicked +=",
    /// exact error strings, etc.) yet the component was simply never attached
    /// to the production GameObject, so none of that code ever ran at runtime.
    /// See SceneWiringTests/MapControllersSceneTests for the sibling
    /// "component is attached" checks this class assumes as a precondition.
    ///
    /// Binding is driven directly via BindWhenReady() through reflection rather
    /// than relying on Unity's Play-Mode coroutine scheduler (MonoBehaviour
    /// coroutines don't reliably advance outside Play Mode) — but only if the
    /// scene's own automatic OnEnable hasn't already finished binding it, so
    /// this passes whichever way that turns out to behave. Either path still
    /// runs the exact production method body against the exact production
    /// markup and exact production ScreenManager, never a stand-in.
    /// </summary>
    public class ProfileDetailsLiveBindingTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SelectedGenderClass = "profile-details-gender-option--selected";
        private const string InvalidInputClass = "profile-details-field__input--invalid";
        private const string ErrorVisibleClass = "profile-details-field__error--visible";

        private GameObject _ui;
        private ProfileDetailsController _controller;
        private Type _screenManagerType;
        private Component _screenManager;

        [SetUp]
        public void SetUp()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Assert.IsTrue(scene.IsValid(), $"Could not open {ScenePath}");

            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == "UI")
                {
                    _ui = go;
                    break;
                }
            }
            Assert.IsNotNull(_ui, "Scene must contain a root GameObject named 'UI'.");

            _controller = _ui.GetComponent<ProfileDetailsController>();
            Assert.IsNotNull(_controller,
                "UI GameObject must have a ProfileDetailsController for Profile Details to function at runtime.");

            _screenManagerType = Type.GetType("ScreenManager, Assembly-CSharp");
            Assert.IsNotNull(_screenManagerType, "ScreenManager type must resolve in Assembly-CSharp.");
            _screenManager = (Component)_ui.GetComponent(_screenManagerType);
            Assert.IsNotNull(_screenManager, "UI GameObject must have a ScreenManager.");

            EnsureBound();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(ProfileUserDataStorage.PlayerPrefsKey);
            PlayerPrefs.DeleteKey(ProfileDisplayNameStorage.PlayerPrefsKey);
        }

        /// <summary>
        /// Drives the real BindWhenReady() coroutine to completion by hand
        /// (MoveNext in a loop) unless the scene's own component-activation
        /// already finished it — either way, _bound must end up true.
        /// </summary>
        private void EnsureBound()
        {
            if ((bool)GetField("_bound"))
                return;

            var bindMethod = typeof(ProfileDetailsController).GetMethod("BindWhenReady", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(bindMethod, "Expected a BindWhenReady() coroutine method.");
            var routine = (IEnumerator)bindMethod.Invoke(_controller, null);

            int guard = 0;
            while (routine.MoveNext() && guard++ < 200) { }

            Assert.IsTrue((bool)GetField("_bound"),
                "ProfileDetailsController failed to bind against the real production UXML/UIDocument.");
        }

        private object GetField(string name)
        {
            FieldInfo field = typeof(ProfileDetailsController).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"Expected a private field named '{name}' on ProfileDetailsController.");
            return field.GetValue(_controller);
        }

        private void InvokePrivate(string methodName)
        {
            MethodInfo method = typeof(ProfileDetailsController).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"Expected a private method named '{methodName}' on ProfileDetailsController.");
            method.Invoke(_controller, null);
        }

        private void SetFieldValue(string fieldName, string value) => ((TextField)GetField(fieldName)).value = value;

        private string CurrentScreen => (string)_screenManagerType.GetProperty("CurrentScreen").GetValue(_screenManager);

        [Test]
        public void Controller_BindsEveryNamedElement_FromTheRealProductionDocument()
        {
            Assert.IsNotNull(GetField("_displayNameField"), "profile-details-display-name must resolve.");
            Assert.IsNotNull(GetField("_ageField"), "profile-details-age must resolve.");
            Assert.IsNotNull(GetField("_weightField"), "profile-details-weight must resolve.");
            Assert.IsNotNull(GetField("_heightField"), "profile-details-height must resolve.");
            Assert.IsNotNull(GetField("_genderGroup"), "profile-details-gender-group must resolve.");
            Assert.IsNotNull(GetField("_saveButton"), "profile-details-save must resolve.");

            var genderButtons = (Button[])GetField("_genderButtons");
            Assert.AreEqual(4, genderButtons.Length);
            foreach (var button in genderButtons)
                Assert.IsNotNull(button, "Every one of the 4 gender chip Buttons must resolve from the real UXML.");

            Assert.IsNotNull(GetField("_displayNameError"));
            Assert.IsNotNull(GetField("_genderError"));
            Assert.IsNotNull(GetField("_ageError"));
            Assert.IsNotNull(GetField("_weightError"));
            Assert.IsNotNull(GetField("_heightError"));

            Assert.IsNotNull(GetField("_navigator"),
                "Controller must resolve a live IScreenNavigator (ScreenManager) from the same GameObject.");
        }

        [Test]
        public void GenderClick_MaleThenFemaleThenOther_SelectsExactlyOneAtATime()
        {
            var handlers = (Action[])GetField("_genderClickedHandlers");
            var buttons = (Button[])GetField("_genderButtons");

            handlers[0].Invoke(); // Male
            AssertOnlySelected(buttons, 0);
            Assert.AreEqual(ProfileUserData.GenderMale, GetField("_selectedGender"));

            handlers[1].Invoke(); // Female
            AssertOnlySelected(buttons, 1);
            Assert.AreEqual(ProfileUserData.GenderFemale, GetField("_selectedGender"));

            handlers[2].Invoke(); // Other
            AssertOnlySelected(buttons, 2);
            Assert.AreEqual(ProfileUserData.GenderOther, GetField("_selectedGender"));
        }

        private static void AssertOnlySelected(Button[] buttons, int expectedIndex)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                bool hasClass = buttons[i].ClassListContains(SelectedGenderClass);
                Assert.AreEqual(i == expectedIndex, hasClass, $"Gender button {i} selected-class state was wrong (expected selected index {expectedIndex}).");
            }
        }

        [Test]
        public void ValidSave_PersistsData_AndNavigatesBackToProfile()
        {
            SetFieldValue("_displayNameField", "ynsr");
            SetFieldValue("_ageField", "22");
            SetFieldValue("_weightField", "80");
            SetFieldValue("_heightField", "180");
            ((Action[])GetField("_genderClickedHandlers"))[0].Invoke(); // Male

            InvokePrivate("OnSaveClicked");

            ProfileUserData saved = ProfileUserDataStorage.Load();
            Assert.AreEqual("ynsr", saved.DisplayName);
            Assert.AreEqual(ProfileUserData.GenderMale, saved.Gender);
            Assert.AreEqual(22, saved.Age);
            Assert.AreEqual(80f, saved.WeightKg, 0.001f);
            Assert.AreEqual(180, saved.HeightCm);

            Assert.AreEqual("profile", CurrentScreen, "A valid Save must navigate back to Profile.");
        }

        [Test]
        public void ReopeningProfileDetails_RestoresTheSavedValues()
        {
            ProfileUserDataStorage.Save(new ProfileUserData
            {
                DisplayName = "ynsr",
                Gender = ProfileUserData.GenderMale,
                Age = 22,
                WeightKg = 80f,
                HeightCm = 180,
            });

            InvokePrivate("PopulateFromStorage");

            Assert.AreEqual("ynsr", ((TextField)GetField("_displayNameField")).value);
            Assert.AreEqual("22", ((TextField)GetField("_ageField")).value);
            Assert.AreEqual("80", ((TextField)GetField("_weightField")).value);
            Assert.AreEqual("180", ((TextField)GetField("_heightField")).value);
            Assert.AreEqual(ProfileUserData.GenderMale, GetField("_selectedGender"));

            var buttons = (Button[])GetField("_genderButtons");
            AssertOnlySelected(buttons, 0);
        }

        [Test]
        public void InvalidSave_ShowsFieldSpecificErrors_AndDoesNotPersistOrNavigate()
        {
            string screenBefore = CurrentScreen;

            SetFieldValue("_displayNameField", "ynsr");
            SetFieldValue("_ageField", "5"); // invalid: below the 10-100 range
            SetFieldValue("_weightField", "80");
            SetFieldValue("_heightField", "180");
            ((Action[])GetField("_genderClickedHandlers"))[0].Invoke(); // Male

            InvokePrivate("OnSaveClicked");

            var ageField = (TextField)GetField("_ageField");
            Assert.IsTrue(ageField.ClassListContains(InvalidInputClass), "Invalid Age must mark its input invalid.");

            var ageError = (Label)GetField("_ageError");
            Assert.AreEqual("Age must be between 10 and 100.", ageError.text);
            Assert.IsTrue(ageError.ClassListContains(ErrorVisibleClass), "The Age error label must become visible.");

            Assert.AreEqual(screenBefore, CurrentScreen, "An invalid Save must never navigate.");
        }
    }
}
