using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Одноразовые эффекты маркера сверх непрерывного ambient-движения.
    /// Сейчас единственный — короткая дрожь отказа у заблокированного.
    /// Отдельно от MapAmbientController (непрерывное 30 Гц движение)
    /// намеренно: это разовая реакция на тап игрока, а не часть вечного
    /// цикла, и ей незачем знать про дыхание/каскад появления.
    ///
    /// <para>
    /// Пишет только transform и прозрачность, как и весь остальной
    /// анимационный код карты — см. MapNodeFeedbackSourceTests.
    /// </para>
    /// </summary>
    public static class MapNodeFeedback
    {
        /// <summary>Шаг между кадрами дрожи отказа.</summary>
        public const int RefusalStepMs = 70;

        /// <summary>
        /// Кадры дрожи в пикселях, затухающие к нулю. Массив НЕ включает
        /// финальный возврат в покой — тот не кадр со значением, а отдельный
        /// последний шаг PlayRefusal, который очищает инлайн-стиль, а не
        /// дописывает 0 (см. PlayRefusal, почему это важно).
        /// </summary>
        public static readonly float[] RefusalOffsets = { 6f, -6f, 3f };

        /// <summary>
        /// Короткая дрожь при тапе по заблокированному маркеру. До этого тап
        /// по locked не давал вообще ничего, и это читалось как «кнопка
        /// сломана», а не «сюда пока нельзя».
        ///
        /// <para>
        /// Пишется на иконку (__icon), а не на узел (у него translate занят
        /// привязкой кончика пина) и не на обёртку дыхания (__breath) — ту
        /// каждый тик переписывает MapAmbientController.TickMarkers, ПОКА
        /// НЕ ЗАВЕРШЁН каскад появления маркеров (до ~0.88с после показа
        /// экрана у последнего маркера Окинавы), и тап по заблокированному
        /// в это окно ничем не запрещён — второй писатель на том же
        /// translate обёртки гонялся бы с тиком и дёргался. У иконки
        /// инлайна не пишет НИКТО, кроме этого метода: TickMarkers её
        /// вообще не трогает (пишет только breath/shadow), а состояние
        /// выбора/блокировки на иконке — целиком USS
        /// (.chapter-node--selected .chapter-node__icon,
        /// .chapter-node--locked.chapter-node--selected .chapter-node__icon)
        /// без единой инлайн-записи с рантайма — переписывать некому.
        /// </para>
        ///
        /// <para>
        /// Последний шаг НЕ дописывает 0, а ОЧИЩАЕТ инлайн-стиль
        /// (StyleKeyword.Null) — тот же приём, что и в задаче 9 для
        /// обёртки дыхания. Инлайн-стиль побеждает USS навсегда, а не на
        /// время: запиши он буквальный 0 и остановись на этом, приподнимание
        /// выбранного через USS оказалось бы у этого маркера заблокировано
        /// намертво даже после разблокировки — инлайновый 0 продолжал бы
        /// побеждать правило подъёма, и снаружи это выглядело бы просто как
        /// «подъём не работает», без единой ошибки. Очистка возвращает
        /// иконку в исключительное владение USS, которое само посчитает
        /// актуальное значение по текущим классам узла (покой, подъём
        /// выбранного или сброс у заблокированного выбранного — какое из
        /// правил применимо на момент завершения дрожи).
        /// </para>
        /// </summary>
        public static void PlayRefusal(VisualElement node)
        {
            VisualElement icon = node == null ? null : FindIcon(node);
            if (icon == null)
                return;

            for (int i = 0; i < RefusalOffsets.Length; i++)
            {
                float offset = RefusalOffsets[i];
                icon.schedule
                    .Execute(() => icon.style.translate = new Translate(offset, 0f))
                    .ExecuteLater(i * RefusalStepMs);
            }

            icon.schedule
                .Execute(() => icon.style.translate = StyleKeyword.Null)
                .ExecuteLater(RefusalOffsets.Length * RefusalStepMs);
        }

        private static VisualElement FindIcon(VisualElement node)
        {
            VisualElement icon = node.Q<VisualElement>(className: "chapter-node__icon");
            return icon ?? node.Q<VisualElement>(className: "level-node__icon");
        }
    }
}
