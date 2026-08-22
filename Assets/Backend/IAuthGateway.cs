namespace Mikey.Backend
{
    /// <summary>
    /// Источник Google ID-токена. Диалог входа асинхронный и системный, поэтому
    /// начало и получение результата разделены, а результат забирается опросом.
    /// </summary>
    public interface IAuthGateway
    {
        /// <summary>Доступен ли вход на этой платформе. В редакторе — нет.</summary>
        bool IsAvailable { get; }

        /// <summary>Показывает системный диалог выбора аккаунта.</summary>
        void BeginSignIn(string webClientId);

        /// <summary>
        /// Забирает полученный ID-токен и сырой nonce той же попытки входа.
        /// Nonce обязателен: без него Supabase отвергнет токен, в котором nonce есть.
        /// </summary>
        bool TryTakeIdToken(out string idToken, out string rawNonce);

        /// <summary>Забирает текст ошибки ровно один раз.</summary>
        bool TryTakeError(out string message);
    }
}
