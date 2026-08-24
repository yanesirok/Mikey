using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>Одна ветровая полоса: где облако идёт, как быстро, насколько крупно и бледно.</summary>
    public readonly struct WindCloud
    {
        /// <summary>Ширина элемента в долях ширины канваса.</summary>
        public readonly float WidthFraction;

        /// <summary>Верх прямоугольника в долях высоты канваса.</summary>
        public readonly float TopFraction;

        /// <summary>Стартовая доля пути в полосе, <c>[0, 1)</c>.</summary>
        public readonly float Phase;

        /// <summary>Секунд на полное пересечение полосы.</summary>
        public readonly float CrossSeconds;

        /// <summary>Прозрачность полосы в середине пути, до пульсации и краевого затухания.</summary>
        public readonly float RestOpacity;

        /// <summary>Множитель глубины: меньше единицы — отстаёт от карты, больше — обгоняет.</summary>
        public readonly float ParallaxFactor;

        /// <summary>Класс USS, дающий этому облаку текстуру.</summary>
        public readonly string TextureClass;

        public WindCloud(float widthFraction, float topFraction, float phase, float crossSeconds,
            float restOpacity, float parallaxFactor, string textureClass)
        {
            WidthFraction = widthFraction;
            TopFraction = topFraction;
            Phase = phase;
            CrossSeconds = crossSeconds;
            RestOpacity = restOpacity;
            ParallaxFactor = parallaxFactor;
            TextureClass = textureClass;
        }
    }

    /// <summary>
    /// Единственный источник истины для семи плывущих облаков. В отличие от
    /// <see cref="MapCloudLayout"/>, координаты здесь нормализованы к КАНВАСУ,
    /// а не к исходному изображению карты: плывущее облако не часть рисунка
    /// карты и не обязано совпадать с её кадрированием.
    ///
    /// <para>
    /// <b>Направление ветра — слева направо.</b> Не произвольный выбор: слева
    /// стоят left1/left2 рамки, справа right1, и вход и выход облака
    /// происходят там, где тушь рамки наиболее плотная.
    /// </para>
    ///
    /// <para>
    /// Этот класс — ЕДИНСТВЕННОЕ место, которому разрешено писать
    /// <c>width</c>/<c>height</c>/<c>top</c>/<c>left</c> плывущих облаков, и
    /// делает это только при показе экрана и при смене размера канваса. Тик
    /// (<see cref="MapWindLayer"/>) к геометрии не прикасается вовсе.
    /// </para>
    /// </summary>
    public static class MapWindLayout
    {
        /// <summary>Пропорция облачных PNG: 2376 x 941. Зафиксирована тестом MapCloudAssetsTests.</summary>
        public const float SourceAspect = 2376f / 941f;

        public const float FarCrossSeconds = 200f;
        public const float MidCrossSeconds = 140f;
        public const float NearCrossSeconds = 95f;

        public const float FarWidthFraction = 0.34f;
        public const float MidWidthFraction = 0.52f;
        public const float NearWidthFraction = 0.78f;

        public const float FarRestOpacity = 0.14f;
        public const float MidRestOpacity = 0.24f;

        /// <summary>Ближнее облако крупное и потому НАМЕРЕННО бледнее среднего: оно проходит близко к глазу и не должно спорить с картой за внимание.</summary>
        public const float NearRestOpacity = 0.18f;

        public const float FarParallax = 0.40f;
        public const float MidParallax = 0.75f;
        public const float NearParallax = 1.30f;

        /// <summary>
        /// Потолок ВИДИМОЙ площади ветровых квадов в долях экрана на зуме
        /// покоя.
        ///
        /// <para>
        /// Считается именно видимая площадь при реальном зуме, а не сумма
        /// долей канваса. Слой — ребёнок <c>.pan-canvas</c>, а канвас рисуется
        /// с зумом, и площадь на экране растёт как КВАДРАТ зума: сырая сумма
        /// по таблице (1.52 экрана) — число состояния, которого не бывает
        /// никогда, потому что зум не опускается ниже 1.4 на входе и стоит на
        /// 2.0 в покое Окинавы. Реальный пик на зуме покоя Окинавы — около
        /// 2.45 экрана.
        /// </para>
        ///
        /// <para>
        /// Защищается это тем же, чем и раньше: карта — статичное меню, а не
        /// бой, и <c>OnDemandRendering.renderFrameInterval = 2</c> уже вдвое
        /// режет частоту обновления. См. §9 спеки и
        /// MapWindLayoutTests.VisibleQuadAreaStaysInsideTheBudget.
        /// </para>
        /// </summary>
        public const float AreaBudgetScreens = 2.7f;

        /// <summary>Пропорция альбомного телефона, на которой считается бюджет площади.</summary>
        public const float BudgetCanvasAspect = 2.17f;

        public static readonly WindCloud[] Clouds =
        {
            new WindCloud(FarWidthFraction, 0.04f, 0.00f, FarCrossSeconds, FarRestOpacity, FarParallax, "map-wind--left-01"),
            new WindCloud(FarWidthFraction, 0.19f, 0.41f, FarCrossSeconds, FarRestOpacity, FarParallax, "map-wind--right-01"),
            new WindCloud(FarWidthFraction, 0.11f, 0.73f, FarCrossSeconds, FarRestOpacity, FarParallax, "map-wind--left-02"),
            new WindCloud(MidWidthFraction, 0.26f, 0.17f, MidCrossSeconds, MidRestOpacity, MidParallax, "map-wind--right-01"),
            new WindCloud(MidWidthFraction, 0.44f, 0.55f, MidCrossSeconds, MidRestOpacity, MidParallax, "map-wind--left-01"),
            new WindCloud(MidWidthFraction, 0.35f, 0.88f, MidCrossSeconds, MidRestOpacity, MidParallax, "map-wind--bottom-01"),
            // Текстура ближней полосы — left_02, а НЕ left_01. Ближнее облако
            // шириной 0.78 канваса проходит ровно сквозь зону рамочного left1
            // (0.6736 после cover-fit, разница масштаба всего 16%, базовый угол
            // у обоих 0°, зоны пересекаются по y = [0.300 ... 0.500]): каждые 95
            // секунд облако проплывало сквозь собственного статичного двойника.
            // left_02 — единственная из четырёх текстур, чьё рамочное облако
            // (y = [-0.393 ... 0.081] на Японии, ещё выше на Окинаве) не
            // пересекается с полосой ближнего вовсе. См. §11 спеки.
            new WindCloud(NearWidthFraction, 0.30f, 0.31f, NearCrossSeconds, NearRestOpacity, NearParallax, "map-wind--left-02"),
        };

        /// <summary>Высота элемента в долях высоты канваса, сохраняющая пропорции исходного PNG.</summary>
        public static float HeightFraction(float widthFraction, float canvasWidth, float canvasHeight)
        {
            if (!IsFinite(widthFraction) || !IsFinite(canvasWidth) || !IsFinite(canvasHeight))
                return 0f;
            if (canvasWidth <= 0f || canvasHeight <= 0f)
                return 0f;

            return widthFraction * (canvasWidth / canvasHeight) / SourceAspect;
        }

        /// <summary>
        /// Пишет геометрию одного облака. <c>left</c> всегда 0: весь путь по
        /// горизонтали несёт <c>translate</c> из тика, иначе движение стоило бы
        /// полного прохода раскладки каждый кадр.
        /// </summary>
        public static void Apply(VisualElement element, WindCloud cloud, float canvasWidth, float canvasHeight)
        {
            if (element == null)
                return;

            float height = HeightFraction(cloud.WidthFraction, canvasWidth, canvasHeight);

            element.style.width = new Length(cloud.WidthFraction * 100f, LengthUnit.Percent);
            element.style.height = new Length(height * 100f, LengthUnit.Percent);
            element.style.top = new Length(cloud.TopFraction * 100f, LengthUnit.Percent);
            element.style.left = new Length(0f, LengthUnit.Percent);
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
