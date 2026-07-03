using NUnit.Framework;
using Mikey.UI.Navigation;

namespace Mikey.UI.Navigation.Tests
{
    public class SceneTransitionTests
    {
        [Test]
        public void SameTarget_PlansNothing()
        {
            var (unload, load) = SceneTransition.Plan("Practice", "Practice");
            Assert.IsNull(unload);
            Assert.IsNull(load);
        }

        [Test]
        public void FromNone_ToScene_LoadsOnly()
        {
            var (unload, load) = SceneTransition.Plan(null, "Practice");
            Assert.IsNull(unload);
            Assert.AreEqual("Practice", load);
        }

        [Test]
        public void FromScene_ToNone_UnloadsOnly()
        {
            var (unload, load) = SceneTransition.Plan("Practice", null);
            Assert.AreEqual("Practice", unload);
            Assert.IsNull(load);
        }

        [Test]
        public void FromScene_ToOtherScene_UnloadsThenLoads()
        {
            var (unload, load) = SceneTransition.Plan("Practice", "CameraTest");
            Assert.AreEqual("Practice", unload);
            Assert.AreEqual("CameraTest", load);
        }
    }
}
