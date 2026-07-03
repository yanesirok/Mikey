using System;
using UnityEngine;

namespace Mikey.Pose
{
    /// <summary>
    /// A source of live pose frames plus (optionally) the camera image to show behind
    /// the HUD. Abstracts where poses come from so the same <see cref="PoseController"/>
    /// and scoring run against real on-device MediaPipe (<see cref="AndroidPoseSource"/>)
    /// or an in-Editor simulation (<see cref="SimulatedPoseSource"/>).
    /// </summary>
    public interface IPoseSource
    {
        /// <summary>Raised on the main thread once per delivered pose frame.</summary>
        event Action<PoseFrame> FrameReceived;

        /// <summary>
        /// The live camera image to render behind the skeleton/HUD, or null when the
        /// source has no preview (e.g. simulation, or before the first frame arrives).
        /// </summary>
        Texture CameraTexture { get; }

        bool IsRunning { get; }

        /// <summary>Starts capture/inference. Safe to call again while running (no-op).</summary>
        void StartSession();

        /// <summary>Stops capture/inference and releases resources.</summary>
        void StopSession();

        /// <summary>
        /// Pumps the source once per frame. Pull-based sources (Android) read the latest
        /// native result here; push/simulated sources may advance their state.
        /// </summary>
        void Tick(float deltaTime);
    }
}
