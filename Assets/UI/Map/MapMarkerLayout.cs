using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>Visual category of an Okinawa mission marker — selects which supplied marker icon is shown.</summary>
    public enum MissionMarkerType
    {
        Special,
        Training,
        Fight,
        BossFight
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

    /// <summary>Normalized (0-1, relative to the Okinawa map artwork) placement + mission type for one LVL 0-6 marker.</summary>
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
    /// artwork (normalizedX = pixelX / 6336, normalizedY = pixelY / 2688,
    /// top-left origin — the source japan_map_final.jpg / okinawa_map_final.jpg
    /// are both 6336x2688), not the viewport, so JapanMapController/
    /// OkinawaMapController can apply them as percentage style.left/style.top
    /// on elements that live inside the same pan/zoom-transformed
    /// ".pan-canvas" as the map art — they move and scale with the map
    /// instead of the viewport. Moving a marker only ever requires editing
    /// its entry here; Map.uss no longer hardcodes any marker's left/top.
    ///
    /// Each coordinate is the point where the marker pin's BOTTOM TIP should
    /// touch the map — not the icon's visual center. ".chapter-node"/
    /// ".level-node" anchor via "translate: -50% -100%" in Map.uss (bottom-
    /// center of the node's own box) precisely because of this; the node's
    /// label is laid out ABOVE its icon (not below) so the box's bottom edge
    /// coincides with the icon's bottom edge, i.e. the pin tip.
    ///
    /// Lock/progression state is NOT duplicated here for missions: LVL 0-6's
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
        /// locked), Hiroshima (western Honshu, locked). Coordinates are exact,
        /// measured directly on the 6336x2688 japan_map_final.jpg source —
        /// Okinawa (1762, 2184), Fukuoka (2375, 1347), Hiroshima (2670, 1253) —
        /// not estimated by eye.
        /// </summary>
        public static readonly ChapterMarkerLayout[] Chapters =
        {
            new ChapterMarkerLayout(OkinawaChapterId, "OKINAWA", 0.27809f, 0.81250f, unlocked: true),
            new ChapterMarkerLayout(FukuokaChapterId, "FUKUOKA", 0.37484f, 0.50112f, unlocked: false),
            new ChapterMarkerLayout(HiroshimaChapterId, "HIROSHIMA", 0.42140f, 0.46615f, unlocked: false),
        };

        /// <summary>
        /// Okinawa MVP mission set: exactly 7 missions (LVL 0-6) — 1 Special
        /// opening mission, 3 Training, 2 Fight, 1 Boss Fight, in that fixed
        /// order. LVL 0 (assessment) and LVL 1 (existing techniques/
        /// foundations route) already have real routes (see
        /// OkinawaMapController.OnLevelCtaClicked) and are unaffected by this
        /// set. LVL 2-6 have no gameplay built yet (OkinawaMapController.
        /// IsLevelLocked always locks them) but their mission TYPE is assigned
        /// here regardless — type and progression state are separate
        /// concerns, so a locked Boss Fight still shows the Boss Fight
        /// marker. Coordinates are exact, measured directly on the 6336x2688
        /// okinawa_map_final.jpg source, not estimated by eye:
        /// LVL0 (2118, 2039), LVL1 (2742, 1613), LVL2 (3498, 1319),
        /// LVL3 (3834, 851), LVL4 (4188, 1217), LVL5 (4734, 1187),
        /// LVL6 (5154, 983).
        /// </summary>
        public static readonly MissionMarkerLayout[] Missions =
        {
            new MissionMarkerLayout(0, 0.33428f, 0.75856f, MissionMarkerType.Special),
            new MissionMarkerLayout(1, 0.43277f, 0.60007f, MissionMarkerType.Training),
            new MissionMarkerLayout(2, 0.55208f, 0.49070f, MissionMarkerType.Fight),
            new MissionMarkerLayout(3, 0.60511f, 0.31659f, MissionMarkerType.Training),
            new MissionMarkerLayout(4, 0.66098f, 0.45275f, MissionMarkerType.Fight),
            new MissionMarkerLayout(5, 0.74716f, 0.44159f, MissionMarkerType.Training),
            new MissionMarkerLayout(6, 0.81345f, 0.36570f, MissionMarkerType.BossFight),
        };

        /// <summary>Writes a normalized (0-1) position onto an element as percentage style.left/style.top.</summary>
        public static void ApplyNormalizedPosition(VisualElement element, float normalizedX, float normalizedY)
        {
            element.style.left = new Length(normalizedX * 100f, LengthUnit.Percent);
            element.style.top = new Length(normalizedY * 100f, LengthUnit.Percent);
        }
    }
}
