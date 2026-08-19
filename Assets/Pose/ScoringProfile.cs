namespace Mikey.Pose
{
    /// <summary>
    /// How strictly an analyzer scores a rep. <see cref="Lenient"/> is the level-0
    /// assessment policy: a full-range rep counts even with a form fault (the fault is
    /// still tallied in <c>NoReps</c> and cued). <see cref="Strict"/> is the level-1
    /// teaching policy: a rep with any named fault counts ONLY as a no-rep, so the
    /// score reflects clean repetitions and the cue tells the player what to fix.
    /// Thresholds themselves may also tighten in <see cref="Strict"/> — each analyzer
    /// documents its own difference.
    /// </summary>
    public enum ScoringProfile
    {
        Lenient,
        Strict,
    }
}
