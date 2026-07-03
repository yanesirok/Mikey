# MikeyPose.androidlib — native CameraX + MediaPipe pose plugin

Native Android module that runs pose inference on-device and feeds landmarks to Unity
(`Assets/Pose/AndroidPoseSource.cs`). Capture + inference stay native; only landmarks
cross into Unity.

## One-time setup

1. **Model asset.** Download the MediaPipe pose model and place it at:
   `src/main/assets/pose_landmarker_lite.task`
   (from https://ai.google.dev/edge/mediapipe/solutions/vision/pose_landmarker#models —
   `lite` for speed, `full`/`heavy` for accuracy; update `MODEL_ASSET` in `PoseSession.kt`
   if you rename it.)

2. **Kotlin on the Gradle classpath.** In Unity: *Player Settings → Publishing Settings*,
   enable **Custom Base Gradle Template**, then in the generated
   `Assets/Plugins/Android/baseProjectTemplate.gradle` add inside `dependencies { }` of
   `buildscript`:
   ```
   classpath 'org.jetbrains.kotlin:kotlin-gradle-plugin:1.9.24'
   ```
   Match the Kotlin version to your AGP/Gradle (Unity 6.3 default AGP works with 1.9.x).

3. **Camera permission.** Declared in this module's `AndroidManifest.xml`; request it at
   runtime before starting a session (Unity `Permission.RequestUserPermission`).

## Contract (must match AndroidPoseSource.cs)

| Kotlin | Purpose |
|---|---|
| `PoseSession(Activity)` | construct with `UnityPlayer.currentActivity` |
| `start()` / `stop()` | begin/end capture + inference |
| `readLatest(): FloatArray?` | pull the latest 33×{x,y,z,visibility}, or null if none new |
| `getTextureId/Width/Height()` | camera preview texture (returns 0 = no preview yet) |

## Status / TODO

- ✅ Landmark path (rep counting + form) — complete.
- ⏳ Camera preview texture — `getTextureId()` returns 0. Implementing the OES→2D GLES blit
  and sharing the id with Unity is the remaining device-side work; the HUD shows reps/cues
  over a placeholder background until then.
- ⏳ Validate on a physical mid-range device (GPU delegate, ≥20–30 fps, correction latency).
