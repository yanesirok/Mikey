using NUnit.Framework;

namespace Mikey.UI.Progression.Tests
{
    /// <summary>Contract for the pure Level0TestCopy lookup — every test has non-empty title/description/illustration; only Yoko-Geri has secondary copy.</summary>
    public class Level0TestCopyTests
    {
        private static readonly Level0Test[] AllTests =
        {
            Level0Test.CameraTest, Level0Test.PushUps, Level0Test.Squats, Level0Test.WallSit, Level0Test.YokoGeri,
        };

        [TestCaseSource(nameof(AllTests))]
        public void EveryTest_HasATitleDescriptionAndIllustration(Level0Test test)
        {
            Assert.IsFalse(string.IsNullOrEmpty(Level0TestCopy.TitleFor(test)));
            Assert.IsFalse(string.IsNullOrEmpty(Level0TestCopy.DescriptionFor(test)));
            Assert.IsFalse(string.IsNullOrEmpty(Level0TestCopy.IllustrationFileName(test)));
        }

        [TestCaseSource(nameof(AllTests))]
        public void OnlyYokoGeri_HasSecondaryCopy(Level0Test test)
        {
            string secondary = Level0TestCopy.SecondaryFor(test);
            if (test == Level0Test.YokoGeri)
                Assert.IsFalse(string.IsNullOrEmpty(secondary));
            else
                Assert.IsTrue(string.IsNullOrEmpty(secondary));
        }

        [Test]
        public void CameraTest_HasNoStatLine_ItIsCalibrationNotAGradedStat()
        {
            Assert.IsTrue(string.IsNullOrEmpty(Level0TestCopy.StatFor(Level0Test.CameraTest)));
        }

        [TestCase(Level0Test.PushUps, "POWER / ENDURANCE")]
        [TestCase(Level0Test.Squats, "LOWER-BODY POWER / ENDURANCE")]
        [TestCase(Level0Test.WallSit, "ENDURANCE")]
        [TestCase(Level0Test.YokoGeri, "CONTROL / FLEXIBILITY / BALANCE")]
        public void GradedTests_HaveTheirSpecStatLine(Level0Test test, string expectedStat)
        {
            Assert.AreEqual(expectedStat, Level0TestCopy.StatFor(test));
        }

        [Test]
        public void IllustrationFileNames_MatchTheSuppliedCombineAssets()
        {
            Assert.AreEqual("combine_camera.png", Level0TestCopy.IllustrationFileName(Level0Test.CameraTest));
            Assert.AreEqual("combine_pushups.png", Level0TestCopy.IllustrationFileName(Level0Test.PushUps));
            Assert.AreEqual("combine_squats.png", Level0TestCopy.IllustrationFileName(Level0Test.Squats));
            Assert.AreEqual("combine_wallsit.png", Level0TestCopy.IllustrationFileName(Level0Test.WallSit));
            Assert.AreEqual("combine_yokogeri.png", Level0TestCopy.IllustrationFileName(Level0Test.YokoGeri));
        }
    }
}
