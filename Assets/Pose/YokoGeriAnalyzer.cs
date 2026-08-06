using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores yoko geri (side kick) at a requested height facing the camera — the leg
    /// travels sideways, so a profile view would hide its height. Same lift signal and
    /// <see cref="LegLiftCycle"/> as mae geri; the height zone is sampled only on frames
    /// where the leg is extended (in-plane knee angle ≥ minExtensionDeg — noisy z depth
    /// is not involved), so a raised chamber alone is not a kick. Lenient policy: a kick
    /// reaching the requested zone OR higher counts; below it is a no-rep ("Выше"); a
    /// lift that never extends is a no-rep ("Выпрями ногу"). <see cref="BestZone"/> keeps
    /// the highest zone this set (flexibility stat); <see cref="TotalLiftedSeconds"/>
    /// accumulates airtime of counted reps (balance stat). Holding a wall for support is
    /// allowed and not checked. Engine-free.
    /// </summary>
    public sealed class YokoGeriAnalyzer : IExerciseAnalyzer
    {
        private const string NotVisibleCue = "В кадр (лицом)";

        private readonly KickZone _requested;
        private readonly LegLiftCycle _cycle;
        private readonly float _minVisibility;
        private readonly float _minExtensionDeg;
        private readonly float _smoothingAlpha;

        private float _smoothedLift = float.NaN;
        private KickZone _peakZone = KickZone.None;
        private float _lastKneeDeg = float.NaN;
        private float _lastVis;

        public string Id => "yokogeri-" + _requested.ToString().ToLowerInvariant();
        public string DisplayName => "Yoko geri " + _requested.ToString().ToLowerInvariant();
        public int Reps { get; private set; }
        public int NoReps { get; private set; }
        public string Cue { get; private set; } = NotVisibleCue;
        public ExerciseFormState FormState { get; private set; } = ExerciseFormState.NotVisible;

        /// <summary>Highest zone reached this set, independent of the requested level.</summary>
        public KickZone BestZone { get; private set; } = KickZone.None;

        /// <summary>Total airtime across counted reps, seconds (balance stat).</summary>
        public double TotalLiftedSeconds { get; private set; }

        public string DebugInfo =>
            $"lift {(float.IsNaN(_smoothedLift) ? "--" : _smoothedLift.ToString("0.00"))}  " +
            $"phase {_cycle.Phase}  peak {_peakZone}  knee {(float.IsNaN(_lastKneeDeg) ? "--" : _lastKneeDeg.ToString("0"))}°  " +
            $"total {TotalLiftedSeconds:0.0}s  vis {_lastVis:0.00}";

        public event Action Changed;

        public YokoGeriAnalyzer(KickZone requested, LegLiftCycle cycle = null, float minVisibility = 0.6f,
            float minExtensionDeg = 150f, float smoothingAlpha = 0.6f)
        {
            if (requested == KickZone.None)
                throw new ArgumentOutOfRangeException(nameof(requested));
            _requested = requested;
            _cycle = cycle ?? new LegLiftCycle(liftedAt: 1.0f, groundedAt: 0.25f, minLiftSeconds: 0.2);
            _minVisibility = minVisibility;
            _minExtensionDeg = minExtensionDeg;
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
                    Cue = string.Empty;
                }

                PoseLandmark ankle = frame.Get(kickLeft ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle);
                PoseLandmark knee = frame.Get(kickLeft ? PoseLandmarkType.LeftKnee : PoseLandmarkType.RightKnee);
                PoseLandmark hip = frame.Get(kickLeft ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
                PoseLandmark shoulder = frame.Get(kickLeft ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);

                _lastKneeDeg = PoseMath.AngleDeg(hip, knee, ankle);
                if (_lastKneeDeg >= _minExtensionDeg)
                {
                    KickZone zone = KickHeightZone.Classify(ankle.Y, hip.Y, shoulder.Y);
                    if (zone > _peakZone)
                        _peakZone = zone;
                }
            }

            if (completed)
            {
                if (_peakZone > BestZone)
                    BestZone = _peakZone;

                if (_peakZone >= _requested)
                {
                    Reps++;
                    TotalLiftedSeconds += _cycle.LiftedSeconds;
                }
                else
                {
                    NoReps++;
                    Cue = _peakZone == KickZone.None ? "Выпрями ногу" : "Выше";
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
            TotalLiftedSeconds = 0;
            _smoothedLift = float.NaN;
            _peakZone = KickZone.None;
            _lastKneeDeg = float.NaN;
            _lastVis = 0f;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            Changed?.Invoke();
        }
    }
}
