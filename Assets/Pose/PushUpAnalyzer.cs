using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Turns a stream of <see cref="PoseFrame"/>s into the HUD numbers: a rep count, the
    /// current state/fault, and one cue. Composes the pure <see cref="RepCounter"/>
    /// (motion) with <see cref="PushUpFormEvaluator"/> (3D posture + visibility), adding:
    /// <list type="bullet">
    ///   <item><b>Gating</b> — only counts while the body is confidently visible AND in a
    ///   valid push-up posture, so ceiling/garbage and out-of-position noise never count.</item>
    ///   <item><b>Smoothing</b> — an EMA on the elbow angle kills the per-frame jitter that
    ///   otherwise racks up phantom reps.</item>
    /// </list>
    /// Counting policy: every full-range rep counts; a bent-body form fault is tallied in
    /// <see cref="NoReps"/> but does not block the count. Engine-free and EditMode-testable.
    /// </summary>
    public sealed class PushUpAnalyzer : IExerciseAnalyzer
    {
        private readonly RepCounter _counter;
        private readonly PushUpFormEvaluator _evaluator;
        private readonly float _smoothingAlpha;
        private readonly float _wristBelowHipMin;
        private int _wristOkFrames;
        private int _wristBadFrames;

        private bool _formOkThisRep = true;
        private float _smoothedElbow = float.NaN;
        private float _lastVis;
        private float _lastBodyAngle = float.NaN;

        public string Id => "pushup";
        public string DisplayName => "Push-ups";

        public ExerciseFormState FormState =>
            !BodyVisible || !InPosition ? ExerciseFormState.NotVisible
            : CurrentFault == PushUpFault.None ? ExerciseFormState.GoodForm
            : ExerciseFormState.BadForm;

        public string DebugInfo =>
            $"elbow {(float.IsNaN(_smoothedElbow) ? "--" : _smoothedElbow.ToString("0"))}°  " +
            $"body {(float.IsNaN(_lastBodyAngle) ? "--" : _lastBodyAngle.ToString("0"))}°  " +
            $"phase {_counter.Phase}  vis {_lastVis:0.00}  {CurrentFault}  wrist {_wristOkFrames}/{_wristBadFrames}";

        public event Action Changed;

        public int Reps { get; private set; }
        public int NoReps { get; private set; }
        public PushUpFault CurrentFault { get; private set; } = PushUpFault.BodyNotVisible;
        public string Cue { get; private set; } = "В кадр";

        public bool BodyVisible => CurrentFault != PushUpFault.BodyNotVisible;
        public bool InPosition => CurrentFault == PushUpFault.None || CurrentFault == PushUpFault.NotStraight;

        public PushUpAnalyzer(RepCounter counter = null, PushUpFormEvaluator evaluator = null,
            float smoothingAlpha = 0.6f, float wristBelowHipMin = 0f)
        {
            _counter = counter ?? new RepCounter();
            _evaluator = evaluator ?? new PushUpFormEvaluator();
            _smoothingAlpha = smoothingAlpha;
            _wristBelowHipMin = wristBelowHipMin;
        }

        public void ProcessFrame(PoseFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            FormAssessment assessment = _evaluator.Evaluate(frame);
            CurrentFault = assessment.Fault;
            Cue = assessment.Cue;
            _lastVis = assessment.Visibility;
            _lastBodyAngle = assessment.BodyAngleDeg;

            // Only advance the rep counter when the body is visible AND in a push-up posture.
            // Otherwise hold state (and drop the smoothing baseline so a resumed rep starts clean).
            if (!assessment.PostureValid)
            {
                _smoothedElbow = float.NaN;
                Changed?.Invoke();
                return;
            }

            _smoothedElbow = float.IsNaN(_smoothedElbow)
                ? assessment.ElbowAngleDeg
                : _smoothedElbow + _smoothingAlpha * (assessment.ElbowAngleDeg - _smoothedElbow);

            RepPhase prevPhase = _counter.Phase;
            bool completed = _counter.Update(_smoothedElbow, frame.TimestampSeconds);
            RepPhase phase = _counter.Phase;

            if (prevPhase != RepPhase.Down && phase == RepPhase.Down)
            {
                _formOkThisRep = true;
                _wristOkFrames = 0;
                _wristBadFrames = 0;
            }
            if (phase == RepPhase.Down)
            {
                if (assessment.Fault == PushUpFault.NotStraight)
                    _formOkThisRep = false;
                // NaN >= x == false, так что неопределённая метрика честно идёт в «плохие».
                if (assessment.WristBelowHip >= _wristBelowHipMin)
                    _wristOkFrames++;
                else
                    _wristBadFrames++;
            }

            if (completed)
            {
                // «Ладони на полу»: если в большинстве кадров нижней фазы запястье было НЕ ниже
                // таза — это не отжимание (стоя со сгибанием рук и т.п.), цикл молча игнорируется.
                if (_wristOkFrames >= _wristBadFrames)
                {
                    Reps++;
                    if (!_formOkThisRep)
                        NoReps++;
                }
                _formOkThisRep = true;
            }

            Changed?.Invoke();
        }

        public void Reset()
        {
            _counter.Reset();
            Reps = 0;
            NoReps = 0;
            _formOkThisRep = true;
            _smoothedElbow = float.NaN;
            _lastVis = 0f;
            _lastBodyAngle = float.NaN;
            _wristOkFrames = 0;
            _wristBadFrames = 0;
            CurrentFault = PushUpFault.BodyNotVisible;
            Cue = "В кадр";
            Changed?.Invoke();
        }
    }
}
