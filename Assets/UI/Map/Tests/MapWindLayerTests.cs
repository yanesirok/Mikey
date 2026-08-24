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

                float parallaxX = MapAmbientMath.ParallaxOffset(PanX, lane.ParallaxFactor);
                float parallaxY = MapAmbientMath.ParallaxOffset(PanY, lane.ParallaxFactor);
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
        public void ParallaxRespectsTheSharedCeiling()
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

            layer.Tick(20f, W, H, 100000f, -100000f);
            for (int i = 0; i < baseX.Length; i++)
            {
                // Обе оси: потолок только на горизонтали оставлял бы ветру право
                // уехать на шестьдесят тысяч пикселей вверх при зелёном наборе.
                Assert.LessOrEqual(
                    Mathf.Abs(Cloud(root, i).style.translate.value.x.value - baseX[i]),
                    MapAmbientMath.MaxParallaxOffsetPixels + 1e-3f,
                    "Потолок обязан держать горизонталь облака " + i + ".");
                Assert.LessOrEqual(
                    Mathf.Abs(Cloud(root, i).style.translate.value.y.value - baseY[i]),
                    MapAmbientMath.MaxParallaxOffsetPixels + 1e-3f,
                    "Потолок обязан держать и вертикаль облака " + i + ".");
            }
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
