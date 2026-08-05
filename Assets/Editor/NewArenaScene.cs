using System.Collections.Generic;
using Mikey.Fight;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Сборка боевой сцены на арене «бамбуковая роща» из Blender — спека 2026-08-05, цифры из
/// Assets/Fight/NewArena/UNITY_HANDOFF (2).md. Идемпотентна: повторный запуск пересоздаёт
/// всё, чем владеет, и не плодит дублей. Старый билдер (FightSceneSetup.RebuildArena) не
/// трогается — это его замена на новую сцену, не правка.
/// </summary>
public static class NewArenaScene
{
    private const string ScenePath = "Assets/Scenes/FightSandbox.unity";
    private const string GlbPath = "Assets/Fight/NewArena/BambooGrove.glb";
    private const string TexDir = "Assets/Fight/NewArena/Textures";
    private const string MatDir = "Assets/Fight/NewArena";

    /// <summary>Материалы по таблице handoff §4. Ключ — имя материала в GLB. Планки моста и
    /// задник в словаре отсутствуют намеренно: их текстуры не запекались и живут в GLB,
    /// материалы gltfast для них правильные как есть.</summary>
    public static Dictionary<string, Material> EnsureMaterials()
    {
        var map = new Dictionary<string, Material>
        {
            // Ферн Opaque принципиально: листья геометрией, альфа 99.8% непрозрачна,
            // clip сломал бы early-Z на 36% треугольников сцены (handoff §4).
            ["fern_02"] = LitMaterial("M_GroveFern", "fern_albedo", clip: false),
            ["M_ArenaLeafCard"] = LitMaterial("M_GroveLeafCard", "bamboo_leaf_albedo", clip: true),
            ["M_ArenaBamboo"] = LitMaterial("M_GroveBamboo", "bamboo_bark_albedo", clip: false),
            ["Bank"] = LitMaterial("M_GroveGround", "ground_albedo", clip: false),
            ["boulder_01"] = LitMaterial("M_GroveRock", "rock_albedo", clip: false),
        };
        AssetDatabase.SaveAssets();
        return map;
    }

    private static Material LitMaterial(string name, string albedo, bool clip)
    {
        string path = $"{MatDir}/{name}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{albedo}.png");
        if (tex == null)
            Debug.LogError($"NewArenaScene: нет текстуры {TexDir}/{albedo}.png");
        mat.SetTexture("_BaseMap", tex);
        // Roughness из glTF в URP/Lit не переложить (другая упаковка каналов); листва и
        // камень матовые, плоского значения достаточно.
        mat.SetFloat("_Smoothness", 0.25f);
        mat.SetFloat("_AlphaClip", clip ? 1f : 0f);
        if (clip)
        {
            // Клип, не blend: blend не пишет глубину — ломает сортировку карт листьев и
            // теневой проход (handoff §4).
            mat.SetFloat("_Cutoff", 0.35f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            mat.SetFloat("_Cull", (float)CullMode.Off); // карты видны с обеих сторон
        }
        else
        {
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = -1;
        }
        return mat;
    }
}
