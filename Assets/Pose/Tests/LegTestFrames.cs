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
    }
}
