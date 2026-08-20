namespace Mikey.UI.Progression
{
    /// <summary>
    /// Minimal persistence abstraction behind <see cref="Level0ProgressionStore"/>
    /// so its state-transition logic can be unit-tested with an in-memory fake,
    /// without touching real Editor/player local storage from a test run. Mirrors
    /// <see cref="ITutorialProgressStorage"/> exactly.
    /// </summary>
    public interface ILevel0ProgressStorage
    {
        /// <summary>Attempts to read the last saved raw value. Returns false if none was ever saved.</summary>
        bool TryLoad(out string value);

        /// <summary>Persists <paramref name="value"/>, surviving an app/Editor restart.</summary>
        void Save(string value);

        /// <summary>Clears any saved value (developer reset).</summary>
        void Delete();
    }
}
