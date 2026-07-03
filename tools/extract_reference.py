"""
Extracts a 3D reference skeleton from the push-up lesson video using the MediaPipe Tasks
PoseLandmarker (VIDEO mode) and writes it in the same CSV format the on-device recorder
uses, so PoseReviewer can load it.

Uses pose_world_landmarks (metric 3D, origin at the hip center) so the reference is a
genuine 3D object with real depth.

Run:
  python tools/extract_reference.py [video] [out_csv] [model.task]
"""
import sys
import csv
import cv2
import numpy as np
import mediapipe as mp
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision

DEFAULT_VIDEO = r"C:\Users\user\Mikey\lessons\The Perfect Push Up   Do it right!.mp4"
DEFAULT_OUT = r"C:\Users\user\Mikey\Assets\PoseRecordings\reference.csv"
DEFAULT_MODEL = r"C:\Users\user\Mikey\Assets\Plugins\Android\MikeyPose.androidlib\src\main\assets\pose_landmarker_lite.task"

video = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_VIDEO
out = sys.argv[2] if len(sys.argv) > 2 else DEFAULT_OUT
model = sys.argv[3] if len(sys.argv) > 3 else DEFAULT_MODEL

cap = cv2.VideoCapture(video)
if not cap.isOpened():
    print(f"ERROR: cannot open {video}")
    sys.exit(1)

fps = cap.get(cv2.CAP_PROP_FPS) or 30.0

options = vision.PoseLandmarkerOptions(
    base_options=mp_python.BaseOptions(model_asset_path=model),
    running_mode=vision.RunningMode.VIDEO,
    num_poses=1,
    min_pose_detection_confidence=0.5,
    min_pose_presence_confidence=0.5,
    min_tracking_confidence=0.5,
)
landmarker = vision.PoseLandmarker.create_from_options(options)

read = 0
written = 0
with open(out, "w", newline="") as f:
    w = csv.writer(f)
    header = ["t"]
    for i in range(33):
        header += [f"x{i}", f"y{i}", f"z{i}", f"v{i}"]
    w.writerow(header)

    while True:
        ok, frame = cap.read()
        if not ok:
            break
        t = read / fps
        ts_ms = int(round(t * 1000.0))
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=np.ascontiguousarray(rgb))
        result = landmarker.detect_for_video(mp_image, ts_ms)
        read += 1

        if not result.pose_world_landmarks:
            continue
        lms = result.pose_world_landmarks[0]
        row = [f"{t:.4f}"]
        for lm in lms:
            vis = lm.visibility if lm.visibility is not None else 0.0
            row += [f"{lm.x:.4f}", f"{lm.y:.4f}", f"{lm.z:.4f}", f"{vis:.4f}"]
        w.writerow(row)
        written += 1

cap.release()
print(f"video fps: {fps:.1f}  frames read: {read}  written: {written}")
print(f"reference written to: {out}")
