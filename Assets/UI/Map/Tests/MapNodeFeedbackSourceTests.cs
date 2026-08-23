using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
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
        private const string UssPath = "Assets/UI/Map/Map.uss";

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

            // Bounded to PlayRefusal's own body (same span as
            // RefusalSettlesByClearingInlineStyle_NotByWritingLiteralZero
            // above): MapNodeFeedback now also has PlayRipple, which
            // legitimately contains its OWN later ".Execute(() =>" blocks —
            // an unbounded scan of the whole file would find one of those
            // instead of PlayRefusal's final settle step.
            int methodStart = source.IndexOf("public static void PlayRefusal(", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1, "Expected PlayRefusal in the source.");
            int methodEnd = source.IndexOf("private static VisualElement FindIcon", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(methodEnd, methodStart, "Expected FindIcon right after PlayRefusal.");

            int disableIndex = source.IndexOf("transitionProperty = new List<StylePropertyName>()", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(disableIndex, -1, "Expected the shake to disable the icon's transition before writing any offset.");
            Assert.Less(disableIndex, methodEnd, "Expected the disable inside PlayRefusal.");

            int firstOffsetWriteIndex = source.IndexOf("new Translate(offset", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(firstOffsetWriteIndex, -1, "Expected a shake frame writing Translate(offset, ...).");
            Assert.Less(disableIndex, firstOffsetWriteIndex,
                "The transition must be disabled before the first shake frame is written, or that frame " +
                "would ease in over 0.18s instead of snapping.");

            // The LAST ".Execute(() =>" WITHIN PlayRefusal's own body (up to
            // methodEnd) is its final settle step (the loop body contributes
            // exactly one earlier occurrence).
            int lastExecute = source.LastIndexOf(".Execute(() =>", methodEnd, System.StringComparison.Ordinal);
            Assert.Greater(lastExecute, disableIndex, "Expected a final .Execute block after the disable.");
            int lastExecuteLater = source.IndexOf(".ExecuteLater(", lastExecute, System.StringComparison.Ordinal);
            Assert.Greater(lastExecuteLater, lastExecute);
            Assert.Less(lastExecuteLater, methodEnd, "Expected the final step to still be inside PlayRefusal.");
            string finalStep = source.Substring(lastExecute, lastExecuteLater - lastExecute);

            StringAssert.Contains("style.translate = StyleKeyword.Null", finalStep,
                "The final step must clear the inline translate.");
            StringAssert.Contains("style.transitionProperty = StyleKeyword.Null", finalStep,
                "The final step must restore the transition IN THE SAME step as clearing the offset — " +
                "splitting them across two scheduled calls would leave a window where one is already " +
                "handed back to styles while the other still isn't.");
        }

        /// <summary>
        /// Кольцо (chapter-node__ripple / level-node__ripple) само объявляет
        /// transition-property: opacity, scale в Map.uss на БАЗОВОМ правиле —
        /// оно действует всегда, а не только при добавленном классе
        /// rippling. Без временного снятия перехода мгновенный прыжок в
        /// пиковое состояние сам уехал бы 0.5с вместо появления мгновенно —
        /// та же ловушка, что и у дрожи отказа (ProTip задачи 9/10), теперь
        /// применительно к opacity/scale вместо translate.
        /// </summary>
        [Test]
        public void Ripple_DisablesTransitionBeforeJumpingToPeak()
        {
            string source = File.ReadAllText(SourcePath);

            int methodStart = source.IndexOf("public static void PlayRipple(", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1, "Expected PlayRipple in the source.");

            int disableIndex = source.IndexOf("transitionProperty = new List<StylePropertyName>()", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(disableIndex, -1, "Expected PlayRipple to disable the ring's transition before writing the peak opacity/scale.");

            int peakOpacityWriteIndex = source.IndexOf("style.opacity = RipplePeakOpacity", methodStart, System.StringComparison.Ordinal);
            Assert.Greater(peakOpacityWriteIndex, -1, "Expected PlayRipple to write the peak opacity.");
            Assert.Less(disableIndex, peakOpacityWriteIndex,
                "The transition must be disabled before the peak opacity/scale jump, or that jump would " +
                "ease in over 0.5s instead of appearing instantly.");
        }

        /// <summary>
        /// Быстрый повторный тап до конца предыдущей волны: без какой-то
        /// защиты отложенная уборка СТАРОГО вызова (снятие класса rippling)
        /// могла бы сработать посреди перехода НОВОГО, оборвав его на
        /// середине пути — видимый скачок кольца обратно к маленькому.
        /// PlayRipple защищается счётчиком поколения на
        /// <see cref="System.Object"/> ripple.userData: каждый вызов
        /// увеличивает счётчик, и оба отложенных шага сверяют его перед тем
        /// как что-то менять. Runtime это не проверить (см. докстринг
        /// класса — schedule требует живую панель), поэтому по тексту.
        /// </summary>
        [Test]
        public void Ripple_GuardsAgainstStaleScheduledStepsFromAnEarlierTap()
        {
            string source = File.ReadAllText(SourcePath);

            int methodStart = source.IndexOf("public static void PlayRipple(", System.StringComparison.Ordinal);
            Assert.Greater(methodStart, -1, "Expected PlayRipple in the source.");
            string body = source.Substring(methodStart);

            StringAssert.Contains("ripple.userData", body,
                "Expected PlayRipple to track a per-ripple generation token via userData.");

            int firstGuard = body.IndexOf("g != generation", System.StringComparison.Ordinal);
            int secondGuard = body.IndexOf("g != generation", firstGuard + 1, System.StringComparison.Ordinal);
            Assert.Greater(firstGuard, -1, "Expected the delayed 'start transition' step to bail out if a newer tap superseded it.");
            Assert.Greater(secondGuard, -1,
                "Expected the delayed 'remove class' cleanup step to ALSO bail out if a newer tap superseded it — " +
                "otherwise an earlier tap's cleanup could fire mid-transition on a later tap and cut its expansion short.");
        }

        /// <summary>
        /// Ре-ревью: RippleDurationMs (уборка — снятие --rippling) обязан
        /// оставаться СТРОГО больше transition-duration кольца в Map.uss, а
        /// не просто совпадать по счастливой случайности двух захардкоженных
        /// чисел. Если одно из них поедет без другого, запас молча
        /// схлопнется или станет отрицательным — и вернётся ровно тот баг,
        /// который чинит счётчик поколения выше: уборка прилетает посреди
        /// ещё не доигравшего перехода, обрывая расширение кольца видимым
        /// скачком. Числа читаются из обоих файлов по тексту (тот же приём,
        /// что и во всём этом классе), а не задаются в тесте по памяти — так
        /// расхождение ловится в любую сторону, а не только когда кто-то
        /// не обновит тест заодно с константой.
        /// </summary>
        [Test]
        public void RippleDurationMs_StaysStrictlyGreaterThanTheUssTransitionDuration()
        {
            string uss = File.ReadAllText(UssPath);
            int rippleRuleStart = uss.IndexOf(".chapter-node__ripple,", System.StringComparison.Ordinal);
            Assert.Greater(rippleRuleStart, -1, "Expected the ripple rule in Map.uss.");
            int rippleRuleEnd = uss.IndexOf('}', rippleRuleStart);
            Assert.Greater(rippleRuleEnd, rippleRuleStart);
            string rippleRule = uss.Substring(rippleRuleStart, rippleRuleEnd - rippleRuleStart);

            Match durationMatch = Regex.Match(rippleRule, @"transition-duration:\s*([\d.]+)s;");
            Assert.IsTrue(durationMatch.Success, "Expected a 'transition-duration: <N>s;' declaration on the ripple rule.");
            float ussDurationMs = float.Parse(durationMatch.Groups[1].Value, CultureInfo.InvariantCulture) * 1000f;

            string source = File.ReadAllText(SourcePath);
            Match constMatch = Regex.Match(source, @"RippleDurationMs\s*=\s*(\d+);");
            Assert.IsTrue(constMatch.Success, "Expected 'RippleDurationMs = <N>;' in MapNodeFeedback.cs.");
            int rippleDurationMs = int.Parse(constMatch.Groups[1].Value, CultureInfo.InvariantCulture);

            Assert.Greater(rippleDurationMs, ussDurationMs,
                "RippleDurationMs must stay strictly greater than the ring's USS transition-duration — " +
                "otherwise the delayed cleanup fires before the transition finishes, cutting the ring's " +
                "expansion short with a visible snap-back.");
        }
    }
}
