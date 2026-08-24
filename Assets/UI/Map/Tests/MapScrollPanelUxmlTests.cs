using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Свиток попапа уровня (Окинава): центрированная модалка-макимоно —
    /// верхний валик стоит, бумага растёт вниз, нижний валик едет на её
    /// нижнем крае.
    ///
    /// Ключевой контракт этого файла — ГРАНИЦА СТОИМОСТИ разворота.
    /// Раскрытие идёт анимацией height, и это осознанное решение владельца
    /// продукта, отменившее прежний translate-вариант: при translate краска
    /// едет вниз вместе с бумагой, при height — стоит на месте и бумага
    /// открывается из-под валика; выбрана вторая картинка. Плата за неё —
    /// проход лэйаута каждый кадр, поэтому тесты ниже пришпиливают ровно
    /// то, чем эта плата ограничена: height анимирует РОВНО ОДИН элемент,
    /// и больше НИКАКОЕ правило свитка не анимирует ни одного свойства
    /// лэйаута. Расползание сюда второго такого перехода — это уже не
    /// решение владельца продукта, а тихая регрессия, и её ловит
    /// ScrollAnimatesExactlyOneLayoutProperty.
    /// </summary>
    public class MapScrollPanelUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/Map/Map.uss";

        [Test]
        public void LevelPanelIsAScrollBuiltFromPaperAndTwoRolls()
        {
            string uxml = File.ReadAllText(UxmlPath);
            StringAssert.Contains("name=\"level-panel-paper\"", uxml);
            StringAssert.Contains("scroll-panel__roll--top", uxml);
            StringAssert.Contains("scroll-panel__roll--bottom", uxml);
            StringAssert.Contains("name=\"level-panel-backdrop\"", uxml);
        }

        [Test]
        public void ScrollArtworkIsPresentAndWiredUp()
        {
            string uss = File.ReadAllText(UssPath);
            foreach (string slice in new[] { "scroll-top", "scroll-center", "scroll-bottom" })
            {
                string path = $"Assets/UI/Media/Images/Map/Scroll/{slice}.png";
                Assert.IsTrue(File.Exists(path), $"Missing scroll artwork: {path}");
                StringAssert.Contains($"/{path}", uss, $"{slice}.png is in the project but no USS rule uses it.");
            }
        }

        /// <summary>
        /// Пойман живьём на этой же задаче: свежеимпортированные PNG
        /// получили дефолтный npotScale = ToNearest, и валик 840x120 лёг в
        /// текстуру 1024x128 — Unity пересэмплировал картинку неравномерно
        /// (по ширине +22%, по высоте +7%), а потом "-unity-background-scale-mode:
        /// stretch-to-fill" впихнул её обратно в коробку 840x120. Геометрия
        /// в итоге сходится, поэтому НИ ОДИН тест по строкам этого бы не
        /// заметил — теряется только резкость, сильнее всего на парче по
        /// краям бумаги. У облаков и маркеров карты npotScale уже None; этот
        /// тест распространяет то же требование на свиток, потому что
        /// молчаливые дефекты импорта в этом проекте уже случались.
        /// </summary>
        [Test]
        public void ScrollArtworkImportsAtItsNativeSize()
        {
            string[] slices = { "scroll-top", "scroll-center", "scroll-bottom" };
            int[] widths = { 840, 766, 840 };
            int[] heights = { 120, 706, 120 };

            for (int i = 0; i < slices.Length; i++)
            {
                string path = $"Assets/UI/Media/Images/Map/Scroll/{slices[i]}.png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsNotNull(importer, $"No texture importer for {path}.");
                Assert.AreEqual(TextureImporterNPOTScale.None, importer.npotScale,
                    $"{slices[i]}.png is being rescaled to a power of two on import -- the artwork must reach the panel at the size it was cut.");

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.IsNotNull(texture, $"{path} did not import as a texture.");
                Assert.AreEqual(widths[i], texture.width, $"{slices[i]}.png imported {texture.width}px wide, not {widths[i]}.");
                Assert.AreEqual(heights[i], texture.height, $"{slices[i]}.png imported {texture.height}px tall, not {heights[i]}.");
            }
        }

        /// <summary>
        /// Валики нарезаны ровно 840x120 и тянуться не должны: их коробка
        /// задана явно. Бумага уже — 766: свес валиков нарисован в самой
        /// картинке и отступом не является.
        /// </summary>
        [Test]
        public void RollsKeepTheirOwnBoxAndThePaperIsNarrower()
        {
            string uss = File.ReadAllText(UssPath);
            Assert.IsTrue(Regex.IsMatch(uss, @"\.scroll-panel__roll\s*\{[^}]*width:\s*840px;[^}]*height:\s*120px;"),
                "The rolls must keep their exact 840x120 cut.");
            Assert.IsTrue(Regex.IsMatch(uss, @"\.scroll-panel__paper\s*\{[^}]*width:\s*766px;"),
                "The paper is 766 wide -- the rolls overhang it by 37px a side, and that overhang is artwork.");
        }

        [Test]
        public void ScrollUnrollsByHeightWithTheApprovedTiming()
        {
            string uss = File.ReadAllText(UssPath);
            string paper = RuleBlock(uss, ".scroll-panel__paper");

            StringAssert.Contains("height: 0;", paper, "Closed, the paper has no height at all.");
            StringAssert.Contains("overflow: hidden;", paper, "The paper must clip its own content while rolled up.");
            Assert.IsTrue(Regex.IsMatch(paper, @"transition-property:\s*height;"));
            Assert.IsTrue(Regex.IsMatch(paper, @"transition-duration:\s*0\.62s;"));

            Assert.IsTrue(Regex.IsMatch(uss, @"\.scroll-panel--open \.scroll-panel__paper\s*\{[^}]*height:\s*690px;"),
                "An unlocked level unrolls to 690px.");
            Assert.IsTrue(Regex.IsMatch(uss, @"\.scroll-panel--open\.scroll-panel--locked \.scroll-panel__paper\s*\{[^}]*height:\s*460px;"),
                "A locked level unrolls shorter (460px) -- and that rule must be the compound selector, or it loses to the 690px one.");
        }

        /// <summary>
        /// Сторож границы стоимости, описанной в комментарии класса. Тест
        /// вытаскивает БЛОКИ правил свитка (по селектору, а не грепом по
        /// файлу — иначе он спотыкался бы о статичные "height: 120px" у
        /// валиков, которые размер, а не переход) и смотрит, что именно
        /// каждый из них анимирует.
        /// </summary>
        [Test]
        public void ScrollAnimatesExactlyOneLayoutProperty()
        {
            string uss = File.ReadAllText(UssPath);
            string withoutComments = Regex.Replace(uss, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            string[] layoutProperties = { "height", "width", "padding", "margin", "top", "left", "right", "bottom" };
            int layoutTransitions = 0;
            int checkedDeclarations = 0;

            foreach (Match block in Regex.Matches(withoutComments, @"([^{}]+)\{([^{}]*)\}"))
            {
                string selector = block.Groups[1].Value.Trim();
                if (!selector.Contains("scroll-panel"))
                    continue;

                foreach (Match prop in Regex.Matches(block.Groups[2].Value, @"transition-property\s*:\s*([^;]+);"))
                {
                    checkedDeclarations++;
                    foreach (string animated in prop.Groups[1].Value.Split(','))
                    {
                        string name = animated.Trim();
                        foreach (string layout in layoutProperties)
                        {
                            if (name != layout)
                                continue;

                            layoutTransitions++;
                            Assert.AreEqual(".scroll-panel__paper", selector,
                                $"Rule \"{selector}\" animates the layout property \"{name}\". The scroll pays for a layout pass per frame in exactly ONE place -- the paper's unroll, which the product owner signed off on. A second one is a regression, not a decision.");
                            Assert.AreEqual("height", name,
                                "The paper's one layout transition is its height. Nothing else.");
                        }
                    }
                }
            }

            Assert.Greater(checkedDeclarations, 0,
                "Found no transition-property declarations inside scroll-panel rule blocks -- this test checked nothing.");
            Assert.AreEqual(1, layoutTransitions,
                "Expected exactly one layout-animating rule in the scroll system (the paper's height).");
        }

        /// <summary>
        /// Чернила проступают ПОСЛЕ того, как свиток в основном
        /// развернулся, и уходят ПЕРВЫМИ при закрытии — задержка объявлена
        /// только на открытом состоянии, поэтому базовое (закрытое) правило
        /// гасит текст сразу.
        /// </summary>
        [Test]
        public void InkAppearsAfterThePaperAndLeavesBeforeIt()
        {
            string uss = File.ReadAllText(UssPath);

            string closed = RuleBlock(uss, ".scroll-panel__text");
            StringAssert.Contains("opacity: 0;", closed);
            StringAssert.DoesNotContain("transition-delay", closed,
                "Closing must drop the ink immediately; a delay here would leave text floating over a rolled-up scroll.");

            Assert.IsTrue(Regex.IsMatch(uss, @"\.scroll-panel--open \.scroll-panel__text\s*\{[^}]*transition-delay:\s*0\.3s;"),
                "Opening reveals the ink 0.3s in -- halfway through the unroll.");
        }

        /// <summary>
        /// Заблокированный уровень не показывает приглушённую кнопку
        /// "LOCKED" — у него кнопки нет вовсе, вместо неё шнур. Оба
        /// переключения — чистый USS от ОДНОГО класса, поэтому контроллер
        /// не трогает style.display.
        /// </summary>
        [Test]
        public void LockedStateSwapsTheCtaForACord()
        {
            string uss = File.ReadAllText(UssPath);

            StringAssert.Contains("display: none;", RuleBlock(uss, ".scroll-panel__cord"));
            Assert.IsTrue(Regex.IsMatch(uss, @"\.scroll-panel--locked \.scroll-panel__cord\s*\{[^}]*display:\s*flex;"));
            Assert.IsTrue(Regex.IsMatch(uss, @"\.scroll-panel--locked \.scroll-panel__cta\s*\{[^}]*display:\s*none;"));
            Assert.IsTrue(Regex.IsMatch(uss, @"\.scroll-panel--locked \.scroll-panel__requirement\s*\{[^}]*display:\s*flex;"));
        }

        /// <summary>
        /// Кисточное подчёркивание CTA, а не скруглённая плашка: коробка
        /// кнопки прозрачна и без рамки, три полосы — тот же приём, что у
        /// ".map-topbar__nav-btn-underline__stroke--a/b/c".
        /// </summary>
        [Test]
        public void CtaIsABrushstrokeNotAButtonBox()
        {
            string uss = File.ReadAllText(UssPath);
            string cta = RuleBlock(uss, ".scroll-panel__cta");

            StringAssert.Contains("background-color: rgba(0, 0, 0, 0);", cta);
            StringAssert.Contains("border-width: 0;", cta);
            foreach (string stroke in new[] { "a", "b", "c" })
            {
                StringAssert.Contains($".scroll-panel__cta-underline__stroke--{stroke}", uss);
            }

            int hover = uss.IndexOf(".scroll-panel__cta:hover", System.StringComparison.Ordinal);
            int active = uss.IndexOf(".scroll-panel__cta:active", System.StringComparison.Ordinal);
            Assert.Greater(active, hover,
                "The press state must be declared AFTER the hover state: they have equal specificity, a press almost always happens while hovering, and the later rule wins -- otherwise the button grows on press instead of sinking.");
        }

        /// <summary>
        /// Подложка читаемости обязана быть: на тёмных потёках текстуры
        /// строки 22-23px проваливаются ниже контраста 3:1. Она же заменяет
        /// mix-blend-mode:multiply из макета, которого в UI Toolkit нет.
        /// </summary>
        [Test]
        public void LegibilityWashIsPresent()
        {
            string wash = RuleBlock(File.ReadAllText(UssPath), ".scroll-panel__wash");
            StringAssert.Contains("background-color: rgba(244, 234, 214,", wash);
        }

        /// <summary>
        /// Кисточные шрифты лежат в проекте и действительно подключены. Без
        /// файла ссылка в USS молча падает на шрифт по умолчанию, и весь
        /// смысл редизайна пропадает, не сломав ни одного другого теста.
        /// </summary>
        [Test]
        public void BrushFontsAreShippedAndReferenced()
        {
            string uss = File.ReadAllText(UssPath);
            foreach (string font in new[] { "YujiMai-Latin.ttf", "ShipporiMincho-Latin.ttf" })
            {
                string path = $"Assets/UI/Fonts/{font}";
                Assert.IsTrue(File.Exists(path), $"Missing font: {path}");
                StringAssert.Contains($"url(\"/{path}\")", uss, $"{font} ships but no scroll rule uses it.");
            }
        }

        /// <summary>
        /// Свиток не имеет права трогать ".detail-panel": тот стиль остался
        /// у панели главы Японии, и его правка перекрасила бы её молча (её
        /// сторожат MapHudRedesignTests и JapanMapScreenUxmlTests).
        /// </summary>
        [Test]
        public void ScrollNeverRestylesTheSharedChapterPanel()
        {
            string uss = File.ReadAllText(UssPath);
            string withoutComments = Regex.Replace(uss, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

            foreach (Match block in Regex.Matches(withoutComments, @"([^{}]+)\{[^{}]*\}"))
            {
                string selector = block.Groups[1].Value.Trim();
                if (!selector.Contains("scroll-panel"))
                    continue;

                Assert.IsFalse(selector.Contains("detail-panel"),
                    $"Selector \"{selector}\" mixes the scroll with the Japan chapter panel's shared style -- the two must stay separate systems.");
            }

            StringAssert.DoesNotContain("detail-panel--scroll", File.ReadAllText(UxmlPath),
                "The level popup no longer borrows the chapter panel's class.");
        }

        /// <summary>Returns the body of the FIRST rule block whose selector is exactly <paramref name="selector"/>.</summary>
        private static string RuleBlock(string uss, string selector)
        {
            string withoutComments = Regex.Replace(uss, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            foreach (Match block in Regex.Matches(withoutComments, @"([^{}]+)\{([^{}]*)\}"))
            {
                if (block.Groups[1].Value.Trim() == selector)
                    return block.Groups[2].Value;
            }

            Assert.Fail($"No rule block found for selector \"{selector}\".");
            return string.Empty;
        }
    }
}
