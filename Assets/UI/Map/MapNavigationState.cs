namespace Mikey.UI.Map
{
    /// <summary>Which map screen the player was last on.</summary>
    public enum MapContext
    {
        JapanWorld,
        OkinawaChapter
    }

    /// <summary>
    /// Session-only memory of the player's current Map context (Japan world map
    /// vs. Okinawa chapter map), so returning to Map from Stats/Techniques/
    /// Settings restores whichever map they were on instead of always resetting
    /// to the Japan world map (see JapanMapController.OnScreenChanged). A plain
    /// static field is deliberate: this is frontend-only session state, not
    /// persisted (resets on app restart) and not part of
    /// <see cref="Mikey.UI.Progression.TutorialProgressState"/>, which tracks
    /// actual tutorial progress, not which map screen is showing.
    /// </summary>
    public static class MapNavigationState
    {
        public static MapContext Current { get; set; } = MapContext.JapanWorld;
    }
}
