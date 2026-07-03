using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Editor/dev pose source: synthesizes a side-on push-up by oscillating the elbow
    /// angle so the real scoring pipeline (counter + form) runs and the HUD counts reps
    /// without a device or camera. Every Nth rep is performed with sagging hips to also
    /// exercise the no-rep path. Not a product feature — the on-device source is
    /// <see cref="AndroidPoseSource"/>.
    /// </summary>
    public sealed class SimulatedPoseSource : IPoseSource
    {
        private const float PeriodSeconds = 2.5f;   // one full down-up cycle
        private const float TopAngle = 170f;
        private const float BottomAngle = 45f;

        private float _t;
        private int _cycle;
        private float _prevPhase; // tracks cosine sign to detect a completed cycle

        public event Action<PoseFrame> FrameReceived;

        public UnityEngine.Texture CameraTexture => null;
        public bool IsRunning { get; private set; }

        public void StartSession()
        {
            _t = 0f;
            _cycle = 0;
            _prevPhase = 0f;
            IsRunning = true;
        }

        public void StopSession() => IsRunning = false;

        public void Tick(float deltaTime)
        {
            if (!IsRunning)
                return;

            _t += deltaTime;
            float phase = (_t % PeriodSeconds) / PeriodSeconds; // 0..1

            // Cosine: 0 at top, 1 at bottom, back to top — smooth and monotone each half.
            float s = 0.5f * (1f - (float)Math.Cos(phase * 2.0 * Math.PI));
            float elbow = TopAngle + (BottomAngle - TopAngle) * s;

            // Count cycles to vary form: every 3rd rep sags at the bottom.
            if (phase < _prevPhase)
                _cycle++;
            _prevPhase = phase;

            bool sagThisRep = (_cycle % 3) == 2;
            float hipOffset = sagThisRep ? 0.02f + 0.07f * s : 0f; // sag deepens toward the bottom

            FrameReceived?.Invoke(BuildFrame(elbow, hipOffset, _t));
        }

        // Same side-on layout the unit tests use: shoulder anchors both the arm and the
        // flat shoulder→ankle body line; the wrist realizes the target elbow angle.
        private static PoseFrame BuildFrame(float elbowAngleDeg, float hipOffset, double timestamp)
        {
            var lm = new PoseLandmark[PoseFrame.LandmarkCount];
            for (int i = 0; i < lm.Length; i++)
                lm[i] = new PoseLandmark(0f, 0f, 0f, 1f);

            float sx = 0.3f, sy = 0.5f;
            float ex = sx, ey = sy + 0.2f;
            double wd = (-90.0 + elbowAngleDeg) * Math.PI / 180.0;
            float wx = ex + 0.2f * (float)Math.Cos(wd);
            float wy = ey + 0.2f * (float)Math.Sin(wd);
            float ax = 0.8f, ay = sy;
            float hx = 0.55f, hy = sy + hipOffset;

            void S(PoseLandmarkType t, float x, float y) => lm[(int)t] = new PoseLandmark(x, y, 0f, 1f);
            S(PoseLandmarkType.LeftShoulder, sx, sy); S(PoseLandmarkType.LeftElbow, ex, ey); S(PoseLandmarkType.LeftWrist, wx, wy);
            S(PoseLandmarkType.LeftHip, hx, hy); S(PoseLandmarkType.LeftAnkle, ax, ay);
            S(PoseLandmarkType.RightShoulder, sx, sy); S(PoseLandmarkType.RightElbow, ex, ey); S(PoseLandmarkType.RightWrist, wx, wy);
            S(PoseLandmarkType.RightHip, hx, hy); S(PoseLandmarkType.RightAnkle, ax, ay);

            return new PoseFrame(lm, timestamp);
        }
    }
}
