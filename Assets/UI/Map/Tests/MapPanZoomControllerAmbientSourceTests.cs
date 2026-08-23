using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Трансформацию ".pan-canvas" пишет ровно один класс —
    /// MapPanZoomController. Ambient-слой отдаёт ему аддитивное смещение и
    /// множитель зума через SetAmbientOffset, а не пишет канвасу сам: два
    /// писателя одной трансформации дрались бы за неё каждый кадр.
    /// </summary>
    public class MapPanZoomControllerAmbientSourceTests
    {
        private const string ControllerPath = "Assets/UI/Map/MapPanZoomController.cs";
        private const string AmbientPath = "Assets/UI/Map/MapAmbientController.cs";

        [Test]
        public void PanZoomControllerAcceptsAnAmbientOffset()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("public void SetAmbientOffset(", source);
            StringAssert.Contains("public float SecondsSinceLastInput", source);
        }

        [Test]
        public void PanZoomControllerFunnelsEveryWriteThroughOneMethod()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("private void ApplyCanvasTransform()", source);
            Assert.AreEqual(1, CountOccurrences(source, "_canvas.transform.position ="),
                "Позиция канваса должна писаться ровно в одном месте.");
            Assert.AreEqual(1, CountOccurrences(source, "_canvas.transform.scale ="),
                "Масштаб канваса должен писаться ровно в одном месте.");
        }

        [Test]
        public void AmbientControllerNeverTouchesTheCanvasTransformItself()
        {
            string source = File.ReadAllText(AmbientPath);
            StringAssert.DoesNotContain("_canvas.transform", source);
            StringAssert.Contains("SetAmbientOffset(", source);
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int index = haystack.IndexOf(needle, System.StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = haystack.IndexOf(needle, index + needle.Length, System.StringComparison.Ordinal);
            }
            return count;
        }
    }
}
