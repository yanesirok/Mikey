using System.IO;
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
    }
}
