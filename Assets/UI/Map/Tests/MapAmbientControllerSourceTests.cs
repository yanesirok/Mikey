using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Контракт ambient-драйвера карты, читаемый по тексту исходника — по той
    /// же причине, что и остальные source-тесты контроллеров карты: корутины и
    /// планировщик MonoBehaviour в EditMode не гоняются.
    ///
    /// Главный тест здесь — <see cref="NeverWritesLayoutProperties"/>: весь
    /// дизайн держится на том, что анимация не трогает геометрию, и нарушение
    /// этого правила должно ломать сборку, а не всплывать как просадка кадров
    /// на телефоне.
    /// </summary>
    public class MapAmbientControllerSourceTests
    {
        private const string SourcePath = "Assets/UI/Map/MapAmbientController.cs";

        [Test]
        public void NeverWritesLayoutProperties()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("style.left", source);
            StringAssert.DoesNotContain("style.top", source);
            StringAssert.DoesNotContain("style.width", source);
            StringAssert.DoesNotContain("style.height", source);
        }

        [Test]
        public void TicksAtThirtyHertz()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("Every(TickIntervalMs)", source);
            StringAssert.Contains("TickIntervalMs = 33", source);
        }

        [Test]
        public void StopsOutsideMapScreens()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("JapanScreenId", source);
            StringAssert.Contains("OkinawaScreenId", source);
        }

        [Test]
        public void RespectsReducedMotionAndTransition()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("ReducedMotion", source);
            StringAssert.Contains("MapCloudTransitionController.IsTransitioning", source);
        }

        [Test]
        public void ThrottlesRenderingWhileOnTheMap()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("OnDemandRendering.renderFrameInterval", source);
        }
    }
}
