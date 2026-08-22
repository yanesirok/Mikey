using System;

namespace Mikey.Backend
{
    /// <summary>
    /// Состояние сессии: короткоживущий access-токен в памяти, долгоживущий
    /// refresh-токен — в <see cref="ITokenStore"/>.
    ///
    /// Часы подаются параметром, а не читаются внутри: иначе арифметику
    /// истечения нельзя проверить тестом, не подменяя системное время.
    /// </summary>
    public sealed class SupabaseSession
    {
        /// <summary>
        /// За сколько секунд до истечения считать токен требующим обновления.
        /// Запрос, отправленный ровно в момент истечения, гарантированно
        /// получит 401 — пока он летит, токен уже мёртв.
        /// </summary>
        public const double RefreshSkewSeconds = 60d;

        private readonly ITokenStore _store;
        private double _expiresAtUnix;

        public SupabaseSession(ITokenStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            if (_store.TryLoadRefreshToken(out string saved))
                RefreshToken = saved;
        }

        /// <summary>Текущий access-токен, или null.</summary>
        public string AccessToken { get; private set; }

        /// <summary>Refresh-токен, переживающий перезапуск. Null, если входа не было.</summary>
        public string RefreshToken { get; private set; }

        /// <summary>Есть ли пригодный access-токен прямо сейчас.</summary>
        public bool IsSignedIn => !string.IsNullOrEmpty(AccessToken);

        /// <summary>Пора ли обновляться к моменту <paramref name="nowUnix"/>.</summary>
        public bool NeedsRefresh(double nowUnix) =>
            !IsSignedIn || nowUnix >= _expiresAtUnix - RefreshSkewSeconds;

        /// <summary>Принимает свежую пару токенов. Пустой access оставляет сессию разлогиненной.</summary>
        public void Adopt(string accessToken, string refreshToken, long expiresInSeconds, double nowUnix)
        {
            if (string.IsNullOrEmpty(accessToken))
            {
                AccessToken = null;
                _expiresAtUnix = 0d;
                return;
            }

            AccessToken = accessToken;
            _expiresAtUnix = nowUnix + expiresInSeconds;

            if (string.IsNullOrEmpty(refreshToken))
                return;

            RefreshToken = refreshToken;
            _store.SaveRefreshToken(refreshToken);
        }

        /// <summary>Забывает сессию целиком. Локальных данных игрока не касается.</summary>
        public void SignOut()
        {
            AccessToken = null;
            RefreshToken = null;
            _expiresAtUnix = 0d;
            _store.Clear();
        }

        /// <summary>Текущее время в секундах Unix — единственная точка чтения часов.</summary>
        public static double NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
