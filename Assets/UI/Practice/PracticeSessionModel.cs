using System;

namespace Mikey.UI.Practice
{
    /// <summary>
    /// Pure, frontend-only state holder for the Practice screen's mock training
    /// session (first lesson: Front Stance / Zenkutsu-dachi). Owns the current
    /// <see cref="PracticeState"/> and step, and derives a deterministic mock
    /// form score, cue and instruction copy from them.
    ///
    /// Kept free of UnityEngine / UI Toolkit types so it can be exercised in
    /// EditMode tests without a live panel (mirrors how <c>CameraTestRepModel</c>
    /// is split out from <c>CameraTestController</c>). All data is local/mock —
    /// there is no camera, pose detection, ML, networking, API or backend input.
    /// </summary>
    public sealed class PracticeSessionModel
    {
        /// <summary>Number of deterministic keyframes (kata pips) in one session.</summary>
        public const int StepCount = 4;

        /// <summary>Mock form score at the start (matches the reference baseline).</summary>
        public const int BaseScore = 41;

        /// <summary>Mock form score once the form is fully held (reference target).</summary>
        public const int TargetScore = 88;

        /// <summary>Raised after any state-changing mutation (Advance / Reset).</summary>
        public event Action Changed;

        /// <summary>Current lifecycle state. Starts <see cref="PracticeState.Ready"/>.</summary>
        public PracticeState State { get; private set; } = PracticeState.Ready;

        /// <summary>Completed keyframes so far, 0..<see cref="StepCount"/>.</summary>
        public int Step { get; private set; }

        /// <summary>True once the session has reached its end.</summary>
        public bool IsComplete => State == PracticeState.Complete;

        /// <summary>The completion action is available only at completion.</summary>
        public bool CanComplete => State == PracticeState.Complete;

        /// <summary>Fractional progress 0..1, deterministic per step.</summary>
        public float Progress => (float)Step / StepCount;

        /// <summary>Deterministic mock form score for the current step.</summary>
        public int Score => ScoreFor(Step);

        /// <summary>Deterministic cue tone for the current state/step.</summary>
        public PracticeCue Cue => CueFor(State, Step);

        /// <summary>Short instruction/step copy for the current state/step.</summary>
        public string Instruction => InstructionFor(State, Step);

        /// <summary>Label for the local advance control (Begin / Next / Done).</summary>
        public string ActionLabel => ActionLabelFor(State);

        /// <summary>
        /// Begin/Next: advances one deterministic step and raises <see cref="Changed"/>.
        /// From <see cref="PracticeState.Ready"/> the first call enters
        /// <see cref="PracticeState.InProgress"/>; the <see cref="StepCount"/>-th call
        /// reaches <see cref="PracticeState.Complete"/>. A no-op once complete (so the
        /// progress can never run past its end), and it does not re-notify in that case.
        /// </summary>
        public void Advance()
        {
            if (State == PracticeState.Complete)
                return;

            Step++;
            State = Step >= StepCount ? PracticeState.Complete : PracticeState.InProgress;
            Changed?.Invoke();
        }

        /// <summary>
        /// Resets to the initial Ready state (step 0) and raises <see cref="Changed"/>.
        /// Called when the controller (re)initializes or the screen is (re)entered so
        /// a previous run never leaks into the next visit.
        /// </summary>
        public void Reset()
        {
            State = PracticeState.Ready;
            Step = 0;
            Changed?.Invoke();
        }

        /// <summary>Pure step → mock-score lookup. Exposed for direct unit testing.</summary>
        public static int ScoreFor(int step)
        {
            if (step <= 0)
                return BaseScore;
            if (step >= StepCount)
                return TargetScore;
            return BaseScore + (TargetScore - BaseScore) * step / StepCount;
        }

        /// <summary>Pure state/step → cue lookup. Exposed for direct unit testing.</summary>
        public static PracticeCue CueFor(PracticeState state, int step)
        {
            switch (state)
            {
                case PracticeState.Ready: return PracticeCue.Ready;
                case PracticeState.Complete: return PracticeCue.Good;
                default: return step * 2 < StepCount ? PracticeCue.Adjust : PracticeCue.Good;
            }
        }

        /// <summary>Pure state/step → instruction copy. Exposed for direct unit testing.</summary>
        public static string InstructionFor(PracticeState state, int step)
        {
            switch (state)
            {
                case PracticeState.Ready: return "Sink into the stance — press Begin.";
                case PracticeState.Complete: return "Form held. Element complete.";
                default:
                    return step * 2 < StepCount
                        ? "Hips lower. Hold the line."
                        : "Good. Hold the line.";
            }
        }

        /// <summary>Pure state → advance-control label. Exposed for direct unit testing.</summary>
        public static string ActionLabelFor(PracticeState state)
        {
            switch (state)
            {
                case PracticeState.Ready: return "Begin";
                case PracticeState.InProgress: return "Next";
                default: return "Done";
            }
        }
    }
}
