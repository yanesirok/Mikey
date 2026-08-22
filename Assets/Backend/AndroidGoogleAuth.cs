using UnityEngine;

namespace Mikey.Backend
{
    /// <summary>
    /// Мост к <c>com.mikey.auth.GoogleAuth</c>. Вне Android-устройства
    /// <see cref="IsAvailable"/> ложно, и приложение просто не показывает вход —
    /// в редакторе Credential Manager не существует.
    /// </summary>
    public sealed class AndroidGoogleAuth : IAuthGateway
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _auth;

        public bool IsAvailable => true;

        public void BeginSignIn(string webClientId)
        {
            try
            {
                if (_auth == null)
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                        _auth = new AndroidJavaObject("com.mikey.auth.GoogleAuth", activity);
                }

                _auth.Call("requestIdToken", webClientId);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AndroidGoogleAuth] не удалось начать вход: {e}");
            }
        }

        public bool TryTakeIdToken(out string idToken)
        {
            idToken = _auth?.Call<string>("consumeIdToken");
            return !string.IsNullOrEmpty(idToken);
        }

        public bool TryTakeError(out string message)
        {
            message = _auth?.Call<string>("consumeError");
            return !string.IsNullOrEmpty(message);
        }
#else
        public bool IsAvailable => false;

        public void BeginSignIn(string webClientId) { }

        public bool TryTakeIdToken(out string idToken)
        {
            idToken = null;
            return false;
        }

        public bool TryTakeError(out string message)
        {
            message = null;
            return false;
        }
#endif
    }
}
