using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// MapNodeFeedback — одноразовые эффекты маркера сверх непрерывного
    /// ambient-движения (сейчас — дрожь отказа у заблокированного).
    /// Источниковые тесты, как и у MapAmbientController: VisualElement.schedule
    /// требует живую панель, которой в EditMode нет, поэтому сама дрожь
    /// (доходит ли icon.style.translate до очистки на последнем шаге) этими
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
        /// Ре-ревью задачи 10: последний шаг дрожи обязан ОЧИЩАТЬ инлайн-стиль
        /// (StyleKeyword.Null), а не дописывать буквальный 0 — иконкой
        /// владеет USS (подъём выбранного, сброс у заблокированного
        /// выбранного), и инлайн-ноль перебил бы эти правила НАВСЕГДА, а не
        /// на время дрожи: маркер, который потом разблокируется, никогда не
        /// поднялся бы при выборе, и снаружи это выглядело бы просто как
        /// «подъём не работает», без единой ошибки. Runtime это не проверить
        /// (см. докстринг класса), но текстовая проверка ловит будущую
        /// правку, которая вернёт запись нуля вместо очистки — тот самый
        /// регресс, который поймало ре-ревью.
        /// </summary>
        [Test]
        public void RefusalSettlesByClearingInlineStyle_NotByWritingLiteralZero()
        {
            string source = File.ReadAllText(SourcePath);
            int start = source.IndexOf("public static void PlayRefusal(", System.StringComparison.Ordinal);
            Assert.Greater(start, -1, "Expected PlayRefusal in the source.");
            int end = source.IndexOf("private static VisualElement FindIcon", start, System.StringComparison.Ordinal);
            Assert.Greater(end, start, "Expected FindIcon right after PlayRefusal.");
            string body = source.Substring(start, end - start);

            StringAssert.Contains("StyleKeyword.Null", body,
                "The last shake step must clear the inline translate by assigning StyleKeyword.Null, " +
                "not write a literal zero Translate — a literal zero would out-live the shake and " +
                "permanently block the USS selected-lift rule once the marker unlocks.");
        }

        /// <summary>
        /// Дрожь обязана писать иконку — единственный элемент маркера, чей
        /// translate не переписывается ambient-тиком (TickMarkers трогает
        /// только обёртку дыхания и тень, а каскад появления пишет обёртку
        /// ещё почти секунду после показа экрана, пока тап уже возможен).
        /// Проверяем по конкретному коду записи (`icon.style.translate =`),
        /// а не по слову "breath" целиком — то слово легитимно встречается в
        /// прозе докстринга, объясняющей именно этот выбор, и текстовый
        /// сторож не должен ронять сам себя за собственное объяснение.
        /// </summary>
        [Test]
        public void Refusal_WritesTheIcon_NotTheBreathWrapper()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("chapter-node__icon", source);
            StringAssert.Contains("level-node__icon", source);
            StringAssert.Contains("icon.style.translate =", source);
            StringAssert.DoesNotContain("breath.style.translate =", source,
                "The shake must not target the breath wrapper — TickMarkers keeps writing its " +
                "translate for up to ~0.88s into the entrance cascade, while a tap is already possible.");
        }
    }
}
