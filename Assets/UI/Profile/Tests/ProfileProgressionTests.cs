using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mikey.UI.Progression;

namespace Mikey.UI.Profile.Tests
{
    /// <summary>
    /// Contract for the Profile navigation-lock-bypass fix: Profile's Map/Techniques
    /// dock tabs must consult the same <see cref="TutorialProgressPresenter"/>
    /// Level1Unlocked gate Home already uses (see TutorialProgressPresenterTests for
    /// the NewPlayer=locked / Level1Unlocked=unlocked boundary this reuses), so a
    /// production player can no longer bypass Home's lock via Profile. Verified by
    /// reading the source, mirroring HomeControllerSourceTests' established
    /// technique for MonoBehaviour internals not practical to drive through a live
    /// panel in EditMode.
    /// </summary>
    public class ProfileProgressionTests
    {
        private const string SourcePath = "Assets/UI/Profile/ProfileController.cs";
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [Test]
        public void NavMapClick_IsGatedByIsMapUnlocked_SoNewPlayerCannotReachMap_AndLevel1UnlockedCan()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnNavMapClicked()", source);
            StringAssert.Contains("if (!TutorialProgressPresenter.IsMapUnlocked(_progress.State))", source,
                "Before Level1Unlocked, IsMapUnlocked(NewPlayer) is false (see TutorialProgressPresenterTests), so this must return without navigating.");
            StringAssert.Contains("_navigator.Show(\"map\");", source,
                "From Level1Unlocked onward, IsMapUnlocked is true, so the click must still navigate to Map.");
        }

        [Test]
        public void NavTechniquesClick_IsGatedByIsTechniquesUnlocked_SoNewPlayerCannotReachTechniques_AndLevel1UnlockedCan()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnNavTechniquesClicked()", source);
            StringAssert.Contains("if (!TutorialProgressPresenter.IsTechniquesUnlocked(_progress.State))", source,
                "Before Level1Unlocked, IsTechniquesUnlocked(NewPlayer) is false (see TutorialProgressPresenterTests), so this must return without navigating.");
            StringAssert.Contains("_navigator.Show(\"techniques\");", source,
                "From Level1Unlocked onward, IsTechniquesUnlocked is true, so the click must still navigate to Techniques.");
        }

        [Test]
        public void BindWhenReady_WiresBothDockTabsByTheirRenamedNonGoNames()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("root.Q<VisualElement>(\"profile-nav-map\")", source);
            StringAssert.Contains("root.Q<VisualElement>(\"profile-nav-techniques\")", source);
        }

        [Test]
        public void OnDisable_UnregistersBothCallbacks_NoLeak()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_navMap.UnregisterCallback(_navMapClickCallback);", source);
            StringAssert.Contains("_navTechniques.UnregisterCallback(_navTechniquesClickCallback);", source);
        }

        private static GameObject OpenSceneAndFindUi()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Assert.IsTrue(scene.IsValid(), $"Could not open {ScenePath}");

            GameObject ui = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == "UI")
                {
                    ui = go;
                    break;
                }
            }
            Assert.IsNotNull(ui, "Scene must contain a root GameObject named 'UI'.");
            return ui;
        }

        [Test]
        public void UiGameObject_HasProfileController()
        {
            GameObject ui = OpenSceneAndFindUi();
            Assert.IsNotNull(ui.GetComponent<ProfileController>(),
                "UI GameObject must have a ProfileController for the Profile navigation-lock-bypass fix to run in a real build.");
        }
    }
}
