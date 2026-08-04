using System;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Builds synthetic <see cref="PoseFrame"/>s with a known elbow angle and hip
    /// deviation so scoring can be exercised deterministically, without a camera.
    ///
    /// Layout is a side-on push-up (left side scored). The shoulder is the shared anchor
    /// of both the arm chain and the body line:
    /// <list type="bullet">
    ///   <item>Arm: elbow sits below the shoulder; the wrist is placed so the interior
    ///   elbow angle equals the requested value.</item>
    ///   <item>Body: shoulder→ankle is a flat horizontal line; the hip is offset
    ///   vertically by <c>hipOffset</c> (positive = sag, negative = pike).</item>
    /// </list>
    /// </summary>
    internal static class PoseTestFrames
    {
        private const double Deg2Rad = Math.PI / 180.0;

        public static PoseFrame Build(float elbowAngleDeg, float hipOffset = 0f, float visibility = 1f, double timestamp = 0)
        {
            var lm = new PoseLandmark[PoseFrame.LandmarkCount];
            for (int i = 0; i < lm.Length; i++)
                lm[i] = new PoseLandmark(0f, 0f, 0f, visibility);

            // Shared shoulder anchor.
            float sx = 0.3f, sy = 0.5f;

            // Arm: elbow 0.2 below the shoulder; wrist placed to realize the target angle.
            float ex = sx, ey = sy + 0.2f;
            double wd = (-90.0 + elbowAngleDeg) * Deg2Rad;
            float wx = ex + 0.2f * (float)Math.Cos(wd);
            float wy = ey + 0.2f * (float)Math.Sin(wd);

            // Body: flat shoulder→ankle line at y = sy; hip offset vertically.
            float ax = 0.8f, ay = sy;
            float hx = 0.55f, hy = sy + hipOffset;

            Set(lm, PoseLandmarkType.LeftShoulder, sx, sy, visibility);
            Set(lm, PoseLandmarkType.LeftElbow, ex, ey, visibility);
            Set(lm, PoseLandmarkType.LeftWrist, wx, wy, visibility);
            Set(lm, PoseLandmarkType.LeftHip, hx, hy, visibility);
            Set(lm, PoseLandmarkType.LeftAnkle, ax, ay, visibility);

            // Mirror onto the right side with equal visibility so the evaluator's
            // side-selection ties to the left (leftVis >= rightVis).
            Set(lm, PoseLandmarkType.RightShoulder, sx, sy, visibility);
            Set(lm, PoseLandmarkType.RightElbow, ex, ey, visibility);
            Set(lm, PoseLandmarkType.RightWrist, wx, wy, visibility);
            Set(lm, PoseLandmarkType.RightHip, hx, hy, visibility);
            Set(lm, PoseLandmarkType.RightAnkle, ax, ay, visibility);

            return new PoseFrame(lm, timestamp);
        }

        /// <summary>
        /// Upright (standing) figure: vertical shoulder→hip→ankle line with a
        /// controllable elbow angle, so arm swing while standing/walking can be
        /// simulated. Proves an upright body is never "in push-up position".
        /// </summary>
        public static PoseFrame BuildStanding(float elbowAngleDeg, float visibility = 1f, double timestamp = 0)
        {
            var lm = new PoseLandmark[PoseFrame.LandmarkCount];
            for (int i = 0; i < lm.Length; i++)
                lm[i] = new PoseLandmark(0f, 0f, 0f, visibility);

            // Vertical body line: shoulder on top, hip below, ankle at the bottom.
            float sx = 0.5f, sy = 0.2f;
            float hx = 0.5f, hy = 0.55f;
            float ax = 0.5f, ay = 0.9f;

            // Arm: elbow directly below the shoulder (the wrist formula assumes it); wrist realizes the target elbow angle.
            float ex = sx, ey = sy + 0.15f;
            double wd = (-90.0 + elbowAngleDeg) * Deg2Rad;
            float wx = ex + 0.2f * (float)Math.Cos(wd);
            float wy = ey + 0.2f * (float)Math.Sin(wd);

            Set(lm, PoseLandmarkType.LeftShoulder, sx, sy, visibility);
            Set(lm, PoseLandmarkType.LeftElbow, ex, ey, visibility);
            Set(lm, PoseLandmarkType.LeftWrist, wx, wy, visibility);
            Set(lm, PoseLandmarkType.LeftHip, hx, hy, visibility);
            Set(lm, PoseLandmarkType.LeftAnkle, ax, ay, visibility);
            Set(lm, PoseLandmarkType.RightShoulder, sx, sy, visibility);
            Set(lm, PoseLandmarkType.RightElbow, ex, ey, visibility);
            Set(lm, PoseLandmarkType.RightWrist, wx, wy, visibility);
            Set(lm, PoseLandmarkType.RightHip, hx, hy, visibility);
            Set(lm, PoseLandmarkType.RightAnkle, ax, ay, visibility);

            return new PoseFrame(lm, timestamp);
        }

        private static void Set(PoseLandmark[] lm, PoseLandmarkType type, float x, float y, float vis)
        {
            lm[(int)type] = new PoseLandmark(x, y, 0f, vis);
        }
    }
}
