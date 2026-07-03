using Mikey.Pose;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Mikey.Pose.DevSandbox
{
    /// <summary>
    /// Standalone development harness for exercise checking. Two screens, drawn zero-asset via
    /// OnGUI: a <b>picker</b> listing every exercise in <see cref="ExerciseCatalog"/>, then a
    /// <b>live</b> screen where the chosen exercise runs against the camera (simulated input in
    /// the Editor, native MediaPipe on an Android device). Adding an exercise to the catalog
    /// makes it appear here automatically — no changes needed.
    ///
    /// Not shipped in the player flow — a dev tool for iterating without walking the game's
    /// navigation.
    /// </summary>
    [RequireComponent(typeof(PoseController))]
    public sealed class ExerciseSandbox : MonoBehaviour
    {
        private PoseController _controller;
        private GUIStyle _title;
        private GUIStyle _big;
        private GUIStyle _mid;
        private GUIStyle _cue;
        private Texture2D _dot;

        // Front-camera preview is usually flipped/mirrored vs screen; toggled live on-device.
        private bool _flipY = true;
        private bool _mirrorX = true;

        private AndroidVoice _voice;
        private string _lastSpokenCue = string.Empty;
        private float _lastSpeakTime;
        private const float MinSpeakInterval = 2.5f;

        private string _savedLogPath;

        private void Awake()
        {
            _controller = GetComponent<PoseController>();
            _voice = new AndroidVoice();
        }

        private void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
                Permission.RequestUserPermission(Permission.Camera);
#endif
        }

        private void Update()
        {
            if (_controller == null || !_controller.HasExercise)
                return;

            IExerciseAnalyzer a = _controller.Analyzer;
            if (a == null)
                return;

            // Speak a form-correction cue when the fault changes (throttled so it doesn't nag).
            // Skip "get in frame" — only speak actual form corrections.
            if (a.FormState == ExerciseFormState.BadForm && !string.IsNullOrEmpty(a.Cue))
            {
                if (a.Cue != _lastSpokenCue && Time.time - _lastSpeakTime >= MinSpeakInterval)
                {
                    _voice.Speak(a.Cue);
                    _lastSpokenCue = a.Cue;
                    _lastSpeakTime = Time.time;
                }
            }
            else
            {
                // Reset so the next fault re-speaks even if it's the same text.
                _lastSpokenCue = string.Empty;
            }
        }

        private void OnDestroy() => _voice?.Dispose();

        private void EnsureStyles()
        {
            if (_big != null)
                return;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 48, fontStyle = FontStyle.Bold };
            _big = new GUIStyle(GUI.skin.label) { fontSize = 120, fontStyle = FontStyle.Bold };
            _mid = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold };
            _cue = new GUIStyle(GUI.skin.label) { fontSize = 56, fontStyle = FontStyle.Bold };

            _dot = new Texture2D(1, 1);
            _dot.SetPixel(0, 0, Color.white);
            _dot.Apply();
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (_controller.HasExercise)
                DrawLive();
            else
                DrawPicker();
        }

        private void DrawPicker()
        {
            GUILayout.BeginArea(new Rect(40, 40, Screen.width - 80, Screen.height - 80));
            GUILayout.Label("SELECT EXERCISE", _title);
            GUILayout.Label("Совет: телефон сбоку, всё тело в кадр (голова–стопы)", _mid);
            GUILayout.Space(24);

            foreach (ExerciseDescriptor exercise in ExerciseCatalog.All)
            {
                if (GUILayout.Button(exercise.DisplayName, GUILayout.Height(110)))
                    _controller.SelectExercise(exercise.Create());
            }

            GUILayout.Space(24);
            if (GUILayout.Button("SAVE LAST LOG", GUILayout.Height(90)))
                _savedLogPath = _controller.SaveRecording();
            if (!string.IsNullOrEmpty(_savedLogPath))
                GUILayout.Label($"saved: {_savedLogPath}", _mid);

            GUILayout.EndArea();
        }

        private void DrawLive()
        {
            IExerciseAnalyzer a = _controller.Analyzer;
            if (a == null)
                return;

            // Camera image + skeleton share one (optionally) flipped/mirrored space so the
            // overlay always lines up with the picture regardless of the toggles.
            Matrix4x4 oldMatrix = GUI.matrix;
            if (_flipY || _mirrorX)
            {
                var scale = new Vector2(_mirrorX ? -1f : 1f, _flipY ? -1f : 1f);
                GUIUtility.ScaleAroundPivot(scale, new Vector2(Screen.width / 2f, Screen.height / 2f));
            }

            Texture cam = _controller.CameraTexture;
            if (cam != null)
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), cam, ScaleMode.StretchToFill);

            DrawSkeleton();
            GUI.matrix = oldMatrix;

            GUILayout.BeginArea(new Rect(40, 40, Screen.width - 80, Screen.height - 80));

            GUILayout.Label(a.DisplayName.ToUpperInvariant(), _mid);
            GUILayout.Label(a.Reps.ToString(), _big);
            GUILayout.Label($"no-reps: {a.NoReps}", _mid);

            Color prev = GUI.color;
            GUI.color = ColorFor(a.FormState);
            GUILayout.Label(StatusText(a.FormState), _mid);
            if (!string.IsNullOrEmpty(a.Cue))
                GUILayout.Label(a.Cue, _cue);
            GUI.color = prev;

            GUILayout.Space(16);
            GUILayout.Label($"source: {(Application.isEditor ? "Simulation (Editor)" : "Device / MediaPipe")}", _mid);
            GUILayout.Label(a.DebugInfo, _mid);

            GUILayout.Space(16);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Back (save)", GUILayout.Width(220), GUILayout.Height(80)))
            {
                // Auto-save the recording on exit so a session is never lost.
                _savedLogPath = _controller.SaveRecording();
                _controller.ClearExercise();
            }
            if (GUILayout.Button("Reset set", GUILayout.Width(200), GUILayout.Height(80)))
                _controller.ResetSession();
            if (GUILayout.Button("Restart cam", GUILayout.Width(240), GUILayout.Height(80)))
            {
                _controller.enabled = false;
                _controller.enabled = true;
            }
            if (GUILayout.Button(_flipY ? "Flip Y: ON" : "Flip Y: off", GUILayout.Width(200), GUILayout.Height(80)))
                _flipY = !_flipY;
            if (GUILayout.Button(_mirrorX ? "Mirror X: ON" : "Mirror X: off", GUILayout.Width(230), GUILayout.Height(80)))
                _mirrorX = !_mirrorX;
            if (GUILayout.Button("SAVE LOG", GUILayout.Width(220), GUILayout.Height(80)))
                _savedLogPath = _controller.SaveRecording();
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_savedLogPath))
                GUILayout.Label($"saved: {_savedLogPath}", _mid);

            GUILayout.EndArea();
        }

        // Draws the 33 pose landmarks as dots so tracking is visible over the camera image.
        // Scored joints (shoulders/elbows/wrists/hips/ankles) are larger. Runs inside the same
        // flip/mirror transform as the camera so dots stay registered to the picture.
        private void DrawSkeleton()
        {
            PoseFrame f = _controller.LatestFrame;
            if (f == null || _dot == null)
                return;

            Color prev = GUI.color;
            for (int i = 0; i < PoseFrame.LandmarkCount; i++)
            {
                PoseLandmark lm = f.Landmark(i);
                if (lm.Visibility < 0.3f)
                    continue;

                // The camera Texture2D is drawn with GUI's inverted-Y convention, so the
                // skeleton must invert Y too (1 - y) to register with the picture; the shared
                // Flip Y / Mirror X transform then orients the whole composite.
                float x = lm.X * Screen.width;
                float y = (1f - lm.Y) * Screen.height;
                float r = IsScored(i) ? 14f : 8f;

                GUI.color = lm.Visibility >= 0.6f ? new Color(0.17f, 0.85f, 0.66f) : new Color(1f, 0.75f, 0.2f);
                GUI.DrawTexture(new Rect(x - r, y - r, r * 2f, r * 2f), _dot);
            }
            GUI.color = prev;
        }

        private static bool IsScored(int index)
        {
            switch (index)
            {
                case 11: case 12: case 13: case 14: case 15: case 16:
                case 23: case 24: case 27: case 28:
                    return true;
                default:
                    return false;
            }
        }

        private static string StatusText(ExerciseFormState state)
        {
            switch (state)
            {
                case ExerciseFormState.NotVisible: return "ALIGN IN FRAME";
                case ExerciseFormState.GoodForm: return "GOOD FORM";
                default: return "FIX FORM";
            }
        }

        private static Color ColorFor(ExerciseFormState state)
        {
            switch (state)
            {
                case ExerciseFormState.NotVisible: return Color.gray;
                case ExerciseFormState.GoodForm: return new Color(0.17f, 0.85f, 0.66f); // jade
                default: return new Color(1f, 0.35f, 0.12f);                             // ember
            }
        }
    }
}
