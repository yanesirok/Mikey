namespace Mikey.UI.Combine
{
    /// <summary>
    /// The four frontend-only display states the Combine screen can show.
    /// Drives which sub-view of the screen is visible. All states are mock /
    /// local-only; no backend, networking, camera, or pose data is involved.
    /// </summary>
    public enum CombineState
    {
        /// <summary>Waiting on (mock) data — spinner / placeholder shown.</summary>
        Loading,

        /// <summary>No baseline items selected yet — empty prompt shown.</summary>
        Empty,

        /// <summary>Baseline ready — the mock selected items are shown.</summary>
        Ready,

        /// <summary>Something went wrong — error prompt with retry shown.</summary>
        Error,
    }
}
