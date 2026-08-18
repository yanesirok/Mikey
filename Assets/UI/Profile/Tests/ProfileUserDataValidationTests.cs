using NUnit.Framework;

namespace Mikey.UI.Profile.Tests
{
    /// <summary>Pure boundary coverage for ProfileUserData's numeric/choice field rules.</summary>
    public class ProfileUserDataValidationTests
    {
        [TestCase(9, false)]
        [TestCase(10, true)]
        [TestCase(100, true)]
        [TestCase(101, false)]
        public void IsValidAge_EnforcesTenToHundredInclusive(int age, bool expected)
        {
            Assert.AreEqual(expected, ProfileUserDataValidation.IsValidAge(age));
        }

        [TestCase(29f, false)]
        [TestCase(30f, true)]
        [TestCase(300f, true)]
        [TestCase(300.1f, false)]
        public void IsValidWeightKg_EnforcesThirtyToThreeHundredInclusive(float weightKg, bool expected)
        {
            Assert.AreEqual(expected, ProfileUserDataValidation.IsValidWeightKg(weightKg));
        }

        [TestCase(99, false)]
        [TestCase(100, true)]
        [TestCase(250, true)]
        [TestCase(251, false)]
        public void IsValidHeightCm_EnforcesOneHundredToTwoFiftyInclusive(int heightCm, bool expected)
        {
            Assert.AreEqual(expected, ProfileUserDataValidation.IsValidHeightCm(heightCm));
        }

        [TestCase(ProfileUserData.GenderMale, true)]
        [TestCase(ProfileUserData.GenderFemale, true)]
        [TestCase(ProfileUserData.GenderOther, true)]
        [TestCase(ProfileUserData.GenderPreferNotToSay, true)]
        [TestCase("", false)]
        [TestCase("Not a real option", false)]
        [TestCase(null, false)]
        public void IsValidGender_OnlyAcceptsTheFourDefinedOptions(string gender, bool expected)
        {
            Assert.AreEqual(expected, ProfileUserDataValidation.IsValidGender(gender));
        }
    }
}
