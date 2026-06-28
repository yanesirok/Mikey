namespace Mikey.UI.CameraTest
{
    /// <summary>
    /// The frontend-only form/detection states the Camera Test HUD can show.
    /// Mirrors the mock "detection locks on as you settle into frame" direction
    /// from the design reference. All states are local/mock — no real camera,
    /// pose detection, ML, networking, or backend data is involved.
    /// </summary>
    public enum CameraFormStatus
    {
        /// <summary>No reps yet — prompt the user to get into frame.</summary>
        Ready,

        /// <summary>Detection still settling — ask the user to hold steady.</summary>
        Adjust,

        /// <summary>Form looks good — encouraging confirmation.</summary>
        Good,
    }
}
