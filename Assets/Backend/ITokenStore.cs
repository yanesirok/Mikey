namespace Mikey.Backend
{
    /// <summary>
    /// Хранилище refresh-токена. Две реализации существуют не «на будущее»: в
    /// редакторе EncryptedSharedPreferences физически недоступны, а тестам нужна
    /// подмена. Тот же приём, что у <c>ITutorialProgressStorage</c> и
    /// <c>IAudioSettingsStorage</c> в этом проекте.
    /// </summary>
    public interface ITokenStore
    {
        bool TryLoadRefreshToken(out string token);
        void SaveRefreshToken(string token);
        void Clear();
    }
}
