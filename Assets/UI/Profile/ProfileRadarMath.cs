using UnityEngine;

namespace Mikey.UI.Profile
{
    /// <summary>
    /// Pure five-axis pentagon geometry behind <see cref="ProfileRadarChart"/>, kept
    /// free of UI Toolkit so it's directly unit-testable (mirrors
    /// <c>Mikey.UI.Map.MapPanZoomMath</c>'s pure-math-class pattern). Axes are laid
    /// out clockwise starting straight up, matching the reference Profile hub's
    /// Strength (top) / Speed / Form / Stamina / Control ordering. Screen space has
    /// Y increasing downward, so a plain cos/sin direction at -90 degrees already
    /// points "up" with no extra axis flip needed.
    /// </summary>
    public static class ProfileRadarMath
    {
        public const int AxisCount = 5;

        public static readonly string[] AxisLabels = { "Strength", "Speed", "Form", "Stamina", "Control" };

        private const float StartAngleDegrees = -90f;
        private const float StepDegrees = 360f / AxisCount;

        /// <summary>Unit direction of axis <paramref name="index"/> from center.</summary>
        public static Vector2 AxisDirection(int index)
        {
            float radians = (StartAngleDegrees + index * StepDegrees) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        /// <summary>Position of one data vertex; <paramref name="value"/> is clamped to [0, maxValue].</summary>
        public static Vector2 VertexPosition(int index, float value, float maxValue, float radius, Vector2 center)
        {
            float fraction = maxValue <= 0f ? 0f : Mathf.Clamp01(value / maxValue);
            return center + AxisDirection(index) * (radius * fraction);
        }

        /// <summary>The five data-polygon vertices for <paramref name="values"/> (must have length <see cref="AxisCount"/>).</summary>
        public static Vector2[] Polygon(float[] values, float maxValue, float radius, Vector2 center)
        {
            var points = new Vector2[AxisCount];
            for (int i = 0; i < AxisCount; i++)
                points[i] = VertexPosition(i, values[i], maxValue, radius, center);
            return points;
        }

        /// <summary>One nested guide-grid pentagon at <paramref name="fraction"/> of the full radius (e.g. 0.25/0.5/0.75/1).</summary>
        public static Vector2[] GridRing(float fraction, float radius, Vector2 center)
        {
            var points = new Vector2[AxisCount];
            for (int i = 0; i < AxisCount; i++)
                points[i] = center + AxisDirection(i) * (radius * fraction);
            return points;
        }

        /// <summary>Anchor point for axis <paramref name="index"/>'s label, offset beyond the outer ring by <paramref name="outset"/>.</summary>
        public static Vector2 LabelAnchor(int index, float radius, Vector2 center, float outset) =>
            center + AxisDirection(index) * (radius + outset);
    }
}
