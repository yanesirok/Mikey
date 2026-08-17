using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Direct behavioral contract for MapMarkerLayout: the one centralized
    /// source of truth for chapter/mission marker placement and mission
    /// type. Unlike the MonoBehaviour controllers, this is a plain static
    /// data class, so it's exercised directly rather than via source-text
    /// assertions. Coordinates are exact values measured directly on the
    /// 6336x2688 source map art (normalizedX = pixelX / 6336, normalizedY =
    /// pixelY / 2688) — the exact-value tests below guard against silent
    /// drift back to by-eye estimation.
    /// </summary>
    public class MapMarkerLayoutTests
    {
        private const float CoordinateTolerance = 0.00001f;

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

        // Exact pixel-measured positions on the 6336x2688 japan_map_final.jpg:
        // Okinawa (1782, 1685), Fukuoka (2352, 1349), Hiroshima (2670, 1253).
        [Test]
        public void Okinawa_CoordinateMatchesExactPixelMeasurement()
        {
            var chapter = FindChapter(MapMarkerLayout.OkinawaChapterId);
            Assert.AreEqual(0.27809f, chapter.NormalizedX, CoordinateTolerance);
            Assert.AreEqual(0.81250f, chapter.NormalizedY, CoordinateTolerance);
        }

        [Test]
        public void Fukuoka_CoordinateMatchesExactPixelMeasurement()
        {
            var chapter = FindChapter(MapMarkerLayout.FukuokaChapterId);
            Assert.AreEqual(0.37484f, chapter.NormalizedX, CoordinateTolerance);
            Assert.AreEqual(0.50112f, chapter.NormalizedY, CoordinateTolerance);
        }

        [Test]
        public void Hiroshima_CoordinateMatchesExactPixelMeasurement()
        {
            var chapter = FindChapter(MapMarkerLayout.HiroshimaChapterId);
            Assert.AreEqual(0.42140f, chapter.NormalizedX, CoordinateTolerance);
            Assert.AreEqual(0.46615f, chapter.NormalizedY, CoordinateTolerance);
        }

        // ---------- missions ----------

        [Test]
        public void Missions_HasExactlySevenMvpDefinitions_ForLvl0Through6()
        {
            // Okinawa's final MVP mission set is exactly 7 missions (1
            // Special + 3 Training + 2 Fight + 1 Boss Fight).
            Assert.AreEqual(7, MapMarkerLayout.Missions.Length);
            for (int i = 0; i < 7; i++)
                Assert.AreEqual(i, MapMarkerLayout.Missions[i].LevelIndex, $"Missions[{i}] must describe LVL {i}.");
        }

        [Test]
        public void Missions_HasExactlyOneSpecial_ThreeTraining_TwoFight_OneBossFight()
        {
            int special = 0, training = 0, fight = 0, boss = 0;
            foreach (var mission in MapMarkerLayout.Missions)
            {
                switch (mission.Type)
                {
                    case MissionMarkerType.Special: special++; break;
                    case MissionMarkerType.Training: training++; break;
                    case MissionMarkerType.Fight: fight++; break;
                    case MissionMarkerType.BossFight: boss++; break;
                }
            }
            Assert.AreEqual(1, special, "Okinawa MVP must have exactly 1 Special mission.");
            Assert.AreEqual(3, training, "Okinawa MVP must have exactly 3 Training missions.");
            Assert.AreEqual(2, fight, "Okinawa MVP must have exactly 2 Fight missions.");
            Assert.AreEqual(1, boss, "Okinawa MVP must have exactly 1 Boss Fight mission.");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void AllMissionCoordinates_AreNormalized0to1(int levelIndex)
        {
            var mission = MapMarkerLayout.Missions[levelIndex];
            Assert.GreaterOrEqual(mission.NormalizedX, 0f);
            Assert.LessOrEqual(mission.NormalizedX, 1f);
            Assert.GreaterOrEqual(mission.NormalizedY, 0f);
            Assert.LessOrEqual(mission.NormalizedY, 1f);
        }

        // Exact pixel-measured positions on the 6336x2688 okinawa_map_final.jpg.
        [TestCase(0, 0.33428f, 0.75856f)]
        [TestCase(1, 0.43277f, 0.60007f)]
        [TestCase(2, 0.55208f, 0.49070f)]
        [TestCase(3, 0.60511f, 0.31659f)]
        [TestCase(4, 0.66098f, 0.45275f)]
        [TestCase(5, 0.74716f, 0.44159f)]
        [TestCase(6, 0.81345f, 0.36570f)]
        public void MissionCoordinate_MatchesExactPixelMeasurement(int levelIndex, float expectedX, float expectedY)
        {
            var mission = MapMarkerLayout.Missions[levelIndex];
            Assert.AreEqual(expectedX, mission.NormalizedX, CoordinateTolerance);
            Assert.AreEqual(expectedY, mission.NormalizedY, CoordinateTolerance);
        }

        [Test]
        public void Lvl0_IsSpecial()
        {
            Assert.AreEqual(MissionMarkerType.Special, MapMarkerLayout.Missions[0].Type);
        }

        [Test]
        public void Lvl1_IsTraining_MatchesItsExistingTechniquesRoute()
        {
            Assert.AreEqual(MissionMarkerType.Training, MapMarkerLayout.Missions[1].Type);
        }

        [Test]
        public void Lvl2_IsFight()
        {
            Assert.AreEqual(MissionMarkerType.Fight, MapMarkerLayout.Missions[2].Type);
        }

        [Test]
        public void Lvl3_IsTraining()
        {
            Assert.AreEqual(MissionMarkerType.Training, MapMarkerLayout.Missions[3].Type);
        }

        [Test]
        public void Lvl4_IsFight()
        {
            Assert.AreEqual(MissionMarkerType.Fight, MapMarkerLayout.Missions[4].Type);
        }

        [Test]
        public void Lvl5_IsTraining()
        {
            Assert.AreEqual(MissionMarkerType.Training, MapMarkerLayout.Missions[5].Type);
        }

        [Test]
        public void Lvl6_IsBossFight()
        {
            Assert.AreEqual(MissionMarkerType.BossFight, MapMarkerLayout.Missions[6].Type);
        }

        [Test]
        public void MissionMarkerType_SupportsAllFourCategories()
        {
            // All four categories are real, distinct enum members backing the
            // four supplied marker icons — covered by the icon-class mapping
            // in OkinawaMapController.IconClassFor and the
            // ".level-node__icon--*" rules in Map.uss (MapMarkerAssetsTests).
            var values = (MissionMarkerType[])System.Enum.GetValues(typeof(MissionMarkerType));
            CollectionAssert.Contains(values, MissionMarkerType.Special);
            CollectionAssert.Contains(values, MissionMarkerType.Training);
            CollectionAssert.Contains(values, MissionMarkerType.Fight);
            CollectionAssert.Contains(values, MissionMarkerType.BossFight);
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
