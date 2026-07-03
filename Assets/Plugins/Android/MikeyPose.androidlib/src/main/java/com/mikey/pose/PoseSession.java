package com.mikey.pose;

import android.app.Activity;
import android.graphics.Bitmap;
import android.os.SystemClock;
import android.util.Log;

import androidx.camera.core.CameraSelector;
import androidx.camera.core.ImageAnalysis;
import androidx.camera.core.ImageProxy;
import androidx.camera.lifecycle.ProcessCameraProvider;
import androidx.core.content.ContextCompat;

import com.google.common.util.concurrent.ListenableFuture;
import com.google.mediapipe.framework.image.BitmapImageBuilder;
import com.google.mediapipe.framework.image.MPImage;
import com.google.mediapipe.tasks.components.containers.NormalizedLandmark;
import com.google.mediapipe.tasks.core.BaseOptions;
import com.google.mediapipe.tasks.core.Delegate;
import com.google.mediapipe.tasks.vision.core.RunningMode;
import com.google.mediapipe.tasks.vision.poselandmarker.PoseLandmarker;
import com.google.mediapipe.tasks.vision.poselandmarker.PoseLandmarkerResult;

import java.nio.ByteBuffer;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Native pose session driven from Unity via JNI (see AndroidPoseSource.cs).
 *
 * Owns the perf-critical path natively: CameraX captures frames and MediaPipe Pose
 * Landmarker (GPU, LIVE_STREAM) runs inference. The latest 33 landmarks are buffered and
 * pulled by Unity through {@link #readLatest()} — camera frames never cross into managed
 * memory. The .task model ships in this module's assets.
 *
 * Contract used by AndroidPoseSource.cs:
 *   start(), stop(), readLatest():float[], getTextureId()/Width()/Height().
 */
public class PoseSession {

    private static final String TAG = "PoseSession";
    private static final int LANDMARKS = 33;
    private static final int FLOATS = LANDMARKS * 4;
    private static final String MODEL_ASSET = "pose_landmarker_lite.task";

    private final Activity activity;
    private final Object lock = new Object();
    private final float[] latest = new float[FLOATS];
    private volatile boolean hasNew = false;

    // Latest camera frame (RGBA8888) for the Unity preview, pulled via readFramePixels().
    private final Object frameLock = new Object();
    private byte[] frameBytes;
    private int frameW;
    private int frameH;
    private volatile boolean hasFrame = false;

    private PoseLandmarker landmarker;
    private ProcessCameraProvider cameraProvider;
    private ExecutorService executor;
    private final SimpleLifecycleOwner lifecycleOwner = new SimpleLifecycleOwner();

    public PoseSession(Activity activity) {
        this.activity = activity;
    }

    public void start() {
        setupLandmarker();
        startCamera();
    }

    public void stop() {
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                if (cameraProvider != null) {
                    cameraProvider.unbindAll();
                }
                lifecycleOwner.stop();
            }
        });
        if (landmarker != null) {
            landmarker.close();
            landmarker = null;
        }
        if (executor != null) {
            executor.shutdown();
            executor = null;
        }
    }

    /**
     * Returns a fresh copy of the latest 33-landmark buffer (x, y, z, visibility per point),
     * or null when no new inference result has arrived since the previous read.
     */
    public float[] readLatest() {
        synchronized (lock) {
            if (!hasNew) {
                return null;
            }
            hasNew = false;
            return latest.clone();
        }
    }

    public int getFrameWidth() {
        synchronized (frameLock) { return frameW; }
    }

    public int getFrameHeight() {
        synchronized (frameLock) { return frameH; }
    }

    /** Latest camera frame as tightly-packed RGBA8888 bytes, or null if none new since last read. */
    public byte[] readFramePixels() {
        synchronized (frameLock) {
            if (!hasFrame) {
                return null;
            }
            hasFrame = false;
            return frameBytes.clone();
        }
    }

    private void captureFrame(Bitmap bitmap) {
        int w = bitmap.getWidth();
        int h = bitmap.getHeight();
        int need = w * h * 4;
        synchronized (frameLock) {
            if (frameBytes == null || frameBytes.length != need) {
                frameBytes = new byte[need];
            }
            ByteBuffer bb = ByteBuffer.wrap(frameBytes);
            bitmap.copyPixelsToBuffer(bb);
            frameW = w;
            frameH = h;
            hasFrame = true;
        }
    }

    private void setupLandmarker() {
        BaseOptions baseOptions = BaseOptions.builder()
                .setModelAssetPath(MODEL_ASSET)
                .setDelegate(Delegate.GPU)
                .build();

        PoseLandmarker.PoseLandmarkerOptions options =
                PoseLandmarker.PoseLandmarkerOptions.builder()
                        .setBaseOptions(baseOptions)
                        .setRunningMode(RunningMode.LIVE_STREAM)
                        .setNumPoses(1)
                        .setMinPoseDetectionConfidence(0.5f)
                        .setMinPosePresenceConfidence(0.5f)
                        .setMinTrackingConfidence(0.5f)
                        .setResultListener(new com.google.mediapipe.tasks.core.OutputHandler.ResultListener<PoseLandmarkerResult, MPImage>() {
                            @Override public void run(PoseLandmarkerResult result, MPImage image) {
                                onResult(result);
                            }
                        })
                        .setErrorListener(new com.google.mediapipe.tasks.core.ErrorListener() {
                            @Override public void onError(RuntimeException e) {
                                Log.e(TAG, "MediaPipe error", e);
                            }
                        })
                        .build();

        landmarker = PoseLandmarker.createFromOptions(activity, options);
    }

    private void onResult(PoseLandmarkerResult result) {
        if (result.landmarks().isEmpty()) {
            return;
        }
        List<NormalizedLandmark> lms = result.landmarks().get(0);
        synchronized (lock) {
            for (int i = 0; i < LANDMARKS; i++) {
                NormalizedLandmark lm = lms.get(i);
                int o = i * 4;
                latest[o] = lm.x();
                latest[o + 1] = lm.y();
                latest[o + 2] = lm.z();
                latest[o + 3] = lm.visibility().isPresent() ? lm.visibility().get() : 0f;
            }
            hasNew = true;
        }
    }

    private void startCamera() {
        executor = Executors.newSingleThreadExecutor();
        final ListenableFuture<ProcessCameraProvider> future = ProcessCameraProvider.getInstance(activity);
        future.addListener(new Runnable() {
            @Override public void run() {
                try {
                    cameraProvider = future.get();

                    ImageAnalysis analysis = new ImageAnalysis.Builder()
                            .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                            .setOutputImageFormat(ImageAnalysis.OUTPUT_IMAGE_FORMAT_RGBA_8888)
                            .build();

                    analysis.setAnalyzer(executor, new ImageAnalysis.Analyzer() {
                        @Override public void analyze(@androidx.annotation.NonNull ImageProxy imageProxy) {
                            PoseSession.this.analyze(imageProxy);
                        }
                    });

                    CameraSelector selector = new CameraSelector.Builder()
                            .requireLensFacing(CameraSelector.LENS_FACING_FRONT)
                            .build();

                    cameraProvider.unbindAll();
                    lifecycleOwner.start();
                    cameraProvider.bindToLifecycle(lifecycleOwner, selector, analysis);
                } catch (Exception e) {
                    Log.e(TAG, "camera start failed", e);
                }
            }
        }, ContextCompat.getMainExecutor(activity));
    }

    private void analyze(ImageProxy imageProxy) {
        try {
            Bitmap bitmap = Bitmap.createBitmap(imageProxy.getWidth(), imageProxy.getHeight(), Bitmap.Config.ARGB_8888);
            bitmap.copyPixelsFromBuffer(imageProxy.getPlanes()[0].getBuffer());
            captureFrame(bitmap);
            MPImage mpImage = new BitmapImageBuilder(bitmap).build();
            if (landmarker != null) {
                landmarker.detectAsync(mpImage, SystemClock.uptimeMillis());
            }
        } catch (Exception e) {
            Log.e(TAG, "analyze failed", e);
        } finally {
            imageProxy.close();
        }
    }
}
