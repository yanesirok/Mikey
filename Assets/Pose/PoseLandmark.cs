namespace Mikey.Pose
{
    /// <summary>
    /// One body landmark as emitted by MediaPipe Pose Landmarker: normalized image
    /// coordinates plus a visibility confidence. Engine-free value type so scoring
    /// stays deterministic and EditMode-testable.
    ///
    /// Coordinate convention (MediaPipe normalized landmarks):
    /// <list type="bullet">
    ///   <item><see cref="X"/>, <see cref="Y"/> in [0,1], origin top-left, Y grows downward.</item>
    ///   <item><see cref="Z"/> roughly metric depth relative to the hips (noisier from a
    ///   single camera — push-up scoring stays in the 2D x/y plane).</item>
    ///   <item><see cref="Visibility"/> in [0,1]; low values mean the point is occluded
    ///   or out of frame and should not be trusted.</item>
    /// </list>
    /// </summary>
    public readonly struct PoseLandmark
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;
        public readonly float Visibility;

        public PoseLandmark(float x, float y, float z, float visibility)
        {
            X = x;
            Y = y;
            Z = z;
            Visibility = visibility;
        }

        /// <summary>True when the point is confident enough to score against.</summary>
        public bool IsReliable(float minVisibility) => Visibility >= minVisibility;
    }
}
