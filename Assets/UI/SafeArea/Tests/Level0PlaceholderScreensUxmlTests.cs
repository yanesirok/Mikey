using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.SafeArea.Tests
{
    /// <summary>
    /// Structural contract for the four Level 0 placeholder test screens
    /// (combinePushups/Squats/Wallsit/Yokogeri) — no real assessment
    /// implementation exists yet for any of them. Each is deliberately static
    /// (no controller): honest "not implemented" copy and a plain return route,
    /// and critically, no button anywhere on any of these four screens may ever
    /// mark a test complete — that would fake real physical performance data
    /// only a real assessment/backend can produce.
    /// </summary>
    public class Level0PlaceholderScreensUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/Level0Tests/Level0Tests.uss";

        private static readonly string[] ScreenIds =
        {
            "combinePushups", "combineSquats", "combineWallsit", "combineYokogeri",
        };

        private static VisualElement BuildTree()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(vta, $"Could not load {UxmlPath}");
            var root = new VisualElement();
            vta.CloneTree(root);
            return root;
        }

        [TestCaseSource(nameof(ScreenIds))]
        public void Screen_ExistsExactlyOnce_AsAScreen(string screenId)
        {
            var matches = BuildTree().Query<VisualElement>(screenId).ToList();
            Assert.AreEqual(1, matches.Count, $"Expected exactly one screen named '{screenId}'.");
            Assert.IsTrue(matches[0].ClassListContains("screen"), $"'{screenId}' must carry the .screen class.");
        }

        [TestCaseSource(nameof(ScreenIds))]
        public void Screen_HasAPlainReturnToCombineRoute(string screenId)
        {
            var screen = BuildTree().Q<VisualElement>(screenId);
            var back = screen.Q<Button>("go-combine");
            Assert.IsNotNull(back, $"'{screenId}' must expose a 'go-combine' return route.");
        }

        [TestCaseSource(nameof(ScreenIds))]
        public void Screen_HasHonestNotImplementedCopy(string screenId)
        {
            var screen = BuildTree().Q<VisualElement>(screenId);
            var texts = screen.Query<Label>().ToList().Select(l => l.text ?? string.Empty);
            Assert.IsTrue(texts.Any(t => t.Contains("not yet implemented")),
                $"'{screenId}' must honestly state the assessment is not yet implemented.");
        }

        [TestCaseSource(nameof(ScreenIds))]
        public void Screen_NeverHasAButtonOtherThanTheReturnRoute(string screenId)
        {
            // Structurally incapable of faking completion: the ONLY button on
            // any of these four screens is the plain "go-combine" return route.
            var screen = BuildTree().Q<VisualElement>(screenId);
            var buttons = screen.Query<Button>().ToList();
            Assert.AreEqual(1, buttons.Count, $"'{screenId}' must have exactly one button (the return route).");
            Assert.AreEqual("go-combine", buttons[0].name);
        }

        [Test]
        public void NoLevel0TestsScreen_HasAnyControllerBoundCompletionAction()
        {
            // Even the raw markup source must never carry a name suggestive of a
            // completion side effect (mirrors the naming convention every real
            // completion action in this app uses — e.g. "camera-test-complete",
            // "practice-complete").
            string markup = File.ReadAllText(UxmlPath);
            int combineStart = markup.IndexOf("name=\"combinePushups\"", System.StringComparison.Ordinal);
            int techniquesStart = markup.IndexOf("<!-- 7 - TECHNIQUES", System.StringComparison.Ordinal);
            Assert.Greater(combineStart, -1);
            Assert.Greater(techniquesStart, combineStart);
            string block = markup.Substring(combineStart, techniquesStart - combineStart);

            StringAssert.DoesNotContain("-complete\"", block,
                "No Level 0 placeholder screen may expose any '...-complete' style action.");
        }

        [Test]
        public void NoCSharpControllerExists_ForAnyLevel0PlaceholderScreen()
        {
            // These four screens are deliberately pure markup — there must be no
            // C# file anywhere that could bind a button on them to
            // Level0Progression.Complete.
            const string folder = "Assets/UI/Level0Tests";
            Assert.IsTrue(Directory.Exists(folder), $"Expected {folder} to exist.");
            var csFiles = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories);
            Assert.IsEmpty(csFiles, "The Level0Tests folder must contain no C# source — the four placeholder screens are static markup only.");
        }

        [Test]
        public void Stylesheet_ReferencesAllFourIllustrationAssets()
        {
            string uss = File.ReadAllText(UssPath);
            foreach (var file in new[] { "combine_pushups.png", "combine_squats.png", "combine_wallsit.png", "combine_yokogeri.png" })
                StringAssert.Contains(file, uss, $"Level0Tests.uss must reference '{file}'.");
        }
    }
}
