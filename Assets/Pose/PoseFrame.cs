namespace Mikey.Pose
{
    /// <summary>
    /// One frame of pose data: all 33 BlazePose landmarks plus a capture timestamp.
    /// The native plugin fills these in index order; scoring reads the subset it needs
    /// via <see cref="PoseLandmarkType"/>. Engine-free so it can be constructed from
    /// synthetic data in tests.
    /// </summary>
    public sealed class PoseFrame
    {
        public const int LandmarkCount = 33;

        private readonly PoseLandmark[] _landmarks;

        /// <summary>Capture time in seconds (native monotonic clock). Used for tempo.</summary>
        public double TimestampSeconds { get; }

        /// <summary>
        /// Wraps a 33-element landmark array. The array is taken by reference and must
        /// not be mutated by the caller afterwards; the native bridge allocates a fresh
        /// array per delivered frame.
        /// </summary>
        public PoseFrame(PoseLandmark[] landmarks, double timestampSeconds)
        {
            if (landmarks == null)
                throw new System.ArgumentNullException(nameof(landmarks));
            if (landmarks.Length != LandmarkCount)
                throw new System.ArgumentException(
                    $"Expected {LandmarkCount} landmarks, got {landmarks.Length}.", nameof(landmarks));

            _landmarks = landmarks;
            TimestampSeconds = timestampSeconds;
        }

        public PoseLandmark Get(PoseLandmarkType type) => _landmarks[(int)type];

        /// <summary>Raw landmark by BlazePose index [0,33) — used by the skeleton overlay.</summary>
        public PoseLandmark Landmark(int index) => _landmarks[index];

        /// <summary>
        /// Lowest visibility across a set of landmarks — a chain is only as trustworthy
        /// as its weakest joint. Used by the evaluator to pick the better-visible body
        /// side for a side-on push-up (where the far arm is often occluded).
        /// </summary>
        public float MinVisibility(PoseLandmarkType a, PoseLandmarkType b, PoseLandmarkType c)
        {
            float va = Get(a).Visibility;
            float vb = Get(b).Visibility;
            float vc = Get(c).Visibility;
            float min = va < vb ? va : vb;
            return min < vc ? min : vc;
        }
    }
}
