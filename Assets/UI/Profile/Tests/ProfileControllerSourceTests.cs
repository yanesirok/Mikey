using System.Globalization;
using System.IO;
using System.Linq;
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

        // ---------- radar glow: fill-only layers, one crisp main outline ----------

        [Test]
        public void RadarChart_HasFourGlowLayers_FillOnly_ScaledAroundTheDataPolygon()
        {
            string source = File.ReadAllText(ChartPath);
            var arrayBlock = ExtractArrayInitializer(source, "GlowLayers");
            Assert.IsNotNull(arrayBlock, "Expected a 'GlowLayers' array.");

            // Anchored on "new Color(" immediately following so this only matches
            // each tuple's own scale value, not the inner Color constructor's
            // first (red-channel) argument.
            var scales = Regex.Matches(arrayBlock, @"\((\d+\.\d+)f,\s*new Color\(");
            Assert.GreaterOrEqual(scales.Count, 2, "Expected 2-4 progressively larger glow layers.");
            Assert.LessOrEqual(scales.Count, 4, "Expected 2-4 progressively larger glow layers.");

            StringAssert.Contains("foreach (var (scale, color) in GlowLayers)", source);
            StringAssert.Contains("DrawPolygonFill(painter, ScalePolygon(data, center, scale), color);", source,
                "Every glow layer must be fill-only.");
        }

        [Test]
        public void RadarGlow_OpacityDecreasesOutward_NoneApproachTheMainFillsStrength()
        {
            string source = File.ReadAllText(ChartPath);
            var arrayBlock = ExtractArrayInitializer(source, "GlowLayers");
            Assert.IsNotNull(arrayBlock);

            // Colors appear outermost-first in source (see the layer-ordering
            // comment in ProfileRadarChart.cs); each successive (inner) layer must
            // be more opaque than the one before it, and even the strongest must
            // stay well under DataFill's 0.32 alpha.
            var alphas = Regex.Matches(arrayBlock, @"0\.7765f,\s*0\.1569f,\s*0\.1569f,\s*(\d+(\.\d+)?)f\)")
                .Select(m => float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                .ToList();
            Assert.GreaterOrEqual(alphas.Count, 2);

            for (int i = 1; i < alphas.Count; i++)
                Assert.Greater(alphas[i], alphas[i - 1], "Opacity must increase layer-by-layer toward the center (i.e. decrease outward).");

            Assert.Less(alphas[alphas.Count - 1], 0.32f, "Even the strongest glow layer must stay clearly under the main fill's opacity.");
        }

        [Test]
        public void RadarChart_MainPolygonIsTheOnlyStrokedShape_GlowLayersHaveNoOutline()
        {
            string source = File.ReadAllText(ChartPath);
            string drawBody = ExtractMethodBody(source, "OnGenerateVisualContent");
            Assert.IsNotNull(drawBody, "Expected to find OnGenerateVisualContent.");

            // Within the glow+data drawing section (after the grid/axis lines,
            // which legitimately use DrawPolygonOutline for the guide grid), the
            // only stroke must be the main data polygon's.
            int progressGuardIndex = drawBody.IndexOf("if (_progress <= 0f)", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(progressGuardIndex, 0, "Expected the early-out guard before glow/data drawing.");
            string glowAndDataSection = drawBody.Substring(progressGuardIndex);

            Assert.AreEqual(1, CountOccurrences(glowAndDataSection, "DrawPolygonOutline("),
                "Exactly one stroked shape (the main data polygon) may appear after the grid/axis lines.");
            StringAssert.Contains("DrawPolygonOutline(painter, data, DataStroke, 2.5f);", glowAndDataSection);
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
            foreach (var method in new[] { "OpenAttributePopup", "CloseAttributePopup", "ToggleAttributePopup" })
            {
                string body = ExtractMethodBody(source, method);
                Assert.IsNotNull(body, $"Expected to find method '{method}'.");
                StringAssert.DoesNotContain("PlayRadarEntrance", body, $"'{method}' must never replay the radar entrance animation.");
                StringAssert.DoesNotContain("_navigator.Show", body, $"'{method}' must never navigate — it's a Profile-local overlay toggle only.");
            }
        }

        // ---------- Profile Details: edit navigation, display-name refresh, one-time redirect ----------

        [Test]
        public void EditIconWiring_NoLongerExistsInController_ItsAPlainGoNavigatorNow()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.DoesNotContain("profile-name-edit-open", source,
                "The edit button is now 'go-profileDetails' (see MikeyApp.uxml) — needs zero controller wiring.");
            StringAssert.DoesNotContain("OpenUsernameEditPopup", source, "The old username-only modal flow must be fully removed.");
            StringAssert.DoesNotContain("ProfileDisplayNameStorage.Save", source,
                "ProfileController must no longer write display name directly — ProfileUserDataStorage is the primary store now.");
        }

        [Test]
        public void DisplayName_RefreshesFromProfileUserDataStorage_OnEveryProfileEntry()
        {
            string source = File.ReadAllText(ControllerPath);
            string refreshBody = ExtractMethodBody(source, "RefreshDisplayName");
            Assert.IsNotNull(refreshBody, "Expected a RefreshDisplayName method.");
            StringAssert.Contains("_displayNameLabel.text = ProfileUserDataStorage.Load().DisplayName;", refreshBody);

            string handleEnteredBody = ExtractMethodBody(source, "HandleProfileEntered");
            Assert.IsNotNull(handleEnteredBody, "Expected a HandleProfileEntered method driving every fresh 'profile' entry.");
            StringAssert.Contains("RefreshDisplayName();", handleEnteredBody);
            StringAssert.Contains("_navigator.ScreenChanged += OnScreenChanged;", source);
        }

        [Test]
        public void IncompleteProfile_RedirectsToProfileDetailsOnce_NeverLoops()
        {
            string source = File.ReadAllText(ControllerPath);
            string handleEnteredBody = ExtractMethodBody(source, "HandleProfileEntered");
            Assert.IsNotNull(handleEnteredBody);

            StringAssert.Contains("_profileDetailsRedirectOffered", handleEnteredBody,
                "Must consult an in-memory (session-only) flag so the redirect fires at most once per session.");
            StringAssert.Contains("ProfileUserDataStorage.IsComplete(ProfileUserDataStorage.Load())", handleEnteredBody);
            StringAssert.Contains("_navigator?.Show(ProfileDetailsScreenId);", handleEnteredBody);

            // The flag must be a plain instance field, never written through
            // PlayerPrefs — it must not survive/leak across sessions or force a
            // permanent redirect loop.
            StringAssert.Contains("private bool _profileDetailsRedirectOffered;", source);
            StringAssert.DoesNotContain("PlayerPrefs", source);
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

        /// <summary>Body of a "private static readonly ... fieldName = { ... };" array/collection initializer.</summary>
        private static string ExtractArrayInitializer(string source, string fieldName)
        {
            int nameIndex = source.IndexOf(fieldName + " =", System.StringComparison.Ordinal);
            if (nameIndex < 0)
                return null;
            int open = source.IndexOf('{', nameIndex);
            if (open < 0)
                return null;
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(open + 1, i - open - 1);
                }
            }
            return null;
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int index = 0;
            while ((index = haystack.IndexOf(needle, index, System.StringComparison.Ordinal)) != -1)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }
    }
}
