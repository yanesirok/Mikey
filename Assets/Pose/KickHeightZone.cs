namespace Mikey.Pose
{
    /// <summary>Karate height levels a kick can reach, ordered so bigger = higher.</summary>
    public enum KickZone
    {
        None = 0,
        Gedan = 1,
        Chudan = 2,
        Jodan = 3,
    }

    /// <summary>
    /// Classifies the kicking ankle's height against the same frame's hip and shoulder
    /// (image-space Y, down-positive) — robust to the player moving in frame and needs
    /// no per-height calibration. The caller decides whether the leg is lifted at all;
    /// a lifted ankle below the hip is Gedan.
    /// </summary>
    public static class KickHeightZone
    {
        public static KickZone Classify(float ankleY, float hipY, float shoulderY) =>
            ankleY <= shoulderY ? KickZone.Jodan
            : ankleY <= hipY ? KickZone.Chudan
            : KickZone.Gedan;
    }
}
