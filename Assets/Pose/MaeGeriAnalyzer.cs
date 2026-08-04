using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores mae geri (front kick) at a requested height from a side-on view. The kicking
    /// leg is whichever ankle rises (no left/right choice in the UI); its lift, normalized
    /// against the support leg's shank (0 = floor, 1 = support-knee height), drives the
    /// shared <see cref="LegLiftCycle"/>. While the leg is lifted the analyzer samples the
    /// peak <see cref="KickZone"/> (same-frame hip/shoulder anchors) and the minimum knee
    /// bend. Lenient policy: a kick reaching the requested zone OR higher counts; below it
    /// is a no-rep ("Выше"); a straight-leg swing without a chamber counts but is tallied
    /// in <see cref="NoReps"/> ("Сначала колено"). <see cref="BestZone"/> keeps the highest
    /// zone reached this set regardless of the request — the flexibility stat reads it.
    /// Engine-free.
    /// </summary>
    public sealed class MaeGeriAnalyzer : IExerciseAnalyzer
    {
        private const string NotVisibleCue = "В кадр (боком)";

        private readonly KickZone _requested;
        private readonly LegLiftCycle _cycle;
        private readonly float _minVisibility;
        private readonly float _chamberMaxKneeDeg;
        private readonly float _smoothingAlpha;

        private float _smoothedLift = float.NaN;
        private KickZone _peakZone = KickZone.None;
        private float _minKneeDeg = 180f;
        private float _lastVis;

        public string Id => "maegeri-" + _requested.ToString().ToLowerInvariant();
        public string DisplayName => "Mae geri " + _requested.ToString().ToLowerInvariant();
        public int Reps { get; private set; }
        public int NoReps { get; private set; }
        public string Cue { get; private set; } = NotVisibleCue;
        public ExerciseFormState FormState { get; private set; } = ExerciseFormState.NotVisible;

        /// <summary>Highest zone reached this set, independent of the requested level.</summary>
        public KickZone BestZone { get; private set; } = KickZone.None;

        public string DebugInfo =>
            $"lift {(float.IsNaN(_smoothedLift) ? "--" : _smoothedLift.ToString("0.00"))}  " +
            $"phase {_cycle.Phase}  peak {_peakZone}  minKnee {_minKneeDeg:0}°  vis {_lastVis:0.00}";

        public event Action Changed;

        public MaeGeriAnalyzer(KickZone requested, LegLiftCycle cycle = null, float minVisibility = 0.6f,
            float chamberMaxKneeDeg = 110f, float smoothingAlpha = 0.6f)
        {
            if (requested == KickZone.None)
                throw new ArgumentOutOfRangeException(nameof(requested));
            _requested = requested;
            _cycle = cycle ?? new LegLiftCycle(liftedAt: 1.0f, groundedAt: 0.25f, minLiftSeconds: 0.2);
            _minVisibility = minVisibility;
            _chamberMaxKneeDeg = chamberMaxKneeDeg;
            _smoothingAlpha = smoothingAlpha;
        }

        public void ProcessFrame(PoseFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            float leftVis = frame.MinVisibility(PoseLandmarkType.LeftHip, PoseLandmarkType.LeftKnee, PoseLandmarkType.LeftAnkle);
            float rightVis = frame.MinVisibility(PoseLandmarkType.RightHip, PoseLandmarkType.RightKnee, PoseLandmarkType.RightAnkle);
            _lastVis = Math.Min(leftVis, rightVis);

            if (_lastVis < _minVisibility)
            {
                _smoothedLift = float.NaN;
                FormState = ExerciseFormState.NotVisible;
                Cue = NotVisibleCue;
                Changed?.Invoke();
                return;
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
                    _minKneeDeg = 180f;
                    Cue = string.Empty;
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
            }

            if (completed)
            {
                if (_peakZone > BestZone)
                    BestZone = _peakZone;

                if (_peakZone >= _requested)
                {
                    Reps++;
                    if (_minKneeDeg > _chamberMaxKneeDeg)
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

            FormState = string.IsNullOrEmpty(Cue) ? ExerciseFormState.GoodForm : ExerciseFormState.BadForm;
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
            _minKneeDeg = 180f;
            _lastVis = 0f;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            Changed?.Invoke();
        }
    }
}
