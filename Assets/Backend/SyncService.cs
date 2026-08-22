using System;
using System.Collections;
using Mikey.Pose;
using Mikey.UI.Profile;
using Mikey.UI.Progression;
using UnityEngine;

namespace Mikey.Backend
{
    /// <summary>
    /// Решает, когда синхронизироваться, и следит за тем, чтобы ни одна неудача
    /// не стоила игроку данных.
    ///
    /// Триггеров намеренно мало: успешный вход, уход приложения в фон и запись
    /// подхода. Никаких таймеров — резервная копия не стоит того, чтобы будить
    /// радиомодуль во время тренировки.
    ///
    /// Ошибки пользователю не показываются: неудавшийся бэкап не является
    /// игровым событием, и прерывать им тренировку неуместно. Отклонённый
    /// сервером запрос не повторяется — повтор по кругу это разряженная батарея,
    /// а не надёжность.
    /// </summary>
    public sealed class SyncService : MonoBehaviour
    {
        private SupabaseConfig _config;
        private SupabaseClient _client;
        private SupabaseSession _session;
        private IAuthGateway _auth;
        private ITutorialProgress _progress;

        private bool _syncing;
        private bool _dirty;

        /// <summary>Поднимается при смене состояния входа — UI перерисовывает себя.</summary>
        public event Action Changed;

        public bool IsSignedIn => _session != null && !string.IsNullOrEmpty(_session.RefreshToken);

        /// <summary>Доступен ли вход на этой платформе (в редакторе — нет).</summary>
        public bool CanSignIn => _auth != null && _auth.IsAvailable && _config != null;

        private void Awake()
        {
            _config = SupabaseConfig.Load();
            if (_config == null)
            {
                Debug.LogWarning("[SyncService] SupabaseConfig не найден; синхронизация выключена.");
                enabled = false;
                return;
            }

            _client = new SupabaseClient(_config);
            _auth = new AndroidGoogleAuth();
            _session = new SupabaseSession(TokenStore());
            _progress = GetComponent<ITutorialProgress>();
        }

        private static ITokenStore TokenStore()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidTokenStore();
#else
            return new MemoryTokenStore();
#endif
        }

        /// <summary>Уход в фон — надёжный момент «пользователь закончил».</summary>
        private void OnApplicationPause(bool paused)
        {
            if (paused)
                RequestSync();
        }

        /// <summary>Начинает вход. Диалог системный, результат забираем опросом.</summary>
        public void SignIn()
        {
            if (!CanSignIn || _syncing)
                return;
            _auth.BeginSignIn(_config.GoogleWebClientId);
            StartCoroutine(AwaitSignIn());
        }

        /// <summary>Забывает сессию. Локальный прогресс не трогает.</summary>
        public void SignOut()
        {
            _session?.SignOut();
            Changed?.Invoke();
        }

        /// <summary>Просит синхронизацию. Безопасно звать часто.</summary>
        public void RequestSync()
        {
            if (!IsSignedIn || !isActiveAndEnabled)
                return;

            if (_syncing)
            {
                _dirty = true;
                return;
            }

            StartCoroutine(SyncRoutine());
        }

        /// <summary>Удаляет аккаунт и всё, что уехало на сервер.</summary>
        public void DeleteAccount()
        {
            if (!IsSignedIn || _syncing)
                return;
            StartCoroutine(DeleteRoutine());
        }

        private IEnumerator AwaitSignIn()
        {
            // Диалог живёт столько, сколько нужно человеку; ограничиваем ожидание,
            // чтобы корутина не висела вечно, если он ушёл из приложения.
            float deadline = Time.unscaledTime + 180f;

            while (Time.unscaledTime < deadline)
            {
                if (_auth.TryTakeError(out string message))
                {
                    Debug.Log($"[SyncService] вход не состоялся: {message}");
                    yield break;
                }

                if (_auth.TryTakeIdToken(out string idToken))
                {
                    yield return _client.ExchangeGoogleIdToken(idToken, (outcome, token) =>
                    {
                        if (outcome == SupabaseClient.Outcome.Ok && token != null)
                        {
                            _session.Adopt(token.access_token, token.refresh_token,
                                           token.expires_in, SupabaseSession.NowUnix());
                            Changed?.Invoke();
                        }
                        else
                        {
                            Debug.LogWarning($"[SyncService] обмен токена не удался: {outcome}");
                        }
                    });

                    if (IsSignedIn)
                        RequestSync();
                    yield break;
                }

                yield return null;
            }
        }

        private IEnumerator EnsureFreshSession()
        {
            if (!_session.NeedsRefresh(SupabaseSession.NowUnix()))
                yield break;
            if (string.IsNullOrEmpty(_session.RefreshToken))
                yield break;

            yield return _client.Refresh(_session.RefreshToken, (outcome, token) =>
            {
                if (outcome == SupabaseClient.Outcome.Ok && token != null)
                {
                    _session.Adopt(token.access_token, token.refresh_token,
                                   token.expires_in, SupabaseSession.NowUnix());
                }
                else if (outcome == SupabaseClient.Outcome.Unauthorized ||
                         outcome == SupabaseClient.Outcome.Rejected)
                {
                    // Refresh-токен мёртв. Разлогиниваемся, но локальные данные —
                    // это данные игрока, и они остаются нетронутыми.
                    _session.SignOut();
                    Changed?.Invoke();
                }
            });
        }

        private IEnumerator SyncRoutine()
        {
            _syncing = true;
            _dirty = false;

            yield return EnsureFreshSession();

            if (_session.IsSignedIn)
            {
                ProfileUserData profile = ProfileUserDataStorage.Load();
                Level0Results level0 = Level0Results.Load();
                Level1Progress level1 = Level1Progress.Load();
                TutorialProgressState progress = _progress?.State ?? TutorialProgressState.NewPlayer;

                SyncState request = SyncPayload.Build(profile, progress, level0, level1);

                yield return _client.SyncProgress(_session.AccessToken, request, (outcome, merged) =>
                {
                    if (outcome != SupabaseClient.Outcome.Ok || merged == null)
                        return;

                    string stampBefore = profile.UpdatedAtIso;
                    TutorialProgressState applied = progress;
                    SyncPayload.Apply(merged, profile, level0, level1, ref applied);

                    level0.Save();
                    level1.Save();

                    // Профиль трогаем, только если Apply действительно принял
                    // серверную копию — и записываем БЕЗ нового штампа, иначе
                    // принятая чужая правка выглядела бы как своя свежая и это
                    // устройство навсегда стало бы «самым новым».
                    if (!string.Equals(profile.UpdatedAtIso, stampBefore, StringComparison.Ordinal))
                        ProfileUserDataStorage.SaveSynced(profile);

                    if (applied > progress)
                        _progress?.Advance(applied);
                });
            }

            _syncing = false;

            if (_dirty)
                RequestSync();
        }

        private IEnumerator DeleteRoutine()
        {
            _syncing = true;

            yield return EnsureFreshSession();

            if (_session.IsSignedIn)
            {
                yield return _client.DeleteAccount(_session.AccessToken, outcome =>
                {
                    if (outcome == SupabaseClient.Outcome.Ok)
                        _session.SignOut();
                    else
                        Debug.LogWarning($"[SyncService] удаление аккаунта не удалось: {outcome}");
                });
            }

            _syncing = false;
            Changed?.Invoke();
        }
    }
}
