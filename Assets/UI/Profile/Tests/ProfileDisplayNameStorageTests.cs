using NUnit.Framework;

namespace Mikey.UI.Profile.Tests
{
    /// <summary>
    /// Contract for the local-only display name placeholder (see
    /// ProfileDisplayNameStorage's class summary for why this is explicitly
    /// approved as PlayerPrefs rather than a real account system).
    /// <see cref="ProfileDisplayNameStorage.Validate"/> is pure, so it's tested
    /// directly rather than by reading source.
    /// </summary>
    public class ProfileDisplayNameStorageTests
    {
        [Test]
        public void DefaultDisplayName_IsMikey()
        {
            Assert.AreEqual("Mikey", ProfileDisplayNameStorage.DefaultDisplayName);
        }

        [Test]
        public void PlayerPrefsKey_IsTheOneApprovedKey()
        {
            Assert.AreEqual("Mikey.Profile.DisplayName", ProfileDisplayNameStorage.PlayerPrefsKey);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void Validate_RejectsEmptyOrWhitespaceOnlyInput(string input)
        {
            Assert.IsNull(ProfileDisplayNameStorage.Validate(input));
        }

        [Test]
        public void Validate_TrimsLeadingAndTrailingWhitespace()
        {
            Assert.AreEqual("Jak", ProfileDisplayNameStorage.Validate("  Jak  "));
        }

        [Test]
        public void Validate_AcceptsAnOrdinaryName()
        {
            Assert.AreEqual("Mikey", ProfileDisplayNameStorage.Validate("Mikey"));
        }

        [Test]
        public void Validate_EnforcesMaxLength()
        {
            string tooLong = new string('A', ProfileDisplayNameStorage.MaxLength + 10);
            string result = ProfileDisplayNameStorage.Validate(tooLong);
            Assert.AreEqual(ProfileDisplayNameStorage.MaxLength, result.Length);
        }

        [Test]
        public void Validate_TrimsBeforeEnforcingMaxLength()
        {
            string padded = " " + new string('B', ProfileDisplayNameStorage.MaxLength) + " ";
            string result = ProfileDisplayNameStorage.Validate(padded);
            Assert.AreEqual(ProfileDisplayNameStorage.MaxLength, result.Length);
            StringAssert.DoesNotContain(" ", result);
        }
    }
}
