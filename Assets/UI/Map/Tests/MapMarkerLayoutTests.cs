using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Direct behavioral contract for MapMarkerLayout (Map Pass 3A): the one
    /// centralized source of truth for chapter/mission marker placement and
    /// mission type. Unlike the MonoBehaviour controllers, this is a plain
    /// static data class, so it's exercised directly rather than via
    /// source-text assertions.
    /// </summary>
    public class MapMarkerLayoutTests
    {
        // ---------- chapters ----------

        [Test]
        public void Chapters_HasExactlyThreeMvpDefinitions()
        {
            Assert.AreEqual(3, MapMarkerLayout.Chapters.Length, "Exactly 3 MVP chapters this pass: Okinawa, Fukuoka, Hiroshima.");
        }

        [Test]
        public void Chapters_AreOrderedOkinawaFukuokaHiroshima_SouthToNorth()
        {
            Assert.AreEqual(MapMarkerLayout.OkinawaChapterId, MapMarkerLayout.Chapters[0].Id);
            Assert.AreEqual(MapMarkerLayout.FukuokaChapterId, MapMarkerLayout.Chapters[1].Id);
            Assert.AreEqual(MapMarkerLayout.HiroshimaChapterId, MapMarkerLayout.Chapters[2].Id);
        }

        [Test]
        public void Okinawa_IsUnlocked()
        {
            Assert.IsTrue(FindChapter(MapMarkerLayout.OkinawaChapterId).Unlocked);
        }

        [Test]
        public void Fukuoka_IsLocked()
        {
            Assert.IsFalse(FindChapter(MapMarkerLayout.FukuokaChapterId).Unlocked);
        }

        [Test]
        public void Hiroshima_IsLocked()
        {
            Assert.IsFalse(FindChapter(MapMarkerLayout.HiroshimaChapterId).Unlocked);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void AllChapterCoordinates_AreNormalized0to1(int index)
        {
            var chapter = MapMarkerLayout.Chapters[index];
            Assert.GreaterOrEqual(chapter.NormalizedX, 0f);
            Assert.LessOrEqual(chapter.NormalizedX, 1f);
            Assert.GreaterOrEqual(chapter.NormalizedY, 0f);
            Assert.LessOrEqual(chapter.NormalizedY, 1f);
        }

        // ---------- missions ----------

        [Test]
        public void Missions_HasExactlySixDefinitions_ForLvl0Through5()
        {
            Assert.AreEqual(6, MapMarkerLayout.Missions.Length);
            for (int i = 0; i < 6; i++)
                Assert.AreEqual(i, MapMarkerLayout.Missions[i].LevelIndex, $"Missions[{i}] must describe LVL {i}.");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void AllMissionCoordinates_AreNormalized0to1(int levelIndex)
        {
            var mission = MapMarkerLayout.Missions[levelIndex];
            Assert.GreaterOrEqual(mission.NormalizedX, 0f);
            Assert.LessOrEqual(mission.NormalizedX, 1f);
            Assert.GreaterOrEqual(mission.NormalizedY, 0f);
            Assert.LessOrEqual(mission.NormalizedY, 1f);
        }

        [Test]
        public void Lvl0_IsTraining_MatchesItsExistingAssessmentRoute()
        {
            Assert.AreEqual(MissionMarkerType.Training, MapMarkerLayout.Missions[0].Type);
        }

        [Test]
        public void Lvl1_IsTraining_MatchesItsExistingTechniquesRoute()
        {
            Assert.AreEqual(MissionMarkerType.Training, MapMarkerLayout.Missions[1].Type);
        }

        [Test]
        public void MissionMarkerType_SupportsBothTrainingAndFight()
        {
            // The marker system must support both categories even though no
            // current LVL 2-5 slot has real gameplay assigning it Fight yet
            // (see MapMarkerLayout's doc comment) — covered by the icon-class
            // mapping in OkinawaMapController.ApplyMissionLayout and the
            // ".level-node__icon--fight" rule in Map.uss (MapMarkerAssetsTests).
            var values = (MissionMarkerType[])System.Enum.GetValues(typeof(MissionMarkerType));
            CollectionAssert.Contains(values, MissionMarkerType.Training);
            CollectionAssert.Contains(values, MissionMarkerType.Fight);
        }

        private static ChapterMarkerLayout FindChapter(string id)
        {
            foreach (var chapter in MapMarkerLayout.Chapters)
            {
                if (chapter.Id == id)
                    return chapter;
            }
            Assert.Fail($"No chapter with id '{id}' in MapMarkerLayout.Chapters.");
            return default;
        }
    }
}
