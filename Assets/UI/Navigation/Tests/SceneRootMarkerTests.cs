using NUnit.Framework;
using UnityEngine;
using Mikey.UI.Navigation;

namespace Mikey.UI.Navigation.Tests
{
    public class SceneRootMarkerTests
    {
        [Test]
        public void Marker_ExposesSerializedScreenId()
        {
            var go = new GameObject("marker-test");
            try
            {
                var marker = go.AddComponent<SceneRootMarker>();
                marker.SetScreenIdForTests("practice");
                Assert.AreEqual("practice", marker.ScreenId);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
