namespace Mikey.Pose
{
    /// <summary>
    /// Accumulates how long a pose predicate stays true. Brief dropouts (tracker blink,
    /// occlusion) up to <c>graceSeconds</c> are bridged transparently — the hold continues
    /// and the gap itself counts into the time. Longer gaps break the hold: the current
    /// time resets, the best time is kept. Engine-free and EditMode-testable.
    /// </summary>
    public sealed class HoldTimer
    {
        private readonly double _graceSeconds;
        private double _holdStart = double.NaN;
        private double _lastInPose = double.NaN;

        /// <summary>Continuous hold so far, seconds (grace-bridged gaps included).</summary>
        public double CurrentSeconds { get; private set; }

        /// <summary>Longest continuous hold this session, seconds.</summary>
        public double BestSeconds { get; private set; }

        public HoldTimer(double graceSeconds = 1.0) => _graceSeconds = graceSeconds;

        public void Update(bool inPose, double timeSeconds)
        {
            if (inPose)
            {
                bool broken = double.IsNaN(_lastInPose) || timeSeconds - _lastInPose > _graceSeconds;
                if (broken)
                {
                    _holdStart = timeSeconds;
                    CurrentSeconds = 0;
                }
                _lastInPose = timeSeconds;
                CurrentSeconds = timeSeconds - _holdStart;
                if (CurrentSeconds > BestSeconds)
                    BestSeconds = CurrentSeconds;
            }
            else if (!double.IsNaN(_lastInPose) && timeSeconds - _lastInPose > _graceSeconds)
            {
                CurrentSeconds = 0;
                _lastInPose = double.NaN;
            }
        }

        public void Reset()
        {
            _holdStart = double.NaN;
            _lastInPose = double.NaN;
            CurrentSeconds = 0;
            BestSeconds = 0;
        }
    }
}
