namespace Mikey.UI.Map
{
    /// <summary>
    /// Чистая математика непрерывного движения карты, свободная от
    /// UnityEngine и UI Toolkit — ровно по тем же причинам, что и
    /// <see cref="MapPanZoomMath"/>: всё считаемое должно проверяться
    /// настоящим EditMode-тестом, а не чтением текста контроллера.
    ///
    /// <para>
    /// Все амплитуды заданы долями от размера канваса, а не пикселями: карта
    /// живёт на телефонах с очень разной плотностью, и движение «на 12
    /// пикселей» на разных экранах читалось бы по-разному.
    /// </para>
    /// </summary>
    public static class MapAmbientMath
    {
        /// <summary>Число декоративных облаков на экран — совпадает с числом элементов в MapCloudLayout.</summary>
        public const int CloudCount = 4;

        /// <summary>Доля ширины канваса, на которую облако рамки уходит от своей раскладки по горизонтали. 0.035, а не прежние 0.012: ниже примерно трёх процентов движение просто не различается глазом, и небо читается мёртвым.</summary>
        public const float DriftAmplitudeX = 0.035f;

        /// <summary>Доля высоты канваса по вертикали — вдвое меньше горизонтальной: небо читается как боковой снос, а не как качели.</summary>
        public const float DriftAmplitudeY = 0.016f;

        /// <summary>На сколько прозрачность облака уходит от своего значения покоя из MapCloudLayout.</summary>
        public const float DriftOpacityAmplitude = 0.07f;

        /// <summary>
        /// Периоды дрейфа по облакам. Взаимно непериодичны специально: с
        /// кратными периодами композиция возвращалась бы в одну и ту же точку
        /// заметным циклом, и небо читалось бы как зацикленная гифка.
        /// </summary>
        public static readonly float[] CloudDriftPeriodsSeconds = { 26f, 31f, 37f, 43f };

        /// <summary>Фазы, чтобы облака не стартовали из одной точки и не шли строем.</summary>
        public static readonly float[] CloudDriftPhases = { 0f, 0.37f, 0.61f, 0.83f };

        /// <summary>Единичная синусоида в [-1, 1]. Небезопасный ввод (нулевой/отрицательный период, NaN) даёт 0 — покой, а не рывок.</summary>
        public static float Wave(float timeSeconds, float periodSeconds, float phase01)
        {
            if (!IsFinite(timeSeconds) || !IsFinite(periodSeconds) || periodSeconds <= 0f)
                return 0f;

            float phase = IsFinite(phase01) ? phase01 : 0f;
            double angle = (timeSeconds / periodSeconds + phase) * 2.0 * System.Math.PI;
            return (float)System.Math.Sin(angle);
        }

        /// <summary>
        /// Множитель «дыхания» в [1, 1 + amplitude], равный ровно 1 в нуле
        /// времени: анимация обязана стартовать из состояния покоя, иначе
        /// первый кадр после показа экрана даёт скачок.
        /// </summary>
        public static float Breath(float timeSeconds, float periodSeconds, float amplitude)
        {
            if (!IsFinite(amplitude))
                return 1f;
            return 1f + amplitude * 0.5f * (1f - (float)System.Math.Cos(WaveAngle(timeSeconds, periodSeconds)));
        }

        /// <summary>Период второй синусоиды составной траектории как доля основного периода.</summary>
        public const float CompoundRatio = 0.377f;

        /// <summary>Вес второй синусоиды.</summary>
        public const float CompoundWeight = 0.42f;

        /// <summary>
        /// Составная траектория: вторая синусоида с несоизмеримым периодом
        /// поверх первой. Одна синусоида возвращается в ту же точку ровно
        /// через период, и небо читается зацикленной гифкой.
        ///
        /// <para>
        /// Нормировка на <c>1 + CompoundWeight</c> обязательна: без неё сумма
        /// двух синусоид выходит за заявленную амплитуду, и рамка уезжает
        /// дальше, чем разрешено композицией.
        /// </para>
        /// </summary>
        public static float Compound(float timeSeconds, float periodSeconds, float phase01)
        {
            float primary = Wave(timeSeconds, periodSeconds, phase01);
            float secondary = Wave(timeSeconds, periodSeconds * CompoundRatio, phase01);
            return (primary + CompoundWeight * secondary) / (1f + CompoundWeight);
        }

        /// <summary>За сколько секунд движение рамки набирает полную амплитуду после показа экрана.</summary>
        public const float DriftSettleSeconds = 1.5f;

        /// <summary>
        /// Множитель ввода рамки в движение, 0 в нуле времени и 1 после
        /// <see cref="DriftSettleSeconds"/>.
        ///
        /// <para>
        /// Нужен потому, что фазы <see cref="CloudDriftPhases"/> ненулевые: при
        /// t = 0 синусоида уже не в нуле, и для фазы 0.37 это около 0.73
        /// амплитуды. На прежних 0.012 ширины это были невидимые 0.9%; на
        /// нынешних 0.035 — заметный скачок на первом же кадре после показа
        /// экрана. Отказаться от фаз нельзя: без них четыре облака стартуют
        /// строем.
        /// </para>
        /// </summary>
        public static float DriftSettle(float timeSeconds)
        {
            if (!IsFinite(timeSeconds) || timeSeconds <= 0f)
                return 0f;
            if (timeSeconds >= DriftSettleSeconds)
                return 1f;

            return MapPanZoomMath.EaseOutCubic(timeSeconds / DriftSettleSeconds);
        }

        /// <summary>Смещение и добавка к прозрачности одного облака относительно его раскладки покоя.</summary>
        public static void CloudDrift(int index, float timeSeconds, float canvasWidth, float canvasHeight,
            out float offsetX, out float offsetY, out float opacityDelta)
        {
            offsetX = 0f;
            offsetY = 0f;
            opacityDelta = 0f;

            if (index < 0 || index >= CloudCount)
                return;
            if (!IsFinite(canvasWidth) || !IsFinite(canvasHeight) || canvasWidth <= 0f || canvasHeight <= 0f)
                return;

            float period = CloudDriftPeriodsSeconds[index];
            float phase = CloudDriftPhases[index];
            float settle = DriftSettle(timeSeconds);

            offsetX = canvasWidth * DriftAmplitudeX * Compound(timeSeconds, period, phase) * settle;
            // Вертикаль идёт своим, более длинным периодом — иначе облако
            // ходило бы по прямой под 45 градусов вместо неспешной петли.
            offsetY = canvasHeight * DriftAmplitudeY * Compound(timeSeconds, period * 1.618f, phase) * settle;
            opacityDelta = DriftOpacityAmplitude * Wave(timeSeconds, period * 0.77f, phase) * settle;
        }

        /// <summary>Амплитуда набухания облака рамки как добавка к масштабу.</summary>
        public const float FrameSwellAmplitude = 0.04f;

        /// <summary>Период набухания рамки как доля периода дрейфа своего облака.</summary>
        public const float FrameSwellPeriodRatio = 0.618f;

        /// <summary>Амплитуда крена рамки в градусах.</summary>
        public const float FrameRollAmplitudeDegrees = 1.5f;

        public const float FrameRollPeriodRatio = 0.347f;

        /// <summary>
        /// Множитель масштаба облака рамки. Через <see cref="Breath"/>, а не
        /// через фазированную форму: экран уже показан, облака уже стоят на
        /// местах, и любой масштаб кроме единицы на первом кадре был бы
        /// видимым скачком. Разные периоды у четырёх облаков расфазируют их
        /// сами, стартовав из общего покоя.
        /// </summary>
        public static float FrameSwell(int index, float timeSeconds)
        {
            if (index < 0 || index >= CloudCount)
                return 1f;

            return Breath(timeSeconds, CloudDriftPeriodsSeconds[index] * FrameSwellPeriodRatio, FrameSwellAmplitude);
        }

        /// <summary>
        /// Крен облака рамки в градусах. Фаза здесь намеренно нулевая — по той
        /// же причине, что и у <see cref="FrameSwell"/>: старт обязан быть из
        /// покоя.
        /// </summary>
        public static float FrameRollDegrees(int index, float timeSeconds)
        {
            if (index < 0 || index >= CloudCount)
                return 0f;

            return FrameRollAmplitudeDegrees
                * Wave(timeSeconds, CloudDriftPeriodsSeconds[index] * FrameRollPeriodRatio, 0f);
        }

        /// <summary>
        /// Насколько быстрее карты движется каждое облако. Больше единицы —
        /// облако «ближе к камере». Порядок совпадает с порядком в
        /// <see cref="CloudDriftPeriodsSeconds"/>.
        /// </summary>
        public static readonly float[] CloudParallaxFactors = { 1.04f, 1.06f, 1.10f, 1.12f };

        /// <summary>
        /// Потолок параллакс-смещения. Без него на максимальном зуме, где пан
        /// исчисляется сотнями пикселей, облака уехали бы из композиции
        /// целиком — а они часть рисунка карты, а не свободный слой.
        /// </summary>
        public const float MaxParallaxOffsetPixels = 40f;

        /// <summary>Смещение облака относительно карты при данном пане и его множителе глубины.</summary>
        public static float ParallaxOffset(float pan, float factor)
        {
            if (!IsFinite(pan) || !IsFinite(factor))
                return 0f;

            float offset = pan * (factor - 1f);
            if (offset > MaxParallaxOffsetPixels)
                return MaxParallaxOffsetPixels;
            if (offset < -MaxParallaxOffsetPixels)
                return -MaxParallaxOffsetPixels;
            return offset;
        }

        /// <summary>Период «дыхания бумаги» — очень длинный специально: это должно чувствоваться телом, а не читаться глазом.</summary>
        public const float PaperBreathPeriodSeconds = 24f;

        /// <summary>Амплитуда дыхания бумаги как множитель зума: 1.000 - 1.006.</summary>
        public const float PaperBreathAmplitude = 0.006f;

        /// <summary>Период дыхания маркера — заметно быстрее неба: маркер живой объект, а не погода.</summary>
        public const float MarkerBreathPeriodSeconds = 3.2f;

        /// <summary>
        /// Амплитуда дыхания маркера как добавка к масштабу. 0.025, а не
        /// 0.035: с FocusBreathMultiplier = 1.4 усиленная цель обязана
        /// остаться в бюджете «не больше четырёх процентов» — с 0.035 это
        /// уже 4.9%, что рвёт MarkerBreath_RespectsTheAmbientCompositionRule.
        /// </summary>
        public const float MarkerBreathAmplitude = 0.025f;

        /// <summary>Во сколько раз сильнее дышит ЕДИНСТВЕННАЯ текущая цель. Остальные разблокированные маркеры дышат обычной амплитудой, locked не дышат вовсе.</summary>
        public const float FocusBreathMultiplier = 1.4f;

        /// <summary>Прозрачность тени маркера в покое. Видимой альфой владеет только она — цвет тени непрозрачен, см. Map.uss.</summary>
        public const float MarkerShadowRestOpacity = 0.35f;

        /// <summary>Насколько бледнеет тень на единицу роста маркера.</summary>
        public const float MarkerShadowOpacityPerScale = 2f;

        /// <summary>
        /// Насколько шире тень выбранного маркера сверх обычной формулы
        /// дыхания. Читается вместе с приподнятой на 6px и увеличенной до
        /// 1.10 иконкой (см. Map.uss ".chapter-node--selected .chapter-node__icon")
        /// как «маркер оторвался от бумаги».
        /// </summary>
        public const float MarkerShadowSelectedScaleBonus = 0.18f;

        /// <summary>Насколько бледнеет тень выбранного маркера сверх обычной формулы дыхания.</summary>
        public const float MarkerShadowSelectedOpacityDrop = 0.12f;

        /// <summary>
        /// Масштаб тени в противофазе к дыханию: маркер растёт — тень
        /// поджимается. У выбранного маркера поверх этого добавляется
        /// <see cref="MarkerShadowSelectedScaleBonus"/> — независимо от фазы
        /// дыхания тень выбранного обязана быть шире, чем у невыбранного.
        /// </summary>
        public static float MarkerShadowScale(float breathScale, bool selected = false)
        {
            float value = IsFinite(breathScale) ? 2f - breathScale : 1f;
            return selected ? value + MarkerShadowSelectedScaleBonus : value;
        }

        /// <summary>
        /// Прозрачность тени в противофазе. Клампится в [0, 1]: перемножения
        /// с альфой цвета больше нет, поэтому выход за диапазон был бы виден
        /// напрямую. У выбранного маркера вычитается
        /// <see cref="MarkerShadowSelectedOpacityDrop"/> до клампа.
        /// </summary>
        public static float MarkerShadowOpacity(float breathScale, bool selected = false)
        {
            float value = IsFinite(breathScale)
                ? MarkerShadowRestOpacity - (breathScale - 1f) * MarkerShadowOpacityPerScale
                : MarkerShadowRestOpacity;
            if (selected)
                value -= MarkerShadowSelectedOpacityDrop;
            return Clamp01(value);
        }

        // ---------- каскад появления маркеров ----------
        // Раньше был USS-переходом (класс ".map-node--enter" + transitionDelay
        // по индексу). Не работал: transitionDelay действует в обе стороны
        // перехода, а класс снимался (ExecuteLater(0), следующий кадр) раньше,
        // чем задержка большинства маркеров успевала истечь — переход "туда"
        // не успевал стартовать, и возвращаться было неоткуда. Каскадило
        // только у маркера с индексом 0 (задержка 0). См. ре-ревью задачи 9.
        //
        // Численный привод той же 30 Гц Tick, что и дыхание — без классов,
        // без переходов, без гонки: пока вход не завершён, тик пишет каскад;
        // как только завершён — тик пишет дыхание. Одно и то же присваивание
        // каждый тик, конкурировать некому по построению.

        /// <summary>Задержка перед стартом входа маркера в каскаде появления, по индексу узла (0 — без задержки).</summary>
        public const float MarkerEntranceStepSeconds = 0.07f;

        /// <summary>Длительность входа одного маркера (полная версия, с перелётом).</summary>
        public const float MarkerEntranceDurationSeconds = 0.32f;

        /// <summary>Длительность входа под «меньше движения» — общее проявление без ступеньки по индексу и без перелёта.</summary>
        public const float MarkerEntranceReducedDurationSeconds = 0.15f;

        /// <summary>Смещение маркера по Y в начале входа (полная версия, до кривой перелёта). Под reducedMotion не используется — там смещения нет вовсе.</summary>
        public const float MarkerEntranceStartOffsetY = -14f;

        /// <summary>Масштаб маркера в начале входа (полная версия, до кривой перелёта). Под reducedMotion не используется — там масштаб не участвует, каскад схлопнут в одну прозрачность.</summary>
        public const float MarkerEntranceStartScale = 0.92f;

        /// <summary>
        /// Линейный прогресс входа маркера с данным индексом в [0, 1] к моменту
        /// <paramref name="timeSeconds"/> с начала показа экрана (0 — ещё не
        /// начал, 1 — вход завершён, дальше маркером управляет дыхание). Под
        /// reducedMotion ступенька по индексу снята: все маркеры входят
        /// одновременно за <see cref="MarkerEntranceReducedDurationSeconds"/>.
        /// Безопасно на NaN/бесконечности/отрицательном индексе — вход
        /// считается завершённым (1), а не зависает на середине: испорченный
        /// ввод не должен оставлять маркер невидимым навсегда.
        /// </summary>
        public static float MarkerEntranceProgress(int index, float timeSeconds, bool reducedMotion)
        {
            float duration = reducedMotion ? MarkerEntranceReducedDurationSeconds : MarkerEntranceDurationSeconds;
            if (!IsFinite(timeSeconds) || duration <= 0f)
                return 1f;

            float delay = !reducedMotion && index > 0 ? index * MarkerEntranceStepSeconds : 0f;
            return Clamp01((timeSeconds - delay) / duration);
        }

        /// <summary>
        /// Масштаб маркера во время входа от прогресса, уже пропущенного через
        /// перелётную кривую (<see cref="MapPanZoomMath.EaseOutBack"/> — это
        /// решает вызывающая сторона, здесь только линейная интерполяция).
        /// Под reducedMotion масштаб в каскаде не участвует — всегда 1.
        /// </summary>
        public static float MarkerEntranceScale(float easedProgress, bool reducedMotion)
        {
            if (reducedMotion)
                return 1f;
            if (!IsFinite(easedProgress))
                return 1f;
            return MarkerEntranceStartScale + (1f - MarkerEntranceStartScale) * easedProgress;
        }

        /// <summary>Смещение маркера по Y во время входа, от того же эйзенного прогресса. Под reducedMotion смещения нет.</summary>
        public static float MarkerEntranceOffsetY(float easedProgress, bool reducedMotion)
        {
            if (reducedMotion)
                return 0f;
            if (!IsFinite(easedProgress))
                return 0f;
            return MarkerEntranceStartOffsetY * (1f - easedProgress);
        }

        /// <summary>
        /// Итог одного тика каскада появления: прозрачность, смещение по Y и
        /// МНОЖИТЕЛЬ масштаба (не сам масштаб — контроллер умножает его на
        /// текущее дыхание, см. MapAmbientController.TickMarkers, поэтому
        /// границы владения между каскадом и дыханием не существует вовсе).
        ///
        /// <para>
        /// Явно доводит до точного покоя (1 / 0 / 1) при progress >= 1, а не
        /// оставляет то, что случайно получилось на предпоследнем тике: тик
        /// 33 мс почти никогда не делит длительность входа (320/150 мс)
        /// нацело, поэтому "прогресс ровно 1" на каком-то конкретном тике не
        /// гарантирован — без явной доводки прозрачность застревала бы чуть
        /// ниже единицы навсегда. См. ре-ревью задачи 9 (регрессия из
        /// численного привода: "opacity никогда не достигает 1").
        /// </para>
        /// </summary>
        public static void MarkerEntranceTransform(float progress, bool reducedMotion,
            out float opacity, out float offsetY, out float scaleMultiplier)
        {
            if (!IsFinite(progress) || progress >= 1f)
            {
                opacity = 1f;
                offsetY = 0f;
                scaleMultiplier = 1f;
                return;
            }

            float clamped = Clamp01(progress);
            float eased = reducedMotion ? clamped : MapPanZoomMath.EaseOutBack(clamped);
            opacity = clamped;
            offsetY = MarkerEntranceOffsetY(eased, reducedMotion);
            scaleMultiplier = MarkerEntranceScale(eased, reducedMotion);
        }

        /// <summary>
        /// Итоговый масштаб маркера — произведение дыхания (или покоя, 1, для
        /// заблокированного) на множитель входа из MarkerEntranceTransform,
        /// который сам стремится к 1. Вынесено отдельной функцией именно
        /// затем, чтобы произведение было проверяемо тестом напрямую: до
        /// этой правки оно считалось прямо в TickMarkers, и ни один тест его
        /// не закреплял — регрессия на границе входа (см. ре-ре-ревью задачи
        /// 9) прошла бы мимо снова.
        /// </summary>
        public static float MarkerScale(float breathScale, float entranceScaleMultiplier)
        {
            if (!IsFinite(breathScale) || !IsFinite(entranceScaleMultiplier))
                return 1f;
            return breathScale * entranceScaleMultiplier;
        }

        /// <summary>Сколько секунд без ввода до включения Ken Burns.</summary>
        public const float IdleDelaySeconds = 5f;

        /// <summary>За сколько Ken Burns набирает полную силу и гаснет при касании. Гашение через вес, а не мгновенным нулём — иначе на касании был бы рывок.</summary>
        public const float KenBurnsFadeSeconds = 0.4f;

        /// <summary>Период дрейфа камеры в простое.</summary>
        public const float KenBurnsPeriodSeconds = 40f;

        /// <summary>Доля вьюпорта, на которую камера уходит в простое.</summary>
        public const float KenBurnsPanAmplitude = 0.008f;

        /// <summary>Добавка к множителю зума в простое.</summary>
        public const float KenBurnsZoomAmplitude = 0.015f;

        /// <summary>
        /// Плавно ведёт вес эффекта к цели со скоростью «полный ход за
        /// <paramref name="fadeSeconds"/>». Используется, чтобы Ken Burns
        /// затухал при касании за 0.4 с, а не обрывался кадром.
        /// </summary>
        public static float ApproachWeight(float current, float target, float deltaSeconds, float fadeSeconds)
        {
            float from = IsFinite(current) ? Clamp01(current) : 0f;
            float to = IsFinite(target) ? Clamp01(target) : 0f;
            if (!IsFinite(deltaSeconds) || deltaSeconds <= 0f)
                return from;
            if (!IsFinite(fadeSeconds) || fadeSeconds <= 0f)
                return to;

            float step = deltaSeconds / fadeSeconds;
            if (to > from)
                return from + step >= to ? to : from + step;
            return from - step <= to ? to : from - step;
        }

        /// <summary>
        /// Смещение камеры в простое. Горизонталь и вертикаль идут разными
        /// периодами, поэтому траектория — медленная петля, а не движение
        /// по прямой туда-обратно. В нуле времени всё ровно по нулям, чтобы
        /// включение эффекта не давало скачка.
        /// </summary>
        public static void KenBurns(float timeSeconds, float viewportWidth, float viewportHeight,
            out float panX, out float panY, out float zoomDelta)
        {
            panX = 0f;
            panY = 0f;
            zoomDelta = 0f;

            if (!IsFinite(viewportWidth) || !IsFinite(viewportHeight) || viewportWidth <= 0f || viewportHeight <= 0f)
                return;

            panX = viewportWidth * KenBurnsPanAmplitude * Wave(timeSeconds, KenBurnsPeriodSeconds, 0f);
            panY = viewportHeight * KenBurnsPanAmplitude * Wave(timeSeconds, KenBurnsPeriodSeconds * 1.618f, 0f);
            zoomDelta = KenBurnsZoomAmplitude * Wave(timeSeconds, KenBurnsPeriodSeconds * 1.31f, 0f);
        }

        /// <summary>Какая доля скорости броска остаётся через секунду. Подобрано так, чтобы карта доезжала примерно за полсекунды и не «ехала вечно».</summary>
        public const float InertiaRemainingPerSecond = 0.06f;

        /// <summary>Ниже этой скорости (пикселей в секунду) инерция считается законченной и гасится, чтобы карта не дрожала на околонулевых значениях.</summary>
        public const float InertiaStopSpeedPixelsPerSecond = 40f;

        /// <summary>Вес последнего замера в сглаживании скорости пальца.</summary>
        public const float VelocitySampleWeight = 0.6f;

        /// <summary>Затухание скорости за произвольный шаг времени. Экспонента, а не вычитание, — иначе результат зависел бы от частоты кадров.</summary>
        public static float DecayVelocity(float velocity, float deltaSeconds)
        {
            if (!IsFinite(velocity))
                return 0f;
            if (!IsFinite(deltaSeconds) || deltaSeconds <= 0f)
                return velocity;
            return velocity * (float)System.Math.Pow(InertiaRemainingPerSecond, deltaSeconds);
        }

        /// <summary>
        /// Сглаживание скорости пальца: одиночный дёрганый замер не должен
        /// целиком становиться скоростью броска — на тач-экране такие выбросы
        /// обычны и дают «выстрел» карты вместо броска.
        /// </summary>
        public static float BlendVelocity(float previous, float sample)
        {
            float prev = IsFinite(previous) ? previous : 0f;
            float next = IsFinite(sample) ? sample : 0f;
            return prev * (1f - VelocitySampleWeight) + next * VelocitySampleWeight;
        }

        /// <summary>Достаточно ли скорость упала, чтобы остановиться.</summary>
        public static bool IsInertiaFinished(float velocityX, float velocityY)
        {
            if (!IsFinite(velocityX) || !IsFinite(velocityY))
                return true;
            double speed = System.Math.Sqrt(velocityX * (double)velocityX + velocityY * (double)velocityY);
            return speed < InertiaStopSpeedPixelsPerSecond;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        private static double WaveAngle(float timeSeconds, float periodSeconds)
        {
            if (!IsFinite(timeSeconds) || !IsFinite(periodSeconds) || periodSeconds <= 0f)
                return 0.0;
            return timeSeconds / periodSeconds * 2.0 * System.Math.PI;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
