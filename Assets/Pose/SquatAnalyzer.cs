using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores squats from a side-on view: the smoothed 3D knee angle drives the shared
    /// <see cref="RepCounter"/> (stand ≥ 160° → depth ≤ 100° → stand = one rep). Lenient
    /// policy, mirroring push-ups: every full-range rep counts; a heavy torso lean at the
    /// bottom is tallied in <see cref="NoReps"/> but does not block the count. A shallow
    /// squat simply never completes the counter's cycle. Engine-free.
    /// </summary>
    public sealed class SquatAnalyzer : IExerciseAnalyzer
    {
        private const string NotVisibleCue = "В кадр (боком)";

        private readonly RepCounter _counter;
        private readonly float _minVisibility;
        private readonly float _maxTorsoLeanDeg;
        private readonly float _smoothingAlpha;

        private float _smoothedKnee = float.NaN;
        private float _lastLean = float.NaN;
        private float _lastVis;
        private bool _leanFaultThisRep;

        public string Id => "squat";
        public string DisplayName => "Squats";
        public int Reps { get; private set; }
        public int NoReps { get; private set; }
        public string Cue { get; private set; } = NotVisibleCue;
        public ExerciseFormState FormState { get; private set; } = ExerciseFormState.NotVisible;

        public string DebugInfo =>
            $"knee {(float.IsNaN(_smoothedKnee) ? "--" : _smoothedKnee.ToString("0"))}°  " +
            $"lean {(float.IsNaN(_lastLean) ? "--" : _lastLean.ToString("0"))}°  " +
            $"phase {_counter.Phase}  vis {_lastVis:0.00}";

        public event Action Changed;

        public SquatAnalyzer(RepCounter counter = null, float minVisibility = 0.6f,
            float maxTorsoLeanDeg = 50f, float smoothingAlpha = 0.6f)
        {
            _counter = counter ?? new RepCounter(upThresholdDeg: 160f, downThresholdDeg: 100f, minRepSeconds: 0.3);
            _minVisibility = minVisibility;
            _maxTorsoLeanDeg = maxTorsoLeanDeg;
            _smoothingAlpha = smoothingAlpha;
        }

        public void ProcessFrame(PoseFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            float leftVis = Math.Min(
                frame.MinVisibility(PoseLandmarkType.LeftHip, PoseLandmarkType.LeftKnee, PoseLandmarkType.LeftAnkle),
                frame.Get(PoseLandmarkType.LeftShoulder).Visibility);
            float rightVis = Math.Min(
                frame.MinVisibility(PoseLandmarkType.RightHip, PoseLandmarkType.RightKnee, PoseLandmarkType.RightAnkle),
                frame.Get(PoseLandmarkType.RightShoulder).Visibility);
            bool useLeft = leftVis >= rightVis;
            _lastVis = useLeft ? leftVis : rightVis;

            if (_lastVis < _minVisibility)
            {
                // Drop the smoothing baseline so a resumed set starts clean.
                _smoothedKnee = float.NaN;
                FormState = ExerciseFormState.NotVisible;
                Cue = NotVisibleCue;
                Changed?.Invoke();
                return;
            }

            PoseLandmark hip = frame.Get(useLeft ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
            PoseLandmark knee = frame.Get(useLeft ? PoseLandmarkType.LeftKnee : PoseLandmarkType.RightKnee);
            PoseLandmark ankle = frame.Get(useLeft ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle);
            PoseLandmark shoulder = frame.Get(useLeft ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);

            float kneeAngle = PoseMath.AngleDeg3D(hip, knee, ankle);
            _smoothedKnee = float.IsNaN(_smoothedKnee)
                ? kneeAngle
                : _smoothedKnee + _smoothingAlpha * (kneeAngle - _smoothedKnee);

            // Torso lean from vertical, degrees (image-space; shoulder is above the hip).
            _lastLean = (float)(Math.Atan2(Math.Abs(shoulder.X - hip.X), Math.Max(1e-6f, hip.Y - shoulder.Y)) * 180.0 / Math.PI);

            RepPhase prevPhase = _counter.Phase;
            bool completed = _counter.Update(_smoothedKnee, frame.TimestampSeconds);

            if (prevPhase != RepPhase.Down && _counter.Phase == RepPhase.Down)
                _leanFaultThisRep = false;
            bool leanFault = _counter.Phase == RepPhase.Down && _lastLean > _maxTorsoLeanDeg;
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

        public void Reset()
        {
            _counter.Reset();
            Reps = 0;
            NoReps = 0;
            _smoothedKnee = float.NaN;
            _lastLean = float.NaN;
            _lastVis = 0f;
            _leanFaultThisRep = false;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            Changed?.Invoke();
        }
    }
}
