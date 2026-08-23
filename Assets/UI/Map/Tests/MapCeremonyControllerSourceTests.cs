using System.IO;
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
            StringAssert.Contains("public void PlayTransitionBlot()", source);
            StringAssert.Contains("public void PlayChapterUnlock(string chapterId)", source);
            StringAssert.Contains("public void PlayLevelCompleteStamp(int index)", source);
        }

        [Test]
        public void UnwiredCeremoniesCarryADebugTrigger()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("ContextMenu", source);
        }

        [Test]
        public void AmbientYieldsWhileACeremonyPlays()
        {
            string source = File.ReadAllText(AmbientPath);
            StringAssert.Contains("MapCeremonyController.IsPlaying", source);
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
        /// Регрессия ревью T14/2: бриф множил долю на константу 1000f
        /// вместо фактической ширины слоя облаков. Симметрично предыдущему
        /// тесту: отсутствие константы плюс наличие чтения resolvedStyle.
        /// </remarks>
        [Test]
        public void CloudDivergenceUsesTheCloudLayersActualWidth()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("1000f", source);
            StringAssert.Contains("resolvedStyle.width", source);
        }
    }
}
