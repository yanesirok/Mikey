using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores the slow yoko-geri (controlled side leg raise) facing the camera — the leg
    /// travels sideways, so a profile view would hide its height. Reuses the mae geri lift
    /// signal (kicking ankle vs the support shank) through <see cref="LegLiftCycle"/>; a
    /// cycle counts only when the leg stayed up at least <c>slowMinSeconds</c> — this is a
    /// balance drill, so a fast swing is the fault ("Медленнее"), not the height reached.
    /// <see cref="TotalLiftedSeconds"/> accumulates airtime across completed cycles for
    /// the balance stat. Engine-free.
    /// </summary>
    public sealed class YokoGeriAnalyzer : IExerciseAnalyzer
    {
        private const string NotVisibleCue = "В кадр (лицом)";

        private readonly LegLiftCycle _cycle;
        private readonly float _minVisibility;
        private readonly double _slowMinSeconds;
        private readonly float _smoothingAlpha;

        private float _smoothedLift = float.NaN;
        private float _lastVis;

        public string Id => "yokogeri-slow";
        public string DisplayName => "Yoko-geri slow";
        public int Reps { get; private set; }
        public int NoReps { get; private set; }
        public string Cue { get; private set; } = NotVisibleCue;
        public ExerciseFormState FormState { get; private set; } = ExerciseFormState.NotVisible;

        /// <summary>Total airtime across completed lift cycles, seconds (balance stat).</summary>
        public double TotalLiftedSeconds { get; private set; }

        public string DebugInfo =>
            $"lift {(float.IsNaN(_smoothedLift) ? "--" : _smoothedLift.ToString("0.00"))}  " +
            $"phase {_cycle.Phase}  air {_cycle.LiftedSeconds:0.0}s  total {TotalLiftedSeconds:0.0}s  vis {_lastVis:0.00}";

        public event Action Changed;

        public YokoGeriAnalyzer(LegLiftCycle cycle = null, float minVisibility = 0.6f,
            double slowMinSeconds = 2.0, float smoothingAlpha = 0.6f)
        {
            _cycle = cycle ?? new LegLiftCycle(liftedAt: 1.0f, groundedAt: 0.25f, minLiftSeconds: 0.2);
            _minVisibility = minVisibility;
            _slowMinSeconds = slowMinSeconds;
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

            float liftLeft = Lift01(frame, kickingLeft: true);
            float liftRight = Lift01(frame, kickingLeft: false);
            float lift = Math.Max(liftLeft, liftRight);

            _smoothedLift = float.IsNaN(_smoothedLift)
                ? lift
                : _smoothedLift + _smoothingAlpha * (lift - _smoothedLift);

            LiftPhase prevPhase = _cycle.Phase;
            bool completed = _cycle.Update(_smoothedLift, frame.TimestampSeconds);

            if (prevPhase == LiftPhase.Grounded && _cycle.Phase == LiftPhase.Lifted)
                Cue = string.Empty;

            if (completed)
            {
                TotalLiftedSeconds += _cycle.LiftedSeconds;
                if (_cycle.LiftedSeconds >= _slowMinSeconds)
                {
                    Reps++;
                }
                else
                {
                    NoReps++;
                    Cue = "Медленнее";
                }
            }

            FormState = string.IsNullOrEmpty(Cue) ? ExerciseFormState.GoodForm : ExerciseFormState.BadForm;
            Changed?.Invoke();
        }

        // Same normalized lift signal as mae geri: ankle height over the other leg's shank.
        private static float Lift01(PoseFrame frame, bool kickingLeft)
        {
            PoseLandmark kickAnkle = frame.Get(kickingLeft ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle);
            PoseLandmark supportAnkle = frame.Get(kickingLeft ? PoseLandmarkType.RightAnkle : PoseLandmarkType.LeftAnkle);
            PoseLandmark supportKnee = frame.Get(kickingLeft ? PoseLandmarkType.RightKnee : PoseLandmarkType.LeftKnee);

            float shank = supportAnkle.Y - supportKnee.Y;
            if (shank < 1e-4f)
                return 0f;
            return (supportAnkle.Y - kickAnkle.Y) / shank;
        }

        public void Reset()
        {
            _cycle.Reset();
            Reps = 0;
            NoReps = 0;
            TotalLiftedSeconds = 0;
            _smoothedLift = float.NaN;
            _lastVis = 0f;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            Changed?.Invoke();
        }
    }
}
