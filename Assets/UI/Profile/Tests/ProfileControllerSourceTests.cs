using System.Globalization;
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

        // ---------- radar glow: removed entirely, one clean fill+stroke polygon ----------

        [Test]
        public void RadarChart_HasNoExpandedGlowPolygons_GlowLayersArrayIsGone()
        {
            string source = File.ReadAllText(ChartPath);
            StringAssert.DoesNotContain("GlowLayers", source,
                "The layered/expanding-polygon glow approximation must be fully removed, not just unused.");
            StringAssert.DoesNotContain("ScalePolygon", source,
                "ScalePolygon existed only to enlarge copies of the data polygon for the old glow effect and must be deleted as dead code.");
        }

        [Test]
        public void RadarChart_MainPolygonIsTheOnlyFillAndOnlyStroke_AfterTheGuideGrid()
        {
            string source = File.ReadAllText(ChartPath);
            string drawBody = ExtractMethodBody(source, "OnGenerateVisualContent");
            Assert.IsNotNull(drawBody, "Expected to find OnGenerateVisualContent.");

            // Within the data-drawing section (after the grid/axis lines, which
            // legitimately use DrawPolygonOutline for the guide grid), exactly one
            // fill and one stroke may appear: the main data polygon's.
            int progressGuardIndex = drawBody.IndexOf("if (_progress <= 0f)", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(progressGuardIndex, 0, "Expected the early-out guard before data drawing.");
            string dataSection = drawBody.Substring(progressGuardIndex);

            Assert.AreEqual(1, CountOccurrences(dataSection, "DrawPolygonFill("),
                "Exactly one filled shape (the main data polygon) may appear after the grid/axis lines — no glow layers.");
            Assert.AreEqual(1, CountOccurrences(dataSection, "DrawPolygonOutline("),
                "Exactly one stroked shape (the main data polygon) may appear after the grid/axis lines.");
            StringAssert.Contains("DrawPolygonFill(painter, data, DataFill);", dataSection);
            StringAssert.Contains("DrawPolygonOutline(painter, data, DataStroke, 2.5f);", dataSection);
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
