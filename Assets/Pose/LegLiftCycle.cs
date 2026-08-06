namespace Mikey.Pose
{
    /// <summary>Where the kicking foot is in the lift cycle.</summary>
    public enum LiftPhase
    {
        Grounded,
        Lifted,
    }

    /// <summary>
    /// Pure detector of one leg-lift cycle (kick, slow raise): foot leaves the floor,
    /// peaks, returns. Feeds on a normalized lift signal (0 = foot at floor level,
    /// 1 = at the support knee's height) with two thresholds for hysteresis; a cycle
    /// shorter than <c>minLiftSeconds</c> is treated as landmark jitter and dropped.
    /// The caller samples what it needs (peak zone, knee bend) while Phase is Lifted.
    /// Engine-free and EditMode-testable.
    /// </summary>
    public sealed class LegLiftCycle
    {
        private readonly float _liftedAt;
        private readonly float _groundedAt;
        private readonly double _minLiftSeconds;
        private double _liftStart;

        public LiftPhase Phase { get; private set; } = LiftPhase.Grounded;

        /// <summary>Duration of the current lift while Lifted; of the last completed one after.</summary>
        public double LiftedSeconds { get; private set; }

        /// <summary>Lift threshold that starts a cycle; kick analyzers gate zone sampling on it.</summary>
        public float LiftedAt => _liftedAt;

        public LegLiftCycle(float liftedAt = 1.0f, float groundedAt = 0.25f, double minLiftSeconds = 0.2)
        {
            _liftedAt = liftedAt;
            _groundedAt = groundedAt;
            _minLiftSeconds = minLiftSeconds;
        }

        /// <summary>Returns true exactly on the frame a long-enough lift returns to the ground.</summary>
        public bool Update(float lift01, double timeSeconds)
        {
            if (Phase == LiftPhase.Grounded)
            {
                if (lift01 >= _liftedAt)
                {
                    Phase = LiftPhase.Lifted;
                    _liftStart = timeSeconds;
                    LiftedSeconds = 0;
                }
                return false;
            }

            LiftedSeconds = timeSeconds - _liftStart;
            if (lift01 > _groundedAt)
                return false;

            Phase = LiftPhase.Grounded;
            return LiftedSeconds >= _minLiftSeconds;
        }

        public void Reset()
        {
            Phase = LiftPhase.Grounded;
            LiftedSeconds = 0;
            _liftStart = 0;
        }
    }
}
