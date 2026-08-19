namespace Mikey.Pose
{
    /// <summary>
    /// The subset of MediaPipe BlazePose's 33 body landmarks that push-up analysis
    /// needs, keyed by their canonical BlazePose index. The native plugin emits all
    /// 33 landmarks in this index order; scoring only reads these.
    ///
    /// Full index reference: https://ai.google.dev/edge/mediapipe/solutions/vision/pose_landmarker
    /// </summary>
    public enum PoseLandmarkType
    {
        /// <summary>Head anchor for jodan (head-level) targets.</summary>
        Nose = 0,

        LeftShoulder = 11,
        RightShoulder = 12,
        LeftElbow = 13,
        RightElbow = 14,
        LeftWrist = 15,
        RightWrist = 16,
        LeftHip = 23,
        RightHip = 24,
        LeftKnee = 25,
        RightKnee = 26,
        LeftAnkle = 27,
        RightAnkle = 28,

        // Стопы: носок относительно пятки задаёт направление «вперёд» у стоек —
        // единственный признак в кадре, который не путается при зеркальной стойке.
        LeftHeel = 29,
        RightHeel = 30,
        LeftFootIndex = 31,
        RightFootIndex = 32,
    }
}
