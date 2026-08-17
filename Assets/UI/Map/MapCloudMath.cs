namespace Mikey.UI.Map
{
    /// <summary>
    /// Pure math behind the cloud transition's motion — kept free of
    /// UnityEngine/UI Toolkit so it can be exercised directly in EditMode
    /// tests (mirrors <see cref="MapPanZoomMath"/>). Smooth ease-in-out
    /// (decelerating in, accelerating out, no overshoot/bounce/elastic) per
    /// the desired "sumi-e mist" motion feel, never a linear or bouncy curve.
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
        /// The local progress of one staggered cloud within an overall phase:
        /// 0 before its own start delay has elapsed, 1 once its own duration
        /// has fully elapsed, linear in between. Each cloud's motion starts
        /// at <paramref name="startDelaySeconds"/> into the phase and takes
        /// <paramref name="cloudDurationSeconds"/> to complete — the phase's
        /// own total duration only needs to be at least
        /// startDelaySeconds + cloudDurationSeconds for every cloud to finish.
        /// </summary>
        public static float LocalProgress(float elapsedSeconds, float startDelaySeconds, float cloudDurationSeconds)
        {
            if (!IsFinite(elapsedSeconds) || !IsFinite(startDelaySeconds) || !IsPositiveFinite(cloudDurationSeconds))
                return 0f;

            float local = (elapsedSeconds - startDelaySeconds) / cloudDurationSeconds;
            return Clamp01(local);
        }

        /// <summary>Eases <paramref name="t"/> and linearly interpolates every field of a <see cref="CloudLayout"/> between two presets.</summary>
        public static CloudLayout Lerp(CloudLayout from, CloudLayout to, float t)
        {
            float eased = EaseInOutCubic(t);
            return new CloudLayout(
                LerpFloat(from.NormalizedX, to.NormalizedX, eased),
                LerpFloat(from.NormalizedY, to.NormalizedY, eased),
                LerpFloat(from.NormalizedWidth, to.NormalizedWidth, eased),
                LerpFloat(from.NormalizedHeight, to.NormalizedHeight, eased),
                LerpFloat(from.RotationDegrees, to.RotationDegrees, eased));
        }

        private static float LerpFloat(float a, float b, float t) => a + (b - a) * t;

        private static float Pow3(float v) => v * v * v;

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static bool IsPositiveFinite(float v) => IsFinite(v) && v > 0f;
    }
}
