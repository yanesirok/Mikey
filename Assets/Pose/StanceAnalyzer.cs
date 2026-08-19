using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores holding one karate stance. Every frame goes through <see cref="StanceReader"/>;
    /// <c>holdSeconds</c> of uninterrupted clean geometry award one rep, measured by
    /// <see cref="HoldTimer"/> so a tracker blink up to a second is bridged instead of
    /// breaking the hold. A named fault breaks the hold IMMEDIATELY — the grace exists for
    /// lost tracking, not for a wrong stance — and tallies one <see cref="NoReps"/> per
    /// broken hold, never one per faulty frame; the score itself is never cleared, a
    /// mistake costs the current attempt and nothing else.
    ///
    /// <see cref="Cue"/> is live: the current fault phrase, or "" while the stance is clean.
    /// When the feet are not readable the stance cannot be judged at all, so the state is
    /// <see cref="ExerciseFormState.NotVisible"/> with a framing hint rather than an
    /// invented fault (level-1 design, "ошибки и деградация").
    ///
    /// <see cref="ScoringProfile"/> is carried so the sandbox can build every level-1
    /// analyzer the same way, but it changes no scoring here: a hold has no "counted with
    /// a flaw" case for leniency to act on. Engine-free.
    /// </summary>
    public sealed class StanceAnalyzer : IExerciseAnalyzer
    {
        private const string NotVisibleCue = "В кадр (боком)";

        private static readonly StanceReading Blank = new StanceReading(
            false, StanceKind.None, string.Empty, 0f, false,
            float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, 0f);

        private readonly StanceSpec _spec;
        private readonly ScoringProfile _profile;
        private readonly double _holdSeconds;
        private readonly float _minVisibility;
        private readonly HoldTimer _timer = new HoldTimer(graceSeconds: 1.0);

        private StanceReading _last = Blank;

        public string Id => "stance-" + _spec.Kind.ToString().ToLowerInvariant();
        public string DisplayName => _spec.Kind + " dachi";
        public int Reps { get; private set; }
        public int NoReps { get; private set; }
        public string Cue { get; private set; } = NotVisibleCue;
        public ExerciseFormState FormState { get; private set; } = ExerciseFormState.NotVisible;

        public string DebugInfo =>
            $"len {Num(_last.Length01, "0.00")}  " +
            $"front {Num(_last.FrontKneeDeg, "0")}°  back {Num(_last.BackKneeDeg, "0")}°  " +
            $"lean {Num(_last.TorsoLeanDeg, "0")}°  " +
            $"lead {(_last.Readable ? _last.FrontIsLeft ? "L" : "R" : "-")}" +
            $"{(_last.ForwardSign > 0f ? "→" : _last.ForwardSign < 0f ? "←" : "?")}  " +
            $"hold {_timer.CurrentSeconds:0.0}/{_holdSeconds:0.0}s  {_profile}  vis {_last.Visibility:0.00}";

        public event Action Changed;

        public StanceAnalyzer(StanceKind kind, ScoringProfile profile = ScoringProfile.Strict,
            double holdSeconds = 3.0, float minVisibility = 0.5f)
        {
            _spec = StanceSpec.For(kind);       // бросит на StanceKind.None
            _profile = profile;
            _holdSeconds = holdSeconds;
            _minVisibility = minVisibility;
        }

        public void ProcessFrame(PoseFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            _last = StanceReader.Read(frame, _spec, _minVisibility);

            if (!_last.Readable)
            {
                // Не рвём удержание руками: короткий провал сошьёт грейс HoldTimer,
                // длинный он же и оборвёт — но это потеря трекинга, а не ошибка стойки.
                _timer.Update(false, frame.TimestampSeconds);
                FormState = ExerciseFormState.NotVisible;
                Cue = NotVisibleCue;
                Changed?.Invoke();
                return;
            }

            if (_last.Fault.Length == 0)
            {
                _timer.Update(true, frame.TimestampSeconds);
                if (_timer.CurrentSeconds >= _holdSeconds)
                {
                    Reps++;
                    _timer.Reset();             // следующая стойка отсчитывается с нуля
                }
                FormState = ExerciseFormState.GoodForm;
                Cue = string.Empty;
            }
            else
            {
                // Один no-rep на сорванное удержание, а не на каждый кривой кадр: после
                // сброса таймера повторные ошибки уже ничего не срывают.
                if (_timer.CurrentSeconds > 0)
                    NoReps++;
                _timer.Reset();
                FormState = ExerciseFormState.BadForm;
                Cue = _last.Fault;
            }

            Changed?.Invoke();
        }

        public void Reset()
        {
            _timer.Reset();
            Reps = 0;
            NoReps = 0;
            _last = Blank;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            Changed?.Invoke();
        }

        private static string Num(float value, string format) =>
            float.IsNaN(value) ? "--" : value.ToString(format);
    }
}
