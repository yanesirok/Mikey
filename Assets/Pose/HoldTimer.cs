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
        private double _lastOutOfPose = double.NaN;
        private double _graceBridgeBias = 0;

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
                    _graceBridgeBias = 0;
                }
                else if (!double.IsNaN(_lastOutOfPose))
                {
                    // Grace-bridged gap: add half of the gap time
                    _graceBridgeBias += (timeSeconds - _lastOutOfPose) / 2;
                    _lastOutOfPose = double.NaN;
                }
                _lastInPose = timeSeconds;
                CurrentSeconds = timeSeconds - _holdStart + _graceBridgeBias;
                if (CurrentSeconds > BestSeconds)
                    BestSeconds = CurrentSeconds;
            }
            else if (!double.IsNaN(_lastInPose) && timeSeconds - _lastInPose > _graceSeconds)
            {
                CurrentSeconds = 0;
                _lastInPose = double.NaN;
                _lastOutOfPose = double.NaN;
                _graceBridgeBias = 0;
            }
            else if (!double.IsNaN(_lastInPose))
            {
                _lastOutOfPose = timeSeconds;
            }
        }

        public void Reset()
        {
            _holdStart = double.NaN;
            _lastInPose = double.NaN;
            _lastOutOfPose = double.NaN;
            CurrentSeconds = 0;
            BestSeconds = 0;
            _graceBridgeBias = 0;
        }
    }
}
