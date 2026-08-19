using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores a ghost step (suri ashi) — sliding one stance length forward or back while
    /// the stance itself, the leading leg and the height of the head all stay put. Reads
    /// every frame through <see cref="StanceReader"/>, so the geometry of the stance lives
    /// in exactly one place; this class only watches how that reading travels.
    ///
    /// Three phases (level-1 design, "Ghost step"):
    /// 1. READY — the requested stance clean for <c>readySeconds</c> (<see cref="HoldTimer"/>,
    ///    so a tracker blink does not restart the wait). Arming snapshots the baseline:
    ///    ankle midpoint, shank, nose height, which leg leads and which way is forward.
    /// 2. STEP — the ankle midpoint leaves the baseline by more than <c>stepStartShanks</c>.
    ///    Travel is measured ALONG the requested direction, so a step the wrong way comes
    ///    out negative.
    /// 3. LANDING — the rep is judged the moment the ankle midpoint stops moving.
    ///
    /// The landing criterion deserves its own note, because it is the only part not given
    /// by the design: without an explicit "the step is over" event the analyzer either
    /// never scores (waiting for a state that never comes) or scores the same step twice
    /// (once mid-slide, once at the end). "Stopped" is measured as SPEED, not as a
    /// per-frame delta: the ankle midpoint travels slower than <c>stillShanksPerSecond</c>
    /// for <c>stillSeconds</c> of readable frames. Speed keeps the threshold honest at any
    /// frame rate — the same slide judged identically at 15 and 60 fps — which a delta in
    /// pixels-per-frame does not. Double counting is then impossible from the other side
    /// too: judging re-arms the analyzer, so the next rep needs a fresh clean hold first.
    ///
    /// A step in the WRONG direction is silently ignored — neither a rep nor a no-rep.
    /// Ghost steps are drilled back and forth: after every counted step forward the player
    /// steps back to where they started, and charging that return as a fault would make
    /// half of every set a mistake.
    ///
    /// Faults, one at a time, the coarsest first: "Не подпрыгивай" (the head bobbed more
    /// than <c>maxHeadBobShanks</c> at any point of the step), "Держи стойку" (the leading
    /// leg swapped, or the fighter landed in the OTHER stance), "Шире шаг" (stopped short
    /// of <c>minStepShanks</c>), then whatever <see cref="StanceReading.Fault"/> says about
    /// the landing itself. The verdict phrase outranks live stance coaching until the next
    /// arming, otherwise the fault of the very next frame overwrites it before the player
    /// (or <c>CoachVoice</c>) has heard it. Unreadable feet are not a fault at all —
    /// <see cref="ExerciseFormState.NotVisible"/> and a framing hint — and a step lost to
    /// tracking for longer than the grace is dropped, never judged off a stale baseline.
    ///
    /// <see cref="ScoringProfile"/> is carried so the sandbox builds every level-1 analyzer
    /// the same way, but changes nothing here: a flawed step is never counted in either
    /// profile, so leniency has no case to act on. Engine-free.
    /// </summary>
    public sealed class GhostStepAnalyzer : IExerciseAnalyzer
    {
        private const string NotVisibleCue = "В кадр (боком)";

        /// <summary>Level-0 dropout policy: a blink up to a second bridges, longer breaks.</summary>
        private const double GraceSeconds = 1.0;

        private static readonly StanceReading Blank = new StanceReading(
            false, StanceKind.None, string.Empty, 0f, false,
            float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, 0f);

        private readonly bool _forward;
        private readonly StanceSpec _spec;
        private readonly StanceSpec _otherSpec;
        private readonly ScoringProfile _profile;
        private readonly float _minVisibility;
        private readonly double _readySeconds;
        private readonly float _minStepShanks;
        private readonly float _maxHeadBobShanks;
        private readonly float _stepStartShanks;
        private readonly float _stillShanksPerSecond;
        private readonly double _stillSeconds;

        private readonly HoldTimer _timer = new HoldTimer(GraceSeconds);

        private StanceReading _last = Blank;
        private bool _armed;
        private bool _stepping;

        // Cue сейчас — приговор прошлому шагу, а не живая стойка. Приговор старше: без
        // этого флага фраза повтора живёт один кадр, её тут же затирает живая ошибка
        // стойки, и вслух человек слышит только вторую (та же логика, что у yoko geri,
        // где фраза цикла держится до следующего цикла).
        private bool _verdict;

        // База — снимок кадра, с которого начат шаг. Нормируем ВСЁ на голень базы, а не
        // текущего кадра: голень в кадре чуть гуляет, и делённый на неё сдвиг дрожал бы
        // вместе с ней, хотя ноги стоят на месте.
        private float _baseX;
        private float _baseNoseY;
        private float _baseShank;
        private float _baseForward;
        private bool _baseFrontIsLeft;

        private float _prevX = float.NaN;
        private double _prevTime;
        private double _still;
        private float _travel;
        private float _headMin;
        private float _headMax;

        public string Id => _forward ? "ghoststep-forward" : "ghoststep-back";
        public string DisplayName => _forward ? "Ghost step вперёд" : "Ghost step назад";
        public int Reps { get; private set; }
        public int NoReps { get; private set; }
        public string Cue { get; private set; } = NotVisibleCue;
        public ExerciseFormState FormState { get; private set; } = ExerciseFormState.NotVisible;

        public string DebugInfo =>
            $"{(_stepping ? "step" : _armed ? "ready" : "hold")}  " +
            $"move {Num(_travel, "0.00")}/{_minStepShanks:0.00}  " +
            $"head {Num(_headMax - _headMin, "0.00")}/{_maxHeadBobShanks:0.00}  " +
            $"len {Num(_last.Length01, "0.00")}  " +
            $"lead {(_last.Readable ? _last.FrontIsLeft ? "L" : "R" : "-")}" +
            $"{(_last.ForwardSign > 0f ? "→" : _last.ForwardSign < 0f ? "←" : "?")}  " +
            $"{_profile}  vis {_last.Visibility:0.00}";

        public event Action Changed;

        /// <param name="forward">Step along the fighter's facing direction, or against it.</param>
        /// <param name="stance">The stance to hold throughout — the step is judged against it.</param>
        /// <param name="minStepShanks">How far the ankle midpoint must travel, in shanks.</param>
        /// <param name="maxHeadBobShanks">Allowed swing of the nose height during the step, in shanks.</param>
        /// <param name="stepStartShanks">Movement that opens the step phase — above stance sway,
        /// well below a real step.</param>
        /// <param name="stillShanksPerSecond">Ankle-midpoint speed counted as "standing".</param>
        /// <param name="stillSeconds">How long that stillness must last to close the rep.</param>
        public GhostStepAnalyzer(bool forward, StanceKind stance = StanceKind.Zenkutsu,
            ScoringProfile profile = ScoringProfile.Strict, float minVisibility = 0.5f,
            double readySeconds = 0.5, float minStepShanks = 0.8f, float maxHeadBobShanks = 0.12f,
            float stepStartShanks = 0.25f, float stillShanksPerSecond = 0.3f, double stillSeconds = 0.2)
        {
            _spec = StanceSpec.For(stance);     // бросит на StanceKind.None
            _otherSpec = StanceSpec.For(stance == StanceKind.Fudo ? StanceKind.Zenkutsu : StanceKind.Fudo);
            _forward = forward;
            _profile = profile;
            _minVisibility = minVisibility;
            _readySeconds = readySeconds;
            _minStepShanks = minStepShanks;
            _maxHeadBobShanks = maxHeadBobShanks;
            _stepStartShanks = stepStartShanks;
            _stillShanksPerSecond = stillShanksPerSecond;
            _stillSeconds = stillSeconds;
        }

        public void ProcessFrame(PoseFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            _last = StanceReader.Read(frame, _spec, _minVisibility);
            if (!_last.Readable)
            {
                // Стопы не видны — судить нечего: грейс HoldTimer сошьёт короткий провал,
                // длинный он же и оборвёт, но ошибкой это не станет.
                _timer.Update(false, frame.TimestampSeconds);

                // Шаг, потерянный дольше грейса, не судим вовсе: база устарела, и человек,
                // вернувшийся в кадр уже стоящим, получил бы выдуманный повтор.
                if (_stepping && frame.TimestampSeconds - _prevTime > GraceSeconds)
                    ReArm();

                FormState = ExerciseFormState.NotVisible;
                Cue = NotVisibleCue;
                _verdict = false;
                Changed?.Invoke();
                return;
            }

            float midX = 0.5f * (frame.Get(PoseLandmarkType.LeftAnkle).X
                + frame.Get(PoseLandmarkType.RightAnkle).X);

            if (!_stepping)
            {
                if (_last.Fault.Length != 0)
                {
                    _timer.Reset();             // грейс — для потери трекинга, не для кривой стойки
                    _armed = false;
                    if (!_verdict)
                        Cue = _last.Fault;
                }
                else
                {
                    _timer.Update(true, frame.TimestampSeconds);
                    if (!_armed)
                    {
                        if (_timer.CurrentSeconds >= _readySeconds)
                        {
                            Arm(frame, midX);
                            Cue = string.Empty; // готов — фраза за прошлый повтор отработала
                            _verdict = false;
                        }
                    }
                    else if (Math.Abs(midX - _baseX) / _baseShank > _stepStartShanks)
                    {
                        StartStep();
                    }
                }
            }

            if (_stepping)
            {
                // Стойку по дороге не судим: в середине шага ноги проходят мимо друг друга,
                // и любая живая фраза о стойке была бы враньём.
                Track(frame, midX);
                if (_still >= _stillSeconds)
                    Judge(frame);
            }

            FormState = Cue.Length == 0 ? ExerciseFormState.GoodForm : ExerciseFormState.BadForm;
            Changed?.Invoke();
        }

        private void Arm(PoseFrame frame, float midX)
        {
            _armed = true;
            _baseX = midX;
            _baseNoseY = frame.Get(PoseLandmarkType.Nose).Y;
            _baseShank = _last.Shank;
            _baseFrontIsLeft = _last.FrontIsLeft;
            _baseForward = _last.ForwardSign;
        }

        private void StartStep()
        {
            _stepping = true;
            _prevX = float.NaN;
            _still = 0;
            _travel = 0f;
            _headMin = 0f;                      // база — сама точка отсчёта размаха
            _headMax = 0f;
        }

        private void Track(PoseFrame frame, float midX)
        {
            float stepDir = _baseForward * (_forward ? 1f : -1f);
            _travel = (midX - _baseX) * stepDir / _baseShank;

            float head = (_baseNoseY - frame.Get(PoseLandmarkType.Nose).Y) / _baseShank;
            if (head < _headMin) _headMin = head;
            if (head > _headMax) _headMax = head;

            double dt = frame.TimestampSeconds - _prevTime;
            if (!float.IsNaN(_prevX) && dt > 0.0)
            {
                float speed = (float)(Math.Abs(midX - _prevX) / _baseShank / dt);
                _still = speed < _stillShanksPerSecond ? _still + dt : 0.0;
            }
            _prevX = midX;
            _prevTime = frame.TimestampSeconds;
        }

        private void Judge(PoseFrame frame)
        {
            // Шаг в другую сторону — это возврат на исходную точку между повторами, а не
            // кривой повтор: no-rep за него сделал бы половину подхода ошибочной.
            if (_travel <= 0f)
            {
                ReArm();
                return;
            }

            string fault =
                _headMax - _headMin > _maxHeadBobShanks ? "Не подпрыгивай" :
                _last.FrontIsLeft != _baseFrontIsLeft || LandedInOtherStance(frame) ? "Держи стойку" :
                _travel < _minStepShanks ? "Шире шаг" :
                _last.Fault;

            if (fault.Length == 0)
                Reps++;
            else
                NoReps++;

            Cue = fault;
            _verdict = fault.Length != 0;
            ReArm();
        }

        /// <summary>
        /// Читатель сверяет кадр с ОДНОЙ спекой, поэтому подмену стойки (зенкуцу → фудо)
        /// видно только вторым чтением, по другой спеке. Иначе приземление в фудо назвалось
        /// бы «Шире шаг» — совет, который правильную стойку не вернёт.
        /// </summary>
        private bool LandedInOtherStance(PoseFrame frame) =>
            _last.Fault.Length != 0 &&
            StanceReader.Read(frame, _otherSpec, _minVisibility).Fault.Length == 0;

        private void ReArm()
        {
            _stepping = false;
            _armed = false;
            _timer.Reset();                     // следующий шаг — с новой стойки и новой базы
            _prevX = float.NaN;
            _still = 0;
        }

        public void Reset()
        {
            ReArm();
            Reps = 0;
            NoReps = 0;
            _travel = 0f;
            _headMin = 0f;
            _headMax = 0f;
            _last = Blank;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            _verdict = false;
            Changed?.Invoke();
        }

        private static string Num(float value, string format) =>
            float.IsNaN(value) ? "--" : value.ToString(format);
    }
}
