using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Задача 13 нашла и закрыла дефект задачи 12: начальное скрытое
    /// состояние каскада было объявлено только для потомков
    /// ".scroll-panel__paper", так что панель главы (у неё нет бумаги — она
    /// выезжает целиком) не имела скрытого состояния, из которого можно
    /// проявиться. Класс "detail-panel--revealed" просто включал уже
    /// видимый текст — правила на месте, класс ставится, каскада нет, и
    /// обычный прогон тестов этого не показывает. Эти тесты — сторож
    /// именно от возврата к той сломанной форме, плюс контракт нового
    /// "уход карты вглубь" (".pan-stage--pushed"). Не дублирует
    /// MapScrollPanelUxmlTests — тот класс проверяет контракт свитка
    /// (translate, не height); этот — что каскад и уход общие для обеих
    /// панелей и не откатились по забывчивости.
    /// </summary>
    public class MapDetailPanelCascadeTests
    {
        private const string UssPath = "Assets/UI/Map/Map.uss";

        private static string ReadUssWithoutComments()
        {
            string uss = File.ReadAllText(UssPath);
            return Regex.Replace(uss, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        }

        /// <summary>Selector list -> declaration body for every rule block in the file.</summary>
        private static System.Collections.Generic.List<(string Selector, string Body)> ParseBlocks(string uss)
        {
            var blocks = new System.Collections.Generic.List<(string, string)>();
            foreach (Match block in Regex.Matches(uss, @"([^{}]+)\{([^{}]*)\}"))
                blocks.Add((block.Groups[1].Value, block.Groups[2].Value));
            return blocks;
        }

        [Test]
        public void InitialHiddenState_ScopesToAnyDetailPanel_NotJustScrollPaper()
        {
            string uss = ReadUssWithoutComments();

            // Негативная половина: старая, сломанная форма скоупа не
            // должна вернуться. Если она снова появится, панель главы
            // опять останется ничем не скрытой.
            StringAssert.DoesNotContain(".scroll-panel__paper .detail-panel__eyebrow", uss);
            StringAssert.DoesNotContain(".scroll-panel__paper .detail-panel__title", uss);
            StringAssert.DoesNotContain(".scroll-panel__paper .detail-panel__desc", uss);
            StringAssert.DoesNotContain(".scroll-panel__paper .detail-panel__cta", uss);

            // Позитивная половина: скрытое состояние объявлено на общем
            // ".detail-panel" -- родительском классе, который несут ОБЕ
            // панели (chapter-panel в MikeyApp.uxml -- напрямую; level-panel
            // -- через ".detail-panel.detail-panel--scroll"), включая
            // ".detail-panel__meta" (счётчик уровней, есть только у панели
            // главы, но должен быть скрыт тем же правилом).
            var blocks = ParseBlocks(uss);
            bool found = false;
            foreach (var (selector, body) in blocks)
            {
                if (!selector.Contains(".detail-panel .detail-panel__eyebrow"))
                    continue;
                found = true;
                StringAssert.Contains(".detail-panel .detail-panel__title", selector);
                StringAssert.Contains(".detail-panel .detail-panel__desc", selector);
                StringAssert.Contains(".detail-panel .detail-panel__meta", selector);
                StringAssert.Contains(".detail-panel .detail-panel__cta", selector);
                StringAssert.Contains("opacity: 0;", body);
            }
            Assert.IsTrue(found, "No initial-hidden-state rule scoped to \".detail-panel .detail-panel__eyebrow\" -- this test checked nothing.");
        }

        [Test]
        public void ScrollCascadeDelays_RemainExactlyAsTask12Approved()
        {
            // Свиток не трогать: те же пять значений, что задача 12 уже
            // одобрила. Если эти строки исчезнут или изменятся, каскад
            // свитка либо разъедется по времени, либо тест это поймает
            // раньше человека.
            string uss = ReadUssWithoutComments();
            StringAssert.Contains(".detail-panel--revealed .detail-panel__eyebrow", uss);
            Assert.IsTrue(Regex.IsMatch(uss, @"\.detail-panel--revealed \.detail-panel__eyebrow\s*\{[^}]*transition-delay:\s*0\.20s;"));
            Assert.IsTrue(Regex.IsMatch(uss, @"\.detail-panel--revealed \.detail-panel__title\s*\{[^}]*transition-delay:\s*0\.24s;"));
            Assert.IsTrue(Regex.IsMatch(uss, @"\.detail-panel--revealed \.detail-panel__subtitle\s*\{[^}]*transition-delay:\s*0\.28s;"));
            Assert.IsTrue(Regex.IsMatch(uss, @"\.detail-panel--revealed \.detail-panel__desc\s*\{[^}]*transition-delay:\s*0\.32s;"));
            Assert.IsTrue(Regex.IsMatch(uss, @"\.detail-panel--revealed \.detail-panel__cta\s*\{[^}]*transition-delay:\s*0\.36s;"));
        }

        [Test]
        public void ChapterMetaCascade_RevealsBetweenDescAndCta()
        {
            // ".detail-panel__meta" -- только у панели главы. Слот между
            // ".desc" (0.32s) и ".cta" (0.36s) держит порядок чтения
            // панели (eyebrow/title/desc/meta/cta сверху вниз), не отнимая
            // слот у ".subtitle" (0.28s), который принадлежит свитку.
            string uss = ReadUssWithoutComments();
            Assert.IsTrue(Regex.IsMatch(uss, @"\.detail-panel--revealed \.detail-panel__meta\s*\{[^}]*transition-delay:\s*0\.34s;"),
                "Expected \".detail-panel--revealed .detail-panel__meta\" with transition-delay: 0.34s (between .desc's 0.32s and .cta's 0.36s).");
        }

        [Test]
        public void ChapterPanelSlidesInWithSpringEasing_SameDurationAsBefore()
        {
            // Только функция сглаживания поменялась на пружину; длительность
            // выезда (0.25s) — нет, это не часть этой задачи.
            string uss = ReadUssWithoutComments();
            var blocks = ParseBlocks(uss);
            bool found = false;
            foreach (var (selector, body) in blocks)
            {
                if (selector.Trim() != ".detail-panel")
                    continue;
                found = true;
                StringAssert.Contains("transition-duration: 0.25s;", body);
                StringAssert.Contains("transition-timing-function: ease-out-back;", body);
            }
            Assert.IsTrue(found, "No base \".detail-panel\" rule block found -- this test checked nothing.");
        }

        [Test]
        public void PanStagePush_ScalesSceneAndDarkensScrim()
        {
            string uss = ReadUssWithoutComments();
            StringAssert.Contains(".pan-stage--pushed", uss);
            Assert.IsTrue(Regex.IsMatch(uss, @"\.pan-stage--pushed\s*\{[^}]*scale:\s*0\.985;"));
            Assert.IsTrue(Regex.IsMatch(uss, @"\.pan-stage--pushed \.pan-canvas-scrim\s*\{[^}]*background-color:"));
        }

        /// <summary>
        /// Тот же дух, что MapScrollPanelUxmlTests.ScrollTransitionsNeverAnimateLayoutProperties
        /// (негативная проверка по блокам, не по всему файлу подряд), но
        /// для новых блоков ".pan-stage"/".pan-canvas-scrim" -- constraint
        /// плана "translate, scale, rotate, opacity, цвет — и никогда
        /// height/width/padding/margin" распространяется и на уход карты
        /// вглубь, а не только на свиток.
        /// </summary>
        [Test]
        public void PanStagePushTransitions_NeverAnimateLayoutProperties()
        {
            string uss = ReadUssWithoutComments();
            string[] forbidden = { "height", "width", "padding", "margin" };
            int checkedDeclarations = 0;

            foreach (var (selector, body) in ParseBlocks(uss))
            {
                bool isPushBlock = selector.Contains(".pan-stage") || selector.Contains(".pan-canvas-scrim");
                if (!isPushBlock)
                    continue;

                foreach (Match prop in Regex.Matches(body, @"transition-property\s*:\s*([^;]+);"))
                {
                    checkedDeclarations++;
                    foreach (string animated in prop.Groups[1].Value.Split(','))
                    {
                        string name = animated.Trim();
                        foreach (string bad in forbidden)
                        {
                            Assert.IsFalse(name.Contains(bad),
                                $"Pan-stage-push rule \"{selector.Trim()}\" transitions \"{name}\" -- " +
                                "layout properties must never be animated (a full layout pass every frame).");
                        }
                    }
                }
            }

            Assert.Greater(checkedDeclarations, 0,
                "Found no transition-property declarations inside .pan-stage/.pan-canvas-scrim rule blocks -- this test checked nothing.");
        }
    }
}
