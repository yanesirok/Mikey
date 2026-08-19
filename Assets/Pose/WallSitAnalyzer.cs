using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores a wall-sit hold from the hip-over-knee margin (<see cref="PoseMath.HipDropMargin"/>)
    /// instead of noisy 3D joint angles, so both the side and the frontal view work. In pose =
    /// the MAX margin over the visible legs sits in [seatLowAt, seatHighAt]: ≈0 seated at
    /// parallel, ≈1 standing (above the window), and below the window the hips are a shin-length
    /// under the knees — sitting on the floor, which is not a wall-sit. Wall proxy: the torso
    /// must stay within <c>maxTorsoLeanDeg</c> of vertical (a back against the wall is upright),
    /// otherwise the timer pauses with a corrective cue. The result is the longest continuous
    /// hold (via <see cref="HoldTimer"/>, tracker blinks bridged), surfaced through
    /// <see cref="Reps"/> as whole seconds because the HUD contract has no time field.
    /// No <see cref="NoReps"/> for a hold. <see cref="ScoringProfile.Strict"/> (level 1 teaching)
    /// narrows the seat window; the cues are the same "Ниже"/"Выше". Engine-free.
    /// </summary>
    public sealed class WallSitAnalyzer : IExerciseAnalyzer
    {
        private const string NotVisibleCue = "В кадр";

        // Строгое окно — колено 85–100°, выраженное в той же мере hip-drop margin:
        // margin = −(бедро/голень)·cos(колено), при типичном отношении бедро/голень ≈ 1.2
        // это [−0.10, +0.20]. Держим Y-метрику, а не угол колена: угол в 2D врёт анфас
        // (бедро уходит в ракурс), ради чего мера и выбиралась. Пороги стартовые.
        private const float StrictSeatLowAt = -0.1f;
        private const float StrictSeatHighAt = 0.2f;

        private readonly HoldTimer _timer;
        private readonly float _minVisibility;
        private readonly float _seatLowAt;
        private readonly float _seatHighAt;
        private readonly float _maxTorsoLeanDeg;

        private float _lastSignal = float.NaN;
        private float _lastMarginLeft = float.NaN;
        private float _lastMarginRight = float.NaN;
        private float _lastLean = float.NaN;
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
            $"sig {(float.IsNaN(_lastSignal) ? "--" : _lastSignal.ToString("0.00"))}  " +
            $"L {(float.IsNaN(_lastMarginLeft) ? "--" : _lastMarginLeft.ToString("0.00"))}  " +
            $"R {(float.IsNaN(_lastMarginRight) ? "--" : _lastMarginRight.ToString("0.00"))}  " +
            $"lean {(float.IsNaN(_lastLean) ? "--" : _lastLean.ToString("0"))}°  " +
            $"hold {_timer.CurrentSeconds:0.0}s  best {_timer.BestSeconds:0.0}s  vis {_lastVis:0.00}";

        public event Action Changed;

        public WallSitAnalyzer(HoldTimer timer = null, float minVisibility = 0.5f,
            float seatLowAt = -0.45f, float seatHighAt = 0.5f, float maxTorsoLeanDeg = 40f,
            ScoringProfile profile = ScoringProfile.Lenient)
        {
            _timer = timer ?? new HoldTimer(graceSeconds: 1.0);
            _minVisibility = minVisibility;
            bool strict = profile == ScoringProfile.Strict;
            _seatLowAt = strict ? StrictSeatLowAt : seatLowAt;
            _seatHighAt = strict ? StrictSeatHighAt : seatHighAt;
            _maxTorsoLeanDeg = maxTorsoLeanDeg;
        }

        public void ProcessFrame(PoseFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            float leftVis = frame.MinVisibility(PoseLandmarkType.LeftHip, PoseLandmarkType.LeftKnee, PoseLandmarkType.LeftAnkle);
            float rightVis = frame.MinVisibility(PoseLandmarkType.RightHip, PoseLandmarkType.RightKnee, PoseLandmarkType.RightAnkle);
            _lastVis = Math.Max(leftVis, rightVis);

            _lastMarginLeft = leftVis >= _minVisibility
                ? PoseMath.HipDropMargin(frame.Get(PoseLandmarkType.LeftHip),
                    frame.Get(PoseLandmarkType.LeftKnee), frame.Get(PoseLandmarkType.LeftAnkle))
                : float.NaN;
            _lastMarginRight = rightVis >= _minVisibility
                ? PoseMath.HipDropMargin(frame.Get(PoseLandmarkType.RightHip),
                    frame.Get(PoseLandmarkType.RightKnee), frame.Get(PoseLandmarkType.RightAnkle))
                : float.NaN;

            bool anyLeg = !float.IsNaN(_lastMarginLeft) || !float.IsNaN(_lastMarginRight);
            if (!anyLeg)
            {
                // Не помечаем «не в позе»: HoldTimer сам сошьёт короткий провал грейсом.
                _lastSignal = float.NaN;
                _lastLean = float.NaN;
                FormState = ExerciseFormState.NotVisible;
                Cue = NotVisibleCue;
                Changed?.Invoke();
                return;
            }

            _lastSignal =
                float.IsNaN(_lastMarginLeft) ? _lastMarginRight
                : float.IsNaN(_lastMarginRight) ? _lastMarginLeft
                : Math.Max(_lastMarginLeft, _lastMarginRight);

            // Наклон торса от вертикали — прокси стены; плечо со стороны более видимой ноги,
            // невидимое плечо (vis < порога) проверку пропускает, а не блокирует.
            bool leanLeft = leftVis >= rightVis;
            PoseLandmark shoulder = frame.Get(leanLeft ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);
            PoseLandmark hip = frame.Get(leanLeft ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
            _lastLean = shoulder.Visibility >= _minVisibility
                ? (float)(Math.Atan2(Math.Abs(shoulder.X - hip.X), Math.Max(1e-6f, hip.Y - shoulder.Y)) * 180.0 / Math.PI)
                : float.NaN;

            bool seated = _lastSignal >= _seatLowAt && _lastSignal <= _seatHighAt;
            bool leanOk = float.IsNaN(_lastLean) || _lastLean <= _maxTorsoLeanDeg;
            bool inPose = seated && leanOk;
            _timer.Update(inPose, frame.TimestampSeconds);

            if (inPose)
            {
                FormState = ExerciseFormState.GoodForm;
                Cue = string.Empty;
            }
            else
            {
                FormState = ExerciseFormState.BadForm;
                Cue = _lastSignal > _seatHighAt ? "Ниже"
                    : _lastSignal < _seatLowAt ? "Выше"
                    : "Спиной к стене";
            }

            Changed?.Invoke();
        }

        public void Reset()
        {
            _timer.Reset();
            _lastSignal = float.NaN;
            _lastMarginLeft = float.NaN;
            _lastMarginRight = float.NaN;
            _lastLean = float.NaN;
            _lastVis = 0f;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            Changed?.Invoke();
        }
    }
}
