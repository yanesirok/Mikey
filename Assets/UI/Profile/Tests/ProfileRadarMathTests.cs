using NUnit.Framework;
using UnityEngine;

namespace Mikey.UI.Profile.Tests
{
    /// <summary>
    /// Pure geometry contract for the capability radar (see ProfileRadarMath /
    /// ProfileRadarChart): exactly five axes in the reference's Strength/Speed/
    /// Form/Stamina/Control order, and the placeholder values ProfileController
    /// centralizes (60/50/45/55/40) land where the math says they should.
    /// </summary>
    public class ProfileRadarMathTests
    {
        private static readonly float[] PlaceholderValues = { 60f, 50f, 45f, 55f, 40f };
        private const float MaxValue = 100f;

        [Test]
        public void AxisCount_IsExactlyFive()
        {
            Assert.AreEqual(5, ProfileRadarMath.AxisCount);
            Assert.AreEqual(5, ProfileRadarMath.AxisLabels.Length);
        }

        [Test]
        public void AxisLabels_MatchReferenceOrder()
        {
            CollectionAssert.AreEqual(
                new[] { "Strength", "Speed", "Form", "Stamina", "Control" },
                ProfileRadarMath.AxisLabels);
        }

        [Test]
        public void PlaceholderValues_MatchSpec()
        {
            Assert.AreEqual(60f, PlaceholderValues[0], "Strength");
            Assert.AreEqual(50f, PlaceholderValues[1], "Speed");
            Assert.AreEqual(45f, PlaceholderValues[2], "Form");
            Assert.AreEqual(55f, PlaceholderValues[3], "Stamina");
            Assert.AreEqual(40f, PlaceholderValues[4], "Control");
        }

        [Test]
        public void AxisDirection_FirstAxisPointsStraightUp()
        {
            Vector2 dir = ProfileRadarMath.AxisDirection(0);
            Assert.AreEqual(0f, dir.x, 0.0001f);
            Assert.AreEqual(-1f, dir.y, 0.0001f, "Screen space: negative Y is up.");
        }

        [Test]
        public void AxisDirections_AreEvenlySpacedAndUnitLength()
        {
            for (int i = 0; i < ProfileRadarMath.AxisCount; i++)
            {
                Vector2 dir = ProfileRadarMath.AxisDirection(i);
                Assert.AreEqual(1f, dir.magnitude, 0.0001f, $"Axis {i} direction must be unit length.");
            }

            float angle = Vector2.Angle(ProfileRadarMath.AxisDirection(0), ProfileRadarMath.AxisDirection(1));
            Assert.AreEqual(72f, angle, 0.01f, "Five axes must be evenly spaced at 360/5 = 72 degrees.");
        }

        [Test]
        public void VertexPosition_AtZeroValue_IsExactlyCenter()
        {
            var center = new Vector2(100f, 100f);
            Vector2 vertex = ProfileRadarMath.VertexPosition(0, 0f, MaxValue, 200f, center);
            Assert.AreEqual(center, vertex);
        }

        [Test]
        public void VertexPosition_AtMaxValue_IsExactlyOnTheOuterRing()
        {
            var center = new Vector2(100f, 100f);
            const float radius = 200f;
            Vector2 vertex = ProfileRadarMath.VertexPosition(0, MaxValue, MaxValue, radius, center);
            Assert.AreEqual(radius, Vector2.Distance(center, vertex), 0.001f);
        }

        [Test]
        public void VertexPosition_ScalesLinearlyWithValue()
        {
            var center = Vector2.zero;
            const float radius = 100f;
            Vector2 half = ProfileRadarMath.VertexPosition(2, 50f, MaxValue, radius, center);
            Assert.AreEqual(50f, half.magnitude, 0.001f);
        }

        [Test]
        public void Polygon_ReturnsFivePoints_OneForEachPlaceholderValue()
        {
            var center = Vector2.zero;
            Vector2[] polygon = ProfileRadarMath.Polygon(PlaceholderValues, MaxValue, 100f, center);
            Assert.AreEqual(5, polygon.Length);
            for (int i = 0; i < polygon.Length; i++)
                Assert.AreEqual(PlaceholderValues[i], polygon[i].magnitude, 0.01f);
        }

        [Test]
        public void GridRing_AtFullFraction_MatchesTheOuterVertexPositions()
        {
            var center = new Vector2(10f, 10f);
            const float radius = 150f;
            Vector2[] ring = ProfileRadarMath.GridRing(1f, radius, center);
            for (int i = 0; i < ring.Length; i++)
                Assert.AreEqual(radius, Vector2.Distance(center, ring[i]), 0.001f);
        }

        [Test]
        public void LabelAnchor_SitsBeyondTheOuterRingByTheOutsetAmount()
        {
            var center = Vector2.zero;
            const float radius = 100f;
            const float outset = 20f;
            Vector2 anchor = ProfileRadarMath.LabelAnchor(0, radius, center, outset);
            Assert.AreEqual(radius + outset, anchor.magnitude, 0.001f);
        }
    }
}
