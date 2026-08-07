using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores yoko geri (side kick) at a requested height facing the camera — the leg
    /// travels sideways, so a profile view would hide its height. Same lift signal and
    /// <see cref="LegLiftCycle"/> as mae geri. The height zone is sampled only on frames
    /// where the leg is extended (in-plane knee angle ≥ minExtensionDeg — noisy z depth
    /// is not involved) and is gated by the RAW lift of that frame (the smoothed value
    /// lags and would leak descent frames): a single extended frame at ≥ fastKickAt
    /// scores immediately (fast kicks live for one frame; a dropping leg never extends
    /// that high), while frames in the working band ≥ kickBandAt score only when the
    /// cycle holds ≥ minBandFrames of them — a controlled kick keeps the leg extended
    /// at height, a pendulum drop passes through in one frame. Lenient policy: reaching
    /// the requested zone OR higher counts; below is a no-rep ("Выше"); a lift that
    /// never extends at height is a no-rep ("Выпрями ногу"). <see cref="BestZone"/>
    /// keeps the highest zone this set (flexibility stat); <see cref="TotalLiftedSeconds"/>
    /// accumulates airtime of counted reps (balance stat). Holding a wall for support
    /// is allowed and not checked. Engine-free.
    /// </summary>
    public sealed class YokoGeriAnalyzer : IExerciseAnalyzer
    {
        private const string NotVisibleCue = "В кадр (лицом)";

        private readonly KickZone _requested;
        private readonly LegLiftCycle _cycle;
        private readonly float _minVisibility;
        private readonly float _minExtensionDeg;
        private readonly float _smoothingAlpha;
        private readonly float _fastKickAt;
        private readonly float _kickBandAt;
        private readonly int _minBandFrames;

        private float _smoothedLift = float.NaN;
        private KickZone _fastPeak = KickZone.None;
        private KickZone _bandPeak = KickZone.None;
        private int _bandFrames;
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
            $"phase {_cycle.Phase}  fast {_fastPeak}  band {_bandPeak}x{_bandFrames}  " +
            $"knee {(float.IsNaN(_lastKneeDeg) ? "--" : _lastKneeDeg.ToString("0"))}°  " +
            $"total {TotalLiftedSeconds:0.0}s  vis {_lastVis:0.00}";

        public event Action Changed;

        public YokoGeriAnalyzer(KickZone requested, LegLiftCycle cycle = null, float minVisibility = 0.6f,
            float minExtensionDeg = 150f, float smoothingAlpha = 0.6f,
            float fastKickAt = 1.2f, float kickBandAt = 0.45f, int minBandFrames = 2)
        {
            if (requested == KickZone.None)
                throw new ArgumentOutOfRangeException(nameof(requested));
            _requested = requested;
            _cycle = cycle ?? new LegLiftCycle(liftedAt: 1.0f, groundedAt: 0.25f, minLiftSeconds: 0.2);
            _minVisibility = minVisibility;
            _minExtensionDeg = minExtensionDeg;
            _smoothingAlpha = smoothingAlpha;
            _fastKickAt = fastKickAt;
            _kickBandAt = kickBandAt;
            _minBandFrames = minBandFrames;
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
                    _fastPeak = KickZone.None;
                    _bandPeak = KickZone.None;
                    _bandFrames = 0;
                    Cue = string.Empty;
                }

                PoseLandmark ankle = frame.Get(kickLeft ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle);
                PoseLandmark knee = frame.Get(kickLeft ? PoseLandmarkType.LeftKnee : PoseLandmarkType.RightKnee);
                PoseLandmark hip = frame.Get(kickLeft ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
                PoseLandmark shoulder = frame.Get(kickLeft ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);

                _lastKneeDeg = PoseMath.AngleDeg(hip, knee, ankle);
                // Гейт — по сырому подъёму кадра: сглаженный отстаёт и протаскивает
                // опускания. Опускающаяся нога-маятник не выпрямляется выше ~1.0 и
                // проносится через полосу за один кадр — сигнатуре удара не отвечает.
                if (_lastKneeDeg >= _minExtensionDeg)
                {
                    KickZone zone = KickHeightZone.Classify(ankle.Y, hip.Y, shoulder.Y);
                    if (lift >= _fastKickAt && zone > _fastPeak)
                        _fastPeak = zone;
                    if (lift >= _kickBandAt)
                    {
                        _bandFrames++;
                        if (zone > _bandPeak)
                            _bandPeak = zone;
                    }
                }
            }

            if (completed)
            {
                KickZone peak = _fastPeak;
                if (_bandFrames >= _minBandFrames && _bandPeak > peak)
                    peak = _bandPeak;

                if (peak > BestZone)
                    BestZone = peak;

                if (peak >= _requested)
                {
                    Reps++;
                    TotalLiftedSeconds += _cycle.LiftedSeconds;
                }
                else
                {
                    NoReps++;
                    Cue = peak == KickZone.None ? "Выпрями ногу" : "Выше";
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
            _fastPeak = KickZone.None;
            _bandPeak = KickZone.None;
            _bandFrames = 0;
            _lastKneeDeg = float.NaN;
            _lastVis = 0f;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            Changed?.Invoke();
        }
    }
}
