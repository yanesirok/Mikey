using System;

namespace Mikey.Pose
{
    /// <summary>Exercise-agnostic form state the HUD renders (colour + prompt).</summary>
    public enum ExerciseFormState
    {
        /// <summary>Not enough of the body is confidently in frame to score.</summary>
        NotVisible,

        /// <summary>Body is tracked but the current rep/pose has a form fault.</summary>
        BadForm,

        /// <summary>Body is tracked and form looks correct.</summary>
        GoodForm,
    }

    /// <summary>
    /// Common contract for scoring any single exercise from a pose stream, so the pose
    /// controller, HUD, and dev sandbox stay exercise-agnostic. Each exercise (push-up,
    /// squat, a karate technique, …) provides its own implementation and registers it in
    /// <see cref="ExerciseCatalog"/>; adding one then requires no changes to the plumbing.
    /// </summary>
    public interface IExerciseAnalyzer
    {
        /// <summary>Stable id used by the catalog and navigation (e.g. "pushup").</summary>
        string Id { get; }

        /// <summary>Human-readable name for selection UI (e.g. "Push-ups").</summary>
        string DisplayName { get; }

        /// <summary>Form-valid reps completed — the primary HUD count.</summary>
        int Reps { get; }

        /// <summary>Full-range reps rejected for broken form (set-summary detail).</summary>
        int NoReps { get; }

        /// <summary>The single prioritized coaching cue for the current frame ("" when good).</summary>
        string Cue { get; }

        /// <summary>Exercise-agnostic form state driving the HUD colour/prompt.</summary>
        ExerciseFormState FormState { get; }

        /// <summary>Live one-line diagnostics for the dev HUD (angles, phase, visibility).</summary>
        string DebugInfo { get; }

        /// <summary>Raised whenever the visible state may have changed.</summary>
        event Action Changed;

        /// <summary>Feeds one pose frame and updates the visible state.</summary>
        void ProcessFrame(PoseFrame frame);

        /// <summary>Resets to a fresh set.</summary>
        void Reset();
    }
}
