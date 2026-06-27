using System;
using UnityEngine;

namespace Mikey.UI.SafeArea
{
    /// <summary>
    /// Result of a safe-area inset calculation, in UI Toolkit panel points.
    /// <see cref="Valid"/> is false when the inputs were degenerate and no
    /// padding should be applied.
    /// </summary>
    public readonly struct Insets
    {
        public readonly float Left;
        public readonly float Top;
        public readonly float Right;
        public readonly float Bottom;
        public readonly bool Valid;

        public Insets(float left, float top, float right, float bottom, bool valid)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
            Valid = valid;
        }

        public static readonly Insets Invalid = new Insets(0f, 0f, 0f, 0f, false);
    }

    /// <summary>
    /// Pure, panel-free safe-area inset math. Kept separate from
    /// <see cref="SafeAreaController"/> so it can be unit-tested in EditMode
    /// without a live UI Toolkit panel.
    /// </summary>
    public static class SafeAreaInsets
    {
        /// <summary>
        /// Converts a screen-space <paramref name="safeArea"/> (pixels, bottom-left
        /// origin — i.e. <see cref="Screen.safeArea"/>) into UI Toolkit panel-point
        /// padding (top-left origin).
        ///
        /// <paramref name="screenToPanel"/> must map a TOP-LEFT-origin screen-pixel
        /// point to a panel point (at runtime: RuntimePanelUtils.ScreenToPanel, which
        /// already applies PanelSettings scaling). The Y flip out of Unity's
        /// bottom-left screen space is performed here before conversion, so callers
        /// must not pre-flip. Returns <see cref="Insets.Invalid"/> when any extent is
        /// non-positive. Insets are clamped to >= 0.
        /// </summary>
        public static Insets Compute(Rect safeArea, Vector2 screenSize, Vector2 panelSize,
            Func<Vector2, Vector2> screenToPanel)
        {
            if (screenToPanel == null)
                return Insets.Invalid;
            if (screenSize.x <= 0f || screenSize.y <= 0f)
                return Insets.Invalid;
            if (panelSize.x <= 0f || panelSize.y <= 0f)
                return Insets.Invalid;
            if (safeArea.width <= 0f || safeArea.height <= 0f)
                return Insets.Invalid;

            float screenHeight = screenSize.y;

            // Safe-area corners expressed in TOP-LEFT-origin screen pixels.
            Vector2 topLeftScreen = new Vector2(
                safeArea.xMin,
                screenHeight - (safeArea.yMin + safeArea.height));
            Vector2 bottomRightScreen = new Vector2(
                safeArea.xMin + safeArea.width,
                screenHeight - safeArea.yMin);

            Vector2 topLeft = screenToPanel(topLeftScreen);
            Vector2 bottomRight = screenToPanel(bottomRightScreen);

            // RuntimePanelUtils.ScreenToPanel can return non-finite values (inf/NaN)
            // when the panel transform is not yet valid; reject before they reach styles.
            if (!IsFinite(topLeft.x) || !IsFinite(topLeft.y)
                || !IsFinite(bottomRight.x) || !IsFinite(bottomRight.y))
                return Insets.Invalid;

            float left = Mathf.Max(0f, topLeft.x);
            float top = Mathf.Max(0f, topLeft.y);
            float right = Mathf.Max(0f, panelSize.x - bottomRight.x);
            float bottom = Mathf.Max(0f, panelSize.y - bottomRight.y);

            // Mathf.Max does not reject inf/NaN, so re-check the computed insets.
            if (!IsFinite(left) || !IsFinite(top) || !IsFinite(right) || !IsFinite(bottom))
                return Insets.Invalid;

            return new Insets(left, top, right, bottom, true);
        }

        /// <summary>
        /// Compatibility-safe finiteness test (true only for real, non-infinite,
        /// non-NaN values). Avoids depending on <c>float.IsFinite</c>, which is
        /// unavailable on older runtimes.
        /// </summary>
        private static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }
    }
}
