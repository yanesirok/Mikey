using NUnit.Framework;

namespace Mikey.Backend.Tests
{
    /// <summary>
    /// Арифметика жизни сессии. Часы подаются параметром, поэтому проверяется
    /// без подмены системного времени.
    /// </summary>
    public class SupabaseSessionTests
    {
        private const double Now = 1_800_000_000d;

        [Test]
        public void FreshSession_IsSignedIn_AndDoesNotNeedRefreshYet()
        {
            var session = new SupabaseSession(new MemoryTokenStore());
            session.Adopt("access", "refresh", expiresInSeconds: 3600, nowUnix: Now);

            Assert.IsTrue(session.IsSignedIn);
            Assert.AreEqual("access", session.AccessToken);
            Assert.IsFalse(session.NeedsRefresh(Now));
            Assert.IsFalse(session.NeedsRefresh(Now + 3000));
        }

        [Test]
        public void Refresh_IsRequestedBeforeExpiry_NotAtIt()
        {
            var session = new SupabaseSession(new MemoryTokenStore());
            session.Adopt("access", "refresh", expiresInSeconds: 3600, nowUnix: Now);

            Assert.IsFalse(session.NeedsRefresh(Now + 3600 - SupabaseSession.RefreshSkewSeconds - 1),
                "Обновлялись слишком рано.");
            Assert.IsTrue(session.NeedsRefresh(Now + 3600 - SupabaseSession.RefreshSkewSeconds),
                "Запрос ровно в момент истечения гарантированно получит 401 — надо обновляться заранее.");
            Assert.IsTrue(session.NeedsRefresh(Now + 4000));
        }

        [Test]
        public void RefreshToken_SurvivesRestart_ViaTheStore()
        {
            var store = new MemoryTokenStore();
            var first = new SupabaseSession(store);
            first.Adopt("access", "refresh-abc", 3600, Now);

            var restarted = new SupabaseSession(store);

            Assert.AreEqual("refresh-abc", restarted.RefreshToken);
            Assert.IsFalse(restarted.IsSignedIn,
                "Access-токен между запусками не переживает — сначала обновление.");
        }

        [Test]
        public void SignOut_ClearsBothTokensAndTheStore()
        {
            var store = new MemoryTokenStore();
            var session = new SupabaseSession(store);
            session.Adopt("access", "refresh", 3600, Now);

            session.SignOut();

            Assert.IsFalse(session.IsSignedIn);
            Assert.IsNull(session.AccessToken);
            Assert.IsNull(session.RefreshToken);
            Assert.IsFalse(store.TryLoadRefreshToken(out _));
        }

        [Test]
        public void Adopt_WithAnEmptyAccessToken_AdoptsNothingAtAll()
        {
            var store = new MemoryTokenStore();
            var session = new SupabaseSession(store);

            // Ответ без токена доступа — испорченный. Брать из него нельзя ничего,
            // в том числе токен обновления: иначе испорченный ответ подменит рабочий
            // ключ от аккаунта, и человек окажется разлогинен без объяснения.
            session.Adopt(string.Empty, "refresh-from-broken-response", 3600, Now);

            Assert.IsFalse(session.IsSignedIn);
            Assert.IsNull(session.RefreshToken,
                "Токен обновления взят из ответа, в котором не было токена доступа.");
            Assert.IsFalse(store.TryLoadRefreshToken(out _),
                "Испорченный ответ записан в хранилище токенов.");
        }
    }
}
