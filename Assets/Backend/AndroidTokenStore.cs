using UnityEngine;

namespace Mikey.Backend
{
    /// <summary>
    /// Мост к <c>com.mikey.auth.SecureStore</c>. Если шифрованное хранилище не
    /// поднялось, ведём себя как «токена нет»: это означает повторный вход, а не
    /// падение приложения.
    /// </summary>
    public sealed class AndroidTokenStore : ITokenStore
    {
        private const string Key = "supabase.refresh";

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _store;

        private AndroidJavaObject Store()
        {
            if (_store != null)
                return _store;

            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    _store = new AndroidJavaObject("com.mikey.auth.SecureStore", activity);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AndroidTokenStore] шифрованное хранилище недоступно: {e}");
            }

            return _store;
        }

        public bool TryLoadRefreshToken(out string token)
        {
            token = Store()?.Call<string>("get", Key);
            return !string.IsNullOrEmpty(token);
        }

        public void SaveRefreshToken(string token) => Store()?.Call("put", Key, token);

        public void Clear() => Store()?.Call("remove", Key);
#else
        public bool TryLoadRefreshToken(out string token)
        {
            token = null;
            return false;
        }

        public void SaveRefreshToken(string token) { }

        public void Clear() { }
#endif
    }
}
