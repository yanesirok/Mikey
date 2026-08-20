using System.IO;
using NUnit.Framework;

namespace Mikey.UI.SafeArea.Tests
{
    /// <summary>
    /// Background-asset contract for Lore and Profile. The shared dark
    /// background (shared_dark_background.png) was approved and applied to
    /// BOTH screens, then Profile was reverted back to its own supplied art
    /// (profile_background.jpg) — it read poorly against Profile's content —
    /// while Lore kept the shared asset. Lore and Profile intentionally use
    /// DIFFERENT background art now; this guards against either direction of
    /// regression (Lore losing the shared asset, or Profile silently
    /// re-acquiring it).
    /// </summary>
    public class SharedBackgroundAssetTests
    {
        private const string SharedAssetPath = "Assets/UI/Media/Images/Shared/shared_dark_background.png";
        private const string SharedAssetUrlFragment = "Media/Images/Shared/shared_dark_background.png";
        private const string ProfileAssetUrlFragment = "Media/Images/Profile/profile_background.jpg";
        private const string IntroUssPath = "Assets/UI/Intro/Intro.uss";
        private const string ProfileUssPath = "Assets/UI/Profile/Profile.uss";

        [Test]
        public void SharedDarkBackgroundAsset_Exists()
        {
            Assert.IsTrue(File.Exists(SharedAssetPath), $"Expected the approved shared background at {SharedAssetPath}.");
        }

        [Test]
        public void Lore_UsesTheSharedDarkBackground()
        {
            string introUss = File.ReadAllText(IntroUssPath);
            StringAssert.Contains(SharedAssetUrlFragment, introUss, "Lore (Intro.uss) must reference the shared dark background.");
        }

        [Test]
        public void Profile_UsesItsOwnArt_NotTheSharedDarkBackground()
        {
            // Reverted: the shared asset looked good on Lore but not on Profile.
            string profileUss = File.ReadAllText(ProfileUssPath);
            StringAssert.Contains(ProfileAssetUrlFragment, profileUss, "Profile.uss must reference its own supplied background art.");
            StringAssert.DoesNotContain(SharedAssetUrlFragment, profileUss,
                "Profile.uss must NOT reference the shared dark background — that change was reverted.");
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
