using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores mae geri (front kick) from a side-on view. The kicking leg is whichever ankle
    /// rises (no left/right choice in the UI); its lift, normalized against the support leg's
    /// shank (0 = floor, 1 = support-knee height), drives the shared <see cref="LegLiftCycle"/>.
    /// While the leg is lifted the analyzer samples the peak <see cref="KickZone"/> (same-frame
    /// hip/shoulder anchors) and whether the knee ever folded into a chamber.
    ///
    /// <b>Lenient</b> — the level-0 measurement, unchanged: a kick reaching the requested zone
    /// OR higher counts; below it is a no-rep ("Выше"); a straight-leg swing without a chamber
    /// counts but is tallied in <see cref="NoReps"/> ("Сначала колено"). The stance is not read
    /// at all, so a kick from anywhere is scored.
    ///
    /// <b>Strict</b> — the level-1 technique "mae geri chudan из стойки", which is a whole
    /// movement rather than a height:
    /// <list type="bullet">
    /// <item>a cycle is judged only when a clean fudo OR zenkutsu was read shortly before the
    /// foot left the floor. Without one nothing is counted — not even a no-rep: a kick out of
    /// no stance is not a bad kick, and the live cue coaches the stance instead;</item>
    /// <item>the chamber must come BEFORE the extension, so only frames after the knee folded
    /// score a zone — a straight-leg swing that happens to bend on the way down does not pass;</item>
    /// <item>the height window is exact (yoko geri v5): higher → "Ниже", lower → "Выше";</item>
    /// <item>the foot must come back into the SAME stance within <c>returnSeconds</c> of landing,
    /// otherwise "Вернись в стойку";</item>
    /// <item>a rep with any named fault lands in <see cref="NoReps"/> only.</item>
    /// </list>
    /// A frame whose stance cannot be read at all (feet out of frame) is
    /// <see cref="ExerciseFormState.NotVisible"/> with a framing hint, never an invented fault.
    ///
    /// <see cref="BestZone"/> keeps the highest zone reached this set regardless of the request —
    /// the flexibility stat reads it. Engine-free.
    /// </summary>
    public sealed class MaeGeriAnalyzer : IExerciseAnalyzer
    {
        private const string NotVisibleCue = "В кадр (боком)";

        private readonly KickZone _requested;
        private readonly LegLiftCycle _cycle;
        private readonly float _minVisibility;
        private readonly float _chamberMaxKneeDeg;
        private readonly float _smoothingAlpha;
        private readonly ScoringProfile _profile;
        private readonly double _stanceMemorySeconds;
        private readonly double _returnSeconds;
        private readonly string _id;
        private readonly string _displayName;

        private float _smoothedLift = float.NaN;
        private KickZone _peakZone = KickZone.None;
        private KickZone _armedZone = KickZone.None;
        private bool _chambered;
        private float _minKneeDeg = 180f;
        private float _lastVis;

        // Стойка вокруг удара — только строгий профиль.
        private StanceKind _stanceBefore = StanceKind.None;     // последняя чистая, пока нога на полу
        private double _stanceBeforeAt = double.NaN;
        private StanceKind _stanceAtLift = StanceKind.None;     // гейт, замороженный на старте подъёма
        private string _stanceFault = string.Empty;
        private bool _awaitingReturn;
        private double _landedAt;

        public string Id => _id;
        public string DisplayName => _displayName;
        public int Reps { get; private set; }
        public int NoReps { get; private set; }
        public string Cue { get; private set; } = NotVisibleCue;
        public ExerciseFormState FormState { get; private set; } = ExerciseFormState.NotVisible;

        /// <summary>Highest zone reached this set, independent of the requested level.</summary>
        public KickZone BestZone { get; private set; } = KickZone.None;

        public string DebugInfo =>
            $"lift {(float.IsNaN(_smoothedLift) ? "--" : _smoothedLift.ToString("0.00"))}  " +
            $"phase {_cycle.Phase}  peak {_peakZone}  minKnee {_minKneeDeg:0}°  " +
            (_profile == ScoringProfile.Strict
                ? $"stance {_stanceBefore}→{_stanceAtLift}  armed {_armedZone}" +
                  $"{(_awaitingReturn ? "  ждём стойку" : string.Empty)}  "
                : string.Empty) +
            $"vis {_lastVis:0.00}";

        public event Action Changed;

        /// <param name="stanceMemorySeconds">Strict only: how stale the last clean stance may be
        /// at the moment the lift trips. Замеряется не «на кадре срыва» — там нога уже в воздухе
        /// и стойки в кадре нет, — а по последней чистой стойке перед ним.</param>
        /// <param name="returnSeconds">Strict only: window after landing in which the same stance
        /// must be back.</param>
        public MaeGeriAnalyzer(KickZone requested, LegLiftCycle cycle = null, float minVisibility = 0.6f,
            float chamberMaxKneeDeg = 110f, float smoothingAlpha = 0.6f,
            double stanceMemorySeconds = 0.5, double returnSeconds = 1.0,
            ScoringProfile profile = ScoringProfile.Lenient)
        {
            if (requested == KickZone.None)
                throw new ArgumentOutOfRangeException(nameof(requested));
            // Строгий профиль — не «та же мерка построже», а именованная техника уровня 1,
            // и Id не должен врать о том, что судили.
            if (profile == ScoringProfile.Strict && requested != KickZone.Chudan)
                throw new ArgumentOutOfRangeException(nameof(requested),
                    "Strict mae geri is the chudan-from-stance technique; it has no other zone.");

            _requested = requested;
            _cycle = cycle ?? new LegLiftCycle(liftedAt: 1.0f, groundedAt: 0.25f, minLiftSeconds: 0.2);
            _minVisibility = minVisibility;
            _chamberMaxKneeDeg = chamberMaxKneeDeg;
            _smoothingAlpha = smoothingAlpha;
            _profile = profile;
            _stanceMemorySeconds = stanceMemorySeconds;
            _returnSeconds = returnSeconds;

            bool strict = profile == ScoringProfile.Strict;
            _id = strict ? "maegeri-chudan-stance" : "maegeri-" + requested.ToString().ToLowerInvariant();
            _displayName = strict
                ? "Mae geri chudan (из стойки)"
                : "Mae geri " + requested.ToString().ToLowerInvariant();
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
            _lastVis = Math.Min(leftVis, rightVis);

            if (_lastVis < _minVisibility)
            {
                NotVisible();
                return;
            }

            StanceKind stance = StanceKind.None;
            if (_profile == ScoringProfile.Strict)
            {
                // Техника принимает обе стойки, поэтому читаем обе и берём чистую.
                StanceReading fudo = StanceReader.Read(frame, StanceSpec.Fudo, _minVisibility);
                StanceReading zenkutsu = StanceReader.Read(frame, StanceSpec.Zenkutsu, _minVisibility);
                if (!fudo.Readable)
                {
                    NotVisible();
                    return;
                }

                stance = fudo.Fault.Length == 0 ? StanceKind.Fudo
                    : zenkutsu.Fault.Length == 0 ? StanceKind.Zenkutsu
                    : StanceKind.None;
                _stanceFault = stance == StanceKind.None ? NearerFault(fudo, zenkutsu) : string.Empty;
            }

            // Kicking leg = the one lifted higher relative to the other leg's shank.
            float liftLeft = Lift01(frame, kickingLeft: true);
            float liftRight = Lift01(frame, kickingLeft: false);
            bool kickLeft = liftLeft >= liftRight;
            float lift = kickLeft ? liftLeft : liftRight;

            _smoothedLift = float.IsNaN(_smoothedLift)
                ? lift
                : _smoothedLift + _smoothingAlpha * (lift - _smoothedLift);

            LiftPhase prevPhase = _cycle.Phase;
            bool completed = _cycle.Update(_smoothedLift, frame.TimestampSeconds);

            if (_cycle.Phase == LiftPhase.Lifted)
            {
                if (prevPhase == LiftPhase.Grounded)
                {
                    _peakZone = KickZone.None;
                    _armedZone = KickZone.None;
                    _chambered = false;
                    _minKneeDeg = 180f;
                    Cue = string.Empty;
                    // Гейт замораживается здесь и по ПАМЯТИ: на кадре срыва нога уже в воздухе,
                    // стойки в нём нет — годится последняя чистая, если она только что была.
                    _stanceAtLift = frame.TimestampSeconds - _stanceBeforeAt <= _stanceMemorySeconds
                        ? _stanceBefore
                        : StanceKind.None;
                }

                PoseLandmark ankle = frame.Get(kickLeft ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle);
                PoseLandmark knee = frame.Get(kickLeft ? PoseLandmarkType.LeftKnee : PoseLandmarkType.RightKnee);
                PoseLandmark hip = frame.Get(kickLeft ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
                PoseLandmark shoulder = frame.Get(kickLeft ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);

                KickZone zone = KickHeightZone.Classify(ankle.Y, hip.Y, shoulder.Y);
                if (zone > _peakZone)
                    _peakZone = zone;

                float kneeDeg = PoseMath.AngleDeg3D(hip, knee, ankle);
                if (kneeDeg < _minKneeDeg)
                    _minKneeDeg = kneeDeg;
                if (kneeDeg <= _chamberMaxKneeDeg)
                    _chambered = true;

                // Порядок «замах раньше выпрямления» держится тем, что зону копим ТОЛЬКО
                // после чамбера: мах прямой ногой, согнувший колено на спуске, так не пройдёт.
                if (_chambered && zone > _armedZone)
                    _armedZone = zone;
            }

            if (completed)
            {
                if (_peakZone > BestZone)
                    BestZone = _peakZone;

                if (_profile == ScoringProfile.Strict)
                    ScoreStrict(frame.TimestampSeconds);
                else
                    ScoreLenient();
            }

            if (_profile == ScoringProfile.Strict)
            {
                // Живой cue стойки — пока нога на полу и по удару сказать нечего.
                if (!completed && !_awaitingReturn && _cycle.Phase == LiftPhase.Grounded)
                    Cue = _stanceFault;

                if (_cycle.Phase == LiftPhase.Grounded && stance != StanceKind.None)
                {
                    _stanceBefore = stance;
                    _stanceBeforeAt = frame.TimestampSeconds;
                }

                if (_awaitingReturn)
                    ResolveReturn(stance, frame.TimestampSeconds);
            }

            FormState = string.IsNullOrEmpty(Cue) ? ExerciseFormState.GoodForm : ExerciseFormState.BadForm;
            Changed?.Invoke();
        }

        // Мягкий зачёт уровня 0: полная амплитуда — повтор, огрех формы только помечается.
        private void ScoreLenient()
        {
            if (_peakZone >= _requested)
            {
                Reps++;
                if (!_chambered)
                {
                    NoReps++;
                    Cue = "Сначала колено";
                }
            }
            else
            {
                NoReps++;
                Cue = "Выше";
            }
        }

        private void ScoreStrict(double now)
        {
            // Не из стойки — судить нечего: это не ошибка удара, а неготовность к нему.
            if (_stanceAtLift == StanceKind.None)
                return;

            // Замах обязателен и обязан быть ПЕРВЫМ: высота, взятая до чамбера (armed отстал
            // от peak), — это мах ногой, а не удар, и правится он той же фразой.
            string fault = !_chambered || _armedZone < _peakZone ? "Сначала колено"
                : _armedZone > _requested ? "Ниже"
                : _armedZone < _requested ? "Выше"
                : string.Empty;

            if (fault.Length != 0)
            {
                NoReps++;
                Cue = fault;
                return;
            }

            // Повтор ещё не повтор: техника кончается стойкой, а не касанием пола, —
            // и на самом кадре приземления стойка ещё не собрана, поэтому зачёт ждёт.
            _awaitingReturn = true;
            _landedAt = now;
        }

        private void ResolveReturn(StanceKind stance, double now)
        {
            if (stance == _stanceAtLift)
            {
                Reps++;
                _awaitingReturn = false;
                Cue = string.Empty;
                return;
            }

            // Ушёл в следующий удар, не собрав стойку — тот же провал, что и вышедшее окно.
            if (_cycle.Phase == LiftPhase.Lifted || now - _landedAt > _returnSeconds)
            {
                NoReps++;
                _awaitingReturn = false;
                Cue = "Вернись в стойку";
            }
        }

        /// <summary>Fault phrase of the stance the fighter is CLOSER to (both are accepted here):
        /// «Шире шаг» из зенкуцу при почти собранной фудо только сбивает.</summary>
        private static string NearerFault(StanceReading fudo, StanceReading zenkutsu) =>
            LengthGap(StanceSpec.Fudo, fudo.Length01) <= LengthGap(StanceSpec.Zenkutsu, zenkutsu.Length01)
                ? fudo.Fault
                : zenkutsu.Fault;

        private static float LengthGap(StanceSpec spec, float length01) =>
            Math.Max(0f, spec.MinLength - length01) + Math.Max(0f, length01 - spec.MaxLength);

        private void NotVisible()
        {
            _smoothedLift = float.NaN;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            Changed?.Invoke();
        }

        // Lift of one ankle normalized by the OTHER (support) leg's shank length.
        private static float Lift01(PoseFrame frame, bool kickingLeft)
        {
            PoseLandmark kickAnkle = frame.Get(kickingLeft ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle);
            PoseLandmark supportAnkle = frame.Get(kickingLeft ? PoseLandmarkType.RightAnkle : PoseLandmarkType.LeftAnkle);
            PoseLandmark supportKnee = frame.Get(kickingLeft ? PoseLandmarkType.RightKnee : PoseLandmarkType.LeftKnee);

            float shank = supportAnkle.Y - supportKnee.Y;   // > 0: колено выше лодыжки
            if (shank < 1e-4f)
                return 0f;
            return (supportAnkle.Y - kickAnkle.Y) / shank;
        }

        public void Reset()
        {
            _cycle.Reset();
            Reps = 0;
            NoReps = 0;
            BestZone = KickZone.None;
            _smoothedLift = float.NaN;
            _peakZone = KickZone.None;
            _armedZone = KickZone.None;
            _chambered = false;
            _minKneeDeg = 180f;
            _lastVis = 0f;
            _stanceBefore = StanceKind.None;
            _stanceBeforeAt = double.NaN;
            _stanceAtLift = StanceKind.None;
            _stanceFault = string.Empty;
            _awaitingReturn = false;
            _landedAt = 0;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            Changed?.Invoke();
        }
    }
}
