using System;
using UnityEngine;

namespace Mikey.Pose
{
    /// <summary>
    /// Production pose source: bridges to the native Kotlin/MediaPipe plugin
    /// (<c>com.mikey.pose.PoseSession</c>). Capture and inference stay entirely native;
    /// this reads the latest 33 landmarks per frame (pull model) and exposes the camera
    /// image as an external GPU texture — no camera frames cross into managed memory.
    ///
    /// The class compiles on every platform but only does real work on an Android device;
    /// <see cref="PoseController"/> only instantiates it there.
    /// </summary>
    public sealed class AndroidPoseSource : IPoseSource
    {
        // 33 landmarks × {x, y, z, visibility}.
        private const int Floats = PoseFrame.LandmarkCount * 4;

        public event Action<PoseFrame> FrameReceived;

        public Texture CameraTexture { get; private set; }
        public bool IsRunning { get; private set; }

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _session;
        private Texture2D _camTex;
        private byte[] _rgba;

        public void StartSession()
        {
            if (IsRunning)
                return;
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    _session = new AndroidJavaObject("com.mikey.pose.PoseSession", activity);
                    _session.Call("start");
                }
                IsRunning = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AndroidPoseSource] Failed to start native pose session: {e}");
            }
        }

        public void StopSession()
        {
            if (_session != null)
            {
                try { _session.Call("stop"); } catch (Exception e) { Debug.LogWarning($"[AndroidPoseSource] stop failed: {e}"); }
                _session.Dispose();
                _session = null;
            }
            IsRunning = false;
        }

        public void Tick(float deltaTime)
        {
            if (_session == null)
                return;

            UpdateCameraTexture();

            // readLatest() returns the 132-float landmark array for a fresh frame, or null
            // when no new inference result is available since the last read.
            float[] data = _session.Call<float[]>("readLatest");
            if (data == null || data.Length != Floats)
                return;

            var lm = new PoseLandmark[PoseFrame.LandmarkCount];
            for (int i = 0; i < PoseFrame.LandmarkCount; i++)
            {
                int o = i * 4;
                lm[i] = new PoseLandmark(data[o], data[o + 1], data[o + 2], data[o + 3]);
            }

            FrameReceived?.Invoke(new PoseFrame(lm, Time.realtimeSinceStartupAsDouble));
        }

        // Camera preview: the plugin hands us the latest analyzed frame as RGBA8888 bytes,
        // which we upload into a Texture2D for the HUD background. Simple and device-agnostic
        // (a per-frame copy — fine for a preview; can move to zero-copy GL interop later).
        private void UpdateCameraTexture()
        {
            // sbyte[] (not byte[]) avoids Unity's per-call "byte array is obsolete" JNI
            // marshalling warning, which otherwise logs a full stack trace every frame.
            sbyte[] pixels = _session.Call<sbyte[]>("readFramePixels");
            if (pixels == null)
                return;

            int w = _session.Call<int>("getFrameWidth");
            int h = _session.Call<int>("getFrameHeight");
            int len = w * h * 4;
            if (w <= 0 || h <= 0 || pixels.Length != len)
                return;

            if (_camTex == null || _camTex.width != w || _camTex.height != h)
                _camTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            if (_rgba == null || _rgba.Length != len)
                _rgba = new byte[len];

            System.Buffer.BlockCopy(pixels, 0, _rgba, 0, len);
            _camTex.LoadRawTextureData(_rgba);
            _camTex.Apply(false);
            CameraTexture = _camTex;
        }
#else
        public void StartSession() => IsRunning = true;
        public void StopSession() => IsRunning = false;
        public void Tick(float deltaTime) { }
#endif
    }
}
