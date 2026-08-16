using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Mikey.UI.Progression.Tests
{
    /// <summary>
    /// Regression guard for the Profile/shared-nav redesign: it introduces no new
    /// persisted state. The only real persisted storage in the app is
    /// PlayerPrefsTutorialProgressStorage's "Mikey.TutorialProgress.State" key and
    /// PlayerPrefsAudioSettingsStorage's Music/SFX/Trainer Voice volumes (both
    /// pre-existing) — every LVL/XP/streak number on the new Profile screen and
    /// shared top HUD is frontend mock data (see ProfileController), so no other
    /// source file should ever call PlayerPrefs.Set*.
    /// </summary>
    public class PlayerPrefsKeyRegressionTests
    {
        private static readonly string[] AllowedFileNames =
        {
            "PlayerPrefsTutorialProgressStorage.cs",
            "PlayerPrefsAudioSettingsStorage.cs",
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
