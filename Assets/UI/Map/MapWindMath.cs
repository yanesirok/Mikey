namespace Mikey.UI.Map
{
    /// <summary>
    /// Чистая математика ветровых полос — намеренно без UnityEngine, чтобы
    /// гоняться прямо в EditMode, как MapAmbientMath и MapPanZoomMath.
    ///
    /// <para>
    /// Небезопасный ввод (NaN, бесконечность, неположительный период или
    /// размер) везде даёт покой, а не рывок: значение, попавшее на элемент
    /// разметки как NaN, ломает его отрисовку до конца сессии.
    /// </para>
    /// </summary>
    public static class MapWindMath
    {
        /// <summary>Доля пути на каждом конце полосы, на которой облако гасится. Страховка на случай зума и пана, когда видимая область не совпадает с канвасом.</summary>
        public const float FadeEdge = 0.06f;

        /// <summary>Амплитуда покачивания как доля высоты канваса.</summary>
        public const float BobAmplitude = 0.018f;

        /// <summary>Период покачивания как доля периода пересечения полосы.</summary>
        public const float BobPeriodRatio = 0.382f;

        /// <summary>Амплитуда набухания как добавка к масштабу.</summary>
        public const float SwellAmplitude = 0.05f;

        public const float SwellPeriodRatio = 0.236f;

        public const float RollAmplitudeDegrees = 1.6f;

        public const float RollPeriodRatio = 0.146f;

        /// <summary>Относительная амплитуда пульсации прозрачности — множитель, а не слагаемое.</summary>
        public const float OpacityAmplitude = 0.18f;

        public const float OpacityPeriodRatio = 0.191f;

        /// <summary>
        /// Дробная часть в <c>[0, 1)</c>, корректная для отрицательного ввода.
        /// Единица не возвращается никогда: при КРОШЕЧНОМ отрицательном вводе
        /// разность округляется ровно до 1 — у <c>-1e-9f</c> точное значение
        /// <c>1 - 1e-9</c> ближайшим float не представимо (шаг у единицы
        /// 1.19e-7), и результат схлопывается в <c>1.0f</c>. Большой ввод как
        /// раз безопасен: там дробная часть далека от границы. А единица в
        /// доле пути означала бы облако за правым краем в момент, который
        /// остальной код считает стартом полосы.
        /// </summary>
        public static float Frac(float value)
        {
            if (!IsFinite(value))
                return 0f;

            float result = value - (float)System.Math.Floor(value);
            if (result < 0f || result >= 1f)
                return 0f;
            return result;
        }

        /// <summary>Доля пройденного пути по своей полосе в <c>[0, 1)</c>.</summary>
        public static float LaneProgress(float timeSeconds, float crossSeconds, float phase01)
        {
            if (!IsFinite(timeSeconds) || !IsFinite(crossSeconds) || crossSeconds <= 0f)
                return 0f;

            float phase = IsFinite(phase01) ? phase01 : 0f;
            return Frac(timeSeconds / crossSeconds + phase);
        }

        /// <summary>
        /// Горизонтальное смещение относительно левого края канваса. Путь
        /// длиной <c>canvasWidth + cloudWidth</c>: в нуле бокс облака равен
        /// ровно <c>[-w, 0]</c>, в единице — ровно <c>[W, W + w]</c>.
        ///
        /// <para>
        /// Очистка краёв точная и с НУЛЕВЫМ запасом, и одной её мало.
        /// <see cref="Swell"/> (до ×1.05 от центра) и <see cref="RollDegrees"/>
        /// (±1.6°) заводят этот бокс обратно в кадр примерно на
        /// <c>0.025·w + (h/2)·sin 1.6°</c> — для ближней полосы при ширине
        /// канваса 1000 это около 24 пикселей. Заворачивание невидимо ровно
        /// потому, что <see cref="EdgeFade"/> в этих точках равен нулю:
        /// затухание здесь несущее, а не страховочное. Снять его,
        /// положившись на «геометрии достаточно», значит получить видимый
        /// щелчок на каждом обороте каждого облака. См. §6.2 спеки.
        /// </para>
        /// </summary>
        public static float LaneOffsetX(float progress01, float canvasWidth, float cloudWidth)
        {
            if (!IsFinite(progress01) || !IsFinite(canvasWidth) || !IsFinite(cloudWidth))
                return 0f;
            if (canvasWidth <= 0f || cloudWidth <= 0f)
                return 0f;

            return -cloudWidth + Clamp01(progress01) * (canvasWidth + cloudWidth);
        }

        /// <summary>Множитель прозрачности на концах полосы: 0 на обоих концах, 1 между <see cref="FadeEdge"/> и <c>1 - FadeEdge</c>.</summary>
        public static float EdgeFade(float progress01)
        {
            if (!IsFinite(progress01))
                return 0f;

            float u = Clamp01(progress01);
            return Clamp01(u / FadeEdge) * Clamp01((1f - u) / FadeEdge);
        }

        /// <summary>Вертикальное покачивание в пикселях.</summary>
        public static float Bob(float timeSeconds, float crossSeconds, float phase01, float canvasHeight)
        {
            if (!IsFinite(canvasHeight) || canvasHeight <= 0f)
                return 0f;

            return canvasHeight * BobAmplitude
                * MapAmbientMath.Wave(timeSeconds, crossSeconds * BobPeriodRatio, phase01);
        }

        /// <summary>
        /// Множитель масштаба в <c>[1, 1 + SwellAmplitude]</c>.
        ///
        /// <para>
        /// Намеренно НЕ через <see cref="MapAmbientMath.Breath"/>: та не берёт
        /// фазу, а три облака одной полосы делят один период пересечения — на
        /// Breath они набухали бы синхронно, то есть строем. Скачка на первом
        /// кадре бояться нечего: до показа экрана этих облаков не было, прыгать
        /// не от чего, а ввод в кадр несёт отдельный множитель прозрачности.
        /// </para>
        /// </summary>
        public static float Swell(float timeSeconds, float crossSeconds, float phase01)
        {
            if (!IsFinite(timeSeconds) || !IsFinite(crossSeconds) || crossSeconds <= 0f)
                return 1f;

            return 1f + SwellAmplitude * 0.5f
                * (1f + MapAmbientMath.Wave(timeSeconds, crossSeconds * SwellPeriodRatio, phase01));
        }

        /// <summary>Крен в градусах.</summary>
        public static float RollDegrees(float timeSeconds, float crossSeconds, float phase01)
        {
            if (!IsFinite(timeSeconds) || !IsFinite(crossSeconds) || crossSeconds <= 0f)
                return 0f;

            return RollAmplitudeDegrees
                * MapAmbientMath.Wave(timeSeconds, crossSeconds * RollPeriodRatio, phase01);
        }

        /// <summary>
        /// Итоговая прозрачность: покой полосы, приглушённый краевым
        /// затуханием и множителем ввода в кадр, и качнутый двусторонней
        /// пульсацией.
        ///
        /// <para>
        /// Множительность даёт не «не выше покоя», а ограниченную огибающую
        /// ВОКРУГ покоя: <c>restOpacity * [1 - OpacityAmplitude, 1 +
        /// OpacityAmplitude]</c>, со средним ровно в покое. Облако умеет и
        /// приглушаться, и подсвечиваться — односторонняя пульсация лишила бы
        /// слой подсветки и занизила бы его среднюю прозрачность примерно на
        /// девять процентов.
        /// </para>
        ///
        /// <para>
        /// Инвариант глубины — не «прозрачность не превышает свой покой», а
        /// «полосы не меняются местами»: огибающие дальней и средней полос не
        /// пересекаются (0.14 * 1.18 = 0.165 &lt; 0.197 = 0.24 * 0.82). Это
        /// свойство пары покоев, а не одной функции, поэтому охраняется
        /// отдельным тестом FarBandCanNeverOutshineTheMidBand. См. §6.4 спеки.
        /// </para>
        /// </summary>
        public static float Opacity(float restOpacity, float progress01, float timeSeconds, float crossSeconds, float phase01, float settle01)
        {
            if (!IsFinite(restOpacity) || restOpacity <= 0f)
                return 0f;

            // Вырожденный период глушит ТОЛЬКО пульсацию. Ранний возврат нулём,
            // как у Bob и RollDegrees, здесь был бы не покоем, а невидимым
            // облаком: покой прозрачности — это покой полосы.
            float pulse = IsFinite(crossSeconds) && crossSeconds > 0f
                ? 1f + OpacityAmplitude
                    * MapAmbientMath.Wave(timeSeconds, crossSeconds * OpacityPeriodRatio, phase01)
                : 1f;
            float settle = IsFinite(settle01) ? Clamp01(settle01) : 0f;

            return Clamp01(restOpacity * EdgeFade(progress01) * pulse * settle);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
