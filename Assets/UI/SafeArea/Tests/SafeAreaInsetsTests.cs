using System;
using NUnit.Framework;
using UnityEngine;

namespace Mikey.UI.SafeArea.Tests
{
    /// <summary>
    /// Pure-math tests for <see cref="SafeAreaInsets.Compute"/>. The screen-to-panel
    /// conversion is injected as a fake so no live UI Toolkit panel is required.
    /// </summary>
    public class SafeAreaInsetsTests
    {
        private static readonly Func<Vector2, Vector2> Identity = p => p;
        private const float Tol = 0.001f;

        [Test]
        public void NoInset_FullScreenSafeArea_ReturnsZero()
        {
            var screen = new Vector2(1920f, 1080f);
            var insets = SafeAreaInsets.Compute(new Rect(0f, 0f, 1920f, 1080f), screen, screen, Identity);

            Assert.IsTrue(insets.Valid);
            Assert.AreEqual(0f, insets.Left, Tol);
            Assert.AreEqual(0f, insets.Top, Tol);
            Assert.AreEqual(0f, insets.Right, Tol);
            Assert.AreEqual(0f, insets.Bottom, Tol);
        }

        [Test]
        public void LeftInset_ShiftedSafeAreaOrigin_ReturnsLeftPadding()
        {
            var screen = new Vector2(1920f, 1080f);
            var insets = SafeAreaInsets.Compute(new Rect(100f, 0f, 1820f, 1080f), screen, screen, Identity);

            Assert.IsTrue(insets.Valid);
            Assert.AreEqual(100f, insets.Left, Tol);
            Assert.AreEqual(0f, insets.Right, Tol);
            Assert.AreEqual(0f, insets.Top, Tol);
            Assert.AreEqual(0f, insets.Bottom, Tol);
        }

        [Test]
        public void RightInset_ReducedWidth_ReturnsRightPadding()
        {
            var screen = new Vector2(1920f, 1080f);
            var insets = SafeAreaInsets.Compute(new Rect(0f, 0f, 1820f, 1080f), screen, screen, Identity);

            Assert.AreEqual(0f, insets.Left, Tol);
            Assert.AreEqual(100f, insets.Right, Tol);
        }

        [Test]
        public void TopInset_ReducedHeightFromTop_ReturnsTopPadding()
        {
            // Bottom-left origin: a top cutout keeps yMin at 0 and reduces height.
            var screen = new Vector2(1920f, 1080f);
            var insets = SafeAreaInsets.Compute(new Rect(0f, 0f, 1920f, 1000f), screen, screen, Identity);

            Assert.AreEqual(80f, insets.Top, Tol);
            Assert.AreEqual(0f, insets.Bottom, Tol);
        }

        [Test]
        public void BottomInset_RaisedOrigin_ReturnsBottomPadding()
        {
            var screen = new Vector2(1920f, 1080f);
            var insets = SafeAreaInsets.Compute(new Rect(0f, 80f, 1920f, 1000f), screen, screen, Identity);

            Assert.AreEqual(0f, insets.Top, Tol);
            Assert.AreEqual(80f, insets.Bottom, Tol);
        }

        [Test]
        public void ScaledPanel_HalfScale_HalvesInsets()
        {
            var screen = new Vector2(2400f, 1080f);
            var panel = new Vector2(1200f, 540f);
            Func<Vector2, Vector2> half = p => p * 0.5f;

            var insets = SafeAreaInsets.Compute(new Rect(100f, 0f, 2300f, 1080f), screen, panel, half);

            Assert.IsTrue(insets.Valid);
            Assert.AreEqual(50f, insets.Left, Tol);
            Assert.AreEqual(0f, insets.Right, Tol);
        }

        [Test]
        public void RepeatedComputation_IsStable_NoAccumulation()
        {
            var screen = new Vector2(1920f, 1080f);
            var rect = new Rect(100f, 40f, 1780f, 1000f);

            var a = SafeAreaInsets.Compute(rect, screen, screen, Identity);
            var b = SafeAreaInsets.Compute(rect, screen, screen, Identity);

            Assert.AreEqual(a.Left, b.Left, Tol);
            Assert.AreEqual(a.Top, b.Top, Tol);
            Assert.AreEqual(a.Right, b.Right, Tol);
            Assert.AreEqual(a.Bottom, b.Bottom, Tol);
        }

        [Test]
        public void InvalidScreenSize_ReturnsInvalid()
        {
            var insets = SafeAreaInsets.Compute(new Rect(0f, 0f, 100f, 100f), Vector2.zero, new Vector2(100f, 100f), Identity);
            Assert.IsFalse(insets.Valid);
        }

        [Test]
        public void InvalidPanelSize_ReturnsInvalid()
        {
            var insets = SafeAreaInsets.Compute(new Rect(0f, 0f, 100f, 100f), new Vector2(100f, 100f), Vector2.zero, Identity);
            Assert.IsFalse(insets.Valid);
        }
    }
}
