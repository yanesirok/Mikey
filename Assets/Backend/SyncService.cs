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

        /// <summary>
        /// Идёт ли сейчас попытка входа: системный диалог открыт или обмен токена
        /// в полёте. Экран входа держит на этом кнопки выключенными, а по спаду
        /// флага без сессии показывает честное «не получилось, попробуйте ещё» —
        /// иначе нажатие, которое ничем не кончилось, выглядит как мёртвая кнопка.
        /// </summary>
        public bool IsSigningIn { get; private set; }

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

        /// <summary>
        /// Корутину убивает выключение компонента, и флаг остался бы взведён навсегда —
        /// синхронизация умерла бы молча до перезагрузки сцены.
        /// </summary>
        private void OnDisable()
        {
            _syncing = false;
            IsSigningIn = false;
        }

        /// <summary>Начинает вход. Диалог системный, результат забираем опросом.</summary>
        public void SignIn()
        {
            if (!CanSignIn || IsSigningIn)
                return;
            IsSigningIn = true;
            Changed?.Invoke();
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

        /// <summary>
        /// Обёртка ровно ради одного: чем бы попытка ни кончилась — успехом,
        /// ошибкой или тем, что человек ушёл из приложения, — флаг снимается и
        /// UI получает сигнал в одном месте, а не в трёх точках выхода.
        /// </summary>
        private IEnumerator AwaitSignIn()
        {
            yield return SignInAttempt();
            IsSigningIn = false;
            Changed?.Invoke();
        }

        private IEnumerator SignInAttempt()
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

                if (_auth.TryTakeIdToken(out string idToken, out string rawNonce))
                {
                    yield return _client.ExchangeGoogleIdToken(idToken, rawNonce, (outcome, token) =>
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
                else if (outcome == SupabaseClient.Outcome.Unauthorized)
                {
                    // Разлогин — только по настоящему отказу в аутентификации. Любой
                    // другой отказ может быть временным, а цена ошибки несимметрична:
                    // лишний повтор стоит одного запроса, лишний разлогин — потерянного
                    // входа без возможности восстановиться.
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

                    // Перечитываем локальное состояние ЗАНОВО. Пока запрос летел, человек
                    // мог записать подход — а запись подхода сама является триггером
                    // синхронизации, то есть запрос уходит ровно в разгар тренировки.
                    // Save() у обоих классов слепо перезаписывает ключ, а не сливает,
                    // поэтому сохранение предполётного снимка стёрло бы всё, что
                    // появилось за время полёта.
                    Level0Results freshLevel0 = Level0Results.Load();
                    Level1Progress freshLevel1 = Level1Progress.Load();
                    ProfileUserData freshProfile = ProfileUserDataStorage.Load();
                    TutorialProgressState current = _progress?.State ?? TutorialProgressState.NewPlayer;

                    string stampBefore = freshProfile.UpdatedAtIso;
                    TutorialProgressState applied = current;
                    SyncPayload.Apply(merged, freshProfile, freshLevel0, freshLevel1, ref applied);

                    freshLevel0.Save();
                    freshLevel1.Save();

                    // Профиль трогаем, только если Apply действительно принял серверную
                    // копию — и записываем БЕЗ нового штампа, иначе устройство навсегда
                    // стало бы «самым новым».
                    if (!string.Equals(freshProfile.UpdatedAtIso, stampBefore, StringComparison.Ordinal))
                        ProfileUserDataStorage.SaveSynced(freshProfile);

                    if (applied > current)
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

            if (_dirty)
                RequestSync();
        }
    }
}
