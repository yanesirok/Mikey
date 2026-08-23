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
    }
}
