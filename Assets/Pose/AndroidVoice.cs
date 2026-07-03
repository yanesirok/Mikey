using System;
using UnityEngine;

namespace Mikey.Pose
{
    /// <summary>
    /// Speaks coaching cues aloud. On an Android device it drives the native
    /// <c>com.mikey.pose.Speaker</c> (Android TextToSpeech); in the Editor it logs instead,
    /// so the same calling code works everywhere. Cheap to construct; call <see cref="Dispose"/>
    /// to release the engine.
    /// </summary>
    public sealed class AndroidVoice : IDisposable
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _speaker;

        public AndroidVoice()
        {
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    _speaker = new AndroidJavaObject("com.mikey.pose.Speaker", activity);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AndroidVoice] init failed: {e}");
            }
        }

        public void Speak(string text)
        {
            if (string.IsNullOrEmpty(text) || _speaker == null)
                return;
            try { _speaker.Call("speak", text); }
            catch (Exception e) { Debug.LogWarning($"[AndroidVoice] speak failed: {e}"); }
        }

        public void Dispose()
        {
            if (_speaker == null)
                return;
            try { _speaker.Call("stop"); } catch { /* ignore */ }
            _speaker.Dispose();
            _speaker = null;
        }
#else
        public void Speak(string text)
        {
            if (!string.IsNullOrEmpty(text))
                Debug.Log($"[Voice] {text}");
        }

        public void Dispose() { }
#endif
    }
}
