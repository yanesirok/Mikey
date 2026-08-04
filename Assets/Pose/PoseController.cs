using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Mikey.Pose
{
    /// <summary>
    /// The seam between pose input and the game. Owns an <see cref="IPoseSource"/> and the
    /// currently selected <see cref="IExerciseAnalyzer"/>, feeds every delivered frame into
    /// the analyzer, and exposes the resulting state plus a <see cref="Changed"/> event.
    ///
    /// Exercise-agnostic: no exercise is active (and the camera stays off) until
    /// <see cref="SelectExercise"/> is called, matching the "pick an exercise, then the camera
    /// starts" flow. Source selection is automatic: native MediaPipe on an Android device,
    /// <see cref="SimulatedPoseSource"/> in the Editor.
    /// </summary>
    public sealed class PoseController : MonoBehaviour
    {
        [Tooltip("Force the in-Editor simulation even on device builds (debugging only).")]
        [SerializeField] private bool _forceSimulation;

        private IPoseSource _source;
        private IExerciseAnalyzer _analyzer;

        // Raw landmark recording for offline debugging: each row is
        // [t, x0,y0,z0,v0, ... x32,y32,z32,v32]. Ring-buffered so it can't grow unbounded.
        private const int MaxRecordFrames = 4000;
        private readonly List<float[]> _recording = new List<float[]>();

        /// <summary>Raised whenever the analyzer's visible state may have changed.</summary>
        public event Action Changed;

        /// <summary>The active exercise analysis, or null when no exercise is selected.</summary>
        public IExerciseAnalyzer Analyzer => _analyzer;

        /// <summary>True while an exercise is selected and the camera should be running.</summary>
        public bool HasExercise => _analyzer != null;

        /// <summary>The camera image to show behind the HUD, or null when unavailable.</summary>
        public Texture CameraTexture => _source?.CameraTexture;

        /// <summary>The most recent pose frame (for the skeleton overlay), or null.</summary>
        public PoseFrame LatestFrame { get; private set; }

        private void Awake()
        {
            _source = CreateSource();
            _source.FrameReceived += OnFrameReceived;
        }

        private IPoseSource CreateSource()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!_forceSimulation)
                return new AndroidPoseSource();
#endif
            return new SimulatedPoseSource();
        }

        /// <summary>
        /// Selects the exercise to score and starts the camera/inference. Replacing an active
        /// exercise swaps the analyzer and keeps the session running.
        /// </summary>
        public void SelectExercise(IExerciseAnalyzer analyzer)
        {
            if (analyzer == null)
                throw new ArgumentNullException(nameof(analyzer));

            // Уровень 0 проверяет, а не учит: каждый CSV — одна сессия одного упражнения,
            // иначе записи склеиваются и разбор с устройства теряет граунд-трус.
            _recording.Clear();

            if (_analyzer != null)
                _analyzer.Changed -= OnAnalyzerChanged;

            _analyzer = analyzer;
            _analyzer.Changed += OnAnalyzerChanged;

            if (isActiveAndEnabled)
                _source.StartSession();

            Changed?.Invoke();
        }

        /// <summary>Stops the camera and clears the selected exercise (back to the picker).</summary>
        public void ClearExercise()
        {
            _source?.StopSession();

            if (_analyzer != null)
                _analyzer.Changed -= OnAnalyzerChanged;
            _analyzer = null;

            Changed?.Invoke();
        }

        /// <summary>Clears the current set without leaving the exercise.</summary>
        public void ResetSession() => _analyzer?.Reset();

        private void OnEnable()
        {
            // Only run the camera when an exercise is actually selected.
            if (_analyzer != null)
                _source?.StartSession();
        }

        private void OnDisable() => _source?.StopSession();

        private void Update() => _source?.Tick(Time.deltaTime);

        private void OnFrameReceived(PoseFrame frame)
        {
            LatestFrame = frame;
            RecordFrame(frame);
            _analyzer?.ProcessFrame(frame);
        }

        private void RecordFrame(PoseFrame frame)
        {
            if (_recording.Count >= MaxRecordFrames)
                _recording.RemoveAt(0);

            var row = new float[1 + PoseFrame.LandmarkCount * 4];
            row[0] = (float)frame.TimestampSeconds;
            for (int i = 0; i < PoseFrame.LandmarkCount; i++)
            {
                PoseLandmark lm = frame.Landmark(i);
                int o = 1 + i * 4;
                row[o] = lm.X; row[o + 1] = lm.Y; row[o + 2] = lm.Z; row[o + 3] = lm.Visibility;
            }
            _recording.Add(row);
        }

        /// <summary>
        /// Writes the recorded landmark stream to a CSV under persistentDataPath and returns
        /// the path (null if nothing recorded). For offline debugging of the detector.
        /// </summary>
        public string SaveRecording()
        {
            if (_recording.Count == 0)
                return null;

            var sb = new StringBuilder();
            sb.Append("t");
            for (int i = 0; i < PoseFrame.LandmarkCount; i++)
                sb.Append(",x").Append(i).Append(",y").Append(i).Append(",z").Append(i).Append(",v").Append(i);
            sb.Append('\n');

            foreach (float[] r in _recording)
            {
                sb.Append(r[0].ToString("0.####", CultureInfo.InvariantCulture));
                for (int k = 1; k < r.Length; k++)
                    sb.Append(',').Append(r[k].ToString("0.####", CultureInfo.InvariantCulture));
                sb.Append('\n');
            }

            string path = Path.Combine(Application.persistentDataPath, $"pose_rec_{DateTime.Now:HHmmss}.csv");
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[PoseController] POSE_LOG_SAVED {path} frames={_recording.Count}");
            return path;
        }

        private void OnAnalyzerChanged() => Changed?.Invoke();

        private void OnDestroy()
        {
            if (_source != null)
                _source.FrameReceived -= OnFrameReceived;
            if (_analyzer != null)
                _analyzer.Changed -= OnAnalyzerChanged;
        }
    }
}
