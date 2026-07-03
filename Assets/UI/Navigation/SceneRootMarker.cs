using UnityEngine;

namespace Mikey.UI.Navigation
{
    /// <summary>
    /// Tags the root of a heavy additive scene with the screenId it backs, so the loader and
    /// tests can recognize a loaded screen scene. One per heavy scene, on a root GameObject.
    /// </summary>
    public sealed class SceneRootMarker : MonoBehaviour
    {
        [SerializeField] private string screenId;

        public string ScreenId => screenId;

        /// <summary>Test-only setter; the field is normally assigned in the Inspector.</summary>
        public void SetScreenIdForTests(string value) => screenId = value;
    }
}
