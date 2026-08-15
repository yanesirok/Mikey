using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Scene-wiring contract for MapPanZoomController: the "UI" GameObject
    /// carries one instance configured for the Japan world map ("map") and one
    /// configured for the Okinawa chapter map ("mapOkinawa"), so pan/zoom
    /// actually runs on both screens in a real build.
    /// </summary>
    public class MapPanZoomControllerSceneTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [Test]
        public void UiGameObject_HasOneMapPanZoomControllerPerMapScreen()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Assert.IsTrue(scene.IsValid(), $"Could not open {ScenePath}");

            GameObject ui = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == "UI")
                {
                    ui = go;
                    break;
                }
            }
            Assert.IsNotNull(ui, "Scene must contain a root GameObject named 'UI'.");

            MapPanZoomController[] controllers = ui.GetComponents<MapPanZoomController>();
            Assert.AreEqual(2, controllers.Length,
                "Expected exactly two MapPanZoomController instances: one for the Japan map, one for the Okinawa map.");

            bool foundJapan = false;
            bool foundOkinawa = false;
            foreach (MapPanZoomController controller in controllers)
            {
                var so = new SerializedObject(controller);
                string screenId = so.FindProperty("screenId").stringValue;
                string viewport = so.FindProperty("viewportElementName").stringValue;
                string canvas = so.FindProperty("canvasElementName").stringValue;

                if (screenId == "map")
                {
                    foundJapan = true;
                    Assert.AreEqual("map-stage", viewport);
                    Assert.AreEqual("map-canvas", canvas);
                }
                else if (screenId == "mapOkinawa")
                {
                    foundOkinawa = true;
                    Assert.AreEqual("okinawa-stage", viewport);
                    Assert.AreEqual("okinawa-canvas", canvas);
                }
            }

            Assert.IsTrue(foundJapan, "One MapPanZoomController must be configured for the 'map' screen.");
            Assert.IsTrue(foundOkinawa, "One MapPanZoomController must be configured for the 'mapOkinawa' screen.");
        }
    }
}
