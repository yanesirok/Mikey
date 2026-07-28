using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Renders the fight camera straight to a PNG at a fixed resolution.
///
/// Exists so the look can be iterated on without a human round-trip for every tweak: light
/// angle, palette, grass density and post-processing all need to be *seen* to be judged.
/// Rendering to a RenderTexture also sidesteps the Game view Scale slider and aspect
/// settings entirely, so what lands in the file is what the camera actually produces.
///
/// Batch use (Unity must not be open — it holds the project lock):
///   Unity.exe -batchmode -quit -projectPath &lt;proj&gt; -executeMethod FightCapture.Shoot
///             -captureOut &lt;file.png&gt; -captureSize 1920x1080
/// Note: no -nographics, or there is no GPU context to render with.
/// </summary>
public static class FightCapture
{
    private const string ScenePath = "Assets/Scenes/FightSandbox.unity";

    [MenuItem("Mikey/Capture Fight Screenshot")]
    public static void Shoot()
    {
        string output = Arg("-captureOut") ?? "Temp/fight_capture.png";
        ParseSize(Arg("-captureSize"), out int width, out int height);

        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath);

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("FightCapture: no camera tagged MainCamera in " + ScenePath);
            return;
        }

        // ARGB32, not DefaultHDR. This is the *final* target, not an intermediate — URP still
        // renders and grades in HDR internally and only blits the tonemapped result here. An
        // HDR target is linear, and reading it straight back into an RGB24 texture skips the
        // sRGB encode the real swap chain performs, so every capture came out darker and bluer
        // than the built player. In a linear project a plain ARGB32 target is sRGB, and what
        // lands in the file is then what a player sees. This cost a whole round of look tuning
        // against an image the game never produced.
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 1, // MSAA comes from the URP asset; forcing it here double-resolves
        };

        // The planar water reflection renders itself in play mode, where URP walks every enabled
        // camera that owns a target texture. Here there is no player loop at all — Shoot renders
        // one camera by hand — so it has to be driven, or the water samples its black default and
        // the capture shows a lake with nothing in it. Which is exactly how a mirrored reflection
        // and an inside-out one both went unnoticed.
        foreach (Mikey.Fight.WaterReflection reflection in
                 UnityEngine.Object.FindObjectsByType<Mikey.Fight.WaterReflection>(FindObjectsSortMode.None))
            reflection.RenderNow();

        RenderTexture previous = RenderTexture.active;
        var shot = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            cam.targetTexture = target;
            RenderTexture.active = target;

            // URP does not produce its final image on the first render of a process, and a
            // capture that renders exactly one frame therefore saves a frame the game never
            // shows. Measured on this scene: the first frame comes back with its shadows crushed
            // about a stop and a half — the fighters at 33% and 73% pure black against 2% and 3%
            // once settled, and the deck's specular reading as chrome over a black grain. Every
            // reference capture this project has taken has been that frame. The cause is inside
            // the post stack, not the volume system: calling VolumeManager.Update first changes
            // nothing, and discarding one render fixes it exactly.
            //
            // Rendered until two frames agree rather than a fixed twice, so that an effect which
            // one day needs a longer warm-up says so instead of quietly shipping frame one. Film
            // grain moves every frame, so the test is the mean and not the pixels.
            float settled = float.NaN;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                cam.Render();
                shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                shot.Apply();

                float mean = Mean(shot);
                if (!float.IsNaN(settled) && Mathf.Abs(mean - settled) < 0.5f)
                    break;
                settled = mean;
                if (attempt == 4)
                    Debug.LogError($"FightCapture: the image was still moving after five renders " +
                                   $"(mean {mean:F2}); the frame written is not what the game shows.");
            }
        }
        finally
        {
            RenderTexture.active = previous;
            cam.targetTexture = null;
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllBytes(output, shot.EncodeToPNG());

        UnityEngine.Object.DestroyImmediate(shot);
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);

        Debug.Log($"FightCapture: wrote {Path.GetFullPath(output)} at {width}x{height}.");
    }

    /// <summary>Mean of every channel of every texel. Film grain is zero-mean, so this is stable
    /// between frames while the pixels are not.</summary>
    private static float Mean(Texture2D shot)
    {
        Color32[] pixels = shot.GetPixels32();
        double sum = 0.0;
        foreach (Color32 pixel in pixels)
            sum += pixel.r + pixel.g + pixel.b;
        return (float)(sum / (pixels.Length * 3.0));
    }

    /// <summary>Value following a flag on the Unity command line, or null when absent.</summary>
    private static string Arg(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == flag)
                return args[i + 1];
        return null;
    }

    private static void ParseSize(string value, out int width, out int height)
    {
        width = 1920;
        height = 1080;
        if (string.IsNullOrEmpty(value))
            return;
        string[] parts = value.Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w > 0 && h > 0)
        {
            width = w;
            height = h;
        }
    }
}
