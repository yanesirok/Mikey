using Mikey.Pose;
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// The catalog is the one place the picker, the controller and the HUD agree on what
    /// exists. These tests pin the level-0 / level-1 split: assessment entries answer to a
    /// profile, teaching entries are strict whatever the toggle says, and the mae geri
    /// heights that were removed from the assessment stay removed.
    /// </summary>
    public class ExerciseCatalogTests
    {
        [Test]
        public void Level1TechniquesAreRegistered()
        {
            Assert.IsNotNull(ExerciseCatalog.Create("stance-fudo"));
            Assert.IsNotNull(ExerciseCatalog.Create("stance-zenkutsu"));
            Assert.IsNotNull(ExerciseCatalog.Create("kizamizuki-jodan"));
            Assert.IsNotNull(ExerciseCatalog.Create("maegeri-chudan-stance"));
            Assert.IsNotNull(ExerciseCatalog.Create("ghoststep-forward"));
            Assert.IsNotNull(ExerciseCatalog.Create("ghoststep-back"));
        }

        [Test]
        public void RemovedMaeGeriHeightsAreGone()
        {
            Assert.IsNull(ExerciseCatalog.Create("maegeri-gedan"));
            Assert.IsNull(ExerciseCatalog.Create("maegeri-chudan"));
            Assert.IsNull(ExerciseCatalog.Create("maegeri-jodan"));
        }

        [Test]
        public void TeachingTechniquesIgnoreTheLenientToggle()
        {
            // Мягкой стойки не бывает: «почти правильная» стойка стойкой не является.
            IExerciseAnalyzer kick = ExerciseCatalog.Create("maegeri-chudan-stance", ScoringProfile.Lenient);
            Assert.AreEqual("maegeri-chudan-stance", kick.Id);
        }

        [Test]
        public void AssessmentEntriesFollowTheProfile()
        {
            Assert.IsNotNull(ExerciseCatalog.Create("pushup", ScoringProfile.Lenient));
            Assert.IsNotNull(ExerciseCatalog.Create("pushup", ScoringProfile.Strict));
        }

        [Test]
        public void EveryEntryHasAnIdAndBuildsAnAnalyzer()
        {
            foreach (ExerciseDescriptor d in ExerciseCatalog.All)
            {
                Assert.IsNotEmpty(d.Id, "id");
                Assert.IsNotEmpty(d.DisplayName, d.Id);
                Assert.IsNotNull(d.Hint, d.Id);
                Assert.IsNotNull(d.Create(ScoringProfile.Strict), d.Id);
            }
        }
    }
}
