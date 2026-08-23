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

        /// <summary>
        /// Ре-ревью: иконка сама объявляет transition-property (см. Map.uss,
        /// ради плавного подъёма выбранного). Без подавления перехода три
        /// голых записи смещения подряд перезапускали бы 0.18с переход
        /// заново на каждой — вместо резкого толчка отказа получилось бы
        /// вязкое покачивание, не успевающее доиграть между 70-миллисекундными
        /// кадрами. Проверяем: переход снимается ДО первой записи смещения, и
        /// финальный шаг возвращает смещение И переход ОДНИМ присваиванием —
        /// не двумя раздельными отложенными вызовами, иначе между ними будет
        /// окно, где один стиль уже отдан обратно, а другой ещё нет.
        /// </summary>
        [Test]
        public void Refusal_SuspendsTransitionBeforeShaking_AndRestoresItWithTheSameStepAsTheOffset()
        {
            string source = File.ReadAllText(SourcePath);

            int disableIndex = source.IndexOf("transitionProperty = new List<StylePropertyName>()", System.StringComparison.Ordinal);
            Assert.Greater(disableIndex, -1, "Expected the shake to disable the icon's transition before writing any offset.");

            int firstOffsetWriteIndex = source.IndexOf("new Translate(offset", System.StringComparison.Ordinal);
            Assert.Greater(firstOffsetWriteIndex, -1, "Expected a shake frame writing Translate(offset, ...).");
            Assert.Less(disableIndex, firstOffsetWriteIndex,
                "The transition must be disabled before the first shake frame is written, or that frame " +
                "would ease in over 0.18s instead of snapping.");

            // The LAST ".Execute(() =>" in the file is the final settle step
            // (the loop body contributes exactly one earlier occurrence).
            int lastExecute = source.LastIndexOf(".Execute(() =>", System.StringComparison.Ordinal);
            Assert.Greater(lastExecute, disableIndex, "Expected a final .Execute block after the disable.");
            int lastExecuteLater = source.IndexOf(".ExecuteLater(", lastExecute, System.StringComparison.Ordinal);
            Assert.Greater(lastExecuteLater, lastExecute);
            string finalStep = source.Substring(lastExecute, lastExecuteLater - lastExecute);

            StringAssert.Contains("style.translate = StyleKeyword.Null", finalStep,
                "The final step must clear the inline translate.");
            StringAssert.Contains("style.transitionProperty = StyleKeyword.Null", finalStep,
                "The final step must restore the transition IN THE SAME step as clearing the offset — " +
                "splitting them across two scheduled calls would leave a window where one is already " +
                "handed back to styles while the other still isn't.");
        }
    }
}
