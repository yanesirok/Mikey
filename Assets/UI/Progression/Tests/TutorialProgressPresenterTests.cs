using NUnit.Framework;

namespace Mikey.UI.Progression.Tests
{
    /// <summary>
    /// Contract for <see cref="TutorialProgressPresenter"/>: the Home CTA's
    /// label/destination per reachable state, the safe fallback for
    /// LessonCompleted and every not-yet-built future state, and the Map/Techniques
    /// unlock boundary at Level1Unlocked.
    /// </summary>
    public class TutorialProgressPresenterTests
    {
        [TestCase(TutorialProgressState.NewPlayer, "START LVL 0", "combineIntro")]
        [TestCase(TutorialProgressState.IntroCompleted, "START LVL 0", "combineIntro")]
        [TestCase(TutorialProgressState.CombineStarted, "CONTINUE LVL 0", "camTest")]
        [TestCase(TutorialProgressState.CombineCompleted, "VIEW RESULTS", "combine")]
        [TestCase(TutorialProgressState.Level1Unlocked, "START LVL 1", "map")]
        [TestCase(TutorialProgressState.LessonStarted, "CONTINUE LESSON", "practice")]
        public void HomeCtaFor_ReachableStates_MatchesSpec(TutorialProgressState state, string expectedLabel, string expectedDestination)
        {
            HomeCtaSpec spec = TutorialProgressPresenter.HomeCtaFor(state);
            Assert.AreEqual(expectedLabel, spec.Label);
            Assert.AreEqual(expectedDestination, spec.Destination);
        }

        [TestCase(TutorialProgressState.LessonCompleted)]
        [TestCase(TutorialProgressState.TutorialCompleted)]
        [TestCase(TutorialProgressState.VowAvailable)]
        [TestCase(TutorialProgressState.VowActivated)]
        [TestCase(TutorialProgressState.ThirtyDayStreakCompleted)]
        public void HomeCtaFor_LessonCompletedAndBeyond_UsesSafeExistingScreenFallback(TutorialProgressState state)
        {
            HomeCtaSpec spec = TutorialProgressPresenter.HomeCtaFor(state);

            Assert.AreEqual(TutorialProgressPresenter.DestinationTechniquesFallback, spec.Destination,
                "States with no built screen yet must fall back to an existing production screen, never a missing one.");
            Assert.AreEqual("techniques", spec.Destination,
                "The fallback destination must be a real, existing screen id.");
        }

        [TestCase(TutorialProgressState.NewPlayer, false)]
        [TestCase(TutorialProgressState.IntroCompleted, false)]
        [TestCase(TutorialProgressState.CombineStarted, false)]
        [TestCase(TutorialProgressState.CombineCompleted, false)]
        [TestCase(TutorialProgressState.Level1Unlocked, true)]
        [TestCase(TutorialProgressState.LessonStarted, true)]
        [TestCase(TutorialProgressState.LessonCompleted, true)]
        [TestCase(TutorialProgressState.ThirtyDayStreakCompleted, true)]
        public void IsMapUnlocked_TrueFromLevel1UnlockedOnward(TutorialProgressState state, bool expectedUnlocked)
        {
            Assert.AreEqual(expectedUnlocked, TutorialProgressPresenter.IsMapUnlocked(state));
        }

        [TestCase(TutorialProgressState.NewPlayer, false)]
        [TestCase(TutorialProgressState.CombineCompleted, false)]
        [TestCase(TutorialProgressState.Level1Unlocked, true)]
        [TestCase(TutorialProgressState.LessonStarted, true)]
        public void IsTechniquesUnlocked_TrueFromLevel1UnlockedOnward(TutorialProgressState state, bool expectedUnlocked)
        {
            Assert.AreEqual(expectedUnlocked, TutorialProgressPresenter.IsTechniquesUnlocked(state));
        }
    }
}
