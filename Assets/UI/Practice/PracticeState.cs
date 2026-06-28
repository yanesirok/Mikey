namespace Mikey.UI.Practice
{
    /// <summary>
    /// High-level lifecycle of a mock Practice session. The session starts
    /// <see cref="Ready"/>, advances through <see cref="InProgress"/> as the user
    /// presses Begin/Next, and finishes at <see cref="Complete"/>. Frontend/mock
    /// only — there is no camera, pose detection, ML, networking or backend here.
    /// </summary>
    public enum PracticeState
    {
        /// <summary>Idle, nothing attempted yet. The only action is "Begin".</summary>
        Ready,

        /// <summary>Mid-session: each "Next" advances deterministic mock progress.</summary>
        InProgress,

        /// <summary>The session reached its end; the completion action is available.</summary>
        Complete,
    }

    /// <summary>
    /// Deterministic cue tone derived purely from session progress. Drives the
    /// HUD cue pill's colour/glyph so the on-screen feedback is fully testable.
    /// </summary>
    public enum PracticeCue
    {
        /// <summary>Not started — get into the stance.</summary>
        Ready,

        /// <summary>Correcting — the mock form still needs adjustment.</summary>
        Adjust,

        /// <summary>Form is holding well.</summary>
        Good,
    }
}
