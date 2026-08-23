using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Контроллер одноразовых сцен карты. Две из них подключены к реальным
    /// событиям (вход на карту, переход между главами), две ждут события,
    /// которого в прогрессии пока нет: состояние блокировки глав захардкожено
    /// в MapMarkerLayout.Chapters, а TutorialProgressState про главы карты
    /// ничего не знает. Поэтому у них публичный вход и отладочный триггер, а
    /// не имитация автоматической работы.
    /// </summary>
    public class MapCeremonyControllerSourceTests
    {
        private const string SourcePath = "Assets/UI/Map/MapCeremonyController.cs";
        private const string AmbientPath = "Assets/UI/Map/MapAmbientController.cs";
        private const string TransitionPath = "Assets/UI/Map/MapCloudTransitionController.cs";
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/Map/Map.uss";

        /// <remarks>
        /// Сканирование ТЕКСТА, а не синтаксического дерева: намеренно грубо,
        /// зато не требует парсера и ловит нарушение в любой форме записи.
        /// Обратная сторона — запрещённые имена нельзя упоминать даже в
        /// комментариях проверяемого файла, иначе он уронит сам себя.
        /// </remarks>
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
        public void ExposesEveryCeremonyEntryPoint()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("public void PlayMapEntryInkWash()", source);
            StringAssert.Contains("public void PlayTransitionBlot(string destinationScreenId)", source);
            StringAssert.Contains("public void PlayChapterUnlock(string chapterId)", source);
            StringAssert.Contains("public void PlayLevelCompleteStamp(int index)", source);
        }

        /// <remarks>
        /// Раньше искалось слово "ContextMenu" где угодно в файле — оно есть и
        /// в прозе. Проверяется, что у КАЖДОЙ неподключённой церемонии есть
        /// свой пункт меню И что он зовёт именно её: без источника события в
        /// прогрессии это единственный способ вообще увидеть сцену.
        /// </remarks>
        [TestCase("PlayChapterUnlock(")]
        [TestCase("PlayLevelCompleteStamp(")]
        public void UnwiredCeremoniesCarryADebugTrigger(string entryPoint)
        {
            string source = File.ReadAllText(SourcePath);

            int index = source.IndexOf("[ContextMenu(", System.StringComparison.Ordinal);
            Assert.Greater(index, -1, "No [ContextMenu] debug trigger at all -- this test checked nothing.");

            bool routed = false;
            while (index >= 0 && !routed)
            {
                int end = source.IndexOf(';', index);
                if (end > index)
                    routed = source.Substring(index, end - index).Contains(entryPoint);
                index = source.IndexOf("[ContextMenu(", index + 1, System.StringComparison.Ordinal);
            }

            Assert.IsTrue(routed,
                $"No [ContextMenu] member calls {entryPoint}). Chapter unlock and the level-complete stamp have no progression event to fire them, so an inspector trigger is their only way to be seen or debugged at all.");
        }

        /// <remarks>
        /// Раньше искалось имя "MapCeremonyController.IsPlaying" где угодно в
        /// тексте ambient — включая комментарии. Проверяются обе половины
        /// договора: ambient действительно ВЫХОДИТ по этому флагу до любой
        /// своей записи, и церемония этот флаг действительно держит.
        /// </remarks>
        [Test]
        public void AmbientYieldsWhileACeremonyPlays()
        {
            string ambient = File.ReadAllText(AmbientPath);
            string tick = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(ambient, "private void Tick()");

            int guard = tick.IndexOf("MapCeremonyController.IsPlaying", System.StringComparison.Ordinal);
            Assert.Greater(guard, -1, "Ambient's Tick must check the ceremony flag.");
            int guardReturn = tick.IndexOf("return;", guard, System.StringComparison.Ordinal);
            Assert.Greater(guardReturn, guard, "The ceremony guard must return.");

            int firstWrite = tick.IndexOf("TickMarkers();", System.StringComparison.Ordinal);
            Assert.Greater(firstWrite, -1, "Expected Tick to drive the markers.");
            Assert.Less(guardReturn, firstWrite,
                "The ceremony guard must return before ambient writes anything — a ceremony has to read as one staged move, not as drift laid over it.");

            string ceremony = File.ReadAllText(SourcePath);
            string run = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(ceremony, "private IEnumerator RunCeremony(IEnumerator routine)");
            int raised = run.IndexOf("IsPlaying = true;", System.StringComparison.Ordinal);
            int plays = run.IndexOf("yield return routine;", System.StringComparison.Ordinal);
            int lowered = run.IndexOf("IsPlaying = false;", System.StringComparison.Ordinal);
            Assert.Greater(raised, -1, "The ceremony flag must be raised.");
            Assert.Greater(plays, raised, "The flag must be raised before the ceremony plays.");
            Assert.Greater(lowered, plays, "The flag must be lowered only after the ceremony has finished.");
        }

        /// <remarks>
        /// Регрессия ревью T14/1: бриф хардкодил печать разблокировки на
        /// узел Фукуоки независимо от переданного chapterId. Проверяем И
        /// отсутствие захардкоженного имени, И наличие резолюции из
        /// параметра -- по отдельности первая половина ловит "убрали
        /// хардкод, но не заменили его резолюцией", вторая -- "резолюция
        /// есть, но хардкод рядом остался".
        /// </remarks>
        [Test]
        public void ChapterUnlockSealResolvesNodeFromTheRequestedChapterId()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("\"chapter-node-fukuoka\"", source);
            StringAssert.Contains("$\"chapter-node-{chapterId}\"", source);
        }

        /// <remarks>
        /// Регрессия ревью T14/2: бриф множил долю на константу 1000f вместо
        /// фактической ширины слоя облаков. Прежняя формулировка запрещала
        /// голый литерал "1000f" где угодно в файле — это ничего не
        /// доказывало (любая другая константа прошла бы) и однажды сработало
        /// бы вхолостую на постороннем числе. Проверяется настоящая гарантия:
        /// КАЖДАЯ запись смещения слоя посчитана от измеренной ширины.
        /// </remarks>
        [Test]
        public void CloudDivergenceUsesTheCloudLayersActualWidth()
        {
            string source = File.ReadAllText(SourcePath);
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                source, "private IEnumerator PlayChapterUnlockRoutine(string chapterId, float sourceX, float sourceY)");

            StringAssert.Contains("layer.resolvedStyle.width", body,
                "The divergence must measure the cloud layer it is about to move.");

            const string Write = "style.translate = new Translate(";
            int index = body.IndexOf(Write, System.StringComparison.Ordinal);
            Assert.Greater(index, -1, "No cloud-layer translate write found -- this test checked nothing.");

            int writes = 0;
            while (index >= 0)
            {
                int end = body.IndexOf(';', index);
                Assert.Greater(end, index, "Unterminated translate write.");
                StringAssert.Contains("layerWidth", body.Substring(index, end - index),
                    "Every divergence offset must scale with the layer's measured width, so a tablet and a narrow phone part by the same FRACTION rather than by the same number of points.");
                writes++;
                index = body.IndexOf(Write, index + Write.Length, System.StringComparison.Ordinal);
            }

            Assert.AreEqual(2, writes,
                "Expected both the outward and the returning halves of the divergence to be written -- if one disappears the clouds never come back.");
        }

        /// <remarks>
        /// Дефект финального ревью: "map-inkwash" существовал только внутри
        /// японского экрана, а PlayJapanToOkinawa звал кляксу прямо ПЕРЕД
        /// Show("mapOkinawa") — весь 0.3-секундный размыв играл в поддереве,
        /// которое та же подмена скрывала (".screen { display: none }"), то
        /// есть был не виден вовсе. Сторож держит связку «клякса ставится на
        /// тот же экран, что уходит в Show».
        /// </remarks>
        [TestCase("public IEnumerator PlayJapanToOkinawa()")]
        [TestCase("public IEnumerator PlayOkinawaToJapan()")]
        public void TransitionBlotPlaysOnTheScreenThatIsAboutToBeShown(string signature)
        {
            string source = File.ReadAllText(TransitionPath);
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(source, signature);

            const string Blot = "PlayTransitionBlot(\"";
            int blot = body.IndexOf(Blot, System.StringComparison.Ordinal);
            Assert.Greater(blot, -1, "Expected the transition blot to be played -- this test checked nothing.");
            int blotEnd = body.IndexOf('"', blot + Blot.Length);
            string blotScreen = body.Substring(blot + Blot.Length, blotEnd - blot - Blot.Length);

            const string Show = "Show(\"";
            int show = body.IndexOf(Show, blot, System.StringComparison.Ordinal);
            Assert.Greater(show, blot, "Expected the screen swap to follow the blot.");
            int showEnd = body.IndexOf('"', show + Show.Length);
            string shownScreen = body.Substring(show + Show.Length, showEnd - show - Show.Length);

            Assert.AreEqual(shownScreen, blotScreen,
                "The blot must be played on the screen that is about to be SHOWN. Played on the screen being hidden, the whole dissolve runs inside a subtree the same swap turns off.");
        }

        /// <remarks>
        /// Вторая половина той же связки: у каждого экрана карты обязана быть
        /// СВОЯ копия ink-wash, иначе выбор по экрану назначения молча
        /// возвращает null и клякса просто не играет.
        /// </remarks>
        [TestCase("map-inkwash")]
        [TestCase("okinawa-inkwash")]
        public void EveryMapScreenCarriesItsOwnInkWash(string elementName)
        {
            string uxml = File.ReadAllText(UxmlPath);
            StringAssert.Contains($"name=\"{elementName}\" class=\"map-inkwash\"", uxml,
                $"'{elementName}' is missing from MikeyApp.uxml -- MapCeremonyController.ResolveInkWash would return null and the ceremony would silently not play on that screen.");

            string controller = File.ReadAllText(SourcePath);
            StringAssert.Contains($"\"{elementName}\"", controller,
                $"MapCeremonyController must resolve '{elementName}'.");
        }

        /// <remarks>
        /// Дефект финального ревью: три пятна 90%x140% сцены со сплошным
        /// цветом фона держались "opacity: 0" в покое. Прозрачный элемент всё
        /// равно отдаёт геометрию — из цепочки отрисовки вырезает только
        /// "display: none". Плюс проверка того, что смена display и старт
        /// перехода по-прежнему разнесены на кадр: в одном кадре переход не
        /// стартует, и на этом план уже обжигался.
        /// </remarks>
        [Test]
        public void InkWashCostsNothingAtRest_AndStillGetsAFrameBeforeItDissolves()
        {
            string uss = File.ReadAllText(UssPath);

            Assert.IsTrue(Regex.IsMatch(uss, @"\.map-inkwash\s*\{[^}]*display:\s*none;"),
                "The ink-wash layer must be display:none at rest -- \"opacity: 0\" still emits geometry, and three near-full-screen opaque blots are ~3.8 screens of transparent overdraw on the most loaded screen in the app.");
            Assert.IsTrue(Regex.IsMatch(uss, @"\.map-inkwash--playing\s*\{[^}]*display:\s*flex;"),
                "The playing state must put the layer back into the render chain.");

            string source = File.ReadAllText(SourcePath);
            string routine = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                source, "private IEnumerator PlayInkWashRoutine(VisualElement inkWash, float dissolveSeconds)");

            int playing = routine.IndexOf("AddToClassList(InkWashPlayingClass);", System.StringComparison.Ordinal);
            Assert.Greater(playing, -1, "Expected the playing class to be added.");
            int frame = routine.IndexOf("yield return null;", playing, System.StringComparison.Ordinal);
            Assert.Greater(frame, playing, "Expected a frame to pass after the layer becomes displayed.");
            int dissolving = routine.IndexOf("AddToClassList(InkWashDissolvingClass);", frame, System.StringComparison.Ordinal);
            Assert.Greater(dissolving, frame,
                "The dissolve class must be added a frame AFTER the display change -- a display flip and a transition start in the same frame is exactly the failure this plan already hit once.");
        }
    }
}
