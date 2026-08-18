using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Contract for <see cref="MapNavigationState"/>: a plain, directly settable
    /// session-only value (not backed by TutorialProgressState or any storage),
    /// defaulting to the Japan world map.
    /// </summary>
    public class MapNavigationStateTests
    {
        [TearDown]
        public void ResetToDefault()
        {
            // Static state — leave it as found so test order never matters.
            MapNavigationState.Current = MapContext.JapanWorld;
        }

        [Test]
        public void DefaultsTo_JapanWorld()
        {
            Assert.AreEqual(MapContext.JapanWorld, MapNavigationState.Current);
        }

        [Test]
        public void IsDirectlySettable_AndReadsBackTheSameValue()
        {
            MapNavigationState.Current = MapContext.OkinawaChapter;
            Assert.AreEqual(MapContext.OkinawaChapter, MapNavigationState.Current);

            MapNavigationState.Current = MapContext.JapanWorld;
            Assert.AreEqual(MapContext.JapanWorld, MapNavigationState.Current);
        }
    }
}
