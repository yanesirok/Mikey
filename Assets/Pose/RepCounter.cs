namespace Mikey.Pose
{
    /// <summary>The two ends of a rep cycle (top/bottom), plus the initial unknown state.</summary>
    public enum RepPhase
    {
        /// <summary>Not yet settled at the top — no rep can complete from here.</summary>
        Unknown,

        /// <summary>Arms extended (top / lockout).</summary>
        Up,

        /// <summary>Chest lowered past the depth threshold (bottom).</summary>
        Down,
    }

    /// <summary>
    /// Pure motion detector: counts one full-range rep from a smoothed scalar signal
    /// (a joint angle in degrees where large = top/rest, small = bottom), using two
    /// thresholds for hysteresis. A rep is the transition Down→Up, but only if the
    /// descent-to-ascent took at least <c>minRepSeconds</c> — this rejects the sub-frame
    /// threshold flicker that noisy landmarks produce.
    ///
    /// Used by push-ups (elbow angle) and squats (knee angle). Detects movement only;
    /// visibility/posture gating is the caller's job.
    /// </summary>
    public sealed class RepCounter
    {
        private readonly float _upThresholdDeg;
        private readonly float _downThresholdDeg;
        private readonly double _minRepSeconds;

        private double _downEnterTime;

        /// <param name="upThresholdDeg">Signal (degrees) at/above which the movement counts as at the top/rest.</param>
        /// <param name="downThresholdDeg">Signal (degrees) at/below which the rep counts as deep (bottom).</param>
        /// <param name="minRepSeconds">Minimum time from reaching the bottom to returning to the top.</param>
        public RepCounter(float upThresholdDeg = 140f, float downThresholdDeg = 105f, double minRepSeconds = 0.3)
        {
            _upThresholdDeg = upThresholdDeg;
            _downThresholdDeg = downThresholdDeg;
            _minRepSeconds = minRepSeconds;
        }

        /// <summary>Number of completed full-range reps detected.</summary>
        public int Reps { get; private set; }

        /// <summary>Current phase of the movement.</summary>
        public RepPhase Phase { get; private set; } = RepPhase.Unknown;

        /// <summary>
        /// Feeds one frame's signal (angle in degrees) at time <paramref name="timeSeconds"/>.
        /// Returns true exactly on the frame a full rep completes (a confirmed, long-enough
        /// bottom followed by a return to the top).
        /// </summary>
        public bool Update(float angleDeg, double timeSeconds)
        {
            if (angleDeg >= _upThresholdDeg)
            {
                bool longEnough = (timeSeconds - _downEnterTime) >= _minRepSeconds;
                bool completed = Phase == RepPhase.Down && longEnough;
                if (completed)
                    Reps++;
                Phase = RepPhase.Up;
                return completed;
            }

            if (angleDeg <= _downThresholdDeg)
            {
                if (Phase == RepPhase.Up)
                {
                    Phase = RepPhase.Down;
                    _downEnterTime = timeSeconds;
                }
            }

            return false;
        }

        /// <summary>Resets to a fresh session (0 reps, unknown phase).</summary>
        public void Reset()
        {
            Reps = 0;
            Phase = RepPhase.Unknown;
            _downEnterTime = 0;
        }
    }
}
