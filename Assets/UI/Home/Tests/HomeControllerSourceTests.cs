using System.IO;
using Mikey.UI.Progression;
using NUnit.Framework;

namespace Mikey.UI.Home.Tests
{
    /// <summary>
    /// Contract for the rebuilt HomeController — the Sign In gate. The two
    /// decisions that actually matter (where the gate leads, and whether it is
    /// shown at all) are pure statics and are unit-tested directly; the rest of
    /// the wiring is verified by reading the source, mirroring
    /// IntroControllerTests / TitleControllerSourceTests for MonoBehaviour
    /// internals not practical to drive through a live panel in EditMode.
    /// </summary>
    public class HomeControllerSourceTests
    {
        private const string SourcePath = "Assets/UI/Home/HomeController.cs";

        // ---------- where the gate leads: Lore once, the Map forever after ----------

        [Test]
        public void FirstLaunch_GoesToLore()
        {
            Assert.AreEqual("intro", HomeController.NextScreenAfterAuth(TutorialProgressState.NewPlayer),
                "A player who has never seen Lore must get it before the Map.");
        }

        [TestCase(TutorialProgressState.IntroCompleted)]
        [TestCase(TutorialProgressState.CombineStarted)]
        [TestCase(TutorialProgressState.LessonCompleted)]
        [TestCase(TutorialProgressState.ThirtyDayStreakCompleted)]
        public void EveryLaunchAfterLore_GoesStraightToTheMap(TutorialProgressState state)
        {
            Assert.AreEqual("map", HomeController.NextScreenAfterAuth(state),
                "Lore is a first-launch-only screen — anything at or past IntroCompleted goes straight to the Map.");
        }

        // ---------- whether the gate is shown at all ----------

        [Test]
        public void OnlyAnExistingSessionSkipsTheGate()
        {
            // Entering the screen consults IsSignedIn and nothing else. In
            // particular it must NOT skip on CanSignIn: the Editor can never sign
            // in, and skipping there would make the screen impossible to see or
            // test. The guest action is always there as the way through.
            string source = File.ReadAllText(SourcePath);
            int enterIndex = source.IndexOf("private void OnScreenEntered(string screenId)", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(enterIndex, 0, "Expected an OnScreenEntered handler.");

            int skipIndex = source.IndexOf("_sync.IsSignedIn)", enterIndex, System.StringComparison.Ordinal);
            int showIndex = source.IndexOf("_navigator?.Show(NextScreen());", enterIndex, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(skipIndex, 0, "The pass-through must be gated on an existing session.");
            Assert.Greater(showIndex, skipIndex, "The pass-through must follow that check.");

            int canSignInIndex = source.IndexOf("CanSignIn", enterIndex, System.StringComparison.Ordinal);
            int clickIndex = source.IndexOf("private void OnSignInClicked()", System.StringComparison.Ordinal);
            Assert.IsTrue(canSignInIndex < 0 || canSignInIndex > clickIndex,
                "OnScreenEntered must not consult CanSignIn — an unsupported platform still sees the gate.");
        }

        [Test]
        public void PressingGoogleWhereItCannotWork_SaysSo_InsteadOfGoingDead()
        {
            string source = File.ReadAllText(SourcePath);
            int clickIndex = source.IndexOf("private void OnSignInClicked()", System.StringComparison.Ordinal);
            int unavailableIndex = source.IndexOf("SetStatus(SignInUnavailableMessage);", clickIndex, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(unavailableIndex, 0,
                "An unavailable sign-in must report itself on the status line, not silently do nothing.");
            StringAssert.Contains("SignInUnavailableMessage = \"Google sign-in is not available on this device.\"", source);
        }

        // ---------- wiring ----------

        [Test]
        public void DrivesTheMenuScreen_AndBothDestinationsAreRealScreenIds()
        {
            Assert.AreEqual("menu", HomeController.ScreenId);
            Assert.AreEqual("intro", HomeController.LoreScreenId);
            Assert.AreEqual("map", HomeController.MapScreenId);
        }

        [Test]
        public void BindsBothIconActions_NeitherAsAScreenNavigator()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("SignInButtonName = \"menu-google-signin\"", source);
            StringAssert.Contains("GuestButtonName = \"menu-guest-continue\"", source);
            StringAssert.Contains("BindButton(_signInButton, OnSignInClicked);", source);
            StringAssert.Contains("BindButton(_guestButton, OnGuestClicked);", source);
            StringAssert.DoesNotContain("go-map", source,
                "Neither action is a ScreenManager 'go-' navigator — where they lead depends on progression state.");
        }

        [Test]
        public void GoogleAction_DelegatesToSyncService_NeverHandlesTokensItself()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_sync.SignIn();", source);
            foreach (var forbidden in new[] { "idToken", "TryTakeIdToken", "rawNonce", "IAuthGateway", "BeginSignIn", "SupabaseClient", "access_token" })
                StringAssert.DoesNotContain(forbidden, source,
                    $"The Sign In screen must never touch '{forbidden}' — SyncService owns the whole exchange.");
        }

        [Test]
        public void PressingGoogle_DisablesBothActions_UntilTheAttemptSettles()
        {
            // A system dialog is up; a second press would open a second dialog.
            string source = File.ReadAllText(SourcePath);
            int clickIndex = source.IndexOf("private void OnSignInClicked()", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(clickIndex, 0, "Expected an OnSignInClicked handler.");

            int statusIndex = source.IndexOf("SetStatus(SigningInMessage);", clickIndex, System.StringComparison.Ordinal);
            int disableIndex = source.IndexOf("SetInteractable(false);", clickIndex, System.StringComparison.Ordinal);
            int signInIndex = source.IndexOf("_sync.SignIn();", clickIndex, System.StringComparison.Ordinal);

            Assert.GreaterOrEqual(statusIndex, 0, "OnSignInClicked must show the pending status.");
            Assert.Greater(signInIndex, statusIndex, "The status must be up before the system dialog opens.");
            Assert.Greater(signInIndex, disableIndex,
                "Both actions must be disabled BEFORE starting the sign-in — a second press would open a second dialog.");
        }

        [Test]
        public void AnAttemptThatEndsWithoutASession_ReenablesTheActions_WithAnHonestNotice()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("if (_sync.IsSigningIn)", source,
                "Render must distinguish 'still trying' from 'finished without a session'.");
            StringAssert.Contains("SetStatus(SignInFailedMessage);", source);
            StringAssert.Contains("SignInFailedMessage = \"Sign-in did not complete. Tap to try again.\"", source);
            // Never a fake success and never a dead button.
            StringAssert.DoesNotContain("Signed in!", source);
        }

        [Test]
        public void SkippingTheGate_UsesAPlainShow_WithNoTransitionOfItsOwn()
        {
            // TitleController is still mid-transition when it swaps this screen in;
            // a second fade here would fight it and flash the gate.
            string source = File.ReadAllText(SourcePath);
            int showIndex = source.IndexOf("_navigator?.Show(NextScreen());", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(showIndex, 0, "The pass-through must be a plain synchronous Show.");
            int leaveIndex = source.IndexOf("_leaveRoutine = StartCoroutine(LeaveRoutine());", System.StringComparison.Ordinal);
            Assert.Greater(leaveIndex, showIndex, "The pass-through must not run the cinematic LeaveRoutine.");
        }

        [Test]
        public void LeavingByAButtonPress_FadesToBlack_SwapsWhileCovered_ThenFadesIn()
        {
            string source = File.ReadAllText(SourcePath);
            int fadeToBlackIndex = source.IndexOf("_overlay.FadeToBlack(FadeToBlackSeconds)", System.StringComparison.Ordinal);
            int blackHoldIndex = source.IndexOf("WaitForSecondsRealtime(BlackHoldSeconds)", System.StringComparison.Ordinal);
            int showIndex = source.IndexOf("_navigator.Show(NextScreen());", System.StringComparison.Ordinal);
            int fadeFromBlackIndex = source.IndexOf("_overlay.FadeFromBlack(FadeInSeconds)", System.StringComparison.Ordinal);

            Assert.GreaterOrEqual(fadeToBlackIndex, 0, "The exit must darken to black first.");
            Assert.Greater(blackHoldIndex, fadeToBlackIndex, "It must hold on full black after the fade.");
            Assert.Greater(showIndex, blackHoldIndex, "The screen swap must happen while fully covered.");
            Assert.Greater(fadeFromBlackIndex, showIndex, "The next screen must fade in from black only after the swap.");
        }

        [Test]
        public void LeavingHappensExactlyOnce_HoweverItWasTriggered()
        {
            // A successful sign-in and a guest press can land in the same frame.
            string source = File.ReadAllText(SourcePath);
            int leaveIndex = source.IndexOf("private void Leave()", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(leaveIndex, 0, "Expected a single Leave() entry point.");

            int guardIndex = source.IndexOf("if (_leaving || _navigator == null)", leaveIndex, System.StringComparison.Ordinal);
            int setIndex = source.IndexOf("_leaving = true;", leaveIndex, System.StringComparison.Ordinal);
            int startIndex = source.IndexOf("_leaveRoutine = StartCoroutine(LeaveRoutine());", leaveIndex, System.StringComparison.Ordinal);

            Assert.GreaterOrEqual(guardIndex, 0, "Leave() must guard on the _leaving flag.");
            Assert.Greater(setIndex, guardIndex, "The flag must be raised right after the guard.");
            Assert.Greater(startIndex, setIndex, "The transition must start only after the flag is raised.");
        }

        [Test]
        public void OnDisable_UnbindsButtons_AndUnsubscribesBothEvents_NoLeak()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("_buttonBindings[i].Unbind();", source);
            StringAssert.Contains("_buttonBindings.Clear();", source);
            StringAssert.Contains("_navigator.ScreenChanged -= OnScreenEntered;", source);
            StringAssert.Contains("_sync.Changed -= Render;", source);
        }

        [Test]
        public void RetiredMainMenuWiring_IsGone()
        {
            string source = File.ReadAllText(SourcePath);
            foreach (var retired in new[] { "_vowModal", "SelectVow", "VowEnrollmentMessage", "OnQuitClicked", "Application.Quit", "menu-settings-open" })
                StringAssert.DoesNotContain(retired, source,
                    $"'{retired}' belongs to the retired PLAY/VOW/SETTINGS/QUIT menu.");
        }

        [Test]
        public void SignInSelection_IsNeverPersistedByThisScreen()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("PlayerPrefs", source,
                "The gate reads progression through ITutorialProgress and sessions through SyncService — it stores nothing of its own.");
        }
    }
}
