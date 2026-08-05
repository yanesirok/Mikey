using System.Collections.Generic;
using Mikey.Fight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

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

    // Камера из handoff §7. К позиции прибавляется дельта выравнивания арены — композиция
    // кадра не меняется, но пол боя ложится на y=0/z=0 из FightRules.
    private static readonly Vector3 HandoffCamPos = new Vector3(-0.68f, 2.60f, -3.219f);
    private static readonly Vector3 HandoffCamEuler = new Vector3(5.0f, 0.5f, 0f);
    private const float HandoffFov = 45.7f; // вертикальный

    // Тёплый бледно-кремовый горизонт (handoff §5.1): туман, фон камеры и амбиент — одна
    // семья. Референс-рендер для пипетки не годится (мёртвые пути текстур красят его в
    // мадженту), поэтому значение — из словесного описания handoff, крутится по месту.
    private static readonly Color FogColor = new Color(0.93f, 0.88f, 0.78f);
    private const float FogDensity = 0.0125f; // 12% на 10 м, 27% на 25 м, 43% на 45 м

    [MenuItem("Mikey/Build Bamboo Grove")]
    public static void Build()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath);

        // Тот же список владения, что у FightSceneSetup.RebuildArena: бойцы, контроллер и
        // тач-управление не трогаются, окружение пересоздаётся целиком.
        var owned = new HashSet<string>
        {
            "Arena", "Water", "Terrain", "Grass", "FX_Mist", "FX_Leaves", "FX_Motes",
            "FX_Drips", "FX_Steam", "FX_Embers", "PostProcessing",
        };
        foreach (GameObject root in scene.GetRootGameObjects())
            if (owned.Contains(root.name))
                Object.DestroyImmediate(root);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
        if (prefab == null)
        {
            Debug.LogError($"NewArenaScene: нет {GlbPath} — сначала экспорт из Blender.");
            return;
        }
        var arena = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        arena.name = "Arena"; // связь с GLB остаётся: переэкспорт из Blender подтянется сам

        // gltfast переводит правостороннюю систему glTF в левостороннюю Unity не тем
        // разворотом, который предполагала ручная конверсия (x, z, -y) из handoff §7: без
        // поправки роща оказывается позади камеры. 180° по Y до выравнивания — и камера
        // остаётся ровно на цифрах handoff.
        arena.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

        Vector3 shift = AlignToFightLine(arena);
        ApplyMaterialsAndLayers(arena);
        AddDeckCollider(arena);
        SetupWater(arena);
        SetupLights();
        SetupAtmosphere();
        SetupCamera(shift);
        CreatePostVolume();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"NewArenaScene: роща собрана, сдвиг арены {shift}.");
    }

    /// <summary>Верх настила — на y=0 (пол бойцов), середина пролёта — на x=0, ось боя — на
    /// z=0. Возвращает применённый сдвиг: на него же смещается камера.</summary>
    private static Vector3 AlignToFightLine(GameObject arena)
    {
        Transform bridge = FindDeep(arena.transform, "Bridge");
        if (bridge == null)
        {
            Debug.LogError("NewArenaScene: в GLB нет объекта Bridge — выравнивание пропущено.");
            return Vector3.zero;
        }

        var renderers = bridge.GetComponentsInChildren<MeshRenderer>();
        Bounds b = renderers[0].bounds;
        foreach (MeshRenderer r in renderers)
            b.Encapsulate(r.bounds);

        // Верх настила — рейкаст в середину моста, а не bounds.max.y: максимум AABB — перила.
        float deckY = b.center.y;
        var mf = bridge.GetComponentInChildren<MeshFilter>();
        var temp = mf.gameObject.AddComponent<MeshCollider>();
        temp.sharedMesh = mf.sharedMesh;
        var ray = new Ray(new Vector3(b.center.x, b.max.y + 1f, b.center.z), Vector3.down);
        if (temp.Raycast(ray, out RaycastHit hit, b.size.y + 2f))
            deckY = hit.point.y;
        else
            Debug.LogWarning("NewArenaScene: рейкаст в настил промахнулся, беру центр bounds.");
        Object.DestroyImmediate(temp);

        var shift = new Vector3(-b.center.x, -deckY, -b.center.z);
        arena.transform.position += shift;
        return shift;
    }

    /// <summary>Наши материалы взамен gltfast-овских (по имени), нормали переносятся из
    /// заменяемого материала. Слои для отражения воды: мост — Reflected, роща — ReflectedFar
    /// (см. WaterReflection: ближний слой рендерится всегда, дальний отключается на мобильном
    /// тире качества).</summary>
    private static void ApplyMaterialsAndLayers(GameObject arena)
    {
        Dictionary<string, Material> mats = EnsureMaterials();

        foreach (MeshRenderer r in arena.GetComponentsInChildren<MeshRenderer>())
        {
            Material[] shared = r.sharedMaterials;
            for (int i = 0; i < shared.Length; i++)
            {
                if (shared[i] == null || !mats.TryGetValue(shared[i].name, out Material own))
                    continue;
                Texture normal = NormalOf(shared[i]);
                if (normal != null)
                {
                    own.SetTexture("_BumpMap", normal);
                    own.EnableKeyword("_NORMALMAP");
                }
                shared[i] = own;
            }
            r.sharedMaterials = shared;
        }

        int near = LayerMask.NameToLayer(WaterReflection.LayerName);
        int far = LayerMask.NameToLayer(WaterReflection.FarLayerName);
        if (near < 0 || far < 0)
        {
            Debug.LogWarning("NewArenaScene: нет слоёв Reflected/ReflectedFar — вода без отражения.");
            return;
        }
        foreach (Transform t in arena.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = far;
        Transform bridgeRoot = FindDeep(arena.transform, "Bridge");
        if (bridgeRoot != null)
            foreach (Transform t in bridgeRoot.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = near;
    }

    /// <summary>Нормали не запекались и приезжают в GLB — снимаем с материала, который
    /// заменяем. Имя свойства зависит от шейдера gltfast, поэтому перебор.</summary>
    private static Texture NormalOf(Material m)
    {
        foreach (string prop in new[] { "_BumpMap", "normalTexture", "_NormalTexture" })
            if (m.HasProperty(prop) && m.GetTexture(prop) != null)
                return m.GetTexture(prop);
        return null;
    }

    /// <summary>BoxCollider по настилу на объекте Arena/Timber — именно этот путь рейкастит
    /// диагностика FightBootstrap.LogDeckProfile.</summary>
    private static void AddDeckCollider(GameObject arena)
    {
        Transform bridge = FindDeep(arena.transform, "Bridge");
        if (bridge == null)
            return;
        var renderers = bridge.GetComponentsInChildren<MeshRenderer>();
        Bounds b = renderers[0].bounds;
        foreach (MeshRenderer r in renderers)
            b.Encapsulate(r.bounds);

        var timber = new GameObject("Timber");
        timber.transform.SetParent(arena.transform);
        timber.transform.position = Vector3.zero; // мировой ноль: настил уже выровнен на y=0
        var box = timber.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, -0.05f, 0f); // верх коробки — ровно y=0
        box.size = new Vector3(b.size.x, 0.1f, b.size.z);
    }

    /// <summary>Вода — «simple»-вариант из handoff §5.4: тёмная гладь URP/Lit. Старый
    /// Water.shader пробовался и разложился на GLB-меше бирюзовыми пятнами: его формулы
    /// глубины и пены читают вершинные данные, которые запекал только старый билдер.
    /// Планарное отражение — известный долг, вернётся вместе с адаптацией шейдера.</summary>
    private static void SetupWater(GameObject arena)
    {
        Transform water = FindDeep(arena.transform, "Water");
        if (water == null)
        {
            Debug.LogWarning("NewArenaScene: в GLB нет Water.");
            return;
        }

        const string path = MatDir + "/M_GroveWater.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", new Color(0.004f, 0.011f, 0.008f)); // цвет из handoff §5.4
        mat.SetFloat("_Smoothness", 0.95f);
        mat.SetFloat("_Metallic", 0f);

        MeshRenderer mr = water.GetComponentInChildren<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.gameObject.layer = 0; // вода — зеркало, в отражённые слои не входит
    }

    private static void SetupLights()
    {
        // 40° элевации — то, что кладёт тени листвы в кадр (handoff §5.2).
        Light key = EnsureLight("Directional Light");
        key.transform.rotation = Quaternion.Euler(40f, 52f, 0f);
        key.color = new Color(1.0f, 0.78f, 0.52f); // тёплые ~3200 K
        key.intensity = 3.0f;
        key.shadows = LightShadows.Soft;

        // Без fill мост — силуэт: он поднимает поверхности, обращённые к камере.
        Light fill = EnsureLight("FillLight");
        fill.transform.rotation = Quaternion.Euler(18f, -208f, 0f);
        fill.color = new Color(0.82f, 0.87f, 1.0f);
        fill.intensity = 0.4f;
        fill.shadows = LightShadows.None;
    }

    private static Light EnsureLight(string name)
    {
        GameObject go = GameObject.Find(name);
        if (go == null)
            go = new GameObject(name);
        Light light = go.GetComponent<Light>();
        if (light == null)
            light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        return light;
    }

    private static void SetupAtmosphere()
    {
        RenderSettings.skybox = null;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = FogDensity;
        RenderSettings.fogColor = FogColor;
        // Не чёрный: иначе затенённая роща проваливается в сплошную темноту. ~20:1 к key.
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = FogColor * 0.15f;
    }

    private static void SetupCamera(Vector3 shift)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("NewArenaScene: нет камеры с тегом MainCamera.");
            return;
        }

        // Кадр фиксированный по handoff §7 — слежение уходит со сцены. Код FightCamera
        // остаётся: вернём отдельной итерацией, если статичный кадр не устроит.
        FightCamera follow = cam.GetComponent<FightCamera>();
        if (follow != null)
            Object.DestroyImmediate(follow);

        cam.transform.SetPositionAndRotation(HandoffCamPos + shift, Quaternion.Euler(HandoffCamEuler));
        cam.fieldOfView = HandoffFov;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 500f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = FogColor;

        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.High;
        data.renderPostProcessing = true;
    }

    private static void CreatePostVolume()
    {
        var go = new GameObject("PostProcessing");
        Volume volume = go.AddComponent<Volume>();
        volume.isGlobal = true;

        const string path = "Assets/Fight/NewArena/GrovePost.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }
        if (!profile.TryGet(out DepthOfField dof))
            dof = profile.Add<DepthOfField>(true);
        dof.mode.Override(DepthOfFieldMode.Bokeh);
        dof.focusDistance.Override(6.18f); // мост; f/2.8 из handoff §7
        dof.aperture.Override(2.8f);
        dof.focalLength.Override(24f);
        EditorUtility.SetDirty(profile);
        volume.sharedProfile = profile;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        Transform exact = null;
        Transform prefix = null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
                exact = exact != null ? exact : t;
            else if (t.name.StartsWith(name))
                prefix = prefix != null ? prefix : t; // glTF-импорт любит суффиксы к именам
        }
        return exact != null ? exact : prefix;
    }

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
