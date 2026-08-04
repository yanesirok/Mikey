using System;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Synthetic side-on lower-body frames for squat/wall-sit/kick scoring tests.
    /// Same idea as <see cref="PoseTestFrames"/> (which stays push-up-specific):
    /// place landmarks so a requested joint angle or ankle height comes out exactly.
    /// Both body sides get identical coordinates so side-selection ties resolve left.
    /// </summary>
    internal static class LegTestFrames
    {
        private const double Deg2Rad = Math.PI / 180.0;

        /// <summary>
        /// Standing/squatting figure: vertical shank (ankle→knee), thigh rotated to
        /// realize <paramref name="kneeAngleDeg"/>, torso tilted from vertical by
        /// <paramref name="torsoLeanDeg"/>.
        /// </summary>
        public static PoseFrame Squat(float kneeAngleDeg, float torsoLeanDeg = 0f, float visibility = 1f, double timestamp = 0)
        {
            var lm = Blank(visibility);

            float ax = 0.5f, ay = 0.9f;                     // ankle
            float kx = 0.5f, ky = 0.7f;                     // knee (shank vertical)
            double phi = (180.0 - kneeAngleDeg) * Deg2Rad;  // 0 = thigh straight up
            float hx = kx + 0.25f * (float)Math.Sin(phi);
            float hy = ky - 0.25f * (float)Math.Cos(phi);
            double lean = torsoLeanDeg * Deg2Rad;
            float sx = hx + 0.3f * (float)Math.Sin(lean);
            float sy = hy - 0.3f * (float)Math.Cos(lean);

            SetBoth(lm, PoseLandmarkType.LeftAnkle, PoseLandmarkType.RightAnkle, ax, ay, visibility);
            SetBoth(lm, PoseLandmarkType.LeftKnee, PoseLandmarkType.RightKnee, kx, ky, visibility);
            SetBoth(lm, PoseLandmarkType.LeftHip, PoseLandmarkType.RightHip, hx, hy, visibility);
            SetBoth(lm, PoseLandmarkType.LeftShoulder, PoseLandmarkType.RightShoulder, sx, sy, visibility);
            return new PoseFrame(lm, timestamp);
        }

        /// <summary>
        /// Wall-sit figure: vertical shank, thigh rotated to realize the knee angle,
        /// torso rotated about the hip to realize the hip angle (90/90 = ideal seat).
        /// </summary>
        public static PoseFrame WallSit(float kneeAngleDeg = 90f, float hipAngleDeg = 90f, float visibility = 1f, double timestamp = 0)
        {
            var lm = Blank(visibility);

            float ax = 0.6f, ay = 0.9f;
            float kx = 0.6f, ky = 0.7f;
            double phi = (180.0 - kneeAngleDeg) * Deg2Rad;
            float hx = kx - 0.25f * (float)Math.Sin(phi);   // бедро уходит назад (влево)
            float hy = ky - 0.25f * (float)Math.Cos(phi);

            // Торс: повернуть направление бедро→колено на hipAngle, чтобы интериорный
            // угол в бедре (плечо–бедро–колено) вышел ровно заданным.
            float thigh = (float)Math.Sqrt((kx - hx) * (kx - hx) + (ky - hy) * (ky - hy));
            float tx = (kx - hx) / thigh, ty = (ky - hy) / thigh;
            double a = hipAngleDeg * Deg2Rad;
            float ux = tx * (float)Math.Cos(a) + ty * (float)Math.Sin(a);
            float uy = -tx * (float)Math.Sin(a) + ty * (float)Math.Cos(a);
            float sx = hx + 0.3f * ux, sy = hy + 0.3f * uy;

            SetBoth(lm, PoseLandmarkType.LeftAnkle, PoseLandmarkType.RightAnkle, ax, ay, visibility);
            SetBoth(lm, PoseLandmarkType.LeftKnee, PoseLandmarkType.RightKnee, kx, ky, visibility);
            SetBoth(lm, PoseLandmarkType.LeftHip, PoseLandmarkType.RightHip, hx, hy, visibility);
            SetBoth(lm, PoseLandmarkType.LeftShoulder, PoseLandmarkType.RightShoulder, sx, sy, visibility);
            return new PoseFrame(lm, timestamp);
        }

        internal static PoseLandmark[] Blank(float visibility)
        {
            var lm = new PoseLandmark[PoseFrame.LandmarkCount];
            for (int i = 0; i < lm.Length; i++)
                lm[i] = new PoseLandmark(0f, 0f, 0f, visibility);
            return lm;
        }

        internal static void SetBoth(PoseLandmark[] lm, PoseLandmarkType left, PoseLandmarkType right, float x, float y, float vis)
        {
            lm[(int)left] = new PoseLandmark(x, y, 0f, vis);
            lm[(int)right] = new PoseLandmark(x, y, 0f, vis);
        }

        /// <summary>
        /// Side-on kicker. Support (right) leg fixed: ankle (0.6, 0.9), knee (0.6, 0.7),
        /// hip (0.6, 0.5), shoulder (0.6, 0.2). Kicking (left) leg: chambered — knee raised,
        /// shin hanging (bent ≈ 108°); otherwise ankle at <paramref name="kickAnkleY"/>
        /// with a straight leg (knee on the hip→ankle midpoint).
        /// Zones with these anchors: gedan 0.65, chudan 0.35, jodan 0.18, floor 0.9.
        /// </summary>
        public static PoseFrame Kick(float kickAnkleY, bool chambered = false, float visibility = 1f, double timestamp = 0)
        {
            var lm = Blank(visibility);

            void Set(PoseLandmarkType t, float x, float y) => lm[(int)t] = new PoseLandmark(x, y, 0f, visibility);

            Set(PoseLandmarkType.RightAnkle, 0.6f, 0.9f);
            Set(PoseLandmarkType.RightKnee, 0.6f, 0.7f);
            Set(PoseLandmarkType.RightHip, 0.6f, 0.5f);
            Set(PoseLandmarkType.RightShoulder, 0.6f, 0.2f);
            Set(PoseLandmarkType.LeftHip, 0.6f, 0.5f);
            Set(PoseLandmarkType.LeftShoulder, 0.6f, 0.2f);

            if (chambered)
            {
                Set(PoseLandmarkType.LeftKnee, 0.45f, 0.55f);
                Set(PoseLandmarkType.LeftAnkle, 0.45f, 0.75f);
            }
            else
            {
                Set(PoseLandmarkType.LeftAnkle, 0.3f, kickAnkleY);
                Set(PoseLandmarkType.LeftKnee, (0.6f + 0.3f) / 2f, (0.5f + kickAnkleY) / 2f);
            }

            return new PoseFrame(lm, timestamp);
        }
    }
}
