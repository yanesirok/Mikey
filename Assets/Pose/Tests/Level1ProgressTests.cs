using NUnit.Framework;
using UnityEngine;

namespace Mikey.Pose.Tests
{
    public class Level1ProgressTests
    {
        private const string PrefsKey = "level1.progress";

        // Ключ чистится и до, и после: тесты пишут в общий PlayerPrefs процесса
        // и иначе цепляются друг за друга и за реальный прогресс на машине.
        [SetUp]
        public void SetUp() => PlayerPrefs.DeleteKey(PrefsKey);

        [TearDown]
        public void TearDown() => PlayerPrefs.DeleteKey(PrefsKey);

        [Test]
        public void UnknownTechniqueHasNoReps()
        {
            var p = new Level1Progress();
            Assert.AreEqual(0, p.RepsFor("kizamizuki-jodan"));
        }

        [Test]
        public void AbsorbKeepsTheBestAttempt()
        {
            var p = new Level1Progress();
            p.Absorb("kizamizuki-jodan", 3);
            Assert.AreEqual(3, p.RepsFor("kizamizuki-jodan"));
            p.Absorb("kizamizuki-jodan", 1);            // слабый подход не портит прогресс
            Assert.AreEqual(3, p.RepsFor("kizamizuki-jodan"));
            p.Absorb("kizamizuki-jodan", 5);
            Assert.AreEqual(5, p.RepsFor("kizamizuki-jodan"));
        }

        [Test]
        public void TechniquesAreTrackedIndependently()
        {
            var p = new Level1Progress();
            p.Absorb("stance-fudo", 5);
            p.Absorb("ghoststep-forward", 2);
            Assert.AreEqual(5, p.RepsFor("stance-fudo"));
            Assert.AreEqual(2, p.RepsFor("ghoststep-forward"));
        }

        [Test]
        public void AbsorbIgnoresGarbageInput()
        {
            var p = new Level1Progress();
            Assert.DoesNotThrow(() => p.Absorb(null, 4));
            Assert.DoesNotThrow(() => p.Absorb(string.Empty, 4));
            p.Absorb("stance-fudo", -1);                // счётчик не уходит ниже нуля
            Assert.AreEqual(0, p.RepsFor("stance-fudo"));
        }

        [Test]
        public void SaveLoadRoundTripsThroughPlayerPrefs()
        {
            var p = new Level1Progress();
            p.Absorb("stance-zenkutsu", 4);
            p.Absorb("maegeri-chudan-stance", 5);
            p.Save();

            Level1Progress loaded = Level1Progress.Load();
            Assert.AreEqual(4, loaded.RepsFor("stance-zenkutsu"));
            Assert.AreEqual(5, loaded.RepsFor("maegeri-chudan-stance"));
            Assert.AreEqual(0, loaded.RepsFor("ghoststep-back"));
        }

        [Test]
        public void SaveOverwritesTheStoredProgress()
        {
            var p = new Level1Progress();
            p.Absorb("stance-fudo", 2);
            p.Save();

            Level1Progress again = Level1Progress.Load();
            again.Absorb("stance-fudo", 5);
            again.Save();

            Assert.AreEqual(5, Level1Progress.Load().RepsFor("stance-fudo"));
        }

        [Test]
        public void EmptyStorageLoadsEmptyProgress()
        {
            Level1Progress loaded = Level1Progress.Load();
            Assert.IsNotNull(loaded);
            Assert.AreEqual(0, loaded.RepsFor("stance-fudo"));
        }

        [Test]
        public void CorruptStorageLoadsEmptyProgressInsteadOfThrowing()
        {
            PlayerPrefs.SetString(PrefsKey, "не json");
            Level1Progress loaded = null;
            Assert.DoesNotThrow(() => loaded = Level1Progress.Load());
            Assert.IsNotNull(loaded);
            Assert.AreEqual(0, loaded.RepsFor("stance-fudo"));
            Assert.DoesNotThrow(() => loaded.Absorb("stance-fudo", 1));   // список не null после битого JSON
            Assert.AreEqual(1, loaded.RepsFor("stance-fudo"));
        }

        [Test]
        public void Level1DoesNotTouchTheLevel0Store()
        {
            // Снимок, а не DeleteKey: ключ оценки может быть занят реальным прогрессом.
            string before = PlayerPrefs.GetString("level0.results", string.Empty);
            var p = new Level1Progress();
            p.Absorb("stance-fudo", 5);
            p.Save();
            Assert.AreEqual(before, PlayerPrefs.GetString("level0.results", string.Empty),
                "прогресс обучения не должен писаться в ключ оценки");
        }

        [Test]
        public void GoalIsFiveCleanReps()
        {
            Assert.AreEqual(5, Level1Progress.Goal);
        }
    }
}
