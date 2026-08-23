using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Одноразовые эффекты маркера: появление каскадом, отказ у
    /// заблокированного, волна от тапа. Отдельно от MapAmbientController
    /// (непрерывное движение) и MapCeremonyController (редкие сцены) —
    /// это короткая реакция на действие игрока.
    ///
    /// <para>
    /// Пишет только transform и прозрачность, как и весь остальной
    /// анимационный код карты — см. MapNodeFeedbackSourceTests.
    /// </para>
    /// </summary>
    public static class MapNodeFeedback
    {
        /// <summary>Класс стартового состояния каскада (см. Map.uss ".map-node--enter").</summary>
        public const string EnterClass = "map-node--enter";

        /// <summary>Класс укороченного каскада при включённой настройке «меньше движения».</summary>
        public const string ReducedClass = "map-node--reduced";

        /// <summary>Задержка между соседними маркерами в каскаде.</summary>
        public const int CascadeStepMs = 70;

        /// <summary>
        /// Проигрывает появление маркеров: ставит стартовое состояние, даёт
        /// каждому свою задержку по порядку и снимает состояние следующим
        /// кадром, чтобы переход действительно проигрался (изменение стиля в
        /// том же кадре, что и добавление класса, переход не запускает).
        /// </summary>
        public static void PlayEntranceCascade(IReadOnlyList<VisualElement> nodes, bool reducedMotion)
        {
            if (nodes == null || nodes.Count == 0)
                return;

            for (int i = 0; i < nodes.Count; i++)
            {
                VisualElement node = nodes[i];
                if (node == null)
                    continue;

                VisualElement breath = FindBreath(node);
                if (breath != null)
                {
                    // Снимаем возможный инлайн-масштаб, оставшийся от
                    // ambient-драйвера (MapAmbientController.ResolveScreenElements
                    // выставляет обёртке масштаб покоя на каждом входе на экран):
                    // инлайн-стиль перебивает USS-класс ".map-node--enter" ниже
                    // независимо от порядка вызовов между двумя контроллерами, и
                    // без этой строки стартовый перелёт по масштабу мог тихо не
                    // произойти вовсе.
                    breath.style.scale = StyleKeyword.Null;
                    breath.style.transitionDelay = new List<TimeValue>
                    {
                        new TimeValue(reducedMotion ? 0 : i * CascadeStepMs, TimeUnit.Millisecond),
                    };
                }

                node.EnableInClassList(ReducedClass, reducedMotion);
                node.AddToClassList(EnterClass);
            }

            VisualElement first = nodes[0];
            first?.schedule.Execute(() =>
            {
                foreach (VisualElement node in nodes)
                    node?.RemoveFromClassList(EnterClass);
            }).ExecuteLater(0);
        }

        private static VisualElement FindBreath(VisualElement node)
        {
            VisualElement breath = node.Q<VisualElement>(className: "chapter-node__breath");
            return breath ?? node.Q<VisualElement>(className: "level-node__breath");
        }
    }
}
