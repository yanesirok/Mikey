using System;

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

        /// <summary>Насколько запястье ниже таза, в долях длины корпуса (плечо–таз).
        /// Положительно в упоре лёжа (ладони на полу), отрицательно у стоящего с согнутой
        /// рукой. NaN, когда тело не видно. Реп-проверку делает анализатор.</summary>
        public readonly float WristBelowHip;

        /// <summary>Signed vertical offset of the hip from the shoulder→ankle line
        /// (<see cref="PoseMath.HipVerticalOffset"/>): positive = sagging, negative = piking.
        /// <see cref="BodyAngleDeg"/> alone is unsigned, so it cannot tell the two apart —
        /// строгий профиль по этому знаку выбирает между «Таз выше» и «Таз ниже».</summary>
        public readonly float HipSag;

        public readonly string Cue;
        public readonly float Visibility;

        public FormAssessment(PushUpFault fault, float elbowAngleDeg, float bodyAngleDeg, float wristBelowHip,
            float hipSag, string cue, float visibility)
        {
            Fault = fault;
            ElbowAngleDeg = elbowAngleDeg;
            BodyAngleDeg = bodyAngleDeg;
            WristBelowHip = wristBelowHip;
            HipSag = hipSag;
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
        public PushUpFormEvaluator(float minVisibility = 0.5f, float straightMinDeg = 160f, float positionMinDeg = 120f)
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
                return new FormAssessment(PushUpFault.BodyNotVisible, float.NaN, float.NaN, float.NaN, float.NaN, "В кадр", vis);

            PoseLandmark shoulderA = frame.Get(useLeftArm ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);
            PoseLandmark elbow = frame.Get(useLeftArm ? PoseLandmarkType.LeftElbow : PoseLandmarkType.RightElbow);
            PoseLandmark wrist = frame.Get(useLeftArm ? PoseLandmarkType.LeftWrist : PoseLandmarkType.RightWrist);
            PoseLandmark shoulderB = frame.Get(useLeftBody ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);
            PoseLandmark hip = frame.Get(useLeftBody ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
            PoseLandmark ankle = frame.Get(useLeftBody ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle);

            // Точка за пределами кадра — экстраполяция, а не наблюдение: MediaPipe дорисовывает
            // её с высокой visibility (на устройстве видели лодыжку на x≈1.05 с vis 0.95),
            // и такая «уверенная» точка порождает фантомные позы. Не видно — значит не видно.
            if (!InFrame(shoulderA) || !InFrame(elbow) || !InFrame(wrist)
                || !InFrame(shoulderB) || !InFrame(hip) || !InFrame(ankle))
                return new FormAssessment(PushUpFault.BodyNotVisible, float.NaN, float.NaN, float.NaN, float.NaN, "В кадр", vis);

            float elbowAngle = PoseMath.AngleDeg3D(shoulderA, elbow, wrist);
            float bodyAngle = PoseMath.AngleDeg3D(shoulderB, hip, ankle);

            // «Ладони на полу»: в упоре лёжа запястья ниже таза; нормируем на длину корпуса,
            // чтобы метрика не зависела от дистанции до камеры. Порог применяет анализатор
            // на уровне повтора (по нижней фазе), а не по кадру — кадровый вариант съедает
            // настоящие повторы из-за дрожания точек.
            float torso = Dist2D(shoulderB, hip);
            float wristBelowHip = torso < 1e-4f ? float.NaN : (wrist.Y - hip.Y) / torso;

            // Знак отклонения таза от линии плечо–лодыжка: провис или пик. Считается здесь,
            // а не в анализаторе, потому что сторона тела выбрана здесь — иначе анализатору
            // пришлось бы повторять этот же выбор и разъезжаться с вердиктом.
            float hipSag = PoseMath.HipVerticalOffset(shoulderB, hip, ankle);

            if (bodyAngle < _positionMinDeg)
                return new FormAssessment(PushUpFault.NotInPosition, elbowAngle, bodyAngle, wristBelowHip, hipSag, "Прими упор лёжа", vis);
            if (bodyAngle < _straightMinDeg)
                return new FormAssessment(PushUpFault.NotStraight, elbowAngle, bodyAngle, wristBelowHip, hipSag, "Держи тело прямым", vis);

            return new FormAssessment(PushUpFault.None, elbowAngle, bodyAngle, wristBelowHip, hipSag, string.Empty, vis);
        }

        private static bool InFrame(PoseLandmark p) =>
            p.X >= 0f && p.X <= 1f && p.Y >= 0f && p.Y <= 1f;

        private static float Dist2D(PoseLandmark a, PoseLandmark b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
