using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Контракт владельца плывущих облаков, читаемый по тексту исходника — по
    /// той же причине, что и остальные source-тесты карты: планировщик и
    /// раскладка UI Toolkit в EditMode не гоняются.
    ///
    /// Главное здесь — разделение писателей. Геометрию пишет ТОЛЬКО
    /// MapWindLayout.Apply и только при смене размера; тик не смеет коснуться
    /// раскладки ни разу.
    /// </summary>
    public class MapWindLayerSourceTests
    {
        private const string SourcePath = "Assets/UI/Map/MapWindLayer.cs";

        [Test]
        public void TickNeverWritesLayoutProperties()
        {
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath),
                "public void Tick(float timeSeconds, float canvasWidth, float canvasHeight, float panX, float panY)");

            StringAssert.DoesNotContain("style.left", body);
            StringAssert.DoesNotContain("style.top", body);
            StringAssert.DoesNotContain("style.width", body);
            StringAssert.DoesNotContain("style.height", body);
            StringAssert.DoesNotContain("style.margin", body);
            StringAssert.DoesNotContain("style.padding", body);

            // Apply пишет ровно те же четыре свойства, только не через "style."
            // в этом файле — без этой строки прямой безусловный вызов Apply из
            // тика прошёл бы все проверки выше.
            StringAssert.DoesNotContain("MapWindLayout.Apply", body,
                "Раскладка идёт только через EnsureLayout, иначе она гоняется каждый кадр.");
        }

        [Test]
        public void TickDelegatesLayoutToAGuardedHelper()
        {
            string source = File.ReadAllText(SourcePath);
            string tick = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(source,
                "public void Tick(float timeSeconds, float canvasWidth, float canvasHeight, float panX, float panY)");
            StringAssert.Contains("EnsureLayout(canvasWidth, canvasHeight)", tick);

            string ensure = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(source,
                "private void EnsureLayout(float canvasWidth, float canvasHeight)");
            StringAssert.Contains("_laidOutWidth", ensure);
            StringAssert.Contains("_laidOutHeight", ensure);
            StringAssert.Contains("return", ensure);

            // Наличия полей и слова return мало: сторож обязан СТОЯТЬ ПЕРЕД
            // раскладкой. Помощник, который кладёт безусловно, а запомненный
            // размер пишет следом, прошёл бы три проверки выше насквозь.
            int guardIndex = ensure.IndexOf("return", System.StringComparison.Ordinal);
            int applyIndex = ensure.IndexOf("MapWindLayout.Apply", System.StringComparison.Ordinal);
            Assert.Greater(applyIndex, -1, "Раскладку кладёт MapWindLayout.Apply.");
            Assert.Less(guardIndex, applyIndex,
                "Ранний возврат по неизменившемуся размеру обязан стоять до раскладки.");
        }

        [Test]
        public void ResetClearsInlineToNullNeverToLiteralZero()
        {
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath), "public void Reset()");

            StringAssert.Contains("style.translate = StyleKeyword.Null", body);
            StringAssert.Contains("style.scale = StyleKeyword.Null", body);
            StringAssert.Contains("style.rotate = StyleKeyword.Null", body);
            StringAssert.Contains("style.opacity = StyleKeyword.Null", body);

            // Считаем именно записи в style, а не любые "= 0f" в теле: Reset
            // законно обнуляет ещё и запомненный размер раскладки.
            int styleWrites = Regex.Matches(body, @"style\.\w+\s*=").Count;
            int nullWrites = Regex.Matches(body, @"style\.\w+\s*=\s*StyleKeyword\.Null").Count;
            Assert.AreEqual(4, styleWrites);
            Assert.AreEqual(styleWrites, nullWrites,
                "Каждое снятие инлайна обязано идти в Null, а не в литеральный ноль.");
        }

        [Test]
        public void ResetAlsoDropsTheLaidOutSizeSoTheNextEntryRelaysOut()
        {
            // Инлайн-геометрия живёт на элементах разметки и переживает уход с
            // экрана. Если Reset не сбросит запомненный размер, повторный вход
            // при изменившемся экране не переразложит слой.
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath), "public void Reset()");
            StringAssert.Contains("_laidOutWidth", body);

            // Именно в ноль: любой другой сентинел мог бы совпасть с реальным
            // размером канваса, а ноль тик отсекает своим же входным сторожем.
            StringAssert.Contains("_laidOutWidth = 0f", body);
            StringAssert.Contains("_laidOutHeight = 0f", body);
        }

        [Test]
        public void EveryCloudGetsDynamicUsageHints()
        {
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath), "public void Bind(VisualElement root, string prefix)");
            StringAssert.Contains("UsageHints.DynamicTransform | UsageHints.DynamicColor", body);
        }

        [Test]
        public void BindPutsTheElementIntoTheStateItsVisibilityFieldClaims()
        {
            // Инлайновый display живёт на элементе разметки и переживает уход с
            // экрана. Bind заявляет полем _visible = true, что слой показан; если
            // при этом на элементе остался display:none с прошлого визита при
            // включённом «меньше движения», ранний выход по visible == _visible в
            // SetVisible превращается в залипание — слой невидим до конца сессии.
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath), "public void Bind(VisualElement root, string prefix)");

            StringAssert.Contains("style.display = DisplayStyle.Flex", body,
                "Bind обязан привести элемент к тому состоянию, которое заявляет полем _visible.");
        }

        [Test]
        public void DoesNotStartASecondScheduler()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("schedule.Execute", source,
                "Единственный планировщик 30 Гц — MapAmbientController.");
        }

        [Test]
        public void SetVisibleTogglesDisplayNotOpacity()
        {
            // display:none вырезает семь прозрачных квадов из цепочки
            // отрисовки; opacity:0 оставил бы их в ней целиком.
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath), "public void SetVisible(bool visible)");
            StringAssert.Contains("DisplayStyle.None", body);
            StringAssert.Contains("DisplayStyle.Flex", body);
        }

        [Test]
        public void IsTheOnlyWriterOfWindTransforms()
        {
            string source = File.ReadAllText(SourcePath);

            Assert.AreEqual(2, Regex.Matches(source, @"style\.translate\s*=").Count,
                "translate плывущих облаков пишут ровно два места: Tick и Reset.");
            Assert.AreEqual(2, Regex.Matches(source, @"style\.scale\s*=").Count);
            Assert.AreEqual(2, Regex.Matches(source, @"style\.rotate\s*=").Count);
            Assert.AreEqual(2, Regex.Matches(source, @"style\.opacity\s*=").Count);
        }

        [Test]
        public void UsesTheSharedParallaxWithItsCeiling()
        {
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath),
                "public void Tick(float timeSeconds, float canvasWidth, float canvasHeight, float panX, float panY)");
            StringAssert.Contains("MapAmbientMath.ParallaxOffset", body,
                "Потолок MaxParallaxOffsetPixels обязан действовать и на ветер.");
        }
    }
}
