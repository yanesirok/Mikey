using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Pure geometric helpers for pose scoring. Engine-free (System.Math only) so the
    /// results are deterministic and unit-testable without a live scene.
    /// </summary>
    public static class PoseMath
    {
        /// <summary>
        /// Interior angle at vertex <paramref name="b"/> formed by the segments
        /// b→a and b→c, in degrees over [0,180]. Used for joint angles such as the
        /// elbow (shoulder–elbow–wrist) and the body line (shoulder–hip–ankle).
        /// Returns 180 (treated as "straight") if either segment is degenerate.
        /// </summary>
        public static float AngleDeg(PoseLandmark a, PoseLandmark b, PoseLandmark c)
        {
            double abx = a.X - b.X;
            double aby = a.Y - b.Y;
            double cbx = c.X - b.X;
            double cby = c.Y - b.Y;

            double abLen = Math.Sqrt(abx * abx + aby * aby);
            double cbLen = Math.Sqrt(cbx * cbx + cby * cby);
            if (abLen < 1e-9 || cbLen < 1e-9)
                return 180f;

            double cos = (abx * cbx + aby * cby) / (abLen * cbLen);
            if (cos > 1.0) cos = 1.0;
            else if (cos < -1.0) cos = -1.0;

            return (float)(Math.Acos(cos) * 180.0 / Math.PI);
        }

        /// <summary>
        /// Interior angle at vertex <paramref name="b"/> using full 3D coordinates
        /// (x, y, z), in degrees over [0,180]. Because an angle between three points is
        /// the same in any coordinate frame, this is invariant to camera placement
        /// (front vs side) — unlike the 2D projection, which foreshortens off-axis.
        /// Returns 180 if either segment is degenerate.
        /// </summary>
        public static float AngleDeg3D(PoseLandmark a, PoseLandmark b, PoseLandmark c)
        {
            double abx = a.X - b.X, aby = a.Y - b.Y, abz = a.Z - b.Z;
            double cbx = c.X - b.X, cby = c.Y - b.Y, cbz = c.Z - b.Z;

            double abLen = Math.Sqrt(abx * abx + aby * aby + abz * abz);
            double cbLen = Math.Sqrt(cbx * cbx + cby * cby + cbz * cbz);
            if (abLen < 1e-9 || cbLen < 1e-9)
                return 180f;

            double cos = (abx * cbx + aby * cby + abz * cbz) / (abLen * cbLen);
            if (cos > 1.0) cos = 1.0;
            else if (cos < -1.0) cos = -1.0;

            return (float)(Math.Acos(cos) * 180.0 / Math.PI);
        }

        /// <summary>
        /// Signed vertical offset of the hip from the straight shoulder→ankle line,
        /// in normalized image units. Positive = hip sits lower on screen than the
        /// line (hips sagging toward the floor); negative = hip sits higher (piking).
        /// Distinguishes sag from pike, which the unsigned body angle alone cannot.
        /// Returns 0 for a vertical/degenerate torso where the measure is undefined.
        /// </summary>
        public static float HipVerticalOffset(PoseLandmark shoulder, PoseLandmark hip, PoseLandmark ankle)
        {
            double dx = ankle.X - shoulder.X;
            if (Math.Abs(dx) < 1e-6)
                return 0f;

            double t = (hip.X - shoulder.X) / dx;
            double lineY = shoulder.Y + (ankle.Y - shoulder.Y) * t;
            return (float)(hip.Y - lineY);
        }

        /// <summary>
        /// How far the hip sits above the knee, in units of that leg's shank length
        /// (image space, Y grows down): ≈1 standing, ≈0 with the thigh at parallel,
        /// negative once the hip drops below the knee. Uses only Y coordinates, so it
        /// is view-independent (no depth). Returns NaN for a degenerate shank.
        /// </summary>
        public static float HipDropMargin(PoseLandmark hip, PoseLandmark knee, PoseLandmark ankle)
        {
            float shank = Math.Abs(ankle.Y - knee.Y);
            return shank < 1e-4f ? float.NaN : (knee.Y - hip.Y) / shank;
        }
    }
}
