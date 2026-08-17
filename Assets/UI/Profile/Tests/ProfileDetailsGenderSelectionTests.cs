using NUnit.Framework;

namespace Mikey.UI.Profile.Tests
{
    /// <summary>
    /// Real behavioral coverage (not just markup presence) for the Gender chip
    /// group's "exactly one selected, or none until a choice is made" invariant,
    /// exercising the actual pure logic <see cref="ProfileDetailsController"/>
    /// delegates to — not a reimplementation of it.
    /// </summary>
    public class ProfileDetailsGenderSelectionTests
    {
        private static readonly string[] Options =
        {
            ProfileUserData.GenderMale, ProfileUserData.GenderFemale, ProfileUserData.GenderOther, ProfileUserData.GenderPreferNotToSay,
        };

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ComputeSelectedFlags_MarksExactlyTheChosenOption_ForEveryOption(int chosenIndex)
        {
            bool[] flags = ProfileDetailsGenderSelection.ComputeSelectedFlags(Options, Options[chosenIndex]);

            Assert.AreEqual(Options.Length, flags.Length);
            for (int i = 0; i < flags.Length; i++)
                Assert.AreEqual(i == chosenIndex, flags[i], $"Option {i} selection flag was wrong for chosen index {chosenIndex}.");
        }

        [Test]
        public void ComputeSelectedFlags_ExactlyOneTrue_WhenAValueMatches()
        {
            bool[] flags = ProfileDetailsGenderSelection.ComputeSelectedFlags(Options, ProfileUserData.GenderFemale);

            int trueCount = 0;
            foreach (bool flag in flags)
                if (flag)
                    trueCount++;
            Assert.AreEqual(1, trueCount, "Exactly one option must read as selected.");
        }

        [Test]
        public void ComputeSelectedFlags_NoneTrue_WhenNoOptionHasBeenChosenYet()
        {
            bool[] flags = ProfileDetailsGenderSelection.ComputeSelectedFlags(Options, string.Empty);

            foreach (bool flag in flags)
                Assert.IsFalse(flag, "No option may read as selected before a choice is made.");
        }

        [Test]
        public void ComputeSelectedFlags_NoneTrue_WhenSelectedValueMatchesNoOption()
        {
            bool[] flags = ProfileDetailsGenderSelection.ComputeSelectedFlags(Options, "not-a-real-gender-value");

            foreach (bool flag in flags)
                Assert.IsFalse(flag);
        }

        [Test]
        public void ComputeSelectedFlags_SwitchingSelection_MovesTheSingleTrueFlag_NeverLeavesTwoSelected()
        {
            bool[] first = ProfileDetailsGenderSelection.ComputeSelectedFlags(Options, ProfileUserData.GenderMale);
            bool[] second = ProfileDetailsGenderSelection.ComputeSelectedFlags(Options, ProfileUserData.GenderOther);

            Assert.IsTrue(first[0]);
            Assert.IsTrue(second[2]);
            // Each call is independent/stateless, so re-selecting must not leave
            // the previous choice marked true alongside the new one.
            Assert.IsFalse(second[0]);
        }
    }
}
