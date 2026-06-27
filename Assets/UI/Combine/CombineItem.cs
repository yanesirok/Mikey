namespace Mikey.UI.Combine
{
    /// <summary>
    /// One mock "selected item" shown in the Combine ready state — a single
    /// baseline measurement card (purely display data, no backend contract).
    /// All values are pre-formatted strings so the view layer stays dumb.
    /// </summary>
    public readonly struct CombineItem
    {
        /// <summary>Human-readable test name, e.g. "Push-ups".</summary>
        public readonly string Name;

        /// <summary>Decorative kanji glyph, e.g. "腕".</summary>
        public readonly string Glyph;

        /// <summary>Pre-formatted measured value, e.g. "24" or "7.5".</summary>
        public readonly string Value;

        /// <summary>Unit label, e.g. "REPS", "SCORE", "SECONDS".</summary>
        public readonly string Unit;

        /// <summary>Stat category, e.g. "Power · upper".</summary>
        public readonly string Stat;

        public CombineItem(string name, string glyph, string value, string unit, string stat)
        {
            Name = name;
            Glyph = glyph;
            Value = value;
            Unit = unit;
            Stat = stat;
        }
    }
}
