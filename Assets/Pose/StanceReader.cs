using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Geometric tolerances of one stance. Every measure is view-independent: the stance
    /// length is counted in SHANK LENGTHS of the same frame (so neither body size nor the
    /// distance to the camera moves a threshold) and the joints in degrees. The numbers
    /// below are the level-1 design starting values, expected to be re-calibrated against
    /// on-device recordings.
    /// </summary>
    public readonly struct StanceSpec
    {
        public readonly StanceKind Kind;

        /// <summary>Horizontal ankle separation, in shank lengths.</summary>
        public readonly float MinLength;
        public readonly float MaxLength;

        /// <summary>Front knee (hip-knee-ankle) window, degrees.</summary>
        public readonly float FrontKneeMin;
        public readonly float FrontKneeMax;

        /// <summary>Back knee window, degrees. Fudo checks both legs against the front one.</summary>
        public readonly float BackKneeMin;
        public readonly float BackKneeMax;

        /// <summary>How far the shoulders-to-hips line may tilt off vertical, degrees.</summary>
        public readonly float MaxTorsoLeanDeg;

        public StanceSpec(StanceKind kind, float minLength, float maxLength,
            float frontKneeMin, float frontKneeMax, float backKneeMin, float backKneeMax,
            float maxTorsoLeanDeg)
        {
            Kind = kind;
            MinLength = minLength;
            MaxLength = maxLength;
            FrontKneeMin = frontKneeMin;
            FrontKneeMax = frontKneeMax;
            BackKneeMin = backKneeMin;
            BackKneeMax = backKneeMax;
            MaxTorsoLeanDeg = maxTorsoLeanDeg;
        }

        /// <summary>Fudo dachi: short stance, both knees softly bent, torso upright.</summary>
        public static StanceSpec Fudo =>
            new StanceSpec(StanceKind.Fudo, 1.0f, 1.6f, 150f, 170f, 150f, 170f, 10f);

        /// <summary>Zenkutsu dachi: long stance, front knee loaded, back leg straight.</summary>
        public static StanceSpec Zenkutsu =>
            new StanceSpec(StanceKind.Zenkutsu, 2.0f, 2.8f, 100f, 130f, 160f, 180f, 15f);

        public static StanceSpec For(StanceKind kind)
        {
            switch (kind)
            {
                case StanceKind.Fudo: return Fudo;
                case StanceKind.Zenkutsu: return Zenkutsu;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }

    /// <summary>
    /// What one frame says about the requested stance: whether it could be read at all,
    /// the single prioritized fault phrase (empty when the stance is correct), and the raw
    /// measures behind that verdict — for the debug HUD and for threshold calibration.
    /// All measures are NaN when <see cref="Readable"/> is false.
    /// </summary>
    public readonly struct StanceReading
    {
        /// <summary>Enough of the body AND at least one foot was confidently in frame.</summary>
        public readonly bool Readable;

        /// <summary>The requested stance when <see cref="Fault"/> is empty, otherwise None.</summary>
        public readonly StanceKind Kind;

        /// <summary>The one coaching phrase to say; "" when the stance matches the spec.</summary>
        public readonly string Fault;

        /// <summary>+1 or -1: which way along X the fighter faces (0 when unreadable).</summary>
        public readonly float ForwardSign;

        /// <summary>True when the left leg is the front one. Mirrored stances read the same.</summary>
        public readonly bool FrontIsLeft;

        /// <summary>Ankle separation in shank lengths.</summary>
        public readonly float Length01;

        public readonly float FrontKneeDeg;
        public readonly float BackKneeDeg;
        public readonly float TorsoLeanDeg;

        /// <summary>Normalization unit: knee-to-ankle length of the better-visible leg.</summary>
        public readonly float Shank;

        /// <summary>Weakest link among the landmarks this reading rests on.</summary>
        public readonly float Visibility;

        public StanceReading(bool readable, StanceKind kind, string fault, float forwardSign,
            bool frontIsLeft, float length01, float frontKneeDeg, float backKneeDeg,
            float torsoLeanDeg, float shank, float visibility)
        {
            Readable = readable;
            Kind = kind;
            Fault = fault;
            ForwardSign = forwardSign;
            FrontIsLeft = frontIsLeft;
            Length01 = length01;
            FrontKneeDeg = frontKneeDeg;
            BackKneeDeg = backKneeDeg;
            TorsoLeanDeg = torsoLeanDeg;
            Shank = shank;
            Visibility = visibility;
        }
    }

    /// <summary>
    /// Reads a karate stance out of one side-on frame. Pure geometry, engine-free: the
    /// stance analyzer, the punch, the kick and the ghost step all gate on this one call.
    ///
    /// Every distance is divided by the shank length of the same frame, which makes the
    /// thresholds independent of body size and camera distance. "Forward" comes from the
    /// feet (toe minus heel along X, averaged over the feet that are visible) — the only
    /// cue in a profile view that does not flip when the fighter stands the other way
    /// round; the front leg is then simply the ankle further along that direction.
    ///
    /// A frame whose feet cannot be seen is NOT a fault: <see cref="StanceReading.Readable"/>
    /// goes false with an empty <see cref="StanceReading.Fault"/> and the caller shows a
    /// framing hint. Inventing "wrong stance" out of a cropped frame would coach the player
    /// into fixing something they never did wrong.
    /// </summary>
    public static class StanceReader
    {
        private const float MinShank = 1e-4f;

        // Носок и пятка совпали по X — фигура стоит в камеру, а не боком: направление
        // «вперёд» из такого кадра не достаётся ни при какой точности.
        private const float MinToeOffset = 1e-4f;

        public static StanceReading Read(PoseFrame frame, StanceSpec spec, float minVisibility = 0.5f)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (spec.Kind == StanceKind.None)
                throw new ArgumentOutOfRangeException(nameof(spec));

            float bodyVis = Math.Min(
                Math.Min(
                    frame.MinVisibility(PoseLandmarkType.LeftShoulder, PoseLandmarkType.LeftHip, PoseLandmarkType.LeftKnee),
                    frame.MinVisibility(PoseLandmarkType.RightShoulder, PoseLandmarkType.RightHip, PoseLandmarkType.RightKnee)),
                Math.Min(
                    frame.Get(PoseLandmarkType.LeftAnkle).Visibility,
                    frame.Get(PoseLandmarkType.RightAnkle).Visibility));

            // Дальняя стопа в профиль часто перекрыта — хватает одной уверенной.
            float leftFootVis = Math.Min(
                frame.Get(PoseLandmarkType.LeftHeel).Visibility,
                frame.Get(PoseLandmarkType.LeftFootIndex).Visibility);
            float rightFootVis = Math.Min(
                frame.Get(PoseLandmarkType.RightHeel).Visibility,
                frame.Get(PoseLandmarkType.RightFootIndex).Visibility);

            float visibility = Math.Min(bodyVis, Math.Max(leftFootVis, rightFootVis));
            if (visibility < minVisibility)
                return Unreadable(visibility);

            float toe = 0f;
            int feet = 0;
            if (leftFootVis >= minVisibility)
            {
                toe += frame.Get(PoseLandmarkType.LeftFootIndex).X - frame.Get(PoseLandmarkType.LeftHeel).X;
                feet++;
            }
            if (rightFootVis >= minVisibility)
            {
                toe += frame.Get(PoseLandmarkType.RightFootIndex).X - frame.Get(PoseLandmarkType.RightHeel).X;
                feet++;
            }
            toe /= feet;
            if (Math.Abs(toe) < MinToeOffset)
                return Unreadable(visibility);
            float forwardSign = toe > 0f ? 1f : -1f;

            bool shankLeft = Math.Min(
                frame.Get(PoseLandmarkType.LeftKnee).Visibility,
                frame.Get(PoseLandmarkType.LeftAnkle).Visibility) >= Math.Min(
                frame.Get(PoseLandmarkType.RightKnee).Visibility,
                frame.Get(PoseLandmarkType.RightAnkle).Visibility);
            float shank = Distance(
                frame.Get(shankLeft ? PoseLandmarkType.LeftKnee : PoseLandmarkType.RightKnee),
                frame.Get(shankLeft ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle));
            if (shank < MinShank)
                return Unreadable(visibility);

            PoseLandmark leftAnkle = frame.Get(PoseLandmarkType.LeftAnkle);
            PoseLandmark rightAnkle = frame.Get(PoseLandmarkType.RightAnkle);
            bool frontIsLeft = leftAnkle.X * forwardSign >= rightAnkle.X * forwardSign;

            PoseLandmark frontAnkle = frontIsLeft ? leftAnkle : rightAnkle;
            PoseLandmark backAnkle = frontIsLeft ? rightAnkle : leftAnkle;
            PoseLandmark frontKnee = frame.Get(frontIsLeft ? PoseLandmarkType.LeftKnee : PoseLandmarkType.RightKnee);
            PoseLandmark backKnee = frame.Get(frontIsLeft ? PoseLandmarkType.RightKnee : PoseLandmarkType.LeftKnee);
            PoseLandmark frontHip = frame.Get(frontIsLeft ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
            PoseLandmark backHip = frame.Get(frontIsLeft ? PoseLandmarkType.RightHip : PoseLandmarkType.LeftHip);

            float length01 = Math.Abs(frontAnkle.X - backAnkle.X) / shank;
            float frontKneeDeg = PoseMath.AngleDeg(frontHip, frontKnee, frontAnkle);
            float backKneeDeg = PoseMath.AngleDeg(backHip, backKnee, backAnkle);

            PoseLandmark leftShoulder = frame.Get(PoseLandmarkType.LeftShoulder);
            PoseLandmark rightShoulder = frame.Get(PoseLandmarkType.RightShoulder);
            float shoulderX = 0.5f * (leftShoulder.X + rightShoulder.X);
            float shoulderY = 0.5f * (leftShoulder.Y + rightShoulder.Y);
            float hipX = 0.5f * (frontHip.X + backHip.X);
            float hipY = 0.5f * (frontHip.Y + backHip.Y);
            float torsoLeanDeg = (float)(Math.Atan2(
                Math.Abs(shoulderX - hipX), Math.Max(1e-6f, hipY - shoulderY)) * 180.0 / Math.PI);

            string fault = Fault(spec, length01, frontKneeDeg, backKneeDeg, torsoLeanDeg);
            return new StanceReading(true, fault.Length == 0 ? spec.Kind : StanceKind.None, fault,
                forwardSign, frontIsLeft, length01, frontKneeDeg, backKneeDeg, torsoLeanDeg,
                shank, visibility);
        }

        // Одна фраза за раз, грубейшая первой: три замечания сразу не исправляют ни одного,
        // а TTS ещё и отстаёт на полсекунды.
        private static string Fault(StanceSpec spec, float length01,
            float frontKneeDeg, float backKneeDeg, float torsoLeanDeg)
        {
            if (spec.Kind == StanceKind.Fudo)
            {
                if (length01 < spec.MinLength) return "Шире";
                if (length01 > spec.MaxLength) return "Уже";
                // Фудо симметрична: одно окно на обе ноги, и подсказка адресована стойке
                // целиком — уточнять, какая именно нога пряма, некогда и незачем.
                if (Math.Max(frontKneeDeg, backKneeDeg) > spec.FrontKneeMax) return "Согни колени";
                // Обратная ошибка новичка: сел в фудо как в присед. Без этой фразы стойка
                // на 120° проходила как чистая, и учить было нечему.
                if (Math.Min(frontKneeDeg, backKneeDeg) < spec.FrontKneeMin) return "Выше";
                if (torsoLeanDeg > spec.MaxTorsoLeanDeg) return "Корпус прямо";
                return string.Empty;
            }

            if (length01 < spec.MinLength) return "Шире шаг";
            if (length01 > spec.MaxLength) return "Короче шаг";
            if (frontKneeDeg > spec.FrontKneeMax) return "Согни переднее колено";
            if (frontKneeDeg < spec.FrontKneeMin) return "Не проседай";
            if (backKneeDeg < spec.BackKneeMin) return "Выпрями заднюю";
            if (torsoLeanDeg > spec.MaxTorsoLeanDeg) return "Корпус прямо";
            return string.Empty;
        }

        private static StanceReading Unreadable(float visibility) =>
            new StanceReading(false, StanceKind.None, string.Empty, 0f, false,
                float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, visibility);

        private static float Distance(PoseLandmark a, PoseLandmark b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
