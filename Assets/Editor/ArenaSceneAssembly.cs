using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mikey.EditorTools
{
    /// <summary>
    /// Одноразовая сборка FightSandbox из присланной сцены BambooArena: переносит геймплей из
    /// копии старой сцены, собирает окружение под корень "Arena", кладёт настил моста на y=0 и
    /// восстанавливает контракт Arena/Timber (FightBootstrap и FootIK рейкастят его коллайдер).
    /// public, потому что вызывается через unity-cli eval.
    /// </summary>
    public static class ArenaSceneAssembly
    {
        private const string NewScenePath = "Assets/Scenes/FightSandbox.unity";
        private const string OldScenePath = "Assets/Scenes/FightSandboxOld.unity";

        private static readonly string[] GameplayRoots = { "TouchControls", "FightRound", "Player", "Enemy" };
        private static readonly string[] ArenaRoots =
            { "BambooArena", "WaterReflection", "Water Reflection Probe", "Key Sun", "Fill Light", "Main Camera" };

        public static void Assemble()
        {
            Scene arena = EditorSceneManager.OpenScene(NewScenePath, OpenSceneMode.Single);
            Scene old = EditorSceneManager.OpenScene(OldScenePath, OpenSceneMode.Additive);

            foreach (string name in GameplayRoots)
            {
                GameObject go = old.GetRootGameObjects().FirstOrDefault(g => g.name == name);
                if (go == null) { Debug.LogError($"ArenaSceneAssembly: в старой сцене нет корня '{name}'"); return; }
                SceneManager.MoveGameObjectToScene(go, arena);
            }
            EditorSceneManager.CloseScene(old, true);

            var rig = new GameObject("Arena");
            SceneManager.MoveGameObjectToScene(rig, arena);
            foreach (string name in ArenaRoots)
            {
                GameObject go = arena.GetRootGameObjects().FirstOrDefault(g => g.name == name);
                if (go == null) { Debug.LogError($"ArenaSceneAssembly: в сцене арены нет корня '{name}'"); return; }
                go.transform.SetParent(rig.transform, true);
            }

            // Авторская камера смотрит в −Z, наши бойцы — лицом к +Z-камере: разворот всего рига.
            rig.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            MeshRenderer[] bridge = BridgeRenderers(rig.transform);
            if (bridge.Length == 0)
            {
                string names = string.Join(", ", rig.GetComponentsInChildren<Transform>(true).Select(t => t.name).Distinct());
                Debug.LogError("ArenaSceneAssembly: не нашёл рендереры моста. Дети рига: " + names);
                return;
            }
            Bounds b = bridge[0].bounds;
            foreach (MeshRenderer r in bridge) b.Encapsulate(r.bounds);

            float deckTop = DeckTopY(bridge, b);
            rig.transform.position -= new Vector3(b.center.x, deckTop, b.center.z);
            Debug.Log($"[assembly] bridge bounds {b.size:F2}, deckTop {deckTop:F3}, rig at {rig.transform.position:F3}");

            // Контракт Arena/Timber — тот же рецепт, что был у старого билдера:
            // плоская коробка по габаритам настила, верх ровно y=0.
            var timber = new GameObject("Timber");
            timber.transform.SetParent(rig.transform);
            timber.transform.position = Vector3.zero;
            var box = timber.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.05f, 0f);
            box.size = new Vector3(b.size.x, 0.1f, b.size.z);
            if (b.size.x * 0.5f < Mikey.Fight.FightRules.ArenaHalfWidth)
                Debug.LogError($"ArenaSceneAssembly: настил короче арены боя — {b.size.x:F2}/2 < {Mikey.Fight.FightRules.ArenaHalfWidth}");

            // Вода уехала вместе с ригом — зеркальная плоскость отражения едет следом.
            var refl = rig.GetComponentInChildren<PlanarReflection>(true);
            if (refl == null) Debug.LogError("ArenaSceneAssembly: PlanarReflection не найден");
            else { refl.waterLevel += rig.transform.position.y; Debug.Log($"[assembly] waterLevel -> {refl.waterLevel:F3}"); }

            Camera cam = rig.GetComponentsInChildren<Camera>(true).FirstOrDefault(c => c.gameObject.name == "Main Camera");
            Debug.Log(cam != null
                ? $"[assembly] camera at {cam.transform.position:F3} rot {cam.transform.rotation.eulerAngles:F1}"
                : "[assembly] ВНИМАНИЕ: Main Camera не найдена в риге");

            EditorSceneManager.MarkSceneDirty(arena);
            EditorSceneManager.SaveScene(arena);
            AssetDatabase.DeleteAsset(OldScenePath);
            Debug.Log("[assembly] DONE");
        }

        private static MeshRenderer[] BridgeRenderers(Transform rig)
        {
            return rig.GetComponentsInChildren<MeshRenderer>(true).Where(r =>
            {
                var mf = r.GetComponent<MeshFilter>();
                string mesh = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "";
                return r.name.ToLowerInvariant().Contains("bridge") || mesh.ToLowerInvariant().Contains("bridge");
            }).ToArray();
        }

        /// <summary>Высота ходовой поверхности настила: bounds.max.y — это верх перил, поэтому
        /// вешаем временные MeshCollider'ы на мост и бьём лучом сверху по центру пролёта
        /// (перила идут по краям, центр дорожки сверху открыт).</summary>
        private static float DeckTopY(MeshRenderer[] bridge, Bounds b)
        {
            var temps = new List<MeshCollider>();
            foreach (MeshRenderer r in bridge)
                if (r.GetComponent<Collider>() == null)
                    temps.Add(r.gameObject.AddComponent<MeshCollider>());
            Physics.SyncTransforms();

            var ray = new Ray(new Vector3(b.center.x, b.max.y + 1f, b.center.z), Vector3.down);
            RaycastHit[] hits = Physics.RaycastAll(ray, b.size.y + 2f);
            float y = hits.Where(h => temps.Contains(h.collider))
                          .OrderByDescending(h => h.point.y)
                          .Select(h => h.point.y)
                          .DefaultIfEmpty(b.max.y)   // fallback: верх bounds, дальше правится по скриншоту
                          .First();

            foreach (MeshCollider c in temps) Object.DestroyImmediate(c);
            return y;
        }
    }
}
