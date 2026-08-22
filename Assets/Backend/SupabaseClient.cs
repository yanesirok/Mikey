using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Mikey.Backend
{
    /// <summary>
    /// Три запроса, которые нужны приложению: обмен Google ID-токена на сессию,
    /// обновление сессии и вызов функции слияния. Ничего не решает — решения
    /// принимает <see cref="SyncService"/>.
    /// </summary>
    public sealed class SupabaseClient
    {
        /// <summary>Ответ GoTrue на выдачу или обновление сессии.</summary>
        [Serializable]
        public sealed class TokenResponse
        {
            public string access_token;
            public string refresh_token;
            public long expires_in;
        }

        /// <summary>Итог запроса. Различает «сеть/сервер» и «отказ» — политика повтора у них разная.</summary>
        public enum Outcome
        {
            Ok,
            Retryable,     // нет сети, таймаут, 5xx — повторим при следующем триггере
            Unauthorized,  // 401 — нужен refresh или разлогин
            Rejected,      // 4xx — повторять бессмысленно
        }

        private const int TimeoutSeconds = 20;

        private readonly SupabaseConfig _config;

        public SupabaseClient(SupabaseConfig config) =>
            _config = config ?? throw new ArgumentNullException(nameof(config));

        /// <summary>Меняет Google ID-токен на сессию Supabase.</summary>
        public IEnumerator ExchangeGoogleIdToken(string idToken, string rawNonce,
                                                 Action<Outcome, TokenResponse> done)
        {
            string url = $"{_config.Url}/auth/v1/token?grant_type=id_token";
            var body = new StringBuilder("{\"provider\":\"google\",\"id_token\":\"")
                .Append(Escape(idToken)).Append('"');
            if (!string.IsNullOrEmpty(rawNonce))
                body.Append(",\"nonce\":\"").Append(Escape(rawNonce)).Append('"');
            body.Append('}');
            return PostJson(url, body.ToString(), accessToken: null, done);
        }

        /// <summary>Обновляет сессию по refresh-токену.</summary>
        public IEnumerator Refresh(string refreshToken, Action<Outcome, TokenResponse> done)
        {
            string url = $"{_config.Url}/auth/v1/token?grant_type=refresh_token";
            string body = "{\"refresh_token\":\"" + Escape(refreshToken) + "\"}";
            return PostJson(url, body, accessToken: null, done, badRequestMeansDeadSession: true);
        }

        /// <summary>Вызывает sync_progress и отдаёт слитое состояние.</summary>
        public IEnumerator SyncProgress(string accessToken, SyncState state, Action<Outcome, SyncState> done)
        {
            string url = $"{_config.Url}/rest/v1/rpc/sync_progress";
            string body = "{\"payload\":" + JsonUtility.ToJson(state) + "}";

            yield return Send(url, body, accessToken, (outcome, text) =>
            {
                SyncState merged = null;
                if (outcome == Outcome.Ok && !string.IsNullOrEmpty(text))
                {
                    try
                    {
                        merged = JsonUtility.FromJson<SyncState>(text);
                    }
                    catch (Exception e)
                    {
                        // Неразбираемый ответ — не повод трогать локальные данные.
                        Debug.LogWarning($"[SupabaseClient] ответ не разобран: {e.Message}");
                        outcome = Outcome.Rejected;
                    }
                }
                done?.Invoke(outcome, merged);
            });
        }

        /// <summary>Удаляет аккаунт на сервере.</summary>
        public IEnumerator DeleteAccount(string accessToken, Action<Outcome> done)
        {
            string url = $"{_config.Url}/rest/v1/rpc/delete_account";
            yield return Send(url, "{}", accessToken, (outcome, _) => done?.Invoke(outcome));
        }

        private IEnumerator PostJson(string url, string body, string accessToken,
                                     Action<Outcome, TokenResponse> done,
                                     bool badRequestMeansDeadSession = false)
        {
            yield return Send(url, body, accessToken, (outcome, text) =>
            {
                TokenResponse parsed = null;
                if (outcome == Outcome.Ok && !string.IsNullOrEmpty(text))
                {
                    try
                    {
                        parsed = JsonUtility.FromJson<TokenResponse>(text);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[SupabaseClient] ответ токена не разобран: {e.Message}");
                        outcome = Outcome.Rejected;
                    }
                }
                done?.Invoke(outcome, parsed);
            }, badRequestMeansDeadSession);
        }

        private IEnumerator Send(string url, string body, string accessToken,
                                 Action<Outcome, string> done,
                                 bool badRequestMeansDeadSession = false)
        {
            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = TimeoutSeconds;
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("apikey", _config.PublishableKey);
                if (!string.IsNullOrEmpty(accessToken))
                    request.SetRequestHeader("Authorization", "Bearer " + accessToken);

                yield return request.SendWebRequest();

                Outcome outcome;
                if (request.result == UnityWebRequest.Result.ConnectionError)
                {
                    outcome = Outcome.Retryable;
                }
                else if (request.responseCode == 401 || request.responseCode == 403)
                {
                    outcome = Outcome.Unauthorized;
                }
                else if (request.responseCode >= 500)
                {
                    outcome = Outcome.Retryable;
                }
                else if (request.responseCode == 429)
                {
                    // Ограничение частоты — состояние временное. Повторим при следующем
                    // естественном триггере, а не разлогиним человека.
                    outcome = Outcome.Retryable;
                }
                else if (badRequestMeansDeadSession && request.responseCode == 400)
                {
                    // На конечной точке обновления 400 означает ровно одно:
                    // refresh-токен недействителен. Это не «сервер отверг запрос»,
                    // это «сессии больше нет».
                    outcome = Outcome.Unauthorized;
                }
                else if (request.responseCode >= 400)
                {
                    Debug.LogWarning($"[SupabaseClient] {request.responseCode} на {url}: " +
                                     $"{request.downloadHandler.text}");
                    outcome = Outcome.Rejected;
                }
                else
                {
                    outcome = Outcome.Ok;
                }

                done?.Invoke(outcome, request.downloadHandler.text);
            }
        }

        private static string Escape(string value) =>
            string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
