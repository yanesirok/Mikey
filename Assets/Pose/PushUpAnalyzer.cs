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
    ///   <item><b>Debounce</b> — the bottom phase opens only after consecutive below-threshold
    ///   frames (see <see cref="RepCounter"/>), so a single noisy frame while holding a plank
    ///   cannot start a phantom rep; smoothing is off by default (α = 1).</item>
    /// </list>
    /// Counting policy depends on <see cref="ScoringProfile"/>:
    /// <list type="bullet">
    ///   <item><b>Lenient</b> (level 0) — every full-range rep counts; a bent-body form fault is
    ///   tallied in <see cref="NoReps"/> but does not block the count.</item>
    ///   <item><b>Strict</b> (level 1 teaching) — a rep with a named fault goes ONLY to
    ///   <see cref="NoReps"/>: hips sagging ("Таз выше") or piking ("Таз ниже"), chest not low
    ///   enough ("Ниже грудь"), arms not locked out at the top ("Выпрями руки").</item>
    /// </list>
    /// Engine-free and EditMode-testable.
    /// </summary>
    public sealed class PushUpAnalyzer : IExerciseAnalyzer
    {
        // Строгие пороги (стартовые, калибруются по записям с устройства, как и мягкие).
        // Дно: локоть должен дойти до прямого угла — счётчик по умолчанию довольствуется 105°.
        // Верх: рука должна распрямиться заметно выше порога счётчика (140°), иначе не дожал.
        private const float StrictDepthMaxElbowDeg = 90f;
        private const float StrictLockoutMinElbowDeg = 150f;

        private readonly RepCounter _counter;
        private readonly PushUpFormEvaluator _evaluator;
        private readonly float _smoothingAlpha;
        private readonly float _wristBelowHipMin;
        private readonly ScoringProfile _profile;
        private int _wristOkFrames;
        private int _wristBadFrames;

        private bool _formOkThisRep = true;
        private float _hipSagAtFault = float.NaN;
        private float _minElbowThisRep = float.NaN;
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
            float smoothingAlpha = 1f, float wristBelowHipMin = 0f,
            ScoringProfile profile = ScoringProfile.Lenient)
        {
            // Дефолт: без сглаживания (α=1) + дебаунс низа. На реальном fps устройства (6–15
            // кадров/с с провалами) EMA не успевала довести угол до порога — повторы терялись;
            // от одиночных шумовых кадров вместо неё защищает дебаунс.
            _counter = counter ?? new RepCounter(downDebounceFrames: 2);
            _evaluator = evaluator ?? new PushUpFormEvaluator();
            _smoothingAlpha = smoothingAlpha;
            _wristBelowHipMin = wristBelowHipMin;
            _profile = profile;
        }

        private bool Strict => _profile == ScoringProfile.Strict;

        /// <summary>Sag vs pike, named the way a coach names it: lift the hips or drop them.</summary>
        private static string HipCue(float hipSag) => hipSag >= 0f ? "Таз выше" : "Таз ниже";

        /// <summary>
        /// Строгий вердикт по завершённому циклу, null — чисто. Порядок фраз = приоритет:
        /// сперва корпус (грубейшее), потом глубина, потом дожим.
        /// ponytail: дожим судим по кадру завершения — счётчик закрывает повтор уже на 140°,
        /// так что при редких кадрах возможен ложный «Выпрями руки»; лечится порогом.
        /// </summary>
        private string StrictRepFault(float topElbowDeg) =>
            !_formOkThisRep ? HipCue(_hipSagAtFault)
            : _minElbowThisRep > StrictDepthMaxElbowDeg ? "Ниже грудь"
            : topElbowDeg < StrictLockoutMinElbowDeg ? "Выпрями руки"
            : null;

        public void ProcessFrame(PoseFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            FormAssessment assessment = _evaluator.Evaluate(frame);
            CurrentFault = assessment.Fault;
            // Строгий профиль называет ошибку конкретно: не «держи тело прямым», а куда таз.
            Cue = Strict && assessment.Fault == PushUpFault.NotStraight
                ? HipCue(assessment.HipSag)
                : assessment.Cue;
            _lastVis = assessment.Visibility;
            _lastBodyAngle = assessment.BodyAngleDeg;

            // Only advance the rep counter when the body is visible AND in a push-up posture.
            // Otherwise hold state (and drop the smoothing baseline so a resumed rep starts clean).
            if (!assessment.PostureValid)
            {
                _smoothedElbow = float.NaN;
                _counter.ResetDownStreak();
                Changed?.Invoke();
                return;
            }

            _smoothedElbow = float.IsNaN(_smoothedElbow)
                ? assessment.ElbowAngleDeg
                : _smoothedElbow + _smoothingAlpha * (assessment.ElbowAngleDeg - _smoothedElbow);

            // Минимум локтя копим по всему циклу, а не по фазе Down: с дебаунсом в 2 кадра
            // самый глубокий кадр может прийтись на первый «низкий», до открытия фазы.
            if (float.IsNaN(_minElbowThisRep) || _smoothedElbow < _minElbowThisRep)
                _minElbowThisRep = _smoothedElbow;

            RepPhase prevPhase = _counter.Phase;
            bool completed = _counter.Update(_smoothedElbow, frame.TimestampSeconds);
            RepPhase phase = _counter.Phase;

            if (prevPhase != RepPhase.Down && phase == RepPhase.Down)
            {
                _formOkThisRep = true;
                _hipSagAtFault = float.NaN;
                _wristOkFrames = 0;
                _wristBadFrames = 0;
            }
            if (phase == RepPhase.Down)
            {
                if (assessment.Fault == PushUpFault.NotStraight)
                {
                    _formOkThisRep = false;
                    if (float.IsNaN(_hipSagAtFault))
                        _hipSagAtFault = assessment.HipSag;
                }
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
                    string strictFault = Strict ? StrictRepFault(_smoothedElbow) : null;
                    if (strictFault != null)
                    {
                        // Строгий зачёт: повтор с названной ошибкой — только в NoReps.
                        NoReps++;
                        Cue = strictFault;
                    }
                    else
                    {
                        Reps++;
                        if (!_formOkThisRep)
                            NoReps++;
                    }
                }
                _formOkThisRep = true;
                _hipSagAtFault = float.NaN;
            }

            // Глубину копим от вершины до вершины: обнуляем на выходе из низа — и после
            // засчитанного повтора, и после отвергнутого (слишком быстрого) цикла.
            if (prevPhase == RepPhase.Down && phase == RepPhase.Up)
                _minElbowThisRep = float.NaN;

            Changed?.Invoke();
        }

        public void Reset()
        {
            _counter.Reset();
            Reps = 0;
            NoReps = 0;
            _formOkThisRep = true;
            _hipSagAtFault = float.NaN;
            _minElbowThisRep = float.NaN;
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
