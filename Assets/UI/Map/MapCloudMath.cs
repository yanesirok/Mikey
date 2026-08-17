namespace Mikey.UI.Map
{
    /// <summary>
    /// Pure math behind the cloud transition's motion — kept free of
    /// UnityEngine/UI Toolkit so it can be exercised directly in EditMode
    /// tests (mirrors <see cref="MapPanZoomMath"/>). Smooth ease-in-out
    /// (decelerating in, accelerating out, no overshoot/bounce/elastic) per
    /// the desired "sumi-e mist" motion feel.
    ///
    /// The transition's dominant motion is each cloud EXPANDING from its own
    /// resting rectangle while one of its edges/corners (see
    /// <see cref="CloudExpansionAnchor"/>) stays fixed — never a slide
    /// toward the viewport center. <see cref="ComputeClosedLayout"/> derives
    /// the fully-closed rectangle for one cloud from its rest layout; the
    /// animation itself is then a plain eased <see cref="Lerp"/> between
    /// that rest layout and the computed closed layout (or, in reveal, from
    /// closed back to the DESTINATION screen's rest layout).
    /// </summary>
    public static class MapCloudMath
    {
        /// <summary>
        /// Symmetric ease-in-out cubic: slow start, fast middle, slow finish.
        /// <paramref name="t"/> is clamped to [0, 1]; EaseInOutCubic(0) == 0,
        /// EaseInOutCubic(1) == 1, EaseInOutCubic(0.5) == 0.5, monotonically
        /// increasing, never overshoots outside [0, 1].
        /// </summary>
        public static float EaseInOutCubic(float t)
        {
            float clamped = Clamp01(IsFinite(t) ? t : 0f);
            return clamped < 0.5f
                ? 4f * clamped * clamped * clamped
                : 1f - Pow3(-2f * clamped + 2f) * 0.5f;
        }

        /// <summary>
        /// The local progress of one (optionally staggered) cloud within an
        /// overall phase: 0 before its own start delay has elapsed, 1 once
        /// its own duration has fully elapsed, linear in between.
        /// </summary>
        public static float LocalProgress(float elapsedSeconds, float startDelaySeconds, float cloudDurationSeconds)
        {
            if (!IsFinite(elapsedSeconds) || !IsFinite(startDelaySeconds) || !IsPositiveFinite(cloudDurationSeconds))
                return 0f;

            float local = (elapsedSeconds - startDelaySeconds) / cloudDurationSeconds;
            return Clamp01(local);
        }

        /// <summary>
        /// Derives the fully-closed (progress == 1) rectangle for one cloud
        /// from its own rest layout: its size grows by
        /// <paramref name="expansionFactor"/> while the edge/corner named by
        /// <paramref name="anchor"/> stays exactly where it was at rest.
        /// Rotation is copied unchanged (never animated); opacity is set to
        /// <paramref name="closedOpacity"/> (full cover always reads as
        /// fully opaque, regardless of the cloud's own rest opacity).
        /// </summary>
        public static CloudLayout ComputeClosedLayout(CloudLayout rest, CloudExpansionAnchor anchor, float expansionFactor, float closedOpacity)
        {
            float factor = IsPositiveFinite(expansionFactor) ? expansionFactor : 1f;
            float closedWidth = rest.NormalizedWidth * factor;

            switch (anchor)
            {
                case CloudExpansionAnchor.LeftEdge:
                    // Left edge fixed; grows rightward. Y/height untouched.
                    return new CloudLayout(rest.NormalizedX, rest.NormalizedY, closedWidth, rest.NormalizedHeight, rest.RotationDegrees, closedOpacity);

                case CloudExpansionAnchor.RightEdge:
                {
                    // Right edge fixed; grows leftward. Y/height untouched.
                    float rightEdge = rest.NormalizedX + rest.NormalizedWidth;
                    float closedX = rightEdge - closedWidth;
                    return new CloudLayout(closedX, rest.NormalizedY, closedWidth, rest.NormalizedHeight, rest.RotationDegrees, closedOpacity);
                }

                case CloudExpansionAnchor.BottomRightCorner:
                default:
                {
                    // Bottom-right corner fixed; grows diagonally toward the
                    // upper-left. Both dimensions scale together.
                    float closedHeight = rest.NormalizedHeight * factor;
                    float rightEdge = rest.NormalizedX + rest.NormalizedWidth;
                    float bottomEdge = rest.NormalizedY + rest.NormalizedHeight;
                    float closedX = rightEdge - closedWidth;
                    float closedY = bottomEdge - closedHeight;
                    return new CloudLayout(closedX, closedY, closedWidth, closedHeight, rest.RotationDegrees, closedOpacity);
                }
            }
        }

        /// <summary>Eases <paramref name="t"/> and linearly interpolates every field (including opacity) of a <see cref="CloudLayout"/> between two endpoints.</summary>
        public static CloudLayout Lerp(CloudLayout from, CloudLayout to, float t)
        {
            float eased = EaseInOutCubic(t);
            return new CloudLayout(
                LerpFloat(from.NormalizedX, to.NormalizedX, eased),
                LerpFloat(from.NormalizedY, to.NormalizedY, eased),
                LerpFloat(from.NormalizedWidth, to.NormalizedWidth, eased),
                LerpFloat(from.NormalizedHeight, to.NormalizedHeight, eased),
                LerpFloat(from.RotationDegrees, to.RotationDegrees, eased),
                LerpFloat(from.Opacity, to.Opacity, eased));
        }

        private static float LerpFloat(float a, float b, float t) => a + (b - a) * t;

        private static float Pow3(float v) => v * v * v;

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static bool IsPositiveFinite(float v) => IsFinite(v) && v > 0f;
    }
}
