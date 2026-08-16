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

        // ---------- Map navigation fix ----------

        [Test]
        public void MapNav_NoLongerReferencesTutorialProgressPresenter_TechniquesStillDoes()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("private void OnNavMapClicked() => _navigator?.Show(\"map\");", source);
            StringAssert.Contains("private void OnNavTechniquesClicked()", source);
            StringAssert.Contains("TutorialProgressPresenter.IsTechniquesUnlocked", source,
                "Techniques must keep its existing progression gate — only Map's gate was removed.");
        }

        // ---------- Attribute Details popup ----------

        [Test]
        public void RadarMount_OpensAttributePopupOnClick()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("_radarMount.RegisterCallback(_radarClickCallback);", source);
            StringAssert.Contains("_radarClickCallback = _ => ToggleAttributePopup();", source);
        }

        [Test]
        public void AttributePopup_ClosesViaScrimAndExplicitCloseButton()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("_attributePopupScrim.RegisterCallback(_attributeScrimClickCallback);", source);
            StringAssert.Contains("_attributeScrimClickCallback = _ => CloseAttributePopup();", source);
            StringAssert.Contains("_attributePopupClose.clicked += CloseAttributePopup;", source);
        }

        [Test]
        public void PopupToggles_NeverCallPlayRadarEntranceOrNavigate_SoTheyCannotReplayTheEntranceAnimation()
        {
            string source = File.ReadAllText(ControllerPath);
            foreach (var method in new[]
            {
                "OpenAttributePopup", "CloseAttributePopup", "ToggleAttributePopup",
                "OpenUsernameEditPopup", "CloseUsernameEditPopup", "OnUsernameSaveClicked"
            })
            {
                string body = ExtractMethodBody(source, method);
                Assert.IsNotNull(body, $"Expected to find method '{method}'.");
                StringAssert.DoesNotContain("PlayRadarEntrance", body, $"'{method}' must never replay the radar entrance animation.");
                StringAssert.DoesNotContain("_navigator.Show", body, $"'{method}' must never navigate — it's a Profile-local overlay toggle only.");
            }
        }

        // ---------- editable username ----------

        [Test]
        public void DisplayName_LoadsFromStorage_OnBind_AndSavesOnSave()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("_displayNameLabel.text = ProfileDisplayNameStorage.Load();", source);
            StringAssert.Contains("ProfileDisplayNameStorage.Validate(_usernameField?.value)", source);
            StringAssert.Contains("ProfileDisplayNameStorage.Save(validated);", source);
            StringAssert.Contains("_displayNameLabel.text = validated;", source,
                "Saving must update the displayed name immediately.");
        }

        [Test]
        public void EmptyUsername_IsRejected_ShowsErrorInsteadOfSaving()
        {
            string source = File.ReadAllText(ControllerPath);
            string saveBody = ExtractMethodBody(source, "OnUsernameSaveClicked");
            Assert.IsNotNull(saveBody);
            StringAssert.Contains("if (validated == null)", saveBody);
            StringAssert.Contains("ShowUsernameError();", saveBody);
            StringAssert.Contains("return;", saveBody);
        }

        /// <summary>
        /// Extracts a method's body, anchored on its DECLARATION ("private void
        /// Name(" — never a call site elsewhere in the file, which would have no
        /// space/access-modifier prefix immediately before it). Handles both
        /// brace bodies and this file's several expression-bodied ("=> ...;")
        /// one-liners.
        /// </summary>
        private static string ExtractMethodBody(string source, string methodName)
        {
            int declIndex = source.IndexOf("private void " + methodName + "(", System.StringComparison.Ordinal);
            if (declIndex < 0)
                return null;

            int parenClose = source.IndexOf(')', declIndex);
            if (parenClose < 0)
                return null;

            int cursor = parenClose + 1;
            while (cursor < source.Length && char.IsWhiteSpace(source[cursor]))
                cursor++;

            if (cursor < source.Length && source[cursor] == '{')
            {
                int depth = 0;
                for (int i = cursor; i < source.Length; i++)
                {
                    if (source[i] == '{')
                        depth++;
                    else if (source[i] == '}')
                    {
                        depth--;
                        if (depth == 0)
                            return source.Substring(cursor + 1, i - cursor - 1);
                    }
                }
                return null;
            }

            if (cursor + 1 < source.Length && source[cursor] == '=' && source[cursor + 1] == '>')
            {
                int semicolon = source.IndexOf(';', cursor);
                return semicolon < 0 ? null : source.Substring(cursor, semicolon - cursor);
            }

            return null;
        }
    }
}
