using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores kizami zuki jodan — the lead-hand head punch that steps fudo dachi out into
    /// zenkutsu dachi — as the four phases of the level-1 design, all four required:
    ///
    /// 1. READY: fudo dachi clean for <c>readySeconds</c>. Until it is held, nothing is
    ///    judged at all: a punch thrown out of no stance is not a bad rep, it is not a rep.
    /// 2. STRIKE: an arm leaves kamae — the wrist is <c>punchReachShanks</c> ahead of its own
    ///    shoulder along the facing direction AND the elbow has opened past
    ///    <c>kamaeElbowDeg</c>. Both are needed: the guard already holds the lead wrist well
    ///    forward, and the folded elbow is what tells a guard from a punch.
    /// 3. PEAK: the frame of the deepest reach of the punching arm. Everything is judged
    ///    there — stance, elbow extension and wrist height — because that is the one instant
    ///    the technique actually claims to be correct.
    /// 4. RECOVERY: both arms back in. Folded back to under <c>kamaeElbowDeg</c> is a proper
    ///    kamae; pulled back still straight is "Верни руку".
    ///
    /// Every distance is in shank lengths and every direction comes from
    /// <see cref="StanceReader"/>, so body size, camera distance and which way the fighter
    /// faces move nothing: a mirrored stance flips <see cref="StanceReading.ForwardSign"/>
    /// and <see cref="StanceReading.FrontIsLeft"/> together, and reach stays positive.
    ///
    /// One cue at a time, grossest first: the live stance phrase while setting up (the
    /// player is fixing the stance, and the verdict of a rep already spoken is stale),
    /// then "Дойди до стойки" (the peak was not zenkutsu), "Ведущей рукой" (the far hand
    /// punched), "Выпрями руку", "Выше, в голову", "Верни руку". A judged verdict is held
    /// for <c>verdictSeconds</c> before the live stance phrase takes over again: right
    /// after the punch the fighter is standing in zenkutsu, so "Уже" would be true, live,
    /// and would flush the actual coaching out of the TTS queue half a second in.
    ///
    /// Unreadable feet mean the technique cannot be judged at all — the state goes
    /// <see cref="ExerciseFormState.NotVisible"/> with a framing hint rather than an
    /// invented fault (level-1 design, "ошибки и деградация"). Engine-free.
    /// </summary>
    public sealed class KizamiZukiAnalyzer : IExerciseAnalyzer
    {
        private const string NotVisibleCue = "В кадр (боком)";

        private readonly ScoringProfile _profile;
        private readonly float _minVisibility;
        private readonly double _readySeconds;
        private readonly float _punchReachShanks;
        private readonly float _minExtensionDeg;
        private readonly float _kamaeElbowDeg;
        private readonly float _wristAboveNoseShanks;
        private readonly float _wristBelowNoseShanks;
        private readonly double _verdictSeconds;

        private readonly HoldTimer _hold = new HoldTimer(graceSeconds: 1.0);

        private bool _punching;
        private bool _leadWasOut;
        private float _leadPeakReach;
        private float _leadPeakElbowDeg;
        private bool _leadPeakInJodan;
        private string _leadPeakStanceFault = string.Empty;
        private float _rearPeakReach;
        private string _rearPeakStanceFault = string.Empty;

        private string _verdict = string.Empty;
        private double _verdictAt = double.NaN;

        // Последние измерения ведущей руки — только для отладочной строки.
        private float _reach = float.NaN;
        private float _elbowDeg = float.NaN;
        private float _headOffset = float.NaN;
        private StanceKind _stance;
        private float _visibility;

        public string Id => "kizamizuki-jodan";
        public string DisplayName => "Kizami zuki jodan";
        public int Reps { get; private set; }
        public int NoReps { get; private set; }
        public string Cue { get; private set; } = NotVisibleCue;
        public ExerciseFormState FormState { get; private set; } = ExerciseFormState.NotVisible;

        public string DebugInfo =>
            $"{(_punching ? "punch" : _hold.CurrentSeconds >= _readySeconds ? "ready" : "setup")}  " +
            $"reach {Num(_reach, "0.00")}  elbow {Num(_elbowDeg, "0")}°  " +
            $"head {Num(_headOffset, "0.00")}  stance {_stance}  " +
            $"hold {_hold.CurrentSeconds:0.0}/{_readySeconds:0.0}s  {_profile}  vis {_visibility:0.00}";

        public event Action Changed;

        /// <param name="profile">Accepted so the sandbox builds every level-1 analyzer the
        /// same way, but it changes no scoring: a technique either passed all four phases or
        /// it did not, and there is no "counted with a flaw" case for leniency to act on.
        /// Придумывать различие ради симметрии не стали (как в <see cref="YokoGeriAnalyzer"/>).</param>
        /// <param name="readySeconds">How long fudo dachi must stay clean before a punch is judged.</param>
        /// <param name="punchReachShanks">Wrist ahead of its own shoulder, in shank lengths.</param>
        /// <param name="minExtensionDeg">Elbow angle at the peak that counts as extended.</param>
        /// <param name="kamaeElbowDeg">Below this the elbow counts as folded into kamae — the
        /// arm is then neither punching nor left hanging.</param>
        /// <param name="wristAboveNoseShanks">How far above the nose the wrist may be at the peak.</param>
        /// <param name="wristBelowNoseShanks">…and how far below it.</param>
        /// <param name="verdictSeconds">How long a judged verdict outlives the live stance cue.</param>
        public KizamiZukiAnalyzer(ScoringProfile profile = ScoringProfile.Strict,
            float minVisibility = 0.5f, double readySeconds = 0.5,
            float punchReachShanks = 0.7f, float minExtensionDeg = 150f, float kamaeElbowDeg = 120f,
            float wristAboveNoseShanks = 0.5f, float wristBelowNoseShanks = 0.25f,
            double verdictSeconds = 1.5)
        {
            _profile = profile;
            _minVisibility = minVisibility;
            _readySeconds = readySeconds;
            _punchReachShanks = punchReachShanks;
            _minExtensionDeg = minExtensionDeg;
            _kamaeElbowDeg = kamaeElbowDeg;
            _wristAboveNoseShanks = wristAboveNoseShanks;
            _wristBelowNoseShanks = wristBelowNoseShanks;
            _verdictSeconds = verdictSeconds;
        }

        public void ProcessFrame(PoseFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            // Стойка читается против обеих спек: fudo — гейт готовности, zenkutsu — зачёт
            // на пике. Чтение чистое и дешёвое, кэшировать нечего.
            StanceReading fudo = StanceReader.Read(frame, StanceSpec.Fudo, _minVisibility);
            StanceReading zenkutsu = StanceReader.Read(frame, StanceSpec.Zenkutsu, _minVisibility);

            float armVisibility = Math.Min(
                frame.Get(PoseLandmarkType.Nose).Visibility,
                Math.Min(
                    frame.MinVisibility(PoseLandmarkType.LeftShoulder, PoseLandmarkType.LeftElbow, PoseLandmarkType.LeftWrist),
                    frame.MinVisibility(PoseLandmarkType.RightShoulder, PoseLandmarkType.RightElbow, PoseLandmarkType.RightWrist)));
            _visibility = Math.Min(fudo.Visibility, armVisibility);

            // Без стоп нет направления «вперёд», без головы нет уровня jodan — судить
            // такой кадр нечем, и выдумывать ошибку по нему нельзя.
            if (!fudo.Readable || armVisibility < _minVisibility)
            {
                _hold.Update(false, frame.TimestampSeconds);
                _reach = _elbowDeg = _headOffset = float.NaN;
                _stance = StanceKind.None;
                FormState = ExerciseFormState.NotVisible;
                Cue = NotVisibleCue;
                Changed?.Invoke();
                return;
            }

            _stance = fudo.Fault.Length == 0 ? StanceKind.Fudo
                : zenkutsu.Fault.Length == 0 ? StanceKind.Zenkutsu
                : StanceKind.None;

            bool leadIsLeft = fudo.FrontIsLeft;             // ведущая рука — со стороны передней ноги
            float shank = fudo.Shank;
            float sign = fudo.ForwardSign;

            float leadReach = Reach(frame, leadIsLeft, sign, shank);
            float rearReach = Reach(frame, !leadIsLeft, sign, shank);
            float leadElbowDeg = ElbowDeg(frame, leadIsLeft);
            float rearElbowDeg = ElbowDeg(frame, !leadIsLeft);
            // Y растёт ВНИЗ: положительное смещение — запястье ниже носа.
            float headOffset = (Wrist(frame, leadIsLeft).Y - frame.Get(PoseLandmarkType.Nose).Y) / shank;

            _reach = leadReach;
            _elbowDeg = leadElbowDeg;
            _headOffset = headOffset;

            bool leadOut = IsOut(leadReach, leadElbowDeg);
            bool rearOut = IsOut(rearReach, rearElbowDeg);

            if (!_punching)
            {
                _hold.Update(fudo.Fault.Length == 0, frame.TimestampSeconds);
                // Грейс HoldTimer переносит готовность через первый кадр удара: стойка
                // на нём уже не fudo, а рука только пошла вперёд.
                if (_hold.CurrentSeconds >= _readySeconds && (leadOut || rearOut))
                    BeginStrike();
            }

            if (_punching)
            {
                if (leadOut || rearOut)
                {
                    if (leadOut)
                        _leadWasOut = true;
                    if (leadReach > _leadPeakReach)
                    {
                        _leadPeakReach = leadReach;
                        _leadPeakElbowDeg = leadElbowDeg;
                        _leadPeakInJodan = headOffset >= -_wristAboveNoseShanks
                            && headOffset <= _wristBelowNoseShanks;
                        _leadPeakStanceFault = zenkutsu.Fault;
                    }
                    if (rearReach > _rearPeakReach)
                    {
                        _rearPeakReach = rearReach;
                        _rearPeakStanceFault = zenkutsu.Fault;
                    }
                }
                else
                {
                    Judge(leadElbowDeg < _kamaeElbowDeg, frame.TimestampSeconds);
                }
            }

            bool verdictFresh = frame.TimestampSeconds - _verdictAt < _verdictSeconds;
            Cue = verdictFresh ? _verdict
                : _punching ? string.Empty
                : fudo.Fault;
            FormState = Cue.Length == 0 ? ExerciseFormState.GoodForm : ExerciseFormState.BadForm;
            Changed?.Invoke();
        }

        public void Reset()
        {
            _hold.Reset();
            Reps = 0;
            NoReps = 0;
            _punching = false;
            _leadWasOut = false;
            _verdict = string.Empty;
            _verdictAt = double.NaN;
            _reach = _elbowDeg = _headOffset = float.NaN;
            _stance = StanceKind.None;
            _visibility = 0f;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            Changed?.Invoke();
        }

        /// <summary>Wrist ahead of its own shoulder along the facing direction, in shanks.</summary>
        private static float Reach(PoseFrame frame, bool left, float forwardSign, float shank) =>
            (Wrist(frame, left).X - frame.Get(left ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder).X)
            * forwardSign / shank;

        private static float ElbowDeg(PoseFrame frame, bool left) => PoseMath.AngleDeg(
            frame.Get(left ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder),
            frame.Get(left ? PoseLandmarkType.LeftElbow : PoseLandmarkType.RightElbow),
            Wrist(frame, left));

        private static PoseLandmark Wrist(PoseFrame frame, bool left) =>
            frame.Get(left ? PoseLandmarkType.LeftWrist : PoseLandmarkType.RightWrist);

        /// <summary>Arm out of kamae: carried forward AND no longer folded.</summary>
        private bool IsOut(float reach, float elbowDeg) =>
            reach >= _punchReachShanks && elbowDeg >= _kamaeElbowDeg;

        private void BeginStrike()
        {
            _punching = true;
            _leadWasOut = false;
            _leadPeakReach = float.NegativeInfinity;
            _leadPeakElbowDeg = float.NaN;
            _leadPeakInJodan = false;
            _leadPeakStanceFault = string.Empty;
            _rearPeakReach = float.NegativeInfinity;
            _rearPeakStanceFault = string.Empty;
            _verdict = string.Empty;
            _verdictAt = double.NaN;
        }

        // Повтор судится там, где он закончился: обе руки убраны. Пик берётся у той руки,
        // которая била — иначе разбор удара дальней рукой читал бы стойку в случайном кадре.
        private void Judge(bool returnedToKamae, double timeSeconds)
        {
            _punching = false;
            _hold.Reset();                                  // следующий повтор — с новой fudo

            bool wrongArm = !_leadWasOut;
            string stanceFault = wrongArm ? _rearPeakStanceFault : _leadPeakStanceFault;

            string fault =
                stanceFault.Length > 0 ? "Дойди до стойки"
                : wrongArm ? "Ведущей рукой"
                : _leadPeakElbowDeg < _minExtensionDeg ? "Выпрями руку"
                : !_leadPeakInJodan ? "Выше, в голову"
                : !returnedToKamae ? "Верни руку"
                : string.Empty;

            if (fault.Length == 0)
                Reps++;
            else
                NoReps++;

            _verdict = fault;
            _verdictAt = timeSeconds;
        }

        private static string Num(float value, string format) =>
            float.IsNaN(value) ? "--" : value.ToString(format);
    }
}
