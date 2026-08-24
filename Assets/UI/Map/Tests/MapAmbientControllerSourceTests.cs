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

        /// <remarks>
        /// Раньше этот тест искал имена констант где угодно в файле и проходил
        /// бы на коде, где ветки «не экран карты» нет вовсе — имена остались бы
        /// в объявлениях. Проверяется САМА развилка OnScreenChanged.
        /// </remarks>
        [Test]
        public void StopsOutsideMapScreens()
        {
            string source = File.ReadAllText(SourcePath);
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(source, "private void OnScreenChanged(string screenId)");

            StringAssert.Contains("screenId == JapanScreenId || screenId == OkinawaScreenId", body,
                "Ambient must decide it is on a map screen from the two map screen ids, nothing else.");

            int start = body.IndexOf("StartTicking();", System.StringComparison.Ordinal);
            Assert.Greater(start, -1, "Expected the on-a-map-screen branch to start ticking.");

            int elseBranch = body.IndexOf("else", start, System.StringComparison.Ordinal);
            Assert.Greater(elseBranch, start, "Expected an else branch for every screen that is not a map screen.");

            int leave = body.IndexOf("LeaveMapScreen();", elseBranch, System.StringComparison.Ordinal);
            Assert.Greater(leave, elseBranch,
                "Leaving a map screen must stop the tick AND restore the frame rate — otherwise ambient keeps ticking (and the map keeps throttling rendering) on every other screen in the app.");
        }

        /// <remarks>
        /// Раньше проверялось лишь то, что слова "ReducedMotion" и
        /// "IsTransitioning" встречаются в файле, — они встречаются и в
        /// комментариях. Проверяется, что обе проверки СТОЯТ В Tick и
        /// возвращают управление ДО непрерывной части ambient.
        /// </remarks>
        [Test]
        public void RespectsReducedMotionAndTransition()
        {
            string source = File.ReadAllText(SourcePath);
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(source, "private void Tick()");

            int clouds = body.IndexOf("TickClouds();", System.StringComparison.Ordinal);
            int camera = body.IndexOf("TickCamera();", System.StringComparison.Ordinal);
            Assert.Greater(clouds, -1, "Expected Tick to drive the clouds.");
            Assert.Greater(camera, clouds, "Expected Tick to drive the camera after the clouds.");

            int transitionGuard = body.IndexOf("MapCloudTransitionController.IsTransitioning", System.StringComparison.Ordinal);
            Assert.Greater(transitionGuard, -1, "Expected a cross-screen-transition guard in Tick.");
            int transitionReturn = body.IndexOf("return;", transitionGuard, System.StringComparison.Ordinal);
            Assert.Greater(transitionReturn, transitionGuard, "The transition guard must return.");
            Assert.Less(transitionReturn, clouds, "The transition guard must return before any ambient write.");

            int reducedGuard = body.IndexOf("if (liveReducedMotion)", System.StringComparison.Ordinal);
            Assert.Greater(reducedGuard, -1, "Expected a live reduced-motion branch in Tick.");
            int reducedReturn = body.IndexOf("return;", reducedGuard, System.StringComparison.Ordinal);
            Assert.Greater(reducedReturn, reducedGuard, "The reduced-motion branch must return.");
            Assert.Less(reducedReturn, clouds,
                "Reduced motion must return before the clouds and the camera — only the short marker entrance may tick under it.");
        }

        /// <remarks>
        /// Раньше проверялось лишь наличие имени свойства и НЕ проверялись
        /// значения — этот тест прошёл бы и на коде, который возвращает полную
        /// частоту кадров, пока карта ещё открыта (тик под «меньше движения»
        /// сам себя останавливает, и такой читатель платил бы за карту вдвое).
        /// </remarks>
        [Test]
        public void ThrottlesRenderingWhileOnTheMap()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("MapRenderFrameInterval = 2", source,
                "The map's throttled interval must stay 2 — the whole \"ambient costs less than the static map\" claim rests on it.");

            string start = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(source, "private void StartTicking()");
            StringAssert.Contains("OnDemandRendering.renderFrameInterval = MapRenderFrameInterval;", start,
                "Entering a map screen must throttle rendering.");

            string stop = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(source, "private void StopTicking()");
            StringAssert.DoesNotContain("renderFrameInterval", stop,
                "StopTicking also fires while the map is still open (reduced motion turned on; the entrance tick self-stopping under it). Restoring the full frame rate there makes a reduced-motion visit cost double, with nothing on screen moving.");

            string leave = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(source, "private void LeaveMapScreen()");
            StringAssert.Contains("OnDemandRendering.renderFrameInterval = 1;", leave,
                "Actually leaving the map must restore the full frame rate.");
            StringAssert.Contains("StopTicking();", leave,
                "Leaving must also stop the tick and settle everything it moved.");
        }

        /// <remarks>
        /// Дефект финального ревью: StopTicking возвращал в покой камеру, но не
        /// облака. Под «меньше движения», включённым посреди визита, каждое
        /// облако оставалось на своём последнем смещении и не-покойной
        /// прозрачности до конца сессии — инлайн живёт на элементах разметки и
        /// переживает повторный вход на экран.
        /// </remarks>
        [Test]
        public void StopTicking_SettlesEverythingTheTickMoved()
        {
            string source = File.ReadAllText(SourcePath);
            string stop = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(source, "private void StopTicking()");

            StringAssert.Contains("SetAmbientOffset(0f, 0f, 1f)", stop,
                "The camera's ambient offset must go back to rest.");
            StringAssert.Contains("ResetCloudDrift();", stop,
                "The clouds' drift must go back to rest too, or they freeze off-rest for the rest of the session.");

            string reset = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(source, "private void ResetCloudDrift()");
            StringAssert.Contains("cloud.style.translate = StyleKeyword.Null;", reset,
                "The drift offset must be cleared.");
            StringAssert.Contains("cloud.style.opacity = _cloudRestOpacity[i];", reset,
                "Cloud rest opacity is itself an inline style written by MapCloudLayout.Apply — it must be restored by value, never cleared to Null, or the layout's own opacity goes with the drift.");
        }

        /// <remarks>
        /// Вторая половина того же дефекта: живые маркеры замирали посреди
        /// вдоха. Множитель дыхания обязан смотреть на ЖИВУЮ настройку, а не на
        /// защёлкнутую _markerEntranceReducedMotion (та про арифметику каскада).
        /// </remarks>
        [Test]
        public void MarkerBreath_StopsAtRestUnderReducedMotion()
        {
            string source = File.ReadAllText(SourcePath);
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(source, "private void TickMarkers()");

            StringAssert.Contains("bool reducedMotion = _motion != null && _motion.ReducedMotion;", body,
                "TickMarkers must read the live reduced-motion setting.");
            StringAssert.Contains("alive && !reducedMotion", body,
                "Only an unlocked marker with motion allowed may breathe; under reduced motion the factor must be the exact rest value, not whatever phase the tick froze on.");
        }

        /// <remarks>
        /// Дефект финального ревью: прозрачность входа писалась ОБЁРТКЕ
        /// дыхания, родителю одной лишь иконки. Тень и подпись ей сёстры,
        /// поэтому подписи выскакивали непрозрачными на первом кадре, а тень
        /// рисовалась темнее и шире своего покоя, пока пин ещё невидим.
        /// </remarks>
        [Test]
        public void MarkerEntranceOpacity_IsWrittenToTheNode_NotOnlyToTheBreathWrapper()
        {
            string source = File.ReadAllText(SourcePath);
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(source, "private void TickMarkers()");

            StringAssert.Contains("node.style.opacity = opacity;", body,
                "The entrance opacity must land on the marker node, so shadow and label fade in with their own pin.");
            StringAssert.DoesNotContain("breath.style.opacity", body,
                "Keeping the entrance opacity on the breath wrapper as well would square it on the icon and leave the node's siblings out of the entrance again.");

            // The node's own translate is the pin-tip anchor and is not ours.
            StringAssert.DoesNotContain("node.style.translate", body,
                "\"translate: -50% -100%\" anchors the pin tip to its map coordinate — the entrance must never touch it.");
            StringAssert.DoesNotContain("node.style.scale", body,
                "The entrance scale belongs on the breath wrapper, not on the anchored node.");
            StringAssert.Contains("breath.style.translate", body, "The entrance offset stays on the breath wrapper.");
            StringAssert.Contains("breath.style.scale", body, "The entrance/breath scale stays on the breath wrapper.");
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

        /// <summary>
        /// Тень маркера должна оставаться непрозрачной ЦВЕТОМ: видимой альфой
        /// владеет только inline opacity, которую пишет TickMarkers/
        /// ResolveScreenElements. UI Toolkit перемножает opacity элемента на
        /// альфу его цвета — верни альфу в rgba(...), и вместе с inline
        /// opacity 0.35 реальная прозрачность станет втрое бледнее
        /// задуманного (0.35*0.35 = 0.12), а дыхание тени станет практически
        /// неразличимым. Ни один рантайм-тест этого не поймает (сравнивать
        /// пришлось бы с УЖЕ испорченным ожиданием), поэтому проверяем текст
        /// правила напрямую — тот же приём, что и в MapCloudAssetsTests.
        /// </summary>
        [TestCase(".chapter-node__shadow {")]
        [TestCase(".level-node__shadow {")]
        public void MarkerShadowRule_UsesOpaqueColor_AlphaOwnedExclusivelyByInlineOpacity(string selector)
        {
            string uss = System.IO.File.ReadAllText(UssPath);
            string block = ExtractRuleBlock(uss, selector);
            Assert.IsNotNull(block, $"Expected a '{selector}' rule in Map.uss.");
            StringAssert.Contains("background-color: rgb(", block);
            // Matched on the declaration itself, not "DoesNotContain(rgba()"
            // over the whole block: the rule's own comment explains the
            // rgba() pitfall in prose and would otherwise trip this test on
            // its own documentation.
            StringAssert.DoesNotContain("background-color: rgba(", block);
        }

        private const string UssPath = "Assets/UI/Map/Map.uss";

        private static string ExtractRuleBlock(string uss, string header)
        {
            int start = uss.IndexOf(header, System.StringComparison.Ordinal);
            if (start < 0)
                return null;

            int open = uss.IndexOf('{', start);
            if (open < 0)
                return null;

            int depth = 0;
            for (int i = open; i < uss.Length; i++)
            {
                if (uss[i] == '{')
                    depth++;
                else if (uss[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return uss.Substring(open + 1, i - open - 1);
                }
            }
            return null;
        }
        // ---------- плывущий слой облаков (MapWindLayer) ----------

        /// <summary>
        /// Вырезает //-комментарии и схлопывает пробелы. Оба шага несущие:
        /// файл комментирует прозой каждое решение, поэтому проверка по
        /// подстроке без вырезания удовлетворялась бы ЗАКОММЕНТИРОВАННЫМ
        /// вызовом, а схлопывание пробелов даёт право требовать соседства
        /// двух операторов, не завися от переносов строк.
        /// </summary>
        private static string Code(string body)
        {
            string withoutComments = System.Text.RegularExpressions.Regex.Replace(body, @"//[^\r\n]*", string.Empty);
            return System.Text.RegularExpressions.Regex.Replace(withoutComments, @"\s+", " ").Trim();
        }

        [Test]
        public void BindsTheWindLayerForTheScreenItResolved()
        {
            string body = Code(MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath), "private void ResolveScreenElements(string screenId)"));

            StringAssert.Contains("_wind.Bind(", body,
                "The floating layer must be bound to the screen that was just resolved.");
            StringAssert.Contains("_wind.Bind(_root, japan ? \"map-wind-\" : \"okinawa-wind-\")", body,
                "The two prefixes must not be swapped. Asserting that \"map-wind-\" and \"okinawa-wind-\" each occur somewhere passes on a swapped ternary — which binds Okinawa's clouds while Japan is shown, on both screens.");
        }

        /// <remarks>
        /// На прямом переходе Япония↔Окинава StopTicking не зовётся вовсе:
        /// тик прошлого экрана ещё жив, и StartTicking возвращается сразу
        /// (см. её комментарий про _kenBurnsWeight). Значит единственный
        /// момент, когда инлайн прошлого экрана можно снять С ЕГО элементов,
        /// — вплотную до перепривязки слоя на новый экран.
        /// </remarks>
        [Test]
        public void ResetsTheWindLayerBeforeRebindingItToTheNextScreen()
        {
            string body = Code(MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath), "private void ResolveScreenElements(string screenId)"));

            StringAssert.Contains("_wind.Reset(); _wind.Bind(", body,
                "Reset must sit immediately BEFORE the rebind: after it, it would clear the new screen's clouds while the previous screen's inline transform stays on its own clouds until the session ends (Japan<->Okinawa never goes through StopTicking).");
        }

        [Test]
        public void TicksTheWindLayerOnlyWhenMotionIsAllowed()
        {
            string body = Code(MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath), "private void Tick()"));

            int visibility = body.IndexOf("_wind.SetVisible(!liveReducedMotion)", System.StringComparison.Ordinal);
            int reducedBranch = body.IndexOf("if (liveReducedMotion)", System.StringComparison.Ordinal);
            int windTick = body.IndexOf("TickWind()", System.StringComparison.Ordinal);

            Assert.Greater(visibility, -1, "Видимость слоя обязана следовать за настройкой.");
            Assert.Greater(reducedBranch, -1);
            Assert.Greater(windTick, reducedBranch,
                "Ветер обязан тикать ПОСЛЕ ветки раннего выхода по «меньше движения».");
            Assert.Less(visibility, reducedBranch,
                "Слой обязан прятаться до раннего выхода, иначе включённая настройка оставит его на экране.");

            // Both orderings above are satisfiable without the behaviour they
            // stand for, so each is pinned down once more:
            int reducedReturn = body.IndexOf("return;", reducedBranch, System.StringComparison.Ordinal);
            Assert.Greater(reducedReturn, reducedBranch, "The reduced-motion branch must return.");
            Assert.Greater(windTick, reducedReturn,
                "TickWind() must stand after the branch RETURNS, not merely after its header — inside the branch it also stands \"after\" it, and would drive the layer exactly where the setting forbids it.");

            StringAssert.Contains(
                "bool liveReducedMotion = _motion != null && _motion.ReducedMotion; _wind.SetVisible(!liveReducedMotion);",
                body,
                "SetVisible must be the very next statement after the setting is read. Position alone allows hiding it inside another condition (even inside \"if (!liveReducedMotion)\"), which keeps the substring order intact while hiding nothing.");
        }

        [Test]
        public void TickWind_UsesTheAmbientClockAndTheLivePan()
        {
            string body = Code(MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath), "private void TickWind()"));

            StringAssert.Contains(
                "_wind.Tick(_elapsedSeconds, width, height, _panZoom?.CurrentPanX ?? 0f, _panZoom?.CurrentPanY ?? 0f)",
                body,
                "The layer's clock is the ambient _elapsedSeconds: the other elapsed field in this class (_markerEntranceElapsedSeconds) is driven to +Infinity by SettleMarkerEntranceImmediately and would turn every cloud position into NaN. The two pan axes must not be swapped either — the parallax would then run across the gesture.");
        }

        /// <remarks>
        /// Вторая половина: «меньше движения», включённое посреди визита,
        /// останавливает тик из OnMotionSettingsChanged, минуя SetVisible из
        /// Tick.
        /// </remarks>
        [Test]
        public void ReturnsTheWindLayerToRestWhenTickingStops()
        {
            string body = Code(MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath), "private void StopTicking()"));

            StringAssert.Contains("_wind.Reset()", body,
                "Inline transforms live on the markup elements and outlive the visit — the stopped tick must clear its own.");
            StringAssert.Contains("_wind.SetVisible(false)", body,
                "Turning reduced motion on mid-visit stops the tick from OnMotionSettingsChanged, bypassing Tick's SetVisible: without this line the layer stays display:Flex and its seven quads stay in the draw chain, which is the whole saving the setting promises. Hiding is right at all three stop sites — the other two are leaving the map and the tick self-stopping under the same setting.");
        }

    }
}
