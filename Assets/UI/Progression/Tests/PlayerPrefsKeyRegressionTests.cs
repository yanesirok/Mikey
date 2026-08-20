using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Mikey.UI.Progression.Tests
{
    /// <summary>
    /// Regression guard for the Profile/shared-nav redesign: real persisted
    /// storage in the app is now PlayerPrefsTutorialProgressStorage's
    /// "Mikey.TutorialProgress.State" key, PlayerPrefsAudioSettingsStorage's
    /// Music/SFX/Trainer Voice volumes, ProfileDisplayNameStorage's old
    /// "Mikey.Profile.DisplayName" key (retained, unwritten, purely as a
    /// migration source — see ProfileUserDataStorage), and
    /// ProfileUserDataStorage's "Mikey.Profile.UserData" key — the one primary
    /// key for Display Name/Gender/Age/Weight/Height. Every LVL/XP/streak number
    /// on Profile and the shared top HUD is still frontend mock data (see
    /// ProfileController), so no other source file should ever call
    /// PlayerPrefs.Set*.
    /// </summary>
    public class PlayerPrefsKeyRegressionTests
    {
        private static readonly string[] AllowedFileNames =
        {
            "PlayerPrefsTutorialProgressStorage.cs",
            "PlayerPrefsAudioSettingsStorage.cs",
            "ProfileDisplayNameStorage.cs",
            "ProfileUserDataStorage.cs",
            "PlayerPrefsLevel0ProgressStorage.cs",
            "PlayerPrefsOkinawaProgressStorage.cs",
            // this file itself: the assertion text below legitimately contains the
            // literal string "PlayerPrefs.Set" it's scanning for.
            nameof(PlayerPrefsKeyRegressionTests) + ".cs",
        };

        [Test]
        public void OnlyKnownFiles_EverCallPlayerPrefsSet()
        {
            string uiRoot = Path.Combine(Application.dataPath, "UI");
            Assert.IsTrue(Directory.Exists(uiRoot), $"Expected {uiRoot} to exist.");

            foreach (string path in Directory.GetFiles(uiRoot, "*.cs", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(path);
                if (System.Array.IndexOf(AllowedFileNames, fileName) >= 0)
                    continue;

                string source = File.ReadAllText(path);
                StringAssert.DoesNotContain("PlayerPrefs.Set", source,
                    $"'{fileName}' must not write PlayerPrefs directly — only {string.Join(" or ", AllowedFileNames)} may.");
            }
        }
    }
}
