using System;

namespace Mikey.UI.CameraTest
{
    /// <summary>
    /// Pure, frontend-only state holder for the Camera Test screen's mock
    /// repetition counter. Owns the current rep count and derives a deterministic
    /// <see cref="CameraFormStatus"/> (and its display copy) from it.
    ///
    /// Kept free of UnityEngine / UI Toolkit types so it can be exercised in
    /// EditMode tests without a live panel (mirrors how <c>CombineViewModel</c> is
    /// split out from <c>CombineScreenController</c>). All data is local/mock —
    /// there is no real camera, pose detection, ML, networking, or backend input.
    /// </summary>
    public sealed class CameraTestRepModel
    {
        // Detection "locks on" as the user settles in: the first couple of mock
        // reps read as still-adjusting, then form reads good. Deterministic so the
        // status copy is fully testable.
        private const int LockOnReps = 3;

        /// <summary>Raised after <see cref="Reps"/> (and therefore the status) changes.</summary>
        public event Action Changed;

        /// <summary>The visible repetition count. Starts at 0.</summary>
        public int Reps { get; private set; }

        /// <summary>
        /// Increments the visible rep count by one and raises <see cref="Changed"/>.
        /// This is the only mutation the "+ simulate reps" control performs — no
        /// real rep is detected; it is a local stand-in for pose input.
        /// </summary>
        public void SimulateRep()
        {
            Reps++;
            Changed?.Invoke();
        }

        /// <summary>
        /// Resets the counter to its initial state (0 reps) and raises
        /// <see cref="Changed"/>. Called when the controller (re)initializes or the
        /// screen restarts so the HUD always starts from a known, predictable state.
        /// </summary>
        public void Reset()
        {
            Reps = 0;
            Changed?.Invoke();
        }

        /// <summary>
        /// Deterministic mock form status derived purely from <see cref="Reps"/>:
        /// 0 reps reads as <see cref="CameraFormStatus.Ready"/> (get in frame),
        /// the first <see cref="LockOnReps"/> reps as <see cref="CameraFormStatus.Adjust"/>
        /// (detection settling), and everything after as <see cref="CameraFormStatus.Good"/>.
        /// </summary>
        public CameraFormStatus Status => StatusFor(Reps);

        /// <summary>Pure rep-count → status lookup. Exposed for direct unit testing.</summary>
        public static CameraFormStatus StatusFor(int reps)
        {
            if (reps <= 0)
                return CameraFormStatus.Ready;
            if (reps < LockOnReps)
                return CameraFormStatus.Adjust;
            return CameraFormStatus.Good;
        }

        /// <summary>Short human-readable copy for the current <see cref="Status"/>.</summary>
        public string StatusText => StatusTextFor(Status);

        /// <summary>Pure status → display-copy lookup. Exposed for direct unit testing.</summary>
        public static string StatusTextFor(CameraFormStatus status)
        {
            switch (status)
            {
                case CameraFormStatus.Ready: return "Align in frame";
                case CameraFormStatus.Adjust: return "Hold steady…";
                case CameraFormStatus.Good: return "Good form";
                default: return "Align in frame";
            }
        }
    }
}
