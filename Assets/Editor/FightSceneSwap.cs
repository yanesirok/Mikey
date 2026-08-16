using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Mikey.Fight;

namespace Mikey.FightEditor
{
    /// <summary>Swaps both fighters' visual model in FightSandbox from the retired
    /// Ch15_nonPBR.fbx to KimonoFighter.fbx, in place.
    ///
    /// Each fighter's root GameObject in the scene is a Model Prefab Instance of Ch15_nonPBR.fbx:
    /// Fighter/FootIK/PlayerFighterInput(or EnemyFighterAI) live on that same root as
    /// instance-level added components, alongside the FBX's own Animator (avatar plus
    /// controller/applyRootMotion overrides) and its skeleton+mesh as children. FightRound's
    /// player/enemy fields and Fighter's opponent field reference those *components* directly,
    /// not the visual hierarchy, so as long as the components are never destroyed the cross
    /// references hold automatically — nothing here re-wires them by hand, and neither does it
    /// touch the root's transform, which is why position/rotation survive untouched too.
    ///
    /// The swap itself: fully unpack the fighter's prefab instance (bakes every current override
    /// — applyRootMotion=false, opponent, moveSpeed, weight, touch/attackCooldown, the added
    /// BlobShadow child — into plain data and drops every remaining reference to Ch15_nonPBR's
    /// GUID except the two the visual model itself still owns: the Animator's avatar and the
    /// SkinnedMeshRenderer's mesh); delete the old skeleton+mesh children; instantiate
    /// KimonoFighter.fbx, unpack that too so its children can be freely reparented, and move its
    /// Armature/Human/Kimono_low straight onto the fighter root at the same depth they had under
    /// the model's own root — the avatar's bone paths are relative to that root and only resolve
    /// if the Animator ends up exactly one level above "Armature" again. Avatar and materials are
    /// reassigned last.
    /// </summary>
    public static class FightSceneSwap
    {
        const string ScenePath = "Assets/Scenes/FightSandbox.unity";
        const string ModelPath = "Assets/Fight/character/KimonoFighter.fbx";
        const string CharacterDir = "Assets/Fight/character";
        const string ControllerPath = "Assets/Fight/Fighter.controller";

        [MenuItem("Mikey/Swap Fighters To Kimono")]
        public static void Run()
        {
            var scene = OpenScene();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
                throw new FileNotFoundException(ModelPath + " is not in the project");
            var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null)
                throw new System.InvalidOperationException(ModelPath + " carries no avatar");

            var skin = LoadMaterial(CharacterDir + "/M_Fighter_Skin.mat");
            var playerKimono = LoadMaterial(CharacterDir + "/M_Player_Kimono.mat");
            var enemyKimono = LoadMaterial(CharacterDir + "/M_Enemy_Kimono.mat");
            var playerBelt = LoadMaterial(CharacterDir + "/M_Player_Belt.mat");
            var enemyBelt = LoadMaterial(CharacterDir + "/M_Enemy_Belt.mat");

            var fighters = scene.GetRootGameObjects()
                .SelectMany(go => go.GetComponentsInChildren<Fighter>(true))
                .ToArray();
            if (fighters.Length == 0)
                throw new System.InvalidOperationException("no Fighter components found in " + ScenePath);

            foreach (var fighter in fighters)
            {
                bool isPlayer = fighter.GetComponent<PlayerFighterInput>() != null;
                Swap(fighter.gameObject, model, avatar, skin,
                     isPlayer ? playerKimono : enemyKimono, isPlayer ? playerBelt : enemyBelt);
            }

            EditorSceneManager.SaveScene(scene);
            Debug.Log("FightSceneSwap: done, " + fighters.Length + " fighter(s) now on KimonoFighter.fbx");
        }

        /// <summary>Samples the kick clip's peak onto both fighters — Edit mode has no Animator
        /// playback, so this is the only way to see the pose without entering Play — and takes
        /// the existing capture tool's screenshot while it holds. Does not save the scene: the
        /// pose is for the screenshot only, not a change to commit.</summary>
        [MenuItem("Mikey/Shoot Kimono Fighters Kick Pose")]
        public static void ShootKickPose()
        {
            var scene = OpenScene();

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                throw new FileNotFoundException(ControllerPath + " is not in the project");
            var kickState = controller.layers
                .SelectMany(l => l.stateMachine.states)
                .Select(c => c.state)
                .FirstOrDefault(s => s.name == "Kick");
            if (kickState == null)
                throw new System.InvalidOperationException(ControllerPath + " has no Kick state");
            var clip = kickState.motion as AnimationClip;
            if (clip == null)
                throw new System.InvalidOperationException("Kick state has no AnimationClip motion");

            var fighters = scene.GetRootGameObjects()
                .SelectMany(go => go.GetComponentsInChildren<Fighter>(true))
                .ToArray();
            if (fighters.Length == 0)
                throw new System.InvalidOperationException("no Fighter components found in " + ScenePath);

            // Around the peak of the kick, not the wind-up or the retract.
            float time = clip.length * 0.5f;
            foreach (var fighter in fighters)
                clip.SampleAnimation(fighter.gameObject, time);

            FightCapture.Shoot();
            Debug.Log("FightSceneSwap: kick pose sampled at t=" + time + "s of " + clip.length + "s");
        }

        static void Swap(GameObject root, GameObject model, Avatar avatar, Material skin,
                         Material kimono, Material belt)
        {
            // Bake in every current override and drop the prefab link to Ch15_nonPBR.
            if (PrefabUtility.IsPartOfPrefabInstance(root))
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);

            // Old skeleton + mesh, direct children of the root — everything but the added blob
            // shadow, which is not part of Ch15_nonPBR and stays as-is.
            foreach (var child in root.transform.Cast<Transform>().ToArray())
                if (child.name != "BlobShadow")
                    Object.DestroyImmediate(child.gameObject);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            foreach (var child in instance.transform.Cast<Transform>().ToArray())
                child.SetParent(root.transform, false);
            Object.DestroyImmediate(instance);

            root.GetComponent<Animator>().avatar = avatar;
            SetMaterial(root, "Human", skin);
            // Kimono_low carries two submeshes — cloth then belt (kimono_fit.py:
            // apply_belt_split) — so it needs the array setter: Renderer.sharedMaterial only
            // ever touches submesh 0, leaving the belt on whatever Unity auto-imported.
            SetMaterials(root, "Kimono_low", new[] { kimono, belt });
        }

        static void SetMaterial(GameObject root, string childName, Material material) =>
            SetMaterials(root, childName, new[] { material });

        static void SetMaterials(GameObject root, string childName, Material[] materials)
        {
            var child = root.transform.Find(childName);
            if (child == null)
                throw new System.InvalidOperationException(
                    root.name + " has no child named " + childName + " after the swap");
            var renderer = child.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null)
                throw new System.InvalidOperationException(childName + " has no SkinnedMeshRenderer");
            if (renderer.sharedMesh.subMeshCount != materials.Length)
                throw new System.InvalidOperationException(
                    childName + " has " + renderer.sharedMesh.subMeshCount + " submesh(es), expected "
                    + materials.Length);
            renderer.sharedMaterials = materials;
        }

        static Material LoadMaterial(string path)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
                throw new FileNotFoundException(path + " is not in the project");
            return mat;
        }

        static UnityEngine.SceneManagement.Scene OpenScene()
        {
            var active = EditorSceneManager.GetActiveScene();
            return active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
