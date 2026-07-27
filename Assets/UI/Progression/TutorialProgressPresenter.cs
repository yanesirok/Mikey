namespace Mikey.UI.Progression
{
    /// <summary>
    /// Pure state → UI decisions shared by Home (dynamic CTA + Map/Techniques lock
    /// state) and the Map/Techniques gating checks. Kept free of UnityEngine/UI
    /// Toolkit types so every decision is directly unit-testable (mirrors
    /// <c>CameraTestRepModel</c>/<c>PracticeSessionModel</c>'s pure static lookups).
    /// </summary>
    public static class TutorialProgressPresenter
    {
        public const string DestinationCombineIntro = "combineIntro";
        public const string DestinationCamTest = "camTest";
        public const string DestinationCombine = "combine";
        public const string DestinationMap = "map";
        public const string DestinationPractice = "practice";

        /// <summary>
        /// Safe existing-screen fallback for LessonCompleted and every state beyond
        /// it (TutorialCompleted, VowAvailable, VowActivated, ThirtyDayStreakCompleted)
        /// — none of those have a built screen yet in this MVP, so the CTA must never
        /// point at a nonexistent Lesson Results / Vow / Paywall / Donation / Payout
        /// screen. This is explicitly temporary: revisit once those screens exist.
        /// </summary>
        public const string DestinationTechniquesFallback = "techniques";

        /// <summary>The single dynamic Home CTA's label + destination for the given progression state.</summary>
        public static HomeCtaSpec HomeCtaFor(TutorialProgressState state)
        {
            switch (state)
            {
                case TutorialProgressState.NewPlayer:
                case TutorialProgressState.IntroCompleted:
                    return new HomeCtaSpec("START LVL 0", DestinationCombineIntro);

                case TutorialProgressState.CombineStarted:
                    return new HomeCtaSpec("CONTINUE LVL 0", DestinationCamTest);

                case TutorialProgressState.CombineCompleted:
                    return new HomeCtaSpec("VIEW RESULTS", DestinationCombine);

                case TutorialProgressState.Level1Unlocked:
                    return new HomeCtaSpec("START LVL 1", DestinationMap);

                case TutorialProgressState.LessonStarted:
                    return new HomeCtaSpec("CONTINUE LESSON", DestinationPractice);

                // LessonCompleted and every future state: safe temporary fallback —
                // see DestinationTechniquesFallback.
                default:
                    return new HomeCtaSpec("VIEW TECHNIQUES", DestinationTechniquesFallback);
            }
        }

        /// <summary>Map is reachable once Level 1 has unlocked (Combine completed).</summary>
        public static bool IsMapUnlocked(TutorialProgressState state) => state >= TutorialProgressState.Level1Unlocked;

        /// <summary>Techniques hub is reachable once Level 1 has unlocked (Combine completed).</summary>
        public static bool IsTechniquesUnlocked(TutorialProgressState state) => state >= TutorialProgressState.Level1Unlocked;
    }
}
