using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Поведенческие тесты плывущего слоя: дерево строится руками, методы
    /// зовутся напрямую, сверяются ФАКТИЧЕСКИЕ значения инлайн-стилей.
    ///
    /// Почему не тесты по исходнику, как у контроллеров карты: там выбор
    /// объясняется тем, что корутины и планировщик MonoBehaviour в EditMode не
    /// гоняются. К <see cref="MapWindLayer"/> это не относится — он не
    /// MonoBehaviour, запись инлайн-стилей это обычные сеттеры C#, и ни панель,
    /// ни планировщик, ни проход раскладки для проверки не нужны. Грепы по
    /// тексту здесь пропускали настоящие поломки: перепутанные оси параллакса,
    /// инверсию SetVisible, раскладку каждый кадр.
    /// </summary>
    public class MapWindLayerTests
    {
        private const string Prefix = "map-wind-";
        private const float W = 1000f;
        private const float H = 460f;

        private static VisualElement BuildScreen()
        {
            var root = new VisualElement();
            root.Add(new VisualElement { name = Prefix + "layer" });
            for (int i = 0; i < MapWindLayout.Clouds.Length; i++)
                root.Add(new VisualElement { name = Prefix + i });
            return root;
        }

        private static VisualElement Layer(VisualElement root) => root.Q<VisualElement>(Prefix + "layer");

        private static VisualElement Cloud(VisualElement root, int i) => root.Q<VisualElement>(Prefix + i);

        private static MapWindLayer BoundLayer(VisualElement root)
        {
            var layer = new MapWindLayer();
            layer.Bind(root, Prefix);
            return layer;
        }

        private static float ExpectedWidthPercent(int i) => MapWindLayout.Clouds[i].WidthFraction * 100f;

        /// <summary>
        /// Снимок ВСЕЙ геометрии, а не перечисленных имён: чёрный список
        /// пропускал minWidth, right, bottom, position и flexGrow.
        /// </summary>
        private static string GeometrySnapshot(VisualElement e)
        {
            var sb = new StringBuilder();
            sb.Append(e.style.width).Append("|")
                .Append(e.style.height).Append("|")
                .Append(e.style.top).Append("|")
                .Append(e.style.left).Append("|")
                .Append(e.style.right).Append("|")
                .Append(e.style.bottom).Append("|")
                .Append(e.style.minWidth).Append("|")
                .Append(e.style.minHeight).Append("|")
                .Append(e.style.maxWidth).Append("|")
                .Append(e.style.maxHeight).Append("|")
                .Append(e.style.marginLeft).Append("|")
                .Append(e.style.marginTop).Append("|")
                .Append(e.style.marginRight).Append("|")
                .Append(e.style.marginBottom).Append("|")
                .Append(e.style.paddingLeft).Append("|")
                .Append(e.style.paddingTop).Append("|")
                .Append(e.style.paddingRight).Append("|")
                .Append(e.style.paddingBottom).Append("|")
                .Append(e.style.borderLeftWidth).Append("|")
                .Append(e.style.borderTopWidth).Append("|")
                .Append(e.style.borderRightWidth).Append("|")
                .Append(e.style.borderBottomWidth).Append("|")
                .Append(e.style.position).Append("|")
                .Append(e.style.flexGrow).Append("|")
                .Append(e.style.flexShrink).Append("|")
                .Append(e.style.flexBasis);
            return sb.ToString();
        }

        // ---------- раскладка ----------

        [Test]
        public void LaysOutOnlyWhenTheCanvasSizeActuallyChanged()
        {
            // Центральный инвариант дизайна: без памяти размера раскладка шла бы
            // каждый тик, то есть полный проход лэйаута 30 раз в секунду.
            VisualElement root = BuildScreen();
            MapWindLayer layer = BoundLayer(root);
            VisualElement cloud = Cloud(root, 0);

            layer.Tick(10f, W, H, 0f, 0f);
            Assert.AreEqual(ExpectedWidthPercent(0), cloud.style.width.value.value, 1e-3f,
                "Первый тик обязан разложить слой.");

            cloud.style.width = new Length(999f, LengthUnit.Percent);
            layer.Tick(11f, W, H, 0f, 0f);
            Assert.AreEqual(999f, cloud.style.width.value.value, 1e-3f,
                "При неизменном размере канваса раскладка обязана пропускаться целиком.");

            layer.Tick(12f, W + 120f, H, 0f, 0f);
            Assert.AreEqual(ExpectedWidthPercent(0), cloud.style.width.value.value, 1e-3f,
                "Смена размера канваса обязана переложить слой.");
        }

        [Test]
        public void TickNeverTouchesGeometryOnceLaidOut()
        {
            // Сверка НЕ «до тика против после тика»: запись константы (например
            // minWidth: 0) одинакова с обеих сторон и такой диф её не видит.
            // Сверяем с эталоном, который получил ТОЛЬКО MapWindLayout.Apply —
            // тогда всё, что тик добавил сверх раскладки, вылезает наружу.
            VisualElement root = BuildScreen();
            MapWindLayer layer = BoundLayer(root);

            layer.Tick(10f, W, H, 0f, 0f);
            layer.Tick(11.7f, W, H, 25f, -14f);
            layer.Tick(13.1f, W, H, -8f, 3f);

            for (int i = 0; i < MapWindLayout.Clouds.Length; i++)
            {
                var reference = new VisualElement();
                MapWindLayout.Apply(reference, MapWindLayout.Clouds[i], W, H);

                Assert.AreEqual(GeometrySnapshot(reference), GeometrySnapshot(Cloud(root, i)),
                    "Геометрия облака " + i + " обязана совпадать с тем, что кладёт одна лишь "
                    + "MapWindLayout.Apply: тик не смеет добавить к ней НИ ОДНОГО свойства.");
            }
        }

        [Test]
        public void DegenerateCanvasSizeIsIgnored_IncludingNaN()
        {
            VisualElement root = BuildScreen();
            MapWindLayer layer = BoundLayer(root);

            void AssertUntouched(float w, float h, string because)
            {
                layer.Tick(10f, w, h, 0f, 0f);
                for (int i = 0; i < MapWindLayout.Clouds.Length; i++)
                {
                    VisualElement c = Cloud(root, i);
                    Assert.AreEqual(StyleKeyword.Null, c.style.translate.keyword, because);
                    Assert.AreEqual(StyleKeyword.Null, c.style.width.keyword, because);
                }
            }

            AssertUntouched(0f, H, "нулевая ширина");
            AssertUntouched(W, 0f, "нулевая высота");
            AssertUntouched(-5f, H, "отрицательная ширина");

            // resolvedStyle.width канваса до первого прохода раскладки именно NaN.
            // Сторож вида "<= 0f" его НЕ ловит, тик проходит внутрь, а поскольку
            // NaN == NaN ложно, память размера не совпадает никогда — и слой
            // перекладывается каждым кадром, пока размер не станет числом.
            AssertUntouched(float.NaN, H, "NaN-ширина");
            AssertUntouched(W, float.NaN, "NaN-высота");
        }

        // ---------- движение ----------

        /// <remarks>
        /// Рамка и ветер входят в кадр ДВУМЯ разными константами
        /// (<see cref="MapWindLayer.SettleSeconds"/> и
        /// <see cref="MapAmbientMath.DriftSettleSeconds"/>), и каждый тест до
        /// этого смотрел только в зеркало своей: мутация
        /// <c>SettleSeconds = 6.0f</c> проходила зелёной — рамка входила за
        /// полторы секунды, ветер за шесть, набор был доволен. Композиция
        /// первой секунды после показа экрана не охранялась ничем от начала до
        /// конца. Здесь она сшивается: и константы, и множитель, реально
        /// дошедший до прозрачности облака.
        /// </remarks>
        [Test]
        public void WindAndFrameEnterTheFrameOnOneClock()
        {
            Assert.AreEqual(MapAmbientMath.DriftSettleSeconds, MapWindLayer.SettleSeconds, 1e-6f,
                "Рамка и ветер обязаны входить в кадр за одно и то же время: экран показывается "
                + "один, и два разных срока превращают вход в две накладывающиеся анимации.");

            const float Early = 0.4f;
            const int Near = 6;
            VisualElement root = BuildScreen();
            MapWindLayer layer = BoundLayer(root);

            layer.Tick(Early, W, H, 0f, 0f);

            WindCloud lane = MapWindLayout.Clouds[Near];
            float progress = MapWindMath.LaneProgress(Early, lane.CrossSeconds, lane.Phase);
            float unsettled = MapWindMath.Opacity(
                lane.RestOpacity, progress, Early, lane.CrossSeconds, lane.Phase, 1f);
            Assert.Greater(unsettled, 0f, "Проверка пуста, если облако в этот момент и так невидимо.");

            float settleOfWind = Cloud(root, Near).style.opacity.value / unsettled;
            Assert.AreEqual(MapAmbientMath.DriftSettle(Early), settleOfWind, 1e-4f,
                "Множитель ввода ветра разошёлся с множителем ввода рамки в ту же секунду.");
        }

        [Test]
        public void EntrySettleReachesOpacityOnly_NeverPosition()
        {
            // Множитель ввода в кадр на положении слепил бы все семь облаков в
            // одну точку их полос на полторы секунды, уничтожив расфазировку,
            // ради которой таблица полос и существует.
            // Пан НЕНУЛЕВОЙ намеренно, и это единственная точка во всём наборе,
            // где утечка множителя ввода в слагаемое параллакса вообще наблюдаема:
            // тесты параллакса гоняют t = 20, где EaseOutCubic уже зажат в единицу,
            // а с нулевым паном слагаемое равно нулю и домножать в нём нечего.
            // Значения подобраны так, чтобы параллакс каждой полосы был ненулевым и
            // при этом НЕ упирался в потолок — иначе клампинг съел бы разницу.
            const float Early = 0.3f;
            const float PanX = 37f;
            const float PanY = -23f;
            VisualElement root = BuildScreen();
            MapWindLayer layer = BoundLayer(root);

            layer.Tick(Early, W, H, PanX, PanY);

            float settle = MapPanZoomMath.EaseOutCubic(Early / MapWindLayer.SettleSeconds);
            Assert.Less(settle, 1f, "Проверка имеет смысл только внутри окна ввода.");

            for (int i = 0; i < MapWindLayout.Clouds.Length; i++)
            {
                WindCloud lane = MapWindLayout.Clouds[i];
                VisualElement c = Cloud(root, i);
                float progress = MapWindMath.LaneProgress(Early, lane.CrossSeconds, lane.Phase);

                float parallaxX = MapAmbientMath.ParallaxOffset(
                    PanX, lane.ParallaxFactor, MapWindMath.ParallaxPanLimit(W));
                float parallaxY = MapAmbientMath.ParallaxOffset(
                    PanY, lane.ParallaxFactor, MapWindMath.ParallaxPanLimit(H));
                Assert.AreNotEqual(0f, parallaxX, "Проверка пуста, если слагаемое параллакса нулевое.");

                Assert.AreEqual(
                    MapWindMath.LaneOffsetX(progress, W, W * lane.WidthFraction) + parallaxX,
                    c.style.translate.value.x.value, 1e-3f,
                    "Множитель ввода не смеет идти на горизонталь облака " + i
                    + " — ни на путь по полосе, ни на слагаемое параллакса.");
                Assert.AreEqual(
                    MapWindMath.Bob(Early, lane.CrossSeconds, lane.Phase, H) + parallaxY,
                    c.style.translate.value.y.value, 1e-3f,
                    "Множитель ввода не смеет идти на вертикаль облака " + i
                    + " — ни на покачивание, ни на слагаемое параллакса.");

                float unsettled = MapWindMath.Opacity(lane.RestOpacity, progress, Early,
                    lane.CrossSeconds, lane.Phase, 1f);
                Assert.AreEqual(
                    MapWindMath.Opacity(lane.RestOpacity, progress, Early, lane.CrossSeconds, lane.Phase, settle),
                    c.style.opacity.value, 1e-5f);
                Assert.Less(c.style.opacity.value, unsettled,
                    "Прозрачность облака " + i + " обязана быть приглушена вводом в кадр.");
            }
        }

        [Test]
        public void ParallaxAxesAreNotSwapped()
        {
            VisualElement root = BuildScreen();
            MapWindLayer layer = BoundLayer(root);

            layer.Tick(20f, W, H, 0f, 0f);
            var baseX = new float[MapWindLayout.Clouds.Length];
            var baseY = new float[MapWindLayout.Clouds.Length];
            for (int i = 0; i < baseX.Length; i++)
            {
                baseX[i] = Cloud(root, i).style.translate.value.x.value;
                baseY[i] = Cloud(root, i).style.translate.value.y.value;
            }

            layer.Tick(20f, W, H, 60f, 0f);
            for (int i = 0; i < baseX.Length; i++)
            {
                Assert.AreNotEqual(baseX[i], Cloud(root, i).style.translate.value.x.value,
                    "Горизонтальный пан обязан двигать облако " + i + " по горизонтали.");
                Assert.AreEqual(baseY[i], Cloud(root, i).style.translate.value.y.value, 1e-4f,
                    "Горизонтальный пан не смеет двигать облако " + i + " по вертикали.");
            }

            layer.Tick(20f, W, H, 0f, 60f);
            for (int i = 0; i < baseX.Length; i++)
            {
                Assert.AreEqual(baseX[i], Cloud(root, i).style.translate.value.x.value, 1e-4f,
                    "Вертикальный пан не смеет двигать облако " + i + " по горизонтали.");
                Assert.AreNotEqual(baseY[i], Cloud(root, i).style.translate.value.y.value,
                    "Вертикальный пан обязан двигать облако " + i + " по вертикали.");
            }
        }

        [Test]
        public void ParallaxRespectsTheWindsOwnCeiling()
        {
            VisualElement root = BuildScreen();
            MapWindLayer layer = BoundLayer(root);

            float[] baseX = Snapshot(root, layer, 0f, 0f, out float[] baseY);

            layer.Tick(20f, W, H, 100000f, -100000f);
            for (int i = 0; i < baseX.Length; i++)
            {
                float depth = Mathf.Abs(MapWindLayout.Clouds[i].ParallaxFactor - 1f);

                // Обе оси: потолок только на горизонтали оставлял бы ветру право
                // уехать на шестьдесят тысяч пикселей вверх при зелёном наборе.
                Assert.LessOrEqual(
                    Mathf.Abs(Cloud(root, i).style.translate.value.x.value - baseX[i]),
                    depth * MapWindMath.ParallaxPanLimit(W) + 1e-3f,
                    "Потолок обязан держать горизонталь облака " + i + ".");
                Assert.LessOrEqual(
                    Mathf.Abs(Cloud(root, i).style.translate.value.y.value - baseY[i]),
                    depth * MapWindMath.ParallaxPanLimit(H) + 1e-3f,
                    "Потолок обязан держать и вертикаль облака " + i + ".");
            }
        }

        /// <remarks>
        /// Потолок, унаследованный от рамки, был задан ОДНИМ числом на
        /// результат — и на множителях ветра (0.40 / 0.75 / 1.30 против
        /// 1.04…1.12 у рамки) насыщал дальнюю полосу уже к 67 пикселям пана
        /// при разрешённых 468. Выше насыщения все полосы отдавали одно и то
        /// же смещение, то есть ехали ровно с картой и друг с другом: глубина,
        /// которую §5 спеки называет ключевой, выключалась на большей части
        /// реального панорамирования. Проверка «эффект жив у нуля» этого не
        /// ловила вовсе, поэтому здесь перебирается ВЕСЬ разрешённый игрой
        /// диапазон пана, вплоть до максимального зума.
        /// </remarks>
        [Test]
        public void WindParallaxKeepsItsDepthAcrossTheWholePanRange()
        {
            const int Far = 0;
            const int Mid = 3;
            const int Near = 6;

            VisualElement root = BuildScreen();
            MapWindLayer layer = BoundLayer(root);
            float[] baseX = Snapshot(root, layer, 0f, 0f, out float[] _);

            float maxPan = MapPanZoomMath.MaxPanForZoom(MapPanZoomMath.MaxZoom, W);
            Assert.Greater(maxPan, MapWindMath.ParallaxPanLimit(W),
                "Проверка пуста, если игра и так не пускает пан за предел ветра.");

            float[] fractions = { 0.1f, 0.25f, 0.5f, 0.75f, 1f };
            int compared = 0;

            foreach (float fraction in fractions)
            {
                float pan = maxPan * fraction;
                layer.Tick(20f, W, H, pan, 0f);

                float far = Cloud(root, Far).style.translate.value.x.value - baseX[Far];
                float mid = Cloud(root, Mid).style.translate.value.x.value - baseX[Mid];
                float near = Cloud(root, Near).style.translate.value.x.value - baseX[Near];

                // Дальняя отстаёт от карты, ближняя обгоняет — знаки разные, и
                // ни одна пара не смеет совпасть НИ НА ОДНОЙ точке диапазона.
                Assert.Less(far, 0f, $"Дальняя полоса обязана отставать от карты при пане {pan:F0}.");
                Assert.Less(mid, 0f, $"Средняя полоса обязана отставать от карты при пане {pan:F0}.");
                Assert.Greater(near, 0f, $"Ближняя полоса обязана обгонять карту при пане {pan:F0}.");

                Assert.Greater(mid - far, 1f,
                    $"При пане {pan:F0} дальняя и средняя полосы едут одинаково — глубина выключена.");
                Assert.Greater(near - mid, 1f,
                    $"При пане {pan:F0} средняя и ближняя полосы едут одинаково — глубина выключена.");

                // Мало «полосы различимы» — обязаны сохраняться ПРОПОРЦИИ.
                // Потолок на результате (тот, что ветер унаследовал от рамки)
                // знаки оставляет на месте, а отношение плющит к единице:
                // дальняя перестаёт быть в 2.4 раза быстрее средней и
                // становится в 1.07. Найдено мутацией — без этой проверки
                // возврат к потолку на результате проходил зелёным здесь.
                float expectedRatio = (MapWindLayout.Clouds[Far].ParallaxFactor - 1f)
                    / (MapWindLayout.Clouds[Mid].ParallaxFactor - 1f);
                Assert.AreEqual(expectedRatio, far / mid, 1e-3f,
                    $"При пане {pan:F0} отношение скоростей дальней и средней полос уехало от "
                    + "заявленного таблицей: потолок жмёт результат, а не пан.");
                compared++;
            }

            Assert.AreEqual(fractions.Length, compared, "Цикл прошёл вхолостую.");
        }

        /// <summary>Снимок горизонталей и вертикалей всех облаков после тика с данным паном.</summary>
        private static float[] Snapshot(VisualElement root, MapWindLayer layer, float panX, float panY, out float[] y)
        {
            layer.Tick(20f, W, H, panX, panY);
            var x = new float[MapWindLayout.Clouds.Length];
            y = new float[MapWindLayout.Clouds.Length];
            for (int i = 0; i < x.Length; i++)
            {
                x[i] = Cloud(root, i).style.translate.value.x.value;
                y[i] = Cloud(root, i).style.translate.value.y.value;
            }

            return x;
        }

        // ---------- показ, покой, привязка ----------

        [Test]
        public void SetVisibleDrivesDisplayInTheRightDirection()
        {
            VisualElement root = BuildScreen();
            MapWindLayer layer = BoundLayer(root);

            layer.SetVisible(false);
            Assert.AreEqual(DisplayStyle.None, Layer(root).style.display.value,
                "«Меньше движения» обязано прятать слой, а не показывать.");

            layer.SetVisible(true);
            Assert.AreEqual(DisplayStyle.Flex, Layer(root).style.display.value);
        }

        [Test]
        public void BindClearsADisplayLeftOverFromAPreviousVisit()
        {
            VisualElement root = BuildScreen();
            Layer(root).style.display = DisplayStyle.None;

            MapWindLayer layer = BoundLayer(root);
            Assert.AreEqual(DisplayStyle.Flex, Layer(root).style.display.value,
                "Bind обязан привести элемент к состоянию, которое заявляет своим полем видимости.");

            layer.SetVisible(true);
            Assert.AreEqual(DisplayStyle.Flex, Layer(root).style.display.value,
                "Иначе ранний выход в SetVisible оставляет слой скрытым до конца сессии.");
        }

        [Test]
        public void ResetReturnsEveryInlineToNullAndForgetsTheLaidOutSize()
        {
            VisualElement root = BuildScreen();
            MapWindLayer layer = BoundLayer(root);

            layer.Tick(10f, W, H, 3f, 4f);
            layer.Reset();

            for (int i = 0; i < MapWindLayout.Clouds.Length; i++)
            {
                VisualElement c = Cloud(root, i);
                Assert.AreEqual(StyleKeyword.Null, c.style.translate.keyword, "translate облака " + i);
                Assert.AreEqual(StyleKeyword.Null, c.style.scale.keyword, "scale облака " + i);
                Assert.AreEqual(StyleKeyword.Null, c.style.rotate.keyword, "rotate облака " + i);
                Assert.AreEqual(StyleKeyword.Null, c.style.opacity.keyword,
                    "opacity облака " + i + " обязана уйти в Null: покой задан правилом .map-wind в USS.");
            }

            // Память размера обязана уйти вместе с инлайном, иначе повторный вход
            // при том же размере оставит слой без раскладки.
            Cloud(root, 0).style.width = new Length(999f, LengthUnit.Percent);
            layer.Tick(11f, W, H, 0f, 0f);
            Assert.AreEqual(ExpectedWidthPercent(0), Cloud(root, 0).style.width.value.value, 1e-3f,
                "После Reset тот же размер обязан снова разложить слой.");
        }

        [Test]
        public void EveryCloudGetsDynamicUsageHints()
        {
            VisualElement root = BuildScreen();
            BoundLayer(root);

            for (int i = 0; i < MapWindLayout.Clouds.Length; i++)
                Assert.AreEqual(UsageHints.DynamicTransform | UsageHints.DynamicColor,
                    Cloud(root, i).usageHints, "Подсказки обязано получить КАЖДОЕ облако, включая " + i + ".");
        }

        [Test]
        public void SurvivesATreeThatHasNeitherLayerNorClouds()
        {
            var layer = new MapWindLayer();

            layer.Bind(new VisualElement(), Prefix);
            Assert.DoesNotThrow(() => layer.Tick(10f, W, H, 1f, 2f));
            Assert.DoesNotThrow(() => layer.Reset());
            Assert.DoesNotThrow(() => layer.SetVisible(false));

            layer.Bind(null, Prefix);
            Assert.DoesNotThrow(() => layer.Tick(10f, W, H, 1f, 2f));
            Assert.DoesNotThrow(() => layer.Reset());
        }
    }
}
