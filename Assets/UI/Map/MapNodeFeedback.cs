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
        /// Кадры дрожи в пикселях. Затухающие и заканчиваются нулём: маркер
        /// обязан вернуться ровно на своё место, иначе кончик пина уедет с
        /// точки на карте.
        /// </summary>
        public static readonly float[] RefusalOffsets = { 6f, -6f, 3f, 0f };

        /// <summary>
        /// Короткая дрожь при тапе по заблокированному маркеру. До этого тап
        /// по locked не давал вообще ничего, и это читалось как «кнопка
        /// сломана», а не «сюда пока нельзя».
        ///
        /// <para>
        /// Пишется на обёртку дыхания (__breath), а не на узел (у него
        /// translate занят привязкой кончика пина) и не на иконку (там живёт
        /// USS-состояние выбора — приподнимание при .chapter-node--selected /
        /// .level-node--selected). Заблокированный маркер у ambient-драйвера
        /// не «alive» (MapAmbientController.TickMarkers) — он не дышит, а
        /// его вход в покой доводится один раз и дальше __breath.style.translate
        /// вообще не трогается (см. _markerEntranceSettled в TickMarkers), так
        /// что во время самого тряски записи с ambient-тиком не пересекаются.
        /// </para>
        /// </summary>
        public static void PlayRefusal(VisualElement node)
        {
            VisualElement breath = node == null ? null : FindBreath(node);
            if (breath == null)
                return;

            for (int i = 0; i < RefusalOffsets.Length; i++)
            {
                float offset = RefusalOffsets[i];
                breath.schedule
                    .Execute(() => breath.style.translate = new Translate(offset, 0f))
                    .ExecuteLater(i * RefusalStepMs);
            }
        }

        private static VisualElement FindBreath(VisualElement node)
        {
            VisualElement breath = node.Q<VisualElement>(className: "chapter-node__breath");
            return breath ?? node.Q<VisualElement>(className: "level-node__breath");
        }
    }
}
