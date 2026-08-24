using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Разметка и стили плывущего слоя. Ключевой контракт — порядок
    /// объявления: слой обязан идти ПОСЛЕ арта карты и ДО первого маркера,
    /// иначе облако начнёт проходить перед единственным интерактивным
    /// элементом экрана.
    /// </summary>
    public class MapWindLayerUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/Map/Map.uss";

        [Test]
        public void BothScreensDeclareSevenWindClouds()
        {
            string uxml = File.ReadAllText(UxmlPath);

            for (int i = 0; i < MapWindLayout.Clouds.Length; i++)
            {
                StringAssert.Contains($"name=\"map-wind-{i}\"", uxml);
                StringAssert.Contains($"name=\"okinawa-wind-{i}\"", uxml);
            }

            StringAssert.Contains("name=\"map-wind-layer\"", uxml);
            StringAssert.Contains("name=\"okinawa-wind-layer\"", uxml);
        }

        [Test]
        public void EveryWindCloudCarriesTheTextureClassItsLaneDeclares()
        {
            string uxml = File.ReadAllText(UxmlPath);

            for (int i = 0; i < MapWindLayout.Clouds.Length; i++)
            {
                string expected = MapWindLayout.Clouds[i].TextureClass;
                AssertElementHasClass(uxml, $"map-wind-{i}", expected);
                AssertElementHasClass(uxml, $"okinawa-wind-{i}", expected);
            }
        }

        [Test]
        public void WindLayerIsDeclaredAfterTheArtAndBeforeTheFirstMarker()
        {
            string uxml = File.ReadAllText(UxmlPath);

            int japanArt = uxml.IndexOf("class=\"map-canvas-art\"", System.StringComparison.Ordinal);
            int japanWind = uxml.IndexOf("name=\"map-wind-layer\"", System.StringComparison.Ordinal);
            int japanMarker = uxml.IndexOf("name=\"chapter-node-okinawa\"", System.StringComparison.Ordinal);

            Assert.Greater(japanArt, -1);
            Assert.Greater(japanWind, japanArt, "Ветер обязан краситься поверх арта карты.");
            Assert.Less(japanWind, japanMarker, "Ветер обязан краситься ПОД маркерами.");

            int okiArt = uxml.IndexOf("class=\"okinawa-canvas-art\"", System.StringComparison.Ordinal);
            int okiWind = uxml.IndexOf("name=\"okinawa-wind-layer\"", System.StringComparison.Ordinal);
            int okiMarker = uxml.IndexOf("name=\"level-node-0\"", System.StringComparison.Ordinal);

            Assert.Greater(okiArt, -1);
            Assert.Greater(okiWind, okiArt);
            Assert.Less(okiWind, okiMarker);
        }

        /// <summary>
        /// Слой ветра обязан стоять ПОД ".pan-canvas-scrim".
        ///
        /// <para>
        /// Два следствия обратного порядка, и второе видно каждым тапом.
        /// Первое: скрим читаемости приглушает арт, и атмосферный слой поверх
        /// него работает против контраста подписей, а не заодно с ним.
        /// Второе: при открытии панели главы скрим уходит в
        /// rgba(8, 10, 12, 0.32) за 0.34 с (Map.uss,
        /// ".pan-stage--pushed .pan-canvas-scrim") — карта темнеет, а слой
        /// поверх скрима не темнеет вовсе и визуально отлипает от арта.
        /// Участвовать в переходе своими средствами он не может: собственных
        /// CSS-переходов у ветра нет и быть не должно.
        /// </para>
        /// </summary>
        [Test]
        public void WindLayerIsDeclaredUnderTheReadabilityScrim()
        {
            string uxml = File.ReadAllText(UxmlPath);

            AssertWindPrecedesItsScrim(uxml, "map-canvas", "map-wind-layer");
            AssertWindPrecedesItsScrim(uxml, "okinawa-canvas", "okinawa-wind-layer");
        }

        private static void AssertWindPrecedesItsScrim(string uxml, string canvasName, string windLayerName)
        {
            int canvas = uxml.IndexOf($"name=\"{canvasName}\"", System.StringComparison.Ordinal);
            Assert.Greater(canvas, -1, $"Канвас {canvasName} не найден в разметке.");

            int wind = uxml.IndexOf($"name=\"{windLayerName}\"", canvas, System.StringComparison.Ordinal);
            int scrim = uxml.IndexOf("class=\"pan-canvas-scrim\"", canvas, System.StringComparison.Ordinal);

            Assert.Greater(wind, -1, $"Слой ветра {windLayerName} не найден внутри {canvasName}.");
            Assert.Greater(scrim, -1, $"Скрим читаемости не найден внутри {canvasName}.");
            Assert.Less(wind, scrim,
                $"{windLayerName} объявлен ПОСЛЕ .pan-canvas-scrim — значит, красится поверх "
                + "скрима: не приглушается им и не темнеет вместе с картой при открытии панели главы.");
        }

        [Test]
        public void FramingCloudLayerStillPaintsAboveTheWind()
        {
            string uxml = File.ReadAllText(UxmlPath);

            int japanWind = uxml.IndexOf("name=\"map-wind-layer\"", System.StringComparison.Ordinal);
            int japanFrame = uxml.IndexOf("name=\"map-cloud-layer\"", System.StringComparison.Ordinal);
            Assert.Greater(japanWind, -1, "Слой ветра Японии не найден в разметке.");
            Assert.Greater(japanFrame, -1, "Слой рамки Японии не найден в разметке.");
            Assert.Less(japanWind, japanFrame, "Рамка маскирует край карты и обязана оставаться сверху.");

            int okiWind = uxml.IndexOf("name=\"okinawa-wind-layer\"", System.StringComparison.Ordinal);
            int okiFrame = uxml.IndexOf("name=\"okinawa-cloud-layer\"", System.StringComparison.Ordinal);
            Assert.Greater(okiWind, -1, "Слой ветра Окинавы не найден в разметке.");
            Assert.Greater(okiFrame, -1, "Слой рамки Окинавы не найден в разметке.");
            Assert.Less(okiWind, okiFrame);
        }

        [Test]
        public void EveryWindElementIgnoresPicking()
        {
            string uxml = File.ReadAllText(UxmlPath);

            MatchCollection matches = Regex.Matches(uxml, @"<ui:VisualElement[^>]*name=""(?:map|okinawa)-wind[^""]*""[^>]*/?>");

            // Без этой проверки тест проходит вхолостую, когда разметки ещё (или уже) нет:
            // цикл ниже просто не выполняется. Хуже того, такой холостой проход
            // неотличим от симптома сломанного UXML, из-за которого тесты становятся
            // пустыми, а не красными. Семь облаков на двух экранах плюс два слоя.
            Assert.AreEqual(MapWindLayout.Clouds.Length * 2 + 2, matches.Count,
                "Ожидались 16 элементов ветра в разметке — тест не проверил ничего.");

            foreach (Match match in matches)
            {
                StringAssert.Contains("picking-mode=\"Ignore\"", match.Value,
                    $"Декоративное облако не должно перехватывать тап: {match.Value}");
            }
        }

        [Test]
        public void WindRestsInvisibleSoResetCannotFlashItOpaque()
        {
            // Тик снимает инлайн в StyleKeyword.Null, а Null отдаёт значение
            // обратно USS. Без opacity:0 здесь сброшенное облако вспыхнуло бы
            // полностью непрозрачным.
            string block = ExtractRuleBlock(File.ReadAllText(UssPath), ".map-wind");
            Assert.IsNotNull(block, "Ожидалось правило '.map-wind' в Map.uss.");
            StringAssert.Contains("opacity: 0", block);
        }

        [Test]
        public void WindTextureClassesPointAtTheSameFilesAsTheFramingOnes()
        {
            string uss = File.ReadAllText(UssPath);
            string[] suffixes = { "left-01", "left-02", "right-01", "bottom-01" };

            foreach (string suffix in suffixes)
            {
                string wind = ExtractRuleBlock(uss, $".map-wind--{suffix}");
                string frame = ExtractRuleBlock(uss, $".map-cloud--{suffix}");

                Assert.IsNotNull(wind, $"Ожидалось правило '.map-wind--{suffix}'.");
                Assert.IsNotNull(frame, $"Ожидалось правило '.map-cloud--{suffix}'.");

                string windUrl = ExtractUrl(wind);
                string frameUrl = ExtractUrl(frame);
                Assert.AreEqual(frameUrl, windUrl,
                    $"'.map-wind--{suffix}' и '.map-cloud--{suffix}' обязаны указывать на один файл.");
            }
        }

        /// <summary>
        /// У ветра не должно быть CSS-переходов ВООБЩЕ: всё его движение
        /// считает тик, и переход поверх посчитанного значения дал бы борьбу
        /// двух источников за одно свойство. Разбираются именно БЛОКИ по
        /// селектору, а не весь файл подстрокой — иначе тест спотыкался бы о
        /// переходы соседних правил.
        /// </summary>
        [Test]
        public void WindDeclaresNoCssTransitionsAtAll()
        {
            string uss = File.ReadAllText(UssPath);
            string withoutComments = Regex.Replace(uss, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            int checkedBlocks = 0;

            foreach (Match block in Regex.Matches(withoutComments, @"([^{}]+)\{([^{}]*)\}"))
            {
                if (!block.Groups[1].Value.Contains("map-wind"))
                    continue;

                checkedBlocks++;
                StringAssert.DoesNotContain("transition", block.Groups[2].Value,
                    $"Правило \"{block.Groups[1].Value.Trim()}\" объявляет CSS-переход — " +
                    "движение ветра целиком считает MapWindLayer.Tick.");
            }

            Assert.GreaterOrEqual(checkedBlocks, 6,
                "Ожидались правила слоя, базовое и четыре класса текстур — тест ничего не проверил.");
        }

        /// <summary>
        /// Единственный тест файла, который РАЗБИРАЕТ разметку, а не читает её
        /// текстом. Нужен отдельно от остальных восьми именно потому, что
        /// сломанная загрузка UXML — например, двойной дефис в теле
        /// XML-комментария — не делает их красными, а делает ПУСТЫМИ:
        /// текст на диске по-прежнему содержит нужные подстроки, проверки
        /// проходят, а дерева уже нет и экран пуст. Здесь поломка
        /// проявляется НАПРЯМУЮ: ассет либо не грузится, либо отдаёт
        /// дерево без ветровых элементов.
        /// </summary>
        [Test]
        public void MarkupParsesIntoATreeSoABrokenCommentCannotHideAsAnEmptySuite()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(vta, $"{UxmlPath} не загрузился как VisualTreeAsset.");

            var root = new VisualElement();
            vta.CloneTree(root);
            var all = root.Query<VisualElement>().ToList();
            Assert.Greater(all.Count, 0, "Дерево инстанцировалось пустым — разметка не разобралась.");

            var wind = all.Where(e => !string.IsNullOrEmpty(e.name) &&
                    (e.name.StartsWith("map-wind") || e.name.StartsWith("okinawa-wind")))
                .ToList();

            Assert.AreEqual(MapWindLayout.Clouds.Length * 2 + 2, wind.Count,
                "В разобранном дереве ожидались семь облаков на двух экранах плюс два слоя.");

            Assert.AreEqual(MapWindLayout.Clouds.Length * 2,
                wind.Count(e => e.GetClasses().Any(c => c.StartsWith("map-wind--"))),
                "Класс текстуры обязан быть у четырнадцати облаков и ни у одного слоя.");

            foreach (var element in wind)
                Assert.AreEqual(PickingMode.Ignore, element.pickingMode,
                    $"Элемент ветра из дерева перехватывает тап: {element.name}");
        }

        private static void AssertElementHasClass(string uxml, string elementName, string className)
        {
            Match match = Regex.Match(uxml, $@"<ui:VisualElement[^>]*name=""{Regex.Escape(elementName)}""[^>]*>");
            Assert.IsTrue(match.Success, $"Не найден элемент '{elementName}' в разметке.");
            StringAssert.Contains(className, match.Value,
                $"Элемент '{elementName}' обязан нести класс текстуры '{className}' из своей полосы.");
        }

        private static string ExtractRuleBlock(string uss, string selector)
        {
            Match match = Regex.Match(uss, Regex.Escape(selector) + @"\s*\{([^}]*)\}");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string ExtractUrl(string block)
        {
            Match match = Regex.Match(block, @"url\(""([^""]+)""\)");
            Assert.IsTrue(match.Success, $"В блоке нет background-image: {block}");
            return match.Groups[1].Value;
        }
    }
}
