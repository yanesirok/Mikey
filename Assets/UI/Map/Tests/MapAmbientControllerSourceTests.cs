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
        private const string MathSourcePath = "Assets/UI/Map/MapAmbientMath.cs";

        [Test]
        public void NeverWritesLayoutProperties()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("style.left", source);
            StringAssert.DoesNotContain("style.top", source);
            StringAssert.DoesNotContain("style.width", source);
            StringAssert.DoesNotContain("style.height", source);
            StringAssert.DoesNotContain("style.margin", source);
            StringAssert.DoesNotContain("style.padding", source);
            StringAssert.DoesNotContain("style.fontSize", source);
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

        [Test]
        public void SubscribesToMotionSettingsChanged()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("Changed += OnMotionSettingsChanged", source);
            StringAssert.Contains("Changed -= OnMotionSettingsChanged", source);
        }

        /// <summary>
        /// В ResolveScreenElements массив суффиксов имён облаков и массив их
        /// прозрачностей покоя спарены по индексу. Перестановка одного
        /// массива относительно другого молча спарит движение одного облака
        /// с прозрачностью покоя другого — ни один рантайм-тест этого не
        /// поймает, поэтому проверяем по тексту, что оба массива перечисляют
        /// облака в одном и том же порядке (right-01/Right1, left-01/Left1,
        /// left-02/Left2, bottom-01/Bottom1).
        /// </summary>
        [Test]
        public void ResolveScreenElements_CloudSuffixOrder_MatchesRestOpacityFieldOrder()
        {
            string source = File.ReadAllText(SourcePath);

            int suffixesStart = source.IndexOf("string[] suffixes = {", System.StringComparison.Ordinal);
            int suffixesEnd = source.IndexOf("};", suffixesStart, System.StringComparison.Ordinal);
            Assert.Greater(suffixesStart, -1, "Expected the cloud suffix array literal.");
            Assert.Greater(suffixesEnd, suffixesStart);
            string suffixesText = source.Substring(suffixesStart, suffixesEnd - suffixesStart);

            int opacityStart = source.IndexOf("restOpacity =", suffixesEnd, System.StringComparison.Ordinal);
            int opacityEnd = source.IndexOf("};", opacityStart, System.StringComparison.Ordinal);
            Assert.Greater(opacityStart, -1, "Expected the rest-opacity array literal.");
            Assert.Greater(opacityEnd, opacityStart);
            string opacityText = source.Substring(opacityStart, opacityEnd - opacityStart);

            string[] expectedSuffixOrder = { "right-01", "left-01", "left-02", "bottom-01" };
            string[] expectedPresetFieldOrder = { "Right1", "Left1", "Left2", "Bottom1" };

            int lastSuffixIndex = -1;
            int lastFieldIndex = -1;
            for (int i = 0; i < expectedSuffixOrder.Length; i++)
            {
                int suffixIndex = suffixesText.IndexOf("\"" + expectedSuffixOrder[i] + "\"", System.StringComparison.Ordinal);
                int fieldIndex = opacityText.IndexOf("preset." + expectedPresetFieldOrder[i] + ".Opacity", System.StringComparison.Ordinal);
                Assert.Greater(suffixIndex, lastSuffixIndex, $"Suffix '{expectedSuffixOrder[i]}' out of order in the suffixes array.");
                Assert.Greater(fieldIndex, lastFieldIndex, $"Preset field '{expectedPresetFieldOrder[i]}' out of order in the restOpacity array.");
                lastSuffixIndex = suffixIndex;
                lastFieldIndex = fieldIndex;
            }
        }

        /// <summary>
        /// MapAmbientMath.CloudParallaxFactors — четвёртый позиционный массив
        /// «на облако», спаренный по индексу с _clouds[i] и suffixes[i] (см.
        /// TickClouds: <c>MapAmbientMath.CloudParallaxFactors[i]</c>). Тот же
        /// риск тихой перестановки, что и у пары suffixes/restOpacity выше —
        /// проверяем тем же приёмом, что оба массива перечисляют облака в
        /// одном порядке (right-01, left-01, left-02, bottom-01).
        /// </summary>
        [Test]
        public void ResolveScreenElements_CloudSuffixOrder_MatchesParallaxFactorOrder()
        {
            string controllerSource = File.ReadAllText(SourcePath);

            int suffixesStart = controllerSource.IndexOf("string[] suffixes = {", System.StringComparison.Ordinal);
            int suffixesEnd = controllerSource.IndexOf("};", suffixesStart, System.StringComparison.Ordinal);
            Assert.Greater(suffixesStart, -1, "Expected the cloud suffix array literal.");
            Assert.Greater(suffixesEnd, suffixesStart);
            string suffixesText = controllerSource.Substring(suffixesStart, suffixesEnd - suffixesStart);

            string mathSource = File.ReadAllText(MathSourcePath);
            int factorsStart = mathSource.IndexOf("CloudParallaxFactors = {", System.StringComparison.Ordinal);
            int factorsEnd = mathSource.IndexOf("};", factorsStart, System.StringComparison.Ordinal);
            Assert.Greater(factorsStart, -1, "Expected the CloudParallaxFactors array literal.");
            Assert.Greater(factorsEnd, factorsStart);
            string factorsText = mathSource.Substring(factorsStart, factorsEnd - factorsStart);

            string[] expectedSuffixOrder = { "right-01", "left-01", "left-02", "bottom-01" };
            string[] expectedFactorLiterals = { "1.04f", "1.06f", "1.10f", "1.12f" };

            int lastSuffixIndex = -1;
            int lastFactorIndex = -1;
            for (int i = 0; i < expectedSuffixOrder.Length; i++)
            {
                int suffixIndex = suffixesText.IndexOf("\"" + expectedSuffixOrder[i] + "\"", System.StringComparison.Ordinal);
                int factorIndex = factorsText.IndexOf(expectedFactorLiterals[i], System.StringComparison.Ordinal);
                Assert.Greater(suffixIndex, lastSuffixIndex, $"Suffix '{expectedSuffixOrder[i]}' out of order in the suffixes array.");
                Assert.Greater(factorIndex, lastFactorIndex, $"Parallax factor '{expectedFactorLiterals[i]}' out of order in CloudParallaxFactors.");
                lastSuffixIndex = suffixIndex;
                lastFactorIndex = factorIndex;
            }
        }
    }
}
