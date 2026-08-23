using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// MapNodeFeedback — одноразовые эффекты маркера (появление каскадом,
    /// отказ у заблокированного, волна от тапа). Отдельно от ambient-драйвера
    /// сознательно: тот про непрерывное движение, эти три — про реакцию на
    /// событие.
    /// </summary>
    public class MapNodeFeedbackSourceTests
    {
        private const string SourcePath = "Assets/UI/Map/MapNodeFeedback.cs";

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
        public void CascadeStaggersByIndex()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("CascadeStepMs", source);
            StringAssert.Contains("transitionDelay", source);
        }

        [Test]
        public void CascadeCollapsesUnderReducedMotion()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("reducedMotion", source);
            StringAssert.Contains("ReducedClass", source);
        }
    }
}
