namespace Mikey.UI.Progression
{
    /// <summary>
    /// Linear MVP tutorial progression. Ordinal order matters: later members are
    /// always "further along" than earlier ones, so gating logic can compare
    /// states with the ordinary relational operators (e.g. <c>state &gt;=
    /// TutorialProgressState.Level1Unlocked</c>) instead of an explicit lookup
    /// table. Frontend/local-only — no backend, auth, or payment data is implied
    /// by any state here.
    ///
    /// Only the states through <see cref="LessonCompleted"/> are reachable in
    /// this MVP; <see cref="TutorialCompleted"/> onward exist so future work can
    /// extend the same model without a breaking change, and current UI must
    /// treat them exactly like <see cref="LessonCompleted"/> (safe existing-screen
    /// fallback) since nothing sets them yet.
    /// </summary>
    public enum TutorialProgressState
    {
        NewPlayer = 0,
        IntroCompleted = 1,
        CombineStarted = 2,
        CombineCompleted = 3,
        Level1Unlocked = 4,
        LessonStarted = 5,
        LessonCompleted = 6,
        TutorialCompleted = 7,
        VowAvailable = 8,
        VowActivated = 9,
        ThirtyDayStreakCompleted = 10,
    }
}
