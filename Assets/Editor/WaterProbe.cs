using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Temporary. Renders the fight camera three times in one process, with the water stripped down
/// differently each time, so two claims about it can be measured rather than eyeballed.
///
/// Three frames from one run, not three runs: the ripple alone moves brightness 12-25 units
/// between Unity launches, which is larger than either effect being measured.
///
///   body.png      reflection off, fog off, caustics off — the pixel is the body of the water
///                 plus the glint, and the depth prediction can be tested against it
///   full.png      the shipping look
///   nocaust.png   the shipping look with caustics off; full - nocaust is exactly where the
///                 caustics landed, which is how "they die in the deck's shadow" gets checked
///
/// Delete this file once the numbers are in the spec.
///
///   Unity.exe -batchmode -quit -projectPath &lt;proj&gt; -executeMethod WaterProbe.Shoot
/// </summary>
public static class WaterProbe
{
    private const string ScenePath = "Assets/Scenes/FightSandbox.unity";
    private const string OutDir = "issues/probe";

    [MenuItem("Mikey/Probe Water")]
    public static void Shoot()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath);

        Camera cam = Camera.main;
        // Found by its component rather than by path: the water is the one thing in the scene that
        // owns a WaterReflection, and a path would break the next time the arena root is renamed.
        var water = Object.FindFirstObjectByType<Mikey.Fight.WaterReflection>();
        if (cam == null || water == null)
        {
            Debug.LogError("WaterProbe: no MainCamera, or no WaterReflection in the scene.");
            return;
        }

        Material mat = water.GetComponent<MeshRenderer>().sharedMaterial;
        float reflection = mat.GetFloat("_ReflectionStrength");
        float caustics = mat.GetFloat("_Caustics");
        float bias = mat.GetFloat("_FresnelBias");
        float power = mat.GetFloat("_FresnelPower");
        bool fog = RenderSettings.fog;

        Directory.CreateDirectory(OutDir);
        try
        {
            // Killing _ReflectionStrength alone is not enough: the shader keeps a flat sky colour
            // as the fallback for when the reflection texture is empty, and that is mixed in by
            // the same Fresnel term. Driving the exponent to 40 with no bias takes Fresnel to zero
            // everywhere and leaves the body of the water alone in the frame.
            mat.SetFloat("_ReflectionStrength", 0f);
            mat.SetFloat("_Caustics", 0f);
            mat.SetFloat("_FresnelBias", 0f);
            mat.SetFloat("_FresnelPower", 40f);
            RenderSettings.fog = false;
            Render(cam, $"{OutDir}/body.png");

            mat.SetFloat("_ReflectionStrength", reflection);
            mat.SetFloat("_FresnelBias", bias);
            mat.SetFloat("_FresnelPower", power);
            RenderSettings.fog = fog;
            Render(cam, $"{OutDir}/nocaust.png");

            mat.SetFloat("_Caustics", caustics);
            Render(cam, $"{OutDir}/full.png");
        }
        finally
        {
            mat.SetFloat("_ReflectionStrength", reflection);
            mat.SetFloat("_Caustics", caustics);
            mat.SetFloat("_FresnelBias", bias);
            mat.SetFloat("_FresnelPower", power);
            RenderSettings.fog = fog;
        }
        Debug.Log($"WaterProbe: three frames written to {OutDir}");
    }

    private static void Render(Camera cam, string path)
    {
        const int width = 1600, height = 900;

        // The planar reflection renders itself in play mode, where URP walks every enabled camera
        // that owns a target texture. There is no player loop here, so it has to be driven.
        foreach (Mikey.Fight.WaterReflection r in
                 Object.FindObjectsByType<Mikey.Fight.WaterReflection>(FindObjectsSortMode.None))
            r.RenderNow();

        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 1,
        };
        RenderTexture previous = RenderTexture.active;
        var shot = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            cam.targetTexture = target;
            RenderTexture.active = target;
            // URP does not produce its final image on the first render of a process; FightCapture
            // learned this the hard way and warms up the same way.
            for (int i = 0; i < 3; i++)
                cam.Render();
            shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            shot.Apply();
            File.WriteAllBytes(path, shot.EncodeToPNG());
        }
        finally
        {
            cam.targetTexture = null;
            RenderTexture.active = previous;
            Object.DestroyImmediate(shot);
            target.Release();
            Object.DestroyImmediate(target);
        }
    }
}
