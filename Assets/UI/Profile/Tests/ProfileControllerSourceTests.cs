using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Mikey.UI.Profile.Tests
{
    /// <summary>
    /// Contract for the parts of the radar entrance/data pipeline that live in
    /// controller/chart source rather than markup (placeholder values, glow layers,
    /// entrance animation timing/trigger) — verified by reading source, mirroring
    /// ProfileProgressionTests' established technique for MonoBehaviour internals
    /// not practical to drive through a live panel in EditMode.
    /// </summary>
    public class ProfileControllerSourceTests
    {
        private const string ControllerPath = "Assets/UI/Profile/ProfileController.cs";
        private const string ChartPath = "Assets/UI/Profile/ProfileRadarChart.cs";

        [Test]
        public void RadarValues_MatchTheSpecPlaceholders_InStrengthSpeedFormStaminaControlOrder()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("RadarValues = { 60f, 50f, 45f, 55f, 40f }", source);
        }

        [Test]
        public void RadarChart_IsMountedIntoProfileRadarMount()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("root.Q<VisualElement>(\"profile-radar-mount\")", source);
            StringAssert.Contains("new ProfileRadarChart()", source);
            StringAssert.Contains("_radarMount.Add(_radarChart)", source);
        }

        [Test]
        public void RadarEntrance_ReplaysOnFreshProfileEntry_ViaScreenChanged()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("_navigator.ScreenChanged += OnScreenChanged;", source);
            StringAssert.Contains("private void OnScreenChanged(string screenId)", source);
            StringAssert.Contains("PlayRadarEntrance();", source);
        }

        [Test]
        public void RadarEntrance_StartsCollapsedAndEasesOutWithNoOvershoot()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("_radarChart.Progress = 0f;", source, "Entrance must start fully collapsed.");
            StringAssert.Contains("1f - Mathf.Pow(1f - t, 3f)", source, "Expected a plain ease-out cubic — no bounce/overshoot library.");
        }

        [Test]
        public void RadarEntrance_DurationIsWithinSpecRange_0_55To0_75Seconds()
        {
            string source = File.ReadAllText(ControllerPath);
            var match = Regex.Match(source, @"RadarEntranceSeconds\s*=\s*(\d+(\.\d+)?)f");
            Assert.IsTrue(match.Success, "Expected a 'RadarEntranceSeconds = <n>f' constant.");
            float seconds = float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.GreaterOrEqual(seconds, 0.55f);
            Assert.LessOrEqual(seconds, 0.75f);
        }

        [Test]
        public void RadarChart_ImplementsALayeredGlow_BehindTheMainPolygon()
        {
            string source = File.ReadAllText(ChartPath);
            StringAssert.Contains("GlowOuter", source);
            StringAssert.Contains("GlowInner", source);
            StringAssert.Contains("ScalePolygon(data, center,", source,
                "Glow must be drawn as larger, lower-alpha copies of the same data polygon behind it.");
        }

        [Test]
        public void RadarChart_UsesPainter2D_NotRectangleVisualElements()
        {
            string source = File.ReadAllText(ChartPath);
            StringAssert.Contains("MeshGenerationContext mgc", source);
            StringAssert.Contains("mgc.painter2D", source);
        }
    }
}
