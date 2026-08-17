using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>Visual category of an Okinawa mission marker — selects which supplied marker icon is shown.</summary>
    public enum MissionMarkerType
    {
        Training,
        Fight
    }

    /// <summary>Normalized (0-1, relative to the Japan map artwork) placement + identity for one chapter marker.</summary>
    public readonly struct ChapterMarkerLayout
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly float NormalizedX;
        public readonly float NormalizedY;
        public readonly bool Unlocked;

        public ChapterMarkerLayout(string id, string displayName, float normalizedX, float normalizedY, bool unlocked)
        {
            Id = id;
            DisplayName = displayName;
            NormalizedX = normalizedX;
            NormalizedY = normalizedY;
            Unlocked = unlocked;
        }
    }

    /// <summary>Normalized (0-1, relative to the Okinawa map artwork) placement + mission type for one LVL 0-4 marker.</summary>
    public readonly struct MissionMarkerLayout
    {
        public readonly int LevelIndex;
        public readonly float NormalizedX;
        public readonly float NormalizedY;
        public readonly MissionMarkerType Type;

        public MissionMarkerLayout(int levelIndex, float normalizedX, float normalizedY, MissionMarkerType type)
        {
            LevelIndex = levelIndex;
            NormalizedX = normalizedX;
            NormalizedY = normalizedY;
            Type = type;
        }
    }

    /// <summary>
    /// ONE centralized source of truth for marker placement and mission type
    /// (Map Pass 3A). Coordinates are normalized 0-1 against each screen's map
    /// artwork, not the viewport, so JapanMapController/OkinawaMapController
    /// can apply them as percentage style.left/style.top on elements that live
    /// inside the same pan/zoom-transformed ".pan-canvas" as the map art —
    /// they move and scale with the map instead of the viewport. Moving a
    /// marker (e.g. "Fukuoka 2% left, 3% down") only ever requires editing its
    /// entry here; Map.uss no longer hardcodes any marker's left/top.
    ///
    /// Lock/progression state is NOT duplicated here for missions: LVL 0-4's
    /// live lock state still comes from OkinawaMapController.IsLevelLocked
    /// reading TutorialProgressState, exactly as before this pass. Chapter
    /// unlocked state IS centralized here since — unlike LVL 0/1 — no chapter
    /// beyond Okinawa has any real unlock gameplay/progression built yet.
    /// </summary>
    public static class MapMarkerLayout
    {
        public const string OkinawaChapterId = "okinawa";
        public const string FukuokaChapterId = "fukuoka";
        public const string HiroshimaChapterId = "hiroshima";

        /// <summary>
        /// MVP chapters, south to north: Okinawa (unlocked), Fukuoka (Kyushu,
        /// locked), Hiroshima (western Honshu, locked). Positions were picked
        /// by eye against the final japan_map_final.jpg ink-map art — Fukuoka
        /// sits at the northern tip of the Kyushu landmass nearest the strait,
        /// Hiroshima sits further northeast along the Honshu coastline.
        /// </summary>
        public static readonly ChapterMarkerLayout[] Chapters =
        {
            new ChapterMarkerLayout(OkinawaChapterId, "OKINAWA", 0.30f, 0.62f, unlocked: true),
            new ChapterMarkerLayout(FukuokaChapterId, "FUKUOKA", 0.38f, 0.47f, unlocked: false),
            new ChapterMarkerLayout(HiroshimaChapterId, "HIROSHIMA", 0.50f, 0.38f, unlocked: false),
        };

        /// <summary>
        /// Okinawa MVP mission set: exactly 5 missions (LVL 0-4), 3 Training +
        /// 2 Fight. LVL 0 (assessment) and LVL 1 (existing techniques/
        /// foundations route) are both non-combat, so Training — both already
        /// have real routes (see OkinawaMapController.OnLevelCtaClicked) and
        /// are unaffected by this set. LVL 2-4 have no gameplay built yet
        /// (OkinawaMapController.IsLevelLocked always locks them) but their
        /// mission TYPE is assigned here regardless — type and progression
        /// state are separate concerns, so a locked Fight mission still shows
        /// the Fight marker. LVL 5 was dropped from the MVP entirely; it must
        /// not appear here or anywhere in the Okinawa UXML/USS/runtime data.
        /// </summary>
        public static readonly MissionMarkerLayout[] Missions =
        {
            new MissionMarkerLayout(0, 0.22f, 0.78f, MissionMarkerType.Training),
            new MissionMarkerLayout(1, 0.34f, 0.60f, MissionMarkerType.Training),
            new MissionMarkerLayout(2, 0.48f, 0.68f, MissionMarkerType.Fight),
            new MissionMarkerLayout(3, 0.60f, 0.48f, MissionMarkerType.Training),
            new MissionMarkerLayout(4, 0.72f, 0.58f, MissionMarkerType.Fight),
        };

        /// <summary>Writes a normalized (0-1) position onto an element as percentage style.left/style.top.</summary>
        public static void ApplyNormalizedPosition(VisualElement element, float normalizedX, float normalizedY)
        {
            element.style.left = new Length(normalizedX * 100f, LengthUnit.Percent);
            element.style.top = new Length(normalizedY * 100f, LengthUnit.Percent);
        }
    }
}
