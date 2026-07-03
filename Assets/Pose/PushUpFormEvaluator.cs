namespace Mikey.Pose
{
    /// <summary>The push-up states/faults the evaluator can report, in priority order.</summary>
    public enum PushUpFault
    {
        /// <summary>In a valid plank and the body is straight — good.</summary>
        None,

        /// <summary>Not enough of the body is confidently in frame to score.</summary>
        BodyNotVisible,

        /// <summary>A body is visible but not in a push-up position (not a plank).</summary>
        NotInPosition,

        /// <summary>In a plank, but the body is bent (sagging or piking) — a form fault.</summary>
        NotStraight,
    }

    /// <summary>
    /// One frame's read-out: the 3D elbow angle (drives rep counting), the 3D body-line angle,
    /// the current state/fault and its cue, and the scored chain's visibility. All angles are
    /// 3D, so scoring is invariant to camera placement (front vs side).
    /// </summary>
    public readonly struct FormAssessment
    {
        public readonly PushUpFault Fault;
        public readonly float ElbowAngleDeg;
        public readonly float BodyAngleDeg;
        public readonly string Cue;
        public readonly float Visibility;

        public FormAssessment(PushUpFault fault, float elbowAngleDeg, float bodyAngleDeg, string cue, float visibility)
        {
            Fault = fault;
            ElbowAngleDeg = elbowAngleDeg;
            BodyAngleDeg = bodyAngleDeg;
            Cue = cue;
            Visibility = visibility;
        }

        /// <summary>Body confidently in frame (may or may not be in position).</summary>
        public bool BodyVisible => Fault != PushUpFault.BodyNotVisible;

        /// <summary>In a countable push-up position (a plank; possibly bent = a form fault).</summary>
        public bool PostureValid => Fault == PushUpFault.None || Fault == PushUpFault.NotStraight;
    }

    /// <summary>
    /// Pure, per-frame push-up scorer. Picks the better-visible body side, then works entirely
    /// in 3D so it is camera-orientation independent:
    /// <list type="bullet">
    ///   <item>Requires high, confident visibility of the scored joints (rejects garbage such
    ///   as MediaPipe hallucinating landmarks on a ceiling).</item>
    ///   <item>Gates on a push-up posture — the shoulder–hip–ankle line must be a plank;
    ///   a bent body (sitting/curled/noise) is "not in position" and never counts.</item>
    ///   <item>Measures the 3D elbow angle for the rep motion.</item>
    /// </list>
    /// Thresholds are constructor config.
    /// </summary>
    public sealed class PushUpFormEvaluator
    {
        private readonly float _minVisibility;
        private readonly float _straightMinDeg;
        private readonly float _positionMinDeg;

        /// <param name="minVisibility">Lowest visibility a scored chain may have to be trusted.</param>
        /// <param name="straightMinDeg">Body angle at/above which the plank counts as straight.</param>
        /// <param name="positionMinDeg">Body angle below which it isn't a push-up position at all.</param>
        public PushUpFormEvaluator(float minVisibility = 0.6f, float straightMinDeg = 160f, float positionMinDeg = 135f)
        {
            _minVisibility = minVisibility;
            _straightMinDeg = straightMinDeg;
            _positionMinDeg = positionMinDeg;
        }

        public FormAssessment Evaluate(PoseFrame frame)
        {
            float leftArmVis = frame.MinVisibility(PoseLandmarkType.LeftShoulder, PoseLandmarkType.LeftElbow, PoseLandmarkType.LeftWrist);
            float rightArmVis = frame.MinVisibility(PoseLandmarkType.RightShoulder, PoseLandmarkType.RightElbow, PoseLandmarkType.RightWrist);
            bool useLeftArm = leftArmVis >= rightArmVis;
            float armVis = useLeftArm ? leftArmVis : rightArmVis;

            float leftBodyVis = frame.MinVisibility(PoseLandmarkType.LeftShoulder, PoseLandmarkType.LeftHip, PoseLandmarkType.LeftAnkle);
            float rightBodyVis = frame.MinVisibility(PoseLandmarkType.RightShoulder, PoseLandmarkType.RightHip, PoseLandmarkType.RightAnkle);
            bool useLeftBody = leftBodyVis >= rightBodyVis;
            float bodyVis = useLeftBody ? leftBodyVis : rightBodyVis;

            float vis = armVis < bodyVis ? armVis : bodyVis;

            if (armVis < _minVisibility || bodyVis < _minVisibility)
                return new FormAssessment(PushUpFault.BodyNotVisible, float.NaN, float.NaN, "В кадр", vis);

            PoseLandmark shoulderA = frame.Get(useLeftArm ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);
            PoseLandmark elbow = frame.Get(useLeftArm ? PoseLandmarkType.LeftElbow : PoseLandmarkType.RightElbow);
            PoseLandmark wrist = frame.Get(useLeftArm ? PoseLandmarkType.LeftWrist : PoseLandmarkType.RightWrist);
            float elbowAngle = PoseMath.AngleDeg3D(shoulderA, elbow, wrist);

            PoseLandmark shoulderB = frame.Get(useLeftBody ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);
            PoseLandmark hip = frame.Get(useLeftBody ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
            PoseLandmark ankle = frame.Get(useLeftBody ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle);
            float bodyAngle = PoseMath.AngleDeg3D(shoulderB, hip, ankle);

            if (bodyAngle < _positionMinDeg)
                return new FormAssessment(PushUpFault.NotInPosition, elbowAngle, bodyAngle, "Прими упор лёжа", vis);
            if (bodyAngle < _straightMinDeg)
                return new FormAssessment(PushUpFault.NotStraight, elbowAngle, bodyAngle, "Держи тело прямым", vis);

            return new FormAssessment(PushUpFault.None, elbowAngle, bodyAngle, string.Empty, vis);
        }
    }
}
