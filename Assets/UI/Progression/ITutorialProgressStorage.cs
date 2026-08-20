namespace Mikey.UI.Progression
{
    /// <summary>
    /// Minimal persistence abstraction behind <see cref="TutorialProgressStore"/> so
    /// its state-transition logic (default state, invalid-data fallback, forward-only
    /// advance) can be unit-tested with an in-memory fake, without touching real
    /// Editor/player local storage from a test run.
    /// </summary>
    public interface ITutorialProgressStorage
    {
        /// <summary>Attempts to read the last saved raw value. Returns false if none was ever saved.</summary>
        bool TryLoad(out string value);

        /// <summary>Persists <paramref name="value"/>, surviving an app/Editor restart.</summary>
        void Save(string value);

        /// <summary>Clears any saved value (developer reset).</summary>
        void Delete();
    }
}
