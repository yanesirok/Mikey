using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores squats from the hip-over-knee margin instead of the knee angle, so both the
    /// side and the frontal view work without MediaPipe's noisy depth. Per leg the margin is
    /// (knee.Y − hip.Y) / |ankle.Y − knee.Y| — ≈1 standing, ≈0 at parallel, negative deeper.
    /// The rep signal is the MAX margin over the visible legs (the "most standing" leg): it
    /// collapses only when BOTH legs bend, so a walking stride (straight support leg) never
    /// looks deep. The shared <see cref="RepCounter"/> runs on this dimensionless signal
    /// (stand ≥ standAt → deep ≤ deepAt, debounced) — the pair that fixed push-up recall.
    /// Lenient policy: a heavy torso lean at the bottom is tallied in <see cref="NoReps"/>
    /// but does not block the count. Engine-free.
    /// </summary>
    public sealed class SquatAnalyzer : IExerciseAnalyzer
    {
        private const string NotVisibleCue = "В кадр";

        private readonly RepCounter _counter;
        private readonly float _minVisibility;
        private readonly float _maxTorsoLeanDeg;
        private readonly float _smoothingAlpha;

        private float _smoothedSignal = float.NaN;
        private float _lastLean = float.NaN;
        private float _lastVis;
        private float _lastMarginLeft = float.NaN;
        private float _lastMarginRight = float.NaN;
        private bool _leanFaultThisRep;

        public string Id => "squat";
        public string DisplayName => "Squats";
        public int Reps { get; private set; }
        public int NoReps { get; private set; }
        public string Cue { get; private set; } = NotVisibleCue;
        public ExerciseFormState FormState { get; private set; } = ExerciseFormState.NotVisible;

        public string DebugInfo =>
            $"sig {(float.IsNaN(_smoothedSignal) ? "--" : _smoothedSignal.ToString("0.00"))}  " +
            $"L {(float.IsNaN(_lastMarginLeft) ? "--" : _lastMarginLeft.ToString("0.00"))}  " +
            $"R {(float.IsNaN(_lastMarginRight) ? "--" : _lastMarginRight.ToString("0.00"))}  " +
            $"lean {(float.IsNaN(_lastLean) ? "--" : _lastLean.ToString("0"))}°  " +
            $"phase {_counter.Phase}  vis {_lastVis:0.00}";

        public event Action Changed;

        public SquatAnalyzer(RepCounter counter = null, float minVisibility = 0.5f,
            float maxTorsoLeanDeg = 50f, float smoothingAlpha = 1f,
            float standAt = 0.7f, float deepAt = 0.45f)
        {
            _counter = counter ?? new RepCounter(upThresholdDeg: standAt, downThresholdDeg: deepAt,
                minRepSeconds: 0.3, downDebounceFrames: 2);
            _minVisibility = minVisibility;
            _maxTorsoLeanDeg = maxTorsoLeanDeg;
            _smoothingAlpha = smoothingAlpha;
        }

        public void ProcessFrame(PoseFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            float leftVis = frame.MinVisibility(PoseLandmarkType.LeftHip, PoseLandmarkType.LeftKnee, PoseLandmarkType.LeftAnkle);
            float rightVis = frame.MinVisibility(PoseLandmarkType.RightHip, PoseLandmarkType.RightKnee, PoseLandmarkType.RightAnkle);
            _lastVis = Math.Max(leftVis, rightVis);

            _lastMarginLeft = leftVis >= _minVisibility ? Margin(frame, left: true) : float.NaN;
            _lastMarginRight = rightVis >= _minVisibility ? Margin(frame, left: false) : float.NaN;

            bool anyLeg = !float.IsNaN(_lastMarginLeft) || !float.IsNaN(_lastMarginRight);
            if (!anyLeg)
            {
                _smoothedSignal = float.NaN;
                _counter.ResetDownStreak();
                FormState = ExerciseFormState.NotVisible;
                Cue = NotVisibleCue;
                Changed?.Invoke();
                return;
            }

            float signal =
                float.IsNaN(_lastMarginLeft) ? _lastMarginRight
                : float.IsNaN(_lastMarginRight) ? _lastMarginLeft
                : Math.Max(_lastMarginLeft, _lastMarginRight);

            _smoothedSignal = float.IsNaN(_smoothedSignal)
                ? signal
                : _smoothedSignal + _smoothingAlpha * (signal - _smoothedSignal);

            // Наклон корпуса — только метка формы; плечо со стороны более видимой ноги.
            bool leanLeft = leftVis >= rightVis;
            PoseLandmark shoulder = frame.Get(leanLeft ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);
            PoseLandmark hip = frame.Get(leanLeft ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
            _lastLean = shoulder.Visibility >= _minVisibility
                ? (float)(Math.Atan2(Math.Abs(shoulder.X - hip.X), Math.Max(1e-6f, hip.Y - shoulder.Y)) * 180.0 / Math.PI)
                : float.NaN;

            RepPhase prevPhase = _counter.Phase;
            bool completed = _counter.Update(_smoothedSignal, frame.TimestampSeconds);

            if (prevPhase != RepPhase.Down && _counter.Phase == RepPhase.Down)
                _leanFaultThisRep = false;
            bool leanFault = _counter.Phase == RepPhase.Down && !float.IsNaN(_lastLean) && _lastLean > _maxTorsoLeanDeg;
            if (leanFault)
                _leanFaultThisRep = true;

            FormState = leanFault ? ExerciseFormState.BadForm : ExerciseFormState.GoodForm;
            Cue = leanFault ? "Спину прямее" : string.Empty;

            if (completed)
            {
                Reps++;
                if (_leanFaultThisRep)
                    NoReps++;
                _leanFaultThisRep = false;
            }

            Changed?.Invoke();
        }

        // Насколько таз выше колена, в долях длины голени этой ноги (image-space, Y вниз):
        // стоя ≈ 1, параллель ≈ 0, глубже — отрицательно.
        private static float Margin(PoseFrame frame, bool left)
        {
            PoseLandmark hip = frame.Get(left ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
            PoseLandmark knee = frame.Get(left ? PoseLandmarkType.LeftKnee : PoseLandmarkType.RightKnee);
            PoseLandmark ankle = frame.Get(left ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle);
            float shank = Math.Abs(ankle.Y - knee.Y);
            return shank < 1e-4f ? float.NaN : (knee.Y - hip.Y) / shank;
        }

        public void Reset()
        {
            _counter.Reset();
            Reps = 0;
            NoReps = 0;
            _smoothedSignal = float.NaN;
            _lastLean = float.NaN;
            _lastVis = 0f;
            _lastMarginLeft = float.NaN;
            _lastMarginRight = float.NaN;
            _leanFaultThisRep = false;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            Changed?.Invoke();
        }
    }
}
