using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// То немногое в контракте плывущего слоя, чего НЕ видно по поведению.
    ///
    /// Всё остальное переехало в MapWindLayerTests и проверяется вызовом
    /// методов на настоящем дереве: греп по тексту пропускал перепутанные оси
    /// параллакса, инверсию SetVisible и раскладку каждый кадр — мутации по
    /// всем трём выживали при зелёном наборе. Здесь остались две проверки, у
    /// которых наблюдаемого следа нет в принципе: отсутствие второго
    /// планировщика и число мест, пишущих трансформ облаков.
    /// </summary>
    public class MapWindLayerSourceTests
    {
        private const string SourcePath = "Assets/UI/Map/MapWindLayer.cs";

        [Test]
        public void DoesNotStartASecondScheduler()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("schedule.Execute", source,
                "Единственный планировщик 30 Гц — MapAmbientController.");
        }

        [Test]
        public void IsTheOnlyWriterOfWindTransforms()
        {
            // Поведение показывает ЧТО записано, но не СКОЛЬКО мест это пишет.
            // Третий писатель разошёлся бы с остальными молча.
            string source = File.ReadAllText(SourcePath);

            Assert.AreEqual(2, Regex.Matches(source, @"style\.translate\s*=").Count,
                "translate плывущих облаков пишут ровно два места: Tick и Reset.");
            Assert.AreEqual(2, Regex.Matches(source, @"style\.scale\s*=").Count);
            Assert.AreEqual(2, Regex.Matches(source, @"style\.rotate\s*=").Count);
            Assert.AreEqual(2, Regex.Matches(source, @"style\.opacity\s*=").Count);
        }
    }
}
