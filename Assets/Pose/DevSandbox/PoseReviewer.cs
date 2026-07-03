using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Mikey.Pose;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mikey.Pose.DevSandbox
{
    /// <summary>
    /// Editor/laptop tool: a free-fly 3D "training ground" that renders two skeletons — YOUR
    /// on-device recording and the REFERENCE extracted from the lesson video — standing apart on
    /// a floor, each normalized to torso length. Fly around freely (WASD/QE + right-mouse look)
    /// to inspect from any angle. Also runs the real <see cref="PushUpAnalyzer"/> on your
    /// recording and shows its verdict (angles, fault, rep count) synced to playback.
    ///
    /// Not shipped; a dev verification/comparison harness. Runs in Play mode.
    /// </summary>
    public sealed class PoseReviewer : MonoBehaviour
    {
        [SerializeField] private string _userFile = "pose_rec.csv";
        [SerializeField] private string _refFile = "reference.csv";
        [SerializeField] private float _scale = 1.0f;
        [SerializeField] private float _speed = 1f;
        [SerializeField] private float _moveSpeed = 3f;
        [Tooltip("Clear gap (metres) between the two skeletons' bounding boxes.")]
        [SerializeField] private float _gap = 2.5f;

        private static readonly int[,] Edges =
        {
            {11,12},{11,13},{13,15},{12,14},{14,16},
            {11,23},{12,24},{23,24},{23,25},{25,27},{24,26},{26,28}
        };

        private sealed class Clip
        {
            public PoseFrame[] Frames;
            public double[] Times;
            public double Duration;
            public Transform[] Joints;
            public LineRenderer[] Bones;
            public double Time;
            public int Index;
            public float HalfWidthX; // max |x| across the clip (normalized+scaled, hip-centered)
            public float MinY;       // lowest y across the clip (to sit it on the floor)
        }

        private struct Snap
        {
            public float Elbow, Body, Vis;
            public PushUpFault Fault;
            public int Reps;
            public bool RepHit;
        }

        private Clip _user, _ref;
        private Snap[] _userSnaps;
        private readonly Vector3[] _pos = new Vector3[33];
        private Material _matUser, _matRef;
        private Camera _cam;
        private float _yaw, _pitch;
        private bool _playing = true;
        private GUIStyle _label, _big;

        private void Start()
        {
            BuildGround();

            _matUser = MakeUnlit(new Color(0.17f, 0.85f, 0.66f)); // jade = you
            _matRef = MakeUnlit(new Color(1f, 0.55f, 0.15f));     // orange = reference

            _user = LoadClip(_userFile);
            _ref = LoadClip(_refFile);

            if (_user != null) ComputeBounds(_user);
            if (_ref != null) ComputeBounds(_ref);

            // Place each skeleton on the floor (lowest point just above y=0) and offset in X so
            // their bounding boxes are separated by exactly _gap — no overlap, no guessing.
            float uHalf = _user != null ? _user.HalfWidthX : 0f;
            float rHalf = _ref != null ? _ref.HalfWidthX : 0f;

            // X offsets separate them; Y is grounded per-frame in Render (lowest joint on floor).
            if (_user != null)
            {
                BuildVisual(_user, _matUser, new Vector3(-(_gap * 0.5f + uHalf), 0.05f, 0f));
                _userSnaps = ComputeSnaps(_user);
            }
            if (_ref != null)
                BuildVisual(_ref, _matRef, new Vector3(_gap * 0.5f + rHalf, 0.05f, 0f));
            if (_user == null && _ref == null)
                Debug.LogError("[PoseReviewer] no clips loaded (check Assets/PoseRecordings/).");

            SetupCamera();
        }

        private void SetupCamera()
        {
            _cam = Camera.main;
            if (_cam == null)
                return;
            _cam.clearFlags = CameraClearFlags.Skybox;
            _cam.transform.position = new Vector3(0f, 2.5f, -9f);
            Quaternion look = Quaternion.LookRotation(new Vector3(0f, 0.8f, 0f) - _cam.transform.position);
            _cam.transform.rotation = look;
            Vector3 e = look.eulerAngles;
            _yaw = e.y;
            _pitch = e.x > 180f ? e.x - 360f : e.x;
        }

        private static Material MakeUnlit(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            m.color = c;
            return m;
        }

        private void BuildGround()
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "TrainingGround";
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(4f, 1f, 4f); // 40 x 40 units
            Destroy(floor.GetComponent<Collider>());

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            Texture2D grid = MakeGridTexture();
            mat.mainTexture = grid;
            mat.mainTextureScale = new Vector2(20f, 20f);
            mat.color = Color.white;
            floor.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private static Texture2D MakeGridTexture()
        {
            const int s = 128;
            var t = new Texture2D(s, s);
            var bg = new Color(0.12f, 0.14f, 0.17f);
            var line = new Color(0.28f, 0.34f, 0.40f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                    t.SetPixel(x, y, (x < 2 || y < 2) ? line : bg);
            t.wrapMode = TextureWrapMode.Repeat;
            t.Apply();
            return t;
        }

        private static float P(string str) => float.Parse(str, CultureInfo.InvariantCulture);

        private Clip LoadClip(string fileName)
        {
            string path = Path.Combine(Application.dataPath, "PoseRecordings", fileName);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[PoseReviewer] not found (skipped): {path}");
                return null;
            }

            string[] lines = File.ReadAllLines(path);
            var frames = new List<PoseFrame>();
            var times = new List<double>();
            for (int li = 1; li < lines.Length; li++)
            {
                string[] c = lines[li].Split(',');
                if (c.Length < 133)
                    continue;
                double t = double.Parse(c[0], CultureInfo.InvariantCulture);
                var lm = new PoseLandmark[33];
                for (int i = 0; i < 33; i++)
                {
                    int o = 1 + i * 4;
                    lm[i] = new PoseLandmark(P(c[o]), P(c[o + 1]), P(c[o + 2]), P(c[o + 3]));
                }
                frames.Add(new PoseFrame(lm, t));
                times.Add(t);
            }
            if (frames.Count == 0)
                return null;

            var clip = new Clip { Frames = frames.ToArray(), Times = times.ToArray() };
            clip.Duration = clip.Times[clip.Times.Length - 1] - clip.Times[0];
            Debug.Log($"[PoseReviewer] loaded {fileName}: {clip.Frames.Length} frames, {clip.Duration:0.0}s");
            return clip;
        }

        // Per-frame hip midpoint and torso length (the normalization anchor and unit).
        private (Vector3 hip, float torso) HipTorso(PoseFrame f)
        {
            Vector3 hip = 0.5f * (V(f.Get(PoseLandmarkType.LeftHip)) + V(f.Get(PoseLandmarkType.RightHip)));
            Vector3 sh = 0.5f * (V(f.Get(PoseLandmarkType.LeftShoulder)) + V(f.Get(PoseLandmarkType.RightShoulder)));
            float torso = Vector3.Distance(sh, hip);
            return (hip, torso < 1e-4f ? 1f : torso);
        }

        private Vector3 Norm(PoseFrame f, int i, Vector3 hip, float torso) =>
            (V(f.Landmark(i)) - hip) / torso * _scale;

        // Measures the skeleton's extent across the whole clip so it can be grounded and spaced.
        private void ComputeBounds(Clip c)
        {
            float maxAbsX = 0.1f, minY = 0f;
            foreach (PoseFrame f in c.Frames)
            {
                (Vector3 hip, float torso) = HipTorso(f);
                for (int i = 0; i < 33; i++)
                {
                    if (f.Landmark(i).Visibility < 0.3f)
                        continue;
                    Vector3 p = Norm(f, i, hip, torso);
                    if (Mathf.Abs(p.x) > maxAbsX) maxAbsX = Mathf.Abs(p.x);
                    if (p.y < minY) minY = p.y;
                }
            }
            c.HalfWidthX = maxAbsX;
            c.MinY = minY;
        }

        private Snap[] ComputeSnaps(Clip clip)
        {
            var evaluator = new PushUpFormEvaluator();
            var analyzer = new PushUpAnalyzer();
            var snaps = new Snap[clip.Frames.Length];
            for (int i = 0; i < clip.Frames.Length; i++)
            {
                FormAssessment a = evaluator.Evaluate(clip.Frames[i]);
                int before = analyzer.Reps;
                analyzer.ProcessFrame(clip.Frames[i]);
                snaps[i] = new Snap
                {
                    Elbow = a.ElbowAngleDeg, Body = a.BodyAngleDeg, Vis = a.Visibility,
                    Fault = a.Fault, Reps = analyzer.Reps, RepHit = analyzer.Reps > before
                };
            }
            Debug.Log($"[PoseReviewer] algorithm counted {snaps[snaps.Length - 1].Reps} reps on {_userFile}.");
            return snaps;
        }

        private void BuildVisual(Clip clip, Material mat, Vector3 rootPos)
        {
            var root = new GameObject("Skeleton").transform;
            root.SetParent(transform, false);
            root.localPosition = rootPos;

            clip.Joints = new Transform[33];
            for (int i = 0; i < 33; i++)
            {
                GameObject s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.transform.SetParent(root, false);
                s.transform.localScale = Vector3.one * 0.09f;
                Destroy(s.GetComponent<Collider>());
                s.GetComponent<Renderer>().sharedMaterial = mat;
                clip.Joints[i] = s.transform;
            }

            clip.Bones = new LineRenderer[Edges.GetLength(0)];
            for (int e = 0; e < clip.Bones.Length; e++)
            {
                var go = new GameObject($"bone{e}");
                go.transform.SetParent(root, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.positionCount = 2;
                lr.widthMultiplier = 0.03f;
                lr.sharedMaterial = mat;
                clip.Bones[e] = lr;
            }
        }

        private void Update()
        {
            if (_playing)
            {
                Advance(_user);
                Advance(_ref);
            }
            Render(_user);
            Render(_ref);
            FlyCamera();
        }

        // Free-fly camera via the new Input System: right-mouse to look, WASD/QE to move.
        private void FlyCamera()
        {
            if (_cam == null)
                return;

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                _yaw += d.x * 0.12f;
                _pitch = Mathf.Clamp(_pitch - d.y * 0.12f, -89f, 89f);
                _cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            Keyboard kb = Keyboard.current;
            if (kb == null)
                return;

            Vector3 move = Vector3.zero;
            if (kb.wKey.isPressed) move += _cam.transform.forward;
            if (kb.sKey.isPressed) move -= _cam.transform.forward;
            if (kb.aKey.isPressed) move -= _cam.transform.right;
            if (kb.dKey.isPressed) move += _cam.transform.right;
            if (kb.eKey.isPressed) move += Vector3.up;
            if (kb.qKey.isPressed) move -= Vector3.up;

            float speed = kb.leftShiftKey.isPressed ? _moveSpeed * 3f : _moveSpeed;
            _cam.transform.position += move.normalized * speed * Time.deltaTime;
        }

        private void Advance(Clip c)
        {
            if (c == null)
                return;
            c.Time += Time.deltaTime * _speed;
            if (c.Time > c.Duration)
                c.Time = 0;
            c.Index = IndexForTime(c, c.Time);
        }

        private static int IndexForTime(Clip c, double tRel)
        {
            double t0 = c.Times[0];
            for (int i = c.Times.Length - 1; i >= 0; i--)
                if (c.Times[i] - t0 <= tRel)
                    return i;
            return 0;
        }

        // Center on the hip midpoint and scale by torso length so any skeleton (image-normalized
        // or metric world landmarks) renders at a common size, comparable across bodies.
        private void Render(Clip c)
        {
            if (c == null)
                return;
            PoseFrame f = c.Frames[c.Index];
            (Vector3 hip, float torso) = HipTorso(f);

            // First pass: normalized positions + this frame's lowest visible point.
            float minY = float.MaxValue;
            for (int i = 0; i < 33; i++)
            {
                _pos[i] = Norm(f, i, hip, torso);
                if (f.Landmark(i).Visibility >= 0.3f && _pos[i].y < minY)
                    minY = _pos[i].y;
            }
            if (minY == float.MaxValue)
                minY = 0f;

            // Second pass: drop the whole skeleton so its lowest point rests on the floor.
            for (int i = 0; i < 33; i++)
            {
                c.Joints[i].localPosition = _pos[i] - new Vector3(0f, minY, 0f);
                c.Joints[i].gameObject.SetActive(f.Landmark(i).Visibility >= 0.3f);
            }
            for (int e = 0; e < c.Bones.Length; e++)
            {
                c.Bones[e].SetPosition(0, c.Joints[Edges[e, 0]].localPosition);
                c.Bones[e].SetPosition(1, c.Joints[Edges[e, 1]].localPosition);
            }
        }

        // Flip Y so "up" in the world is up (both conventions store Y growing downward).
        private static Vector3 V(PoseLandmark lm) => new Vector3(lm.X, -lm.Y, lm.Z);

        private void OnGUI()
        {
            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
                _big = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold };
            }

            GUILayout.BeginArea(new Rect(20, 20, 780, 470));
            GUILayout.Label("YOU = jade (left)      REFERENCE = orange (right)", _label);
            GUILayout.Label("fly: W A S D / Q E move · hold RIGHT-MOUSE + move to look · Shift = fast", _label);

            if (_userSnaps != null && _user != null)
            {
                Snap s = _userSnaps[_user.Index];
                GUILayout.Label($"you  t {_user.Time:0.0}/{_user.Duration:0.0}s   elbow {Fmt(s.Elbow)}  body {Fmt(s.Body)}  vis {s.Vis:0.00}  {s.Fault}", _label);
                GUI.color = s.RepHit ? new Color(0.17f, 0.85f, 0.66f) : Color.white;
                GUILayout.Label($"REPS {s.Reps}", _big);
                GUI.color = Color.white;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_playing ? "Pause" : "Play", GUILayout.Width(120), GUILayout.Height(46)))
                _playing = !_playing;
            if (GUILayout.Button("Restart", GUILayout.Width(120), GUILayout.Height(46)))
            {
                if (_user != null) _user.Time = 0;
                if (_ref != null) _ref.Time = 0;
            }
            GUILayout.EndHorizontal();

            if (_ref != null)
            {
                GUILayout.Label($"reference scrub  ({_ref.Time:0.0}/{_ref.Duration:0.0}s — video has talk sections, drag to a push-up):", _label);
                _ref.Time = GUILayout.HorizontalSlider((float)_ref.Time, 0f, (float)_ref.Duration, GUILayout.Width(700));
            }
            GUILayout.EndArea();
        }

        private static string Fmt(float v) => float.IsNaN(v) ? "--" : v.ToString("0") + "°";
    }
}
