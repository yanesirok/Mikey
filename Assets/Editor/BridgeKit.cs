using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Детали моста, запечённые Blender-скриптом Tools/Blender/bridge_kit.py: восемь именованных
/// мешей в одном FBX плюс атласы нормалей и маски. Этот класс только читает их; вся
/// расстановка — позиции, кривая провиса, наклоны, тона — остаётся в BambooArena, потому что
/// выверена против конкретной камеры и правится там одной константой.
///
/// Договорённость по осям: линейные детали лежат длинной осью по X, столбы и сваи — по Y.
/// BambooArena масштабирует деталь по её баундам под свои размеры, поэтому здесь нет ни
/// одного номинального размера — источник истины один, и он в C#.
/// </summary>
public static class BridgeKit
{
    public const string Dir = "Assets/Fight/Arena/BridgeKit/";
    public const string FbxPath = Dir + "BridgeKit.fbx";
    public const string NormalPath = Dir + "T_BridgeKit_N.png";
    public const string MaskPath = Dir + "T_BridgeKit_Mask.png";

    public static readonly string[] Required =
        { "Post", "PostEnd", "Rail1m", "LowerRail1m", "Sill1m", "Pile", "PileBeam", "Lashing" };

    public sealed class Part
    {
        public Vector3[] Positions;
        public Vector3[] Normals;
        public Vector2[] Uvs;
        public int[] Triangles;
        public Bounds Bounds;
    }

    private static Dictionary<string, Part> _parts;

    public static Part Get(string name)
    {
        if (_parts == null)
            Load();
        if (_parts != null && _parts.TryGetValue(name, out Part part))
            return part;
        Debug.LogError($"BridgeKit: деталь '{name}' не найдена в {FbxPath} — " +
                       "прогони Tools/Blender/build_bridge_kit.ps1 и закоммить результат.");
        return null;
    }

    /// <summary>Сбрасывает кэш, чтобы пересборка арены увидела переэкспортированный FBX.</summary>
    public static void Reset() => _parts = null;

    // bridge_kit.py строит столбы и сваю со скруглением по локальной оси Y Blender'а (её же
    // высотой), ожидая, что она станет "верхом" и в Unity. Но экспорт (axis_forward=-Z,
    // axis_up=Y) — это конверсия из системы Blender (Z — верх), а не тождество: подтверждено
    // на Rail1m/Sill1m/PileBeam (их баунды после импорта совпадают с исходным Blender-scale
    // с точностью до перестановки Y<->Z). В итоге высота этих трёх деталей приходит в Unity
    // по Z, а не по Y. Правим поворотом на 90° вокруг X (собственный поворот, det=+1 — винды
    // треугольников и нормали остаются согласованными без доп. правок): (x,y,z) -> (x,z,-y).
    // Знак проверен диагностикой: у Pile сужение (r_top) сидит на +Z, после поворота остаётся
    // на +Y — верх остаётся верхом. bridge_kit.py прошёл ревью отдельно — не трогаем его,
    // правим только здесь.
    private static readonly string[] AxisSwapYZ = { "Post", "PostEnd", "Pile" };

    private static Part BuildPart(Mesh m)
    {
        var part = new Part
        {
            Positions = m.vertices,
            Normals = m.normals,
            Uvs = m.uv,
            Triangles = m.triangles,
            Bounds = m.bounds,
        };
        if (!AxisSwapYZ.Contains(m.name))
            return part;

        Vector3 Rot(Vector3 v) => new Vector3(v.x, v.z, -v.y);
        for (int i = 0; i < part.Positions.Length; i++)
            part.Positions[i] = Rot(part.Positions[i]);
        for (int i = 0; i < part.Normals.Length; i++)
            part.Normals[i] = Rot(part.Normals[i]);
        Vector3 size = part.Bounds.size;
        part.Bounds = new Bounds(Rot(part.Bounds.center), new Vector3(size.x, size.z, size.y));
        return part;
    }

    private static void Load()
    {
        EnsureImportSettings();
        List<Mesh> meshes = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<Mesh>().ToList();
        if (meshes.Count == 0)
        {
            Debug.LogError($"BridgeKit: {FbxPath} отсутствует или пуст — " +
                           "прогони Tools/Blender/build_bridge_kit.ps1.");
            return;
        }
        _parts = new Dictionary<string, Part>();
        foreach (Mesh m in meshes)
            _parts[m.name] = BuildPart(m);
    }

    /// <summary>
    /// FBX должен быть readable (BambooArena читает вершины в редакторе), материалы из него не
    /// нужны. Оба PNG — данные, не цвет: шейдер декодирует нормали вручную (rgb*2-1) и читает
    /// AO из G-канала, поэтому sRGB выключен и тип текстуры Default — Unity NormalMap
    /// перекодировал бы каналы под UnpackNormal, которого в Arena.shader нет.
    /// </summary>
    public static void EnsureImportSettings()
    {
        if (AssetImporter.GetAtPath(FbxPath) is ModelImporter model &&
            (!model.isReadable || model.materialImportMode != ModelImporterMaterialImportMode.None))
        {
            model.isReadable = true;
            model.materialImportMode = ModelImporterMaterialImportMode.None;
            model.importAnimation = false;
            model.SaveAndReimport();
        }
        foreach (string path in new[] { NormalPath, MaskPath })
            if (AssetImporter.GetAtPath(path) is TextureImporter tex &&
                (tex.sRGBTexture || tex.textureType != TextureImporterType.Default))
            {
                tex.sRGBTexture = false;
                tex.textureType = TextureImporterType.Default;
                tex.SaveAndReimport();
            }
    }

    [MenuItem("Mikey/Verify Bridge Kit")]
    public static void Verify()
    {
        Reset();
        int total = 0;
        foreach (string name in Required)
        {
            Part p = Get(name);
            if (p == null)
                return; // ошибка уже в консоли
            int tris = p.Triangles.Length / 3;
            total += tris;
            Vector3 s = p.Bounds.size;
            // Договорённость по осям, на которой держится масштабирование в BambooArena.
            // Вязка приземистая — у неё длинной оси нет, её не проверяем.
            bool linear = name.EndsWith("1m") || name == "PileBeam";
            bool tall = name == "Post" || name == "PostEnd" || name == "Pile";
            bool axisOk = linear ? s.x >= s.y && s.x >= s.z
                                 : !tall || (s.y >= s.x && s.y >= s.z);
            if (!axisOk)
                Debug.LogError($"BridgeKit: у '{name}' длинная ось не там — баунды {s}. " +
                               "Линейные детали лежат по X, столбы и сваи — по Y.");
            Debug.Log($"BridgeKit: {name} — {tris} трисов, баунды {s}.");
        }
        if (total > 2500)
            Debug.LogError($"BridgeKit: кит целиком {total} трисов (потолок 2500).");
        else
            Debug.Log($"BridgeKit: OK — {Required.Length} деталей, {total} трисов.");
    }
}
