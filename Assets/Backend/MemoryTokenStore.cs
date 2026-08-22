namespace Mikey.Backend
{
    /// <summary>
    /// Хранилище на время процесса — для редактора и тестов. Намеренно ничего не
    /// записывает на диск: refresh-токен это долгоживущий ключ от аккаунта, и
    /// класть его в PlayerPrefs нельзя (обычный XML, попадающий в автобэкап
    /// Android). На устройстве работает <c>AndroidTokenStore</c>.
    /// </summary>
    public sealed class MemoryTokenStore : ITokenStore
    {
        private string _token;

        public bool TryLoadRefreshToken(out string token)
        {
            token = _token;
            return !string.IsNullOrEmpty(_token);
        }

        public void SaveRefreshToken(string token) => _token = token;

        public void Clear() => _token = null;
    }
}
