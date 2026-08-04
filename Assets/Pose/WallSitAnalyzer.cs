using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores a wall-sit hold from a side-on view: both the knee angle (hip–knee–ankle)
    /// and the hip angle (shoulder–hip–knee) must sit in a lenient window around 90°.
    /// The result is the longest continuous hold (via <see cref="HoldTimer"/>, tracker
    /// blinks bridged), surfaced through <see cref="Reps"/> as whole seconds because the
    /// HUD contract has no time field. No <see cref="NoReps"/> for a hold — a drifted
    /// seat just pauses the timer with a corrective cue. Engine-free.
    /// </summary>
    public sealed class WallSitAnalyzer : IExerciseAnalyzer
    {
        private const string NotVisibleCue = "В кадр (боком)";

        private readonly HoldTimer _timer;
        private readonly float _minVisibility;
        private readonly float _minAngleDeg;
        private readonly float _maxAngleDeg;

        private float _lastKnee = float.NaN;
        private float _lastHip = float.NaN;
        private float _lastVis;

        public string Id => "wallsit";
        public string DisplayName => "Wall-sit (сек)";
        public int Reps => (int)_timer.BestSeconds;
        public int NoReps => 0;
        public string Cue { get; private set; } = NotVisibleCue;
        public ExerciseFormState FormState { get; private set; } = ExerciseFormState.NotVisible;

        public double BestHoldSeconds => _timer.BestSeconds;
        public double CurrentHoldSeconds => _timer.CurrentSeconds;

        public string DebugInfo =>
            $"knee {(float.IsNaN(_lastKnee) ? "--" : _lastKnee.ToString("0"))}°  " +
            $"hip {(float.IsNaN(_lastHip) ? "--" : _lastHip.ToString("0"))}°  " +
            $"hold {_timer.CurrentSeconds:0.0}s  best {_timer.BestSeconds:0.0}s  vis {_lastVis:0.00}";

        public event Action Changed;

        public WallSitAnalyzer(HoldTimer timer = null, float minVisibility = 0.6f,
            float minAngleDeg = 70f, float maxAngleDeg = 120f)
        {
            _timer = timer ?? new HoldTimer(graceSeconds: 1.0);
            _minVisibility = minVisibility;
            _minAngleDeg = minAngleDeg;
            _maxAngleDeg = maxAngleDeg;
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
                // Не помечаем "не в позе": HoldTimer сам сошьёт короткий провал грейсом.
                FormState = ExerciseFormState.NotVisible;
                Cue = NotVisibleCue;
                Changed?.Invoke();
                return;
            }

            PoseLandmark shoulder = frame.Get(useLeft ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);
            PoseLandmark hip = frame.Get(useLeft ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
            PoseLandmark knee = frame.Get(useLeft ? PoseLandmarkType.LeftKnee : PoseLandmarkType.RightKnee);
            PoseLandmark ankle = frame.Get(useLeft ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle);

            _lastKnee = PoseMath.AngleDeg3D(hip, knee, ankle);
            _lastHip = PoseMath.AngleDeg3D(shoulder, hip, knee);

            bool inPose = _lastKnee >= _minAngleDeg && _lastKnee <= _maxAngleDeg
                       && _lastHip >= _minAngleDeg && _lastHip <= _maxAngleDeg;
            _timer.Update(inPose, frame.TimestampSeconds);

            if (inPose)
            {
                FormState = ExerciseFormState.GoodForm;
                Cue = string.Empty;
            }
            else
            {
                FormState = ExerciseFormState.BadForm;
                Cue = _lastKnee > _maxAngleDeg || _lastHip > _maxAngleDeg ? "Ниже" : "Выше";
            }

            Changed?.Invoke();
        }

        public void Reset()
        {
            _timer.Reset();
            _lastKnee = float.NaN;
            _lastHip = float.NaN;
            _lastVis = 0f;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            Changed?.Invoke();
        }
    }
}
