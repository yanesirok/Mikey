namespace Mikey.UI.Progression.Tests
{
    /// <summary>In-memory <see cref="ILevel0ProgressStorage"/> so store tests never touch real local storage.</summary>
    public sealed class FakeLevel0ProgressStorage : ILevel0ProgressStorage
    {
        private string _value;
        private bool _hasValue;

        /// <summary>Seeds a raw saved value directly (including invalid/garbage), bypassing Save.</summary>
        public void Seed(string rawValue)
        {
            _value = rawValue;
            _hasValue = true;
        }

        public bool TryLoad(out string value)
        {
            value = _value;
            return _hasValue;
        }

        public void Save(string value)
        {
            _value = value;
            _hasValue = true;
        }

        public void Delete()
        {
            _value = null;
            _hasValue = false;
        }
    }
}
