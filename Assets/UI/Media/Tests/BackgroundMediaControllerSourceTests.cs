using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Media.Tests
{
    /// <summary>
    /// Contract for BackgroundMediaController's <see cref="Mikey.UI.SafeArea.IShellPreloader"/>
    /// implementation: Logo Intro calls <c>BeginPreload()</c> the moment it
    /// starts so the Main Menu's background video is already preparing well
    /// before Lore hands off to Menu, and <c>IsReady</c> reflects whether that
    /// preparation has finished (or there was nothing to prepare at all) so a
    /// caller waiting on it never blocks forever. BackgroundMediaController
    /// lives in Assembly-CSharp, which this test assembly cannot reference
    /// directly, so this is verified by reading the source — mirroring
    /// TitleControllerSourceTests for MonoBehaviour internals not practical to
    /// drive through a live panel in EditMode.
    /// </summary>
    public class BackgroundMediaControllerSourceTests
    {
        private const string SourcePath = "Assets/UI/Media/BackgroundMediaController.cs";

        [Test]
        public void ImplementsIShellPreloader()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("class BackgroundMediaController : MonoBehaviour, IShellPreloader", source);
        }

        [Test]
        public void BeginPreload_TargetsTheMenuScreen()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("private const string ShellPreloadScreenId = \"menu\";", source,
                "The launch shell's only preload target is the Main Menu background video.");
            StringAssert.Contains("public void BeginPreload()", source);
        }

        [Test]
        public void BeginPreload_PreparesWithoutPlaying()
        {
            string source = File.ReadAllText(SourcePath);
            int methodIndex = source.IndexOf("public void BeginPreload()", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(methodIndex, 0);
            int nextMethodIndex = source.IndexOf("public bool IsReady", methodIndex, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(nextMethodIndex, 0);
            string body = source.Substring(methodIndex, nextMethodIndex - methodIndex);

            StringAssert.Contains("player.Prepare();", body,
                "BeginPreload must prepare the clip, not play it — the video only becomes visible once its screen actually becomes active.");
            StringAssert.DoesNotContain("player.Play();", body,
                "BeginPreload must never play the Main Menu video while Logo Intro/Lore are still showing.");
        }

        [Test]
        public void IsReady_TrueWhenNothingToPreload_SoACallerNeverBlocksForever()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("!_videoBindingsById.ContainsKey(ShellPreloadScreenId) ||", source,
                "With no Main Menu video binding configured, IsReady must default true rather than hang whatever is waiting on it.");
        }
    }
}
