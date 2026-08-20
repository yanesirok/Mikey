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
    /// Contract for Profile's shared-HUD navigation: Techniques keeps consulting
    /// <see cref="TutorialProgressPresenter"/>'s Level1Unlocked gate Home already
    /// uses (see TutorialProgressPresenterTests for the NewPlayer=locked /
    /// Level1Unlocked=unlocked boundary this reuses), so a production player can
    /// no longer bypass Home's lock via Profile. Map is deliberately UNGATED —
    /// see NavMapClick_IsUngated_MirroringMenusGoMapPlayButton for why gating it
    /// made it a dead duplicate of Main Menu's own always-available PLAY.
    /// Verified by reading the source, mirroring HomeControllerSourceTests'
    /// established technique for MonoBehaviour internals not practical to drive
    /// through a live panel in EditMode.
    /// </summary>
    public class ProfileProgressionTests
    {
        private const string SourcePath = "Assets/UI/Profile/ProfileController.cs";
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [Test]
        public void NavMapClick_IsUngated_MirroringMenusGoMapPlayButton()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private void OnNavMapClicked() => _navigator?.Show(\"map\");", source,
                "Map must be a plain, unconditional navigation — Main Menu's 'go-map' PLAY button is never gated either " +
                "(see HomeControllerSourceTests), so gating Profile's copy just made it a dead duplicate.");
            StringAssert.DoesNotContain("IsMapUnlocked", source,
                "The old Level1Unlocked gate on Map must be fully removed, not just bypassed in one place.");
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
