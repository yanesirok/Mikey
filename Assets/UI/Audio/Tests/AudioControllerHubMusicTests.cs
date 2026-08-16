using NUnit.Framework;

namespace Mikey.UI.Audio.Tests
{
    /// <summary>
    /// Direct behavioral coverage for the hub soundtrack's transition policy:
    /// <see cref="AudioController.IsHubScreen"/> classifies every production
    /// screen id into hub (Main Menu, Japan/Okinawa Map, Profile, Techniques) or
    /// non-hub (Logo Intro, Lore, and training/gameplay: combineIntro, camTest,
    /// combine, practice), and the pure <see cref="AudioController.ShouldStartHubMusic"/>
    /// / <see cref="AudioController.ShouldStopHubMusic"/> decisions prove the
    /// exact continuity matrix the launch/shell flow depends on — hub-to-hub
    /// transitions (Menu -&gt; Map -&gt; Profile -&gt; Techniques, and any local
    /// overlay on top of them) never restart or re-fade the soundtrack, and it
    /// only fades at a genuine hub/training boundary. Unlike
    /// AudioControllerSourceTests (which pins the MonoBehaviour wiring that
    /// calls these), this exercises the actual decision functions directly —
    /// they are pure and static, so no live panel/coroutine is needed, mirroring
    /// IntroControllerTests.IsIntroExit.
    /// </summary>
    public class AudioControllerHubMusicTests
    {
        // --- Screen classification ---

        [TestCase("menu", true)]
        [TestCase("map", true)]
        [TestCase("mapOkinawa", true)]
        [TestCase("profile", true)]
        [TestCase("techniques", true)]
        [TestCase("title", false)]
        [TestCase("intro", false)]
        [TestCase("combineIntro", false)]
        [TestCase("camTest", false)]
        [TestCase("combine", false)]
        [TestCase("practice", false)]
        public void IsHubScreen_ClassifiesEveryProductionScreen(string screenId, bool expectedIsHub)
        {
            Assert.AreEqual(expectedIsHub, AudioController.IsHubScreen(screenId));
        }

        // --- Hub-to-hub: must never restart or stop (tests 10-13) ---

        [TestCase("menu", "map")]
        [TestCase("map", "mapOkinawa")]
        [TestCase("map", "profile")]
        [TestCase("profile", "techniques")]
        [TestCase("techniques", "menu")]
        public void HubToHubTransition_NeitherStartsNorStops_KeepsPlayingContinuously(string from, string to)
        {
            bool wasInHub = AudioController.IsHubScreen(from);
            bool isHub = AudioController.IsHubScreen(to);
            Assert.IsTrue(wasInHub, $"Precondition: '{from}' must be a hub screen for this case.");
            Assert.IsTrue(isHub, $"Precondition: '{to}' must be a hub screen for this case.");

            Assert.IsFalse(AudioController.ShouldStartHubMusic(wasInHub, isHub),
                $"{from} -> {to} is hub-to-hub; the soundtrack is already playing and must not be restarted/re-faded.");
            Assert.IsFalse(AudioController.ShouldStopHubMusic(wasInHub, isHub),
                $"{from} -> {to} is hub-to-hub; the soundtrack must not stop.");
        }

        // --- Genuine hub entry: starts exactly once (not on every hub-to-hub hop) ---

        [TestCase("intro", "menu")]
        [TestCase("practice", "menu")]
        [TestCase("combineIntro", "map")]
        public void NonHubToHubTransition_Starts(string from, string to)
        {
            bool wasInHub = AudioController.IsHubScreen(from);
            bool isHub = AudioController.IsHubScreen(to);
            Assert.IsFalse(wasInHub, $"Precondition: '{from}' must not be a hub screen for this case.");
            Assert.IsTrue(isHub, $"Precondition: '{to}' must be a hub screen for this case.");

            Assert.IsTrue(AudioController.ShouldStartHubMusic(wasInHub, isHub),
                $"{from} -> {to} enters the hub from outside it — the soundtrack must (re)start.");
        }

        // --- Entering training/gameplay: fades (test 15) ---

        [TestCase("menu", "combineIntro")]
        [TestCase("map", "camTest")]
        [TestCase("techniques", "practice")]
        [TestCase("profile", "combine")]
        public void HubToTrainingTransition_Stops(string from, string to)
        {
            bool wasInHub = AudioController.IsHubScreen(from);
            bool isHub = AudioController.IsHubScreen(to);
            Assert.IsTrue(wasInHub, $"Precondition: '{from}' must be a hub screen for this case.");
            Assert.IsFalse(isHub, $"Precondition: '{to}' must be training/gameplay for this case.");

            Assert.IsTrue(AudioController.ShouldStopHubMusic(wasInHub, isHub),
                $"{from} -> {to} leaves the hub for training/gameplay — the soundtrack must fade out.");
        }

        // --- Within training: must not repeatedly re-trigger stop (no duplicate fades) ---

        [TestCase("combineIntro", "camTest")]
        [TestCase("camTest", "combine")]
        public void TrainingToTrainingTransition_NeitherStartsNorStops(string from, string to)
        {
            bool wasInHub = AudioController.IsHubScreen(from);
            bool isHub = AudioController.IsHubScreen(to);
            Assert.IsFalse(wasInHub, $"Precondition: '{from}' must be training/gameplay for this case.");
            Assert.IsFalse(isHub, $"Precondition: '{to}' must be training/gameplay for this case.");

            Assert.IsFalse(AudioController.ShouldStartHubMusic(wasInHub, isHub));
            Assert.IsFalse(AudioController.ShouldStopHubMusic(wasInHub, isHub),
                $"{from} -> {to} stays within training; the soundtrack is already stopped and must not be re-paused/re-faded.");
        }

        // --- Pre-hub: Logo Intro's own embedded audio must never be layered under hub music ---

        [Test]
        public void TitleToIntroTransition_NeverStartsHubMusic()
        {
            bool wasInHub = AudioController.IsHubScreen("title");
            bool isHub = AudioController.IsHubScreen("intro");
            Assert.IsFalse(AudioController.ShouldStartHubMusic(wasInHub, isHub),
                "Hub music must never start over Logo Intro's own embedded audio or during Lore.");
        }
    }
}
