using System.IO;
using NUnit.Framework;

namespace Mikey.UI.SafeArea.Tests
{
    /// <summary>
    /// Cross-screen contract for the approved shared dark background: Lore
    /// ("intro") and Profile must bind the exact same asset
    /// (shared_dark_background.png) — background-only change, never a
    /// duplicated per-screen copy of the image.
    /// </summary>
    public class SharedBackgroundAssetTests
    {
        private const string SharedAssetPath = "Assets/UI/Media/Images/Shared/shared_dark_background.png";
        private const string SharedAssetUrlFragment = "Media/Images/Shared/shared_dark_background.png";
        private const string IntroUssPath = "Assets/UI/Intro/Intro.uss";
        private const string ProfileUssPath = "Assets/UI/Profile/Profile.uss";

        [Test]
        public void SharedDarkBackgroundAsset_Exists()
        {
            Assert.IsTrue(File.Exists(SharedAssetPath), $"Expected the approved shared background at {SharedAssetPath}.");
        }

        [Test]
        public void LoreAndProfile_BothReference_TheExactSameSharedAssetPath()
        {
            string introUss = File.ReadAllText(IntroUssPath);
            string profileUss = File.ReadAllText(ProfileUssPath);

            StringAssert.Contains(SharedAssetUrlFragment, introUss, "Lore (Intro.uss) must reference the shared dark background.");
            StringAssert.Contains(SharedAssetUrlFragment, profileUss, "Profile.uss must reference the shared dark background.");
        }

        [Test]
        public void ProfileDetails_IsNotTouched_StillUsesItsOwnBackground()
        {
            // ProfileDetails is a separate screen, not mentioned by this pass —
            // it must keep its own existing background untouched, never migrated
            // onto the shared asset by accident.
            const string profileDetailsUssPath = "Assets/UI/Profile/ProfileDetails.uss";
            string uss = File.ReadAllText(profileDetailsUssPath);
            StringAssert.DoesNotContain(SharedAssetUrlFragment, uss,
                "ProfileDetails.uss must not have been changed to reference the shared dark background.");
        }
    }
}
