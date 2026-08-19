using System;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Builds synthetic side-on karate frames whose stance measures come out EXACTLY as
    /// requested, so stance, punch, kick and step scoring can be exercised without a camera.
    /// Shared by every level-1 test; configure the public fields and call <see cref="Build"/>.
    ///
    /// The figure is solved, not sketched: both ankles sit on the floor at the requested
    /// separation, and the pelvis is placed where the two knee angles put it (circle
    /// intersection of the hip-to-ankle distances that the law of cosines gives for those
    /// angles), then each knee is placed on the circle intersection of its ankle and the
    /// pelvis, taking the forward solution — knees bend forward. Both hips therefore share
    /// one pelvis point and both shoulders one point, as a profile view really looks, and
    /// <see cref="StanceReader"/> reads back the requested length, angles and lean to within
    /// float noise. Anatomically impossible combinations (a stance longer than the legs can
    /// reach) degrade gracefully: the pelvis drops onto the ankle line and the angles come
    /// out approximate — every preset and every documented fault case stays exact.
    ///
    /// Conventions match MediaPipe: normalized coordinates, Y grows DOWN. Offsets named
    /// "Forward" run along <see cref="ForwardSign"/>, offsets named "Up" run against Y.
    /// Distances suffixed "Shanks" are in shank lengths, the same unit the reader normalizes
    /// by, so changing <see cref="Shank"/> alone rescales the whole figure without moving a
    /// single measured value.
    ///
    /// <code>
    /// var f = StanceTestFrames.Zenkutsu();      // эталон: всё в середине окон spec
    /// f.LeadWristForwardShanks = 1.5f;          // ведущая рука выброшена вперёд
    /// analyzer.ProcessFrame(f.Build(timestamp: 0.5));
    /// </code>
    /// </summary>
    public sealed class StanceTestFrames
    {
        private const double Deg2Rad = Math.PI / 180.0;

        // Стопа: пятка позади лодыжки, носок впереди — именно этот сдвиг читается
        // как направление «вперёд», поэтому он завязан на ForwardSign.
        private const float HeelBehindShanks = 0.15f;
        private const float ToeAheadShanks = 0.35f;
        private const float FootDropShanks = 0.10f;

        /// <summary>Shank (knee-to-ankle) length in normalized units — the figure scale.</summary>
        public float Shank = 0.2f;

        /// <summary>Thigh length in shanks.</summary>
        public float ThighShanks = 1.25f;

        /// <summary>Hip-to-shoulder length in shanks.</summary>
        public float TorsoShanks = 1.6f;

        /// <summary>Floor line: Y of both ankles.</summary>
        public float GroundY = 0.9f;

        /// <summary>X midpoint between the ankles.</summary>
        public float CenterX = 0.5f;

        /// <summary>Shift of the WHOLE figure — the reading must not depend on it.</summary>
        public float OffsetX;

        /// <summary>Vertical shift of the whole figure (a hop, for the ghost step).</summary>
        public float OffsetY;

        /// <summary>Ankle separation in shanks.</summary>
        public float Length01 = 2.4f;

        public float FrontKneeDeg = 115f;
        public float BackKneeDeg = 170f;

        /// <summary>Torso tilt off vertical, degrees; leans toward <see cref="ForwardSign"/>.</summary>
        public float TorsoLeanDeg;

        /// <summary>+1 = the fighter faces +X, -1 = faces -X.</summary>
        public float ForwardSign = 1f;

        /// <summary>Which leg is the front one — flip it (with the sign) for a mirrored stance.</summary>
        public bool FrontIsLeft = true;

        /// <summary>Nose above the shoulders, in shanks (vary it to fake head bob).</summary>
        public float NoseAboveShouldersShanks = 0.7f;

        /// <summary>Nose ahead of the shoulders, in shanks.</summary>
        public float NoseForwardShanks = 0.15f;

        /// <summary>Lead (front-leg side) arm, offsets from that shoulder, in shanks.</summary>
        public float LeadElbowForwardShanks = 0.25f;
        public float LeadElbowUpShanks = -0.70f;
        public float LeadWristForwardShanks = 0.80f;
        public float LeadWristUpShanks = -0.50f;

        /// <summary>Rear arm, offsets from that shoulder, in shanks (kamae by default).</summary>
        public float RearElbowForwardShanks = 0.10f;
        public float RearElbowUpShanks = -0.75f;
        public float RearWristForwardShanks = 0.35f;
        public float RearWristUpShanks = -0.45f;

        /// <summary>Visibility written to every landmark.</summary>
        public float Visibility = 1f;

        /// <summary>Extra cap on heels and toes only — hide the feet without hiding the body.</summary>
        public float FootVisibility = 1f;

        /// <summary>
        /// A textbook stance: length and both knee angles at the middle of the
        /// <see cref="StanceSpec"/> windows, torso upright. Derived from the spec, so a
        /// re-calibrated threshold moves the reference frame with it.
        /// Mirrored = the fighter turned around: the other leg leads and forward flips.
        /// </summary>
        public static StanceTestFrames Reference(StanceKind kind, bool mirrored = false)
        {
            StanceSpec spec = StanceSpec.For(kind);
            return new StanceTestFrames
            {
                Length01 = 0.5f * (spec.MinLength + spec.MaxLength),
                FrontKneeDeg = 0.5f * (spec.FrontKneeMin + spec.FrontKneeMax),
                BackKneeDeg = 0.5f * (spec.BackKneeMin + spec.BackKneeMax),
                TorsoLeanDeg = 0f,
                ForwardSign = mirrored ? -1f : 1f,
                FrontIsLeft = !mirrored,
            };
        }

        /// <summary>Reference zenkutsu dachi (2.4 shanks, front knee 115, back knee 170).</summary>
        public static StanceTestFrames Zenkutsu(bool mirrored = false) =>
            Reference(StanceKind.Zenkutsu, mirrored);

        /// <summary>Reference fudo dachi (1.3 shanks, both knees 160).</summary>
        public static StanceTestFrames Fudo(bool mirrored = false) =>
            Reference(StanceKind.Fudo, mirrored);

        public PoseFrame Build(double timestamp = 0)
        {
            var lm = new PoseLandmark[PoseFrame.LandmarkCount];
            for (int i = 0; i < lm.Length; i++)
                lm[i] = new PoseLandmark(0f, 0f, 0f, Visibility);

            float fwd = ForwardSign >= 0f ? 1f : -1f;
            float shank = Shank;
            float thigh = ThighShanks * shank;
            float half = 0.5f * Length01 * shank;
            float ankleY = GroundY;
            float frontAnkleX = CenterX + fwd * half;
            float backAnkleX = CenterX - fwd * half;

            // Таз — на пересечении окружностей: расстояние таз-лодыжка однозначно задано
            // углом колена (теорема косинусов), поэтому оба угла выходят точными.
            float frontSpan = LimbSpan(shank, thigh, FrontKneeDeg);
            float backSpan = LimbSpan(shank, thigh, BackKneeDeg);
            Intersect(frontAnkleX, ankleY, frontSpan, backAnkleX, ankleY, backSpan,
                out float px1, out float py1, out float px2, out float py2);
            bool upperIsFirst = py1 <= py2;
            float pelvisX = upperIsFirst ? px1 : px2;
            float pelvisY = upperIsFirst ? py1 : py2;

            PutLeg(lm, FrontIsLeft, frontAnkleX, ankleY, pelvisX, pelvisY, shank, thigh, fwd);
            PutLeg(lm, !FrontIsLeft, backAnkleX, ankleY, pelvisX, pelvisY, shank, thigh, fwd);

            double lean = TorsoLeanDeg * Deg2Rad;
            float torso = TorsoShanks * shank;
            float shoulderX = pelvisX + fwd * torso * (float)Math.Sin(lean);
            float shoulderY = pelvisY - torso * (float)Math.Cos(lean);
            Put(lm, PoseLandmarkType.LeftShoulder, shoulderX, shoulderY, Visibility);
            Put(lm, PoseLandmarkType.RightShoulder, shoulderX, shoulderY, Visibility);
            Put(lm, PoseLandmarkType.Nose,
                shoulderX + fwd * NoseForwardShanks * shank,
                shoulderY - NoseAboveShouldersShanks * shank, Visibility);

            PutArm(lm, FrontIsLeft, shoulderX, shoulderY, shank, fwd,
                LeadElbowForwardShanks, LeadElbowUpShanks, LeadWristForwardShanks, LeadWristUpShanks);
            PutArm(lm, !FrontIsLeft, shoulderX, shoulderY, shank, fwd,
                RearElbowForwardShanks, RearElbowUpShanks, RearWristForwardShanks, RearWristUpShanks);

            return new PoseFrame(lm, timestamp);
        }

        private void Put(PoseLandmark[] lm, PoseLandmarkType type, float x, float y, float vis) =>
            lm[(int)type] = new PoseLandmark(x + OffsetX, y + OffsetY, 0f, vis);

        private void PutLeg(PoseLandmark[] lm, bool left,
            float ankleX, float ankleY, float pelvisX, float pelvisY, float shank, float thigh, float fwd)
        {
            Intersect(ankleX, ankleY, shank, pelvisX, pelvisY, thigh,
                out float kx1, out float ky1, out float kx2, out float ky2);
            bool firstIsForward = kx1 * fwd >= kx2 * fwd;      // колено гнётся вперёд
            float kneeX = firstIsForward ? kx1 : kx2;
            float kneeY = firstIsForward ? ky1 : ky2;

            float footVis = Math.Min(Visibility, FootVisibility);
            Put(lm, left ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle, ankleX, ankleY, Visibility);
            Put(lm, left ? PoseLandmarkType.LeftKnee : PoseLandmarkType.RightKnee, kneeX, kneeY, Visibility);
            Put(lm, left ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip, pelvisX, pelvisY, Visibility);
            Put(lm, left ? PoseLandmarkType.LeftHeel : PoseLandmarkType.RightHeel,
                ankleX - fwd * HeelBehindShanks * shank, ankleY + FootDropShanks * shank, footVis);
            Put(lm, left ? PoseLandmarkType.LeftFootIndex : PoseLandmarkType.RightFootIndex,
                ankleX + fwd * ToeAheadShanks * shank, ankleY + FootDropShanks * shank, footVis);
        }

        private void PutArm(PoseLandmark[] lm, bool left,
            float shoulderX, float shoulderY, float shank, float fwd,
            float elbowForward, float elbowUp, float wristForward, float wristUp)
        {
            Put(lm, left ? PoseLandmarkType.LeftElbow : PoseLandmarkType.RightElbow,
                shoulderX + fwd * elbowForward * shank, shoulderY - elbowUp * shank, Visibility);
            Put(lm, left ? PoseLandmarkType.LeftWrist : PoseLandmarkType.RightWrist,
                shoulderX + fwd * wristForward * shank, shoulderY - wristUp * shank, Visibility);
        }

        /// <summary>Hip-to-ankle distance of a leg bent to <paramref name="kneeDeg"/>.</summary>
        private static float LimbSpan(float shank, float thigh, float kneeDeg) =>
            (float)Math.Sqrt(shank * shank + thigh * thigh
                - 2.0 * shank * thigh * Math.Cos(kneeDeg * Deg2Rad));

        /// <summary>
        /// Both points at distance r1 from p1 and r2 from p2. Non-intersecting circles
        /// collapse to the single point on the line of centres, so a physically impossible
        /// request still yields a frame instead of a NaN.
        /// </summary>
        private static void Intersect(float x1, float y1, float r1, float x2, float y2, float r2,
            out float ax, out float ay, out float bx, out float by)
        {
            double dx = x2 - x1, dy = y2 - y1;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 1e-9)
            {
                ax = bx = x1;
                ay = by = y1;
                return;
            }

            double a = (d * d + (double)r1 * r1 - (double)r2 * r2) / (2.0 * d);
            double h = Math.Sqrt(Math.Max(0.0, (double)r1 * r1 - a * a));
            double mx = x1 + a * dx / d, my = y1 + a * dy / d;
            double nx = -dy / d * h, ny = dx / d * h;

            ax = (float)(mx + nx);
            ay = (float)(my + ny);
            bx = (float)(mx - nx);
            by = (float)(my - ny);
        }
    }
}
