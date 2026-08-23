using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Одноразовые эффекты маркера сверх непрерывного ambient-движения:
    /// короткая дрожь отказа у заблокированного (PlayRefusal) и волна от
    /// тапа у ДОСТУПНОГО (PlayRipple) — ровно один из двух на один тап,
    /// никогда оба сразу. Волна — подтверждение СОСТОЯВШЕГОСЯ входа, а у
    /// заблокированного входа не происходит, поэтому она там не играет:
    /// вызывающий код (JapanMapController.SelectChapter,
    /// OkinawaMapController.SelectLevel) гейтит PlayRipple тем же признаком
    /// блокировки, которым чуть раньше в пути тапа решается PlayRefusal.
    /// Эти два вызова НЕЛЬЗЯ унифицировать в один безусловный — у
    /// заблокированного волна+звук читались бы как «принято, засчитано»
    /// одновременно с дрожью «нельзя», два противоречащих сигнала одним
    /// тапом (было так до ре-ревью, см. RippleAndSealStamp_OnlyPlayForAn...
    /// в JapanMapControllerSourceTests/OkinawaMapControllerSourceTests).
    /// Отдельно от MapAmbientController (непрерывное 30 Гц движение)
    /// намеренно: это разовые реакции на тап игрока, а не часть вечного
    /// цикла, и им незачем знать про дыхание/каскад появления.
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
        /// Иконка САМА объявляет transition-property: translate, scale
        /// (см. Map.uss — ради плавного подъёма выбранного). Три голых
        /// записи смещения подряд перезапускали бы этот 0.18с переход
        /// заново на каждой, и вместо резкого толчка отказа получилось бы
        /// вязкое покачивание, не успевающее доиграть между кадрами.
        /// Поэтому первым делом, синхронно и ДО первой записи смещения,
        /// переход снимается инлайном (пустой список transitionProperty —
        /// то же самое, что USS "transition-property: none"); кадры дрожи
        /// после этого применяются мгновенно, без интерполяции.
        /// </para>
        ///
        /// <para>
        /// Последний шаг НЕ дописывает 0, а ОДНИМ присваиванием ОЧИЩАЕТ
        /// СРАЗУ ДВА инлайн-стиля — и смещение, и сам переход (тот же
        /// приём, что и в задаче 9 для обёртки дыхания). Оба в одном шаге,
        /// а не в двух последовательных: разнеси их по разным кадрам — и
        /// между ними будет окно, где одно уже отдано стилям, а другое ещё
        /// нет (либо мгновенный скачок вместо плавного возврата подъёма,
        /// либо переход играет из точки дрожи, которую сам же обнулил).
        /// Инлайн-стиль побеждает USS навсегда, а не на время: запиши он
        /// буквальный 0 и остановись на этом, приподнимание выбранного
        /// через USS оказалось бы у этого маркера заблокировано намертво
        /// даже после разблокировки — инлайновый 0 продолжал бы побеждать
        /// правило подъёма, и снаружи это выглядело бы просто как «подъём
        /// не работает», без единой ошибки. Очистка возвращает иконку в
        /// исключительное владение USS, которое само посчитает актуальное
        /// значение по текущим классам узла (покой, подъём выбранного или
        /// сброс у заблокированного выбранного — какое из правил применимо
        /// на момент завершения дрожи) и само же плавно доедет туда своим
        /// восстановленным переходом.
        /// </para>
        /// </summary>
        public static void PlayRefusal(VisualElement node)
        {
            VisualElement icon = node == null ? null : FindIcon(node);
            if (icon == null)
                return;

            icon.style.transitionProperty = new List<StylePropertyName>();

            for (int i = 0; i < RefusalOffsets.Length; i++)
            {
                float offset = RefusalOffsets[i];
                icon.schedule
                    .Execute(() => icon.style.translate = new Translate(offset, 0f))
                    .ExecuteLater(i * RefusalStepMs);
            }

            icon.schedule
                .Execute(() =>
                {
                    icon.style.translate = StyleKeyword.Null;
                    icon.style.transitionProperty = StyleKeyword.Null;
                })
                .ExecuteLater(RefusalOffsets.Length * RefusalStepMs);
        }

        private static VisualElement FindIcon(VisualElement node)
        {
            VisualElement icon = node.Q<VisualElement>(className: "chapter-node__icon");
            return icon ?? node.Q<VisualElement>(className: "level-node__icon");
        }

        /// <summary>Пиковая прозрачность кольца в начале волны.</summary>
        public const float RipplePeakOpacity = 0.45f;

        /// <summary>
        /// Сколько кольцо катится и гаснет, в мс — чуть больше объявленного
        /// в Map.uss transition-duration (0.5с), чтобы уборка не срезала
        /// последний кадр перехода.
        /// </summary>
        private const int RippleDurationMs = 520;

        /// <summary>
        /// Волна от тапа по маркеру — читается как «тап принят, вход
        /// состоялся». Играет ТОЛЬКО у доступного маркера: у заблокированного
        /// входа не происходит, поэтому там играет дрожь отказа выше, а не
        /// волна — метод сам не знает про блокировку и ничего не проверяет,
        /// гейт стоит на стороне вызывающего (JapanMapController.SelectChapter,
        /// OkinawaMapController.SelectLevel — оба вызывают PlayRipple, только
        /// когда узел не заблокирован, тем же признаком, которым решается
        /// PlayRefusal). НЕ звать безусловно из одного места с PlayRefusal —
        /// тогда заблокированный маркер получал бы одновременно «нельзя»
        /// (дрожь) и «принято, засчитано» (волна+звук печати), два
        /// противоречащих сигнала одним тапом.
        ///
        /// <para>
        /// Кольцо (__ripple) — новый элемент, ничей больше: ambient-тик
        /// (MapAmbientController.TickMarkers) пишет только __breath и
        /// __shadow, дрожь отказа выше пишет только __icon — писателей на
        /// __ripple, кроме этого метода, нет.
        /// </para>
        ///
        /// <para>
        /// Кольцо САМО объявляет transition-property: opacity, scale (см.
        /// Map.uss) — это правило действует ВСЕГДА, а не только при
        /// добавленном классе rippling, потому что более специфичный
        /// селектор .rippling не переобъявляет transition-property и
        /// каскад берёт значение из базового правила. Значит голая запись
        /// пикового opacity/scale ниже сама уехала бы 0.5с вместо
        /// мгновенного скачка — поэтому переход сначала снимается инлайном
        /// (тот же приём, что и в дрожи отказа выше), и лишь после
        /// мгновенного скачка в пик возвращается на следующем кадре, когда
        /// уже пора плавно ехать к расширенному прозрачному состоянию.
        /// </para>
        ///
        /// <para>
        /// Повторный тап до конца предыдущей волны: <see cref="VisualElement.userData"/>
        /// кольца хранит счётчик поколения. Каждый новый вызов увеличивает
        /// его и синхронно перехватывает кольцо (снимает переход, гасит
        /// класс, прыгает в пик) НЕМЕДЛЕННО — даже если предыдущая волна
        /// ещё не доиграла. Оба запланированных шага предыдущего вызова
        /// сверяют текущее поколение перед тем как что-то писать и, если
        /// оно уже другое, молча не делают ничего. Без этой сверки
        /// отложенная уборка старого тапа (снятие класса на 520мс СТАРОГО
        /// вызова) могла бы сработать посреди перехода нового — оборвав его
        /// расширение на середине пути и дав видимый скачок обратно к
        /// маленькому кольцу.
        /// </para>
        /// </summary>
        public static void PlayRipple(VisualElement node)
        {
            if (node == null)
                return;

            bool chapter = node.ClassListContains("chapter-node");
            string rippleClass = chapter ? "chapter-node__ripple" : "level-node__ripple";
            string activeClass = chapter ? "chapter-node--rippling" : "level-node--rippling";

            VisualElement ripple = node.Q<VisualElement>(className: rippleClass);
            if (ripple == null)
                return;

            int generation = (ripple.userData is int currentGeneration ? currentGeneration : 0) + 1;
            ripple.userData = generation;

            ripple.style.transitionProperty = new List<StylePropertyName>();
            node.RemoveFromClassList(activeClass);
            ripple.style.opacity = RipplePeakOpacity;
            ripple.style.scale = new Scale(new Vector2(0.4f, 0.4f));

            ripple.schedule
                .Execute(() =>
                {
                    if (!(ripple.userData is int g) || g != generation)
                        return;
                    ripple.style.opacity = StyleKeyword.Null;
                    ripple.style.scale = StyleKeyword.Null;
                    ripple.style.transitionProperty = StyleKeyword.Null;
                    node.AddToClassList(activeClass);
                })
                .ExecuteLater(0);

            ripple.schedule
                .Execute(() =>
                {
                    if (!(ripple.userData is int g) || g != generation)
                        return;
                    node.RemoveFromClassList(activeClass);
                })
                .ExecuteLater(RippleDurationMs);
        }
    }
}
