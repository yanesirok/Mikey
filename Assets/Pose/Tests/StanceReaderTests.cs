using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class StanceReaderTests
    {
        private static StanceReading Read(StanceTestFrames frames, StanceKind kind) =>
            StanceReader.Read(frames.Build(), StanceSpec.For(kind));

        [Test]
        public void ReferenceZenkutsuIsRecognized()
        {
            StanceReading r = Read(StanceTestFrames.Zenkutsu(), StanceKind.Zenkutsu);

            Assert.IsTrue(r.Readable);
            Assert.AreEqual(string.Empty, r.Fault);
            Assert.AreEqual(StanceKind.Zenkutsu, r.Kind);
            Assert.AreEqual(2.4f, r.Length01, 0.01f);
            Assert.AreEqual(115f, r.FrontKneeDeg, 0.5f);
            Assert.AreEqual(170f, r.BackKneeDeg, 0.5f);
            Assert.AreEqual(0f, r.TorsoLeanDeg, 0.5f);
            Assert.AreEqual(1f, r.ForwardSign, 1e-6f);
            Assert.IsTrue(r.FrontIsLeft);
        }

        [Test]
        public void ReferenceFudoIsRecognized()
        {
            StanceReading r = Read(StanceTestFrames.Fudo(), StanceKind.Fudo);

            Assert.IsTrue(r.Readable);
            Assert.AreEqual(string.Empty, r.Fault);
            Assert.AreEqual(StanceKind.Fudo, r.Kind);
            Assert.AreEqual(1.3f, r.Length01, 0.01f);
            Assert.AreEqual(160f, r.FrontKneeDeg, 0.5f);
            Assert.AreEqual(160f, r.BackKneeDeg, 0.5f);
        }

        [TestCase(1.6f, 115f, 170f, 0f, "Шире шаг")]
        [TestCase(3.2f, 115f, 170f, 0f, "Короче шаг")]
        [TestCase(2.4f, 150f, 170f, 0f, "Согни переднее колено")]
        [TestCase(2.4f, 90f, 170f, 0f, "Не проседай")]
        [TestCase(2.4f, 115f, 140f, 0f, "Выпрями заднюю")]
        [TestCase(2.4f, 115f, 170f, 25f, "Корпус прямо")]
        public void EachZenkutsuFaultSpeaksItsOwnPhrase(float length, float front, float back, float lean, string phrase)
        {
            var f = StanceTestFrames.Zenkutsu();
            f.Length01 = length;
            f.FrontKneeDeg = front;
            f.BackKneeDeg = back;
            f.TorsoLeanDeg = lean;

            StanceReading r = Read(f, StanceKind.Zenkutsu);

            Assert.IsTrue(r.Readable);
            Assert.AreEqual(phrase, r.Fault);
            Assert.AreEqual(StanceKind.None, r.Kind);       // с ошибкой стойки нет
        }

        [TestCase(0.7f, 160f, 160f, 0f, "Шире")]
        [TestCase(2.0f, 160f, 160f, 0f, "Уже")]
        [TestCase(1.3f, 160f, 178f, 0f, "Согни колени")]    // окно одно на обе ноги
        [TestCase(1.3f, 120f, 160f, 0f, "Выше")]            // сел в фудо как в присед
        [TestCase(1.3f, 160f, 160f, 20f, "Корпус прямо")]
        public void EachFudoFaultSpeaksItsOwnPhrase(float length, float front, float back, float lean, string phrase)
        {
            var f = StanceTestFrames.Fudo();
            f.Length01 = length;
            f.FrontKneeDeg = front;
            f.BackKneeDeg = back;
            f.TorsoLeanDeg = lean;

            StanceReading r = Read(f, StanceKind.Fudo);

            Assert.IsTrue(r.Readable);
            Assert.AreEqual(phrase, r.Fault);
            Assert.AreEqual(StanceKind.None, r.Kind);
        }

        [Test]
        public void TwoFaultsAtOnceNameTheGrosserOne()
        {
            var shortAndStraight = StanceTestFrames.Zenkutsu();
            shortAndStraight.Length01 = 1.6f;
            shortAndStraight.FrontKneeDeg = 150f;
            Assert.AreEqual("Шире шаг", Read(shortAndStraight, StanceKind.Zenkutsu).Fault);

            var straightAndLeaning = StanceTestFrames.Zenkutsu();
            straightAndLeaning.FrontKneeDeg = 150f;
            straightAndLeaning.TorsoLeanDeg = 25f;
            Assert.AreEqual("Согни переднее колено", Read(straightAndLeaning, StanceKind.Zenkutsu).Fault);
        }

        [Test]
        public void MirroredStanceReadsTheSame()
        {
            StanceReading r = Read(StanceTestFrames.Zenkutsu(mirrored: true), StanceKind.Zenkutsu);

            Assert.IsTrue(r.Readable);
            Assert.AreEqual(string.Empty, r.Fault);
            Assert.AreEqual(StanceKind.Zenkutsu, r.Kind);
            Assert.AreEqual(2.4f, r.Length01, 0.01f);
            Assert.AreEqual(115f, r.FrontKneeDeg, 0.5f);
            Assert.AreEqual(170f, r.BackKneeDeg, 0.5f);
            Assert.AreEqual(-1f, r.ForwardSign, 1e-6f);
            Assert.IsFalse(r.FrontIsLeft);                  // зеркально — впереди правая
        }

        [Test]
        public void MirroredFudoReadsTheSame()
        {
            StanceReading r = Read(StanceTestFrames.Fudo(mirrored: true), StanceKind.Fudo);

            Assert.AreEqual(string.Empty, r.Fault);
            Assert.AreEqual(StanceKind.Fudo, r.Kind);
            Assert.AreEqual(-1f, r.ForwardSign, 1e-6f);
        }

        [Test]
        public void HiddenFeetAreNotReadableAndNotAFault()
        {
            var f = StanceTestFrames.Zenkutsu();
            f.FootVisibility = 0.1f;                        // стопы за кадром/перекрыты

            StanceReading r = Read(f, StanceKind.Zenkutsu);

            Assert.IsFalse(r.Readable);
            Assert.AreEqual(StanceKind.None, r.Kind);
            Assert.AreEqual(string.Empty, r.Fault);         // ложную ошибку не выдумываем
            Assert.AreEqual(0f, r.ForwardSign, 1e-6f);
        }

        [Test]
        public void HiddenBodyIsNotReadable()
        {
            var f = StanceTestFrames.Zenkutsu();
            f.Visibility = 0.2f;

            StanceReading r = Read(f, StanceKind.Zenkutsu);

            Assert.IsFalse(r.Readable);
            Assert.AreEqual(string.Empty, r.Fault);
        }

        [Test]
        public void FudoIsTooShortToPassAsZenkutsu()
        {
            StanceReading r = Read(StanceTestFrames.Fudo(), StanceKind.Zenkutsu);

            Assert.AreEqual(StanceKind.None, r.Kind);
            Assert.AreEqual("Шире шаг", r.Fault);
        }

        [Test]
        public void ReadingIsInvariantToBodySizeAndPositionInFrame()
        {
            var f = StanceTestFrames.Zenkutsu();
            f.Shank = 0.15f;                                // ниже ростом
            f.OffsetX = 0.2f;                               // и стоит в другом углу кадра

            StanceReading r = Read(f, StanceKind.Zenkutsu);

            Assert.AreEqual(string.Empty, r.Fault);
            Assert.AreEqual(2.4f, r.Length01, 0.01f);
            Assert.AreEqual(0.15f, r.Shank, 1e-4f);
        }
    }
}
