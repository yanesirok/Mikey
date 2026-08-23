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

        /// <summary>Доля ширины канваса, на которую облако уходит от своей раскладки по горизонтали.</summary>
        public const float DriftAmplitudeX = 0.012f;

        /// <summary>Доля высоты канваса по вертикали — вдвое меньше горизонтальной: небо читается как боковой снос, а не как качели.</summary>
        public const float DriftAmplitudeY = 0.006f;

        /// <summary>На сколько прозрачность облака уходит от своего значения покоя из MapCloudLayout.</summary>
        public const float DriftOpacityAmplitude = 0.04f;

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

            offsetX = canvasWidth * DriftAmplitudeX * Wave(timeSeconds, period, phase);
            // Вертикаль идёт своим, более длинным периодом — иначе облако
            // ходило бы по прямой под 45 градусов вместо неспешной петли.
            offsetY = canvasHeight * DriftAmplitudeY * Wave(timeSeconds, period * 1.618f, phase);
            opacityDelta = DriftOpacityAmplitude * Wave(timeSeconds, period * 0.77f, phase);
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

        private static double WaveAngle(float timeSeconds, float periodSeconds)
        {
            if (!IsFinite(timeSeconds) || !IsFinite(periodSeconds) || periodSeconds <= 0f)
                return 0.0;
            return timeSeconds / periodSeconds * 2.0 * System.Math.PI;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
