using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// MapNodeFeedback — одноразовые эффекты маркера сверх непрерывного
    /// ambient-движения (сейчас — дрожь отказа у заблокированного).
    /// Источниковые тесты, как и у MapAmbientController: VisualElement.schedule
    /// требует живую панель, которой в EditMode нет, поэтому сама дрожь
    /// (доходит ли breath.style.translate до нуля на последнем кадре) этими
    /// тестами не проигрывается — только текст источника.
    /// </summary>
    public class MapNodeFeedbackSourceTests
    {
        private const string SourcePath = "Assets/UI/Map/MapNodeFeedback.cs";

        /// <remarks>
        /// Сканирование ТЕКСТА, а не синтаксического дерева: намеренно грубо,
        /// зато не требует парсера и ловит нарушение в любой форме записи.
        /// Обратная сторона — запрещённые имена нельзя упоминать даже в
        /// комментариях проверяемого файла, иначе он уронит сам себя.
        /// </remarks>
        [Test]
        public void NeverWritesLayoutProperties()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("style.left", source);
            StringAssert.DoesNotContain("style.top", source);
            StringAssert.DoesNotContain("style.width", source);
            StringAssert.DoesNotContain("style.height", source);
            StringAssert.DoesNotContain("style.margin", source);
            StringAssert.DoesNotContain("style.padding", source);
            StringAssert.DoesNotContain("style.fontSize", source);
        }

        [Test]
        public void RefusalShakesAndSettlesBackToRest()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("public static void PlayRefusal(", source);
            StringAssert.Contains("RefusalOffsets", source);
            StringAssert.Contains("RefusalStepMs", source);
        }

        /// <summary>
        /// Последний кадр дрожи обязан быть нулевым смещением — иначе маркер
        /// остаётся сдвинут и кончик пина уезжает с точки на карте. Runtime
        /// этого не проверить (см. докстринг класса), но текстовая проверка
        /// последнего литерала массива ловит будущую правку, которая уберёт
        /// этот ноль.
        /// </summary>
        [Test]
        public void RefusalOffsets_LastFrameIsZero()
        {
            string source = File.ReadAllText(SourcePath);
            int start = source.IndexOf("RefusalOffsets = {", System.StringComparison.Ordinal);
            Assert.Greater(start, -1, "Expected the RefusalOffsets array literal.");
            int end = source.IndexOf("};", start, System.StringComparison.Ordinal);
            Assert.Greater(end, start);
            string arrayText = source.Substring(start, end - start);
            int lastComma = arrayText.LastIndexOf(',');
            Assert.Greater(lastComma, -1, "Expected at least one comma-separated offset.");
            string lastToken = arrayText.Substring(lastComma + 1).Trim();
            Assert.AreEqual("0f", lastToken, "Refusal shake must settle back to zero offset, or the marker stays shifted.");
        }
    }
}
