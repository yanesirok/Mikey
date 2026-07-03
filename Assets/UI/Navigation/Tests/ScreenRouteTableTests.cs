using NUnit.Framework;
using Mikey.UI.Navigation;

namespace Mikey.UI.Navigation.Tests
{
    public class ScreenRouteTableTests
    {
        [Test]
        public void UnregisteredScreen_IsPanel()
        {
            var table = new ScreenRouteTable();
            Assert.AreEqual(ScreenKind.Panel, table.KindOf("profile"));
            Assert.IsFalse(table.IsScene("profile"));
            Assert.IsNull(table.SceneNameOf("profile"));
        }

        [Test]
        public void RegisteredScreen_IsScene_WithSceneName()
        {
            var table = new ScreenRouteTable();
            table.RegisterScene("practice", "Practice");
            Assert.AreEqual(ScreenKind.Scene, table.KindOf("practice"));
            Assert.IsTrue(table.IsScene("practice"));
            Assert.AreEqual("Practice", table.SceneNameOf("practice"));
        }

        [Test]
        public void BuildDefault_HasNoSceneScreens_BeforePracticeMigration()
        {
            // Updated in plan Task 7 once "practice" becomes a scene.
            ScreenRouteTable table = MikeyScreens.BuildDefault();
            Assert.IsFalse(table.IsScene("practice"));
        }
    }
}
