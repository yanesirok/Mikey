using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Свиток попапа уровня. Ключевой контракт — раскрытие идёт СМЕЩЕНИЕМ
    /// бумаги внутри контейнера с overflow:hidden, а не анимацией высоты:
    /// высота — это лэйаут каждый кадр, ровно то, чего весь дизайн избегает.
    /// Вариант «scaleY от нуля» тоже отвергнут — он сплющивал бы текст.
    /// </summary>
    public class MapScrollPanelUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/Map/Map.uss";

        [Test]
        public void LevelPanelWrapsItsContentInPaper()
        {
            string uxml = File.ReadAllText(UxmlPath);
            StringAssert.Contains("name=\"level-panel-paper\"", uxml);
            StringAssert.Contains("scroll-panel__paper", uxml);
            StringAssert.Contains("scroll-panel__rod", uxml);
        }

        [Test]
        public void ScrollOpensByTranslateNotHeight()
        {
            string uss = File.ReadAllText(UssPath);
            StringAssert.Contains(".scroll-panel__paper", uss);
            StringAssert.Contains("transition-property: translate", uss);
        }

        [Test]
        public void ScrollPanelClipsItsPaper()
        {
            string uss = File.ReadAllText(UssPath);
            StringAssert.Contains(".detail-panel--scroll", uss);
            StringAssert.Contains("overflow: hidden", uss);
        }

        // ScrollOpensByTranslateNotHeight above is a positive check: it only
        // confirms "transition-property: translate" appears SOMEWHERE in the
        // file. It would stay green even if someone appended
        // "transition-property: translate, height" right next to it -- the
        // exact regression the class comment above warns about. This test is
        // the negative half: it pulls out just the rule BLOCKS that belong to
        // the scroll system (matched by selector, not by scanning the whole
        // file for a substring) and checks the actual list of animated
        // properties inside each one. Scoping to blocks -- rather than
        // grepping the file for "height" -- is what keeps this from tripping
        // on the rod's static "height: 14px", which is a size, not something
        // being transitioned.
        [Test]
        public void ScrollTransitionsNeverAnimateLayoutProperties()
        {
            string uss = File.ReadAllText(UssPath);
            string withoutComments = Regex.Replace(uss, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            string[] forbidden = { "height", "width", "padding", "margin" };
            int checkedDeclarations = 0;

            foreach (Match block in Regex.Matches(withoutComments, @"([^{}]+)\{([^{}]*)\}"))
            {
                string selector = block.Groups[1].Value;
                bool isScrollBlock = selector.Contains("scroll-panel")
                    || selector.Contains("detail-panel--scroll")
                    || selector.Contains("detail-panel--revealed");
                if (!isScrollBlock)
                    continue;

                foreach (Match prop in Regex.Matches(block.Groups[2].Value, @"transition-property\s*:\s*([^;]+);"))
                {
                    checkedDeclarations++;
                    foreach (string animated in prop.Groups[1].Value.Split(','))
                    {
                        string name = animated.Trim();
                        foreach (string bad in forbidden)
                        {
                            Assert.IsFalse(name.Contains(bad),
                                $"Scroll rule \"{selector.Trim()}\" transitions \"{name}\" -- " +
                                "layout properties must never be animated (a full layout pass every frame).");
                        }
                    }
                }
            }

            Assert.Greater(checkedDeclarations, 0,
                "Found no transition-property declarations inside scroll-panel rule blocks -- this test checked nothing.");
        }
    }
}
