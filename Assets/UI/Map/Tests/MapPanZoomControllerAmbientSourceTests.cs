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

        [Test]
        public void ResetTransform_MarksInput_SoTheIdleClockRestartsOnFreshScreenEntry()
        {
            // Находка 1 ревью: _lastInputTime по умолчанию 0 и без сброса на
            // входе на экран отсчитывался бы от запуска приложения, а не от
            // появления карты — простой давно "истёк" бы к первому тику.
            string source = File.ReadAllText(ControllerPath);
            string body = ExtractMethodBody(source, "private void ResetTransform()");
            StringAssert.Contains("MarkInput();", body,
                "ResetTransform must reset the idle clock via MarkInput(), or SecondsSinceLastInput counts from app launch instead of screen entry.");
        }

        [Test]
        public void OnPointerMove_MarksInputUnconditionally_NotOnlyAtTheDragThresholdCrossing()
        {
            // Находка 2 ревью: непрерывный пан длиннее IdleDelaySeconds должен
            // не давать Ken Burns включиться под пальцем — значит MarkInput()
            // обязан стоять ВНЕ (до) ветки "if (!_dragging)", а не только внутри неё.
            string source = File.ReadAllText(ControllerPath);
            string body = ExtractMethodBody(source, "private void OnPointerMove(PointerMoveEvent evt)");

            int dragBranchIndex = body.IndexOf("if (!_dragging)", System.StringComparison.Ordinal);
            Assert.Greater(dragBranchIndex, -1, "Expected the drag-threshold-crossing branch.");

            int markInputIndex = body.IndexOf("MarkInput();", System.StringComparison.Ordinal);
            Assert.Greater(markInputIndex, -1, "Expected a MarkInput() call in OnPointerMove.");
            Assert.Less(markInputIndex, dragBranchIndex,
                "MarkInput() must fire unconditionally before the threshold-crossing branch, on every drag frame — not only once, when the drag threshold is first crossed.");
        }

        [Test]
        public void Update_MarksInputOnPinchContinuation_NotJustAtPinchStart()
        {
            // Находка 2 ревью, вторая половина: пинч длиннее IdleDelaySeconds
            // должен так же не пускать Ken Burns под пальцами — MarkInput()
            // обязан стоять и в ветке продолжения пинча, не только в её старте.
            string source = File.ReadAllText(ControllerPath);
            string body = ExtractMethodBody(source, "private void Update()");

            int continuationStart = body.IndexOf("if (_pinchStartDistance > 0.01f)", System.StringComparison.Ordinal);
            Assert.Greater(continuationStart, -1, "Expected the pinch-continuation branch.");
            int setZoomIndex = body.IndexOf("SetZoom(_pinchStartZoom * ratio);", continuationStart, System.StringComparison.Ordinal);
            Assert.Greater(setZoomIndex, continuationStart, "Expected the pinch-continuation SetZoom call.");

            string continuationBranch = body.Substring(continuationStart, setZoomIndex - continuationStart);
            StringAssert.Contains("MarkInput();", continuationBranch,
                "MarkInput() must be called on every pinch-continuation frame, not only at pinch start — otherwise a pinch longer than IdleDelaySeconds crosses the idle threshold mid-gesture.");
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

        /// <summary>
        /// Извлекает тело метода по его сигнатуре, считая глубину фигурных
        /// скобок — а не наивным поиском первой "}": у Update() и подобных
        /// методов внутри есть вложенные блоки (if/for), и первая же "}"
        /// закрыла бы вложенный блок, а не сам метод.
        /// </summary>
        private static string ExtractMethodBody(string source, string methodSignature)
        {
            int signatureIndex = source.IndexOf(methodSignature, System.StringComparison.Ordinal);
            Assert.Greater(signatureIndex, -1, $"Expected to find '{methodSignature}'.");

            int braceOpen = source.IndexOf('{', signatureIndex);
            int depth = 0;
            int i = braceOpen;
            for (; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        break;
                }
            }
            return source.Substring(braceOpen, i - braceOpen + 1);
        }
    }
}
