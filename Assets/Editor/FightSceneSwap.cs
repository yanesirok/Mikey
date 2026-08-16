using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Mikey.Fight;

namespace Mikey.FightEditor
{
    /// <summary>Swaps each fighter's visual model in FightSandbox in place — the player gets
    /// KimonoFighter_Player.fbx, the enemy KimonoFighter_Enemy.fbx, so the two read as different
    /// people rather than as one fighter in two belts.
    ///
    /// Each fighter's root GameObject in the scene is a Model Prefab Instance of the model it is
    /// currently wearing:
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
    /// BlobShadow child — into plain data and drops every remaining reference to the outgoing
    /// model's GUID except the two the visual model itself still owns: the Animator's avatar and
    /// the SkinnedMeshRenderer's mesh); delete the old skeleton+mesh children; instantiate
    /// the fighter's own model, unpack that too so its children can be freely reparented, and
    /// move its Armature plus every mesh straight onto the fighter root at the same depth under
    /// the model's own root — the avatar's bone paths are relative to that root and only resolve
    /// if the Animator ends up exactly one level above "Armature" again. Avatar and kimono
    /// materials are reassigned last; body, hair and eye materials are left exactly as the model
    /// delivers them, because they are Mixamo's own and carry the face texture.
    /// </summary>
    public static class FightSceneSwap
    {
        const string ScenePath = "Assets/Scenes/FightSandbox.unity";
        const string CharacterDir = "Assets/Fight/character";
        const string PlayerModelPath = CharacterDir + "/KimonoFighter_Player.fbx";
        const string EnemyModelPath = CharacterDir + "/KimonoFighter_Enemy.fbx";
        const string ControllerPath = "Assets/Fight/Fighter.controller";

        [MenuItem("Mikey/Swap Fighters To Kimono")]
        public static void Run()
        {
            var scene = OpenScene();

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
                var modelPath = isPlayer ? PlayerModelPath : EnemyModelPath;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (model == null)
                    throw new FileNotFoundException(modelPath + " is not in the project");
                // From the same file as the mesh, not from a shared model: an avatar describes
                // one specific skeleton, and retargeting silently stops working on another.
                var avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
                if (avatar == null)
                    throw new System.InvalidOperationException(modelPath + " carries no avatar");

                Swap(fighter.gameObject, model, avatar,
                     isPlayer ? playerKimono : enemyKimono, isPlayer ? playerBelt : enemyBelt);
            }

            EditorSceneManager.SaveScene(scene);
            Debug.Log("FightSceneSwap: done, " + fighters.Length + " fighter(s) on their own model");
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

        static void Swap(GameObject root, GameObject model, Avatar avatar,
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
            SetKimonoMaterials(root, kimono, belt);
        }

        /// <summary>Assigns the two kimono materials by the name of the placeholder the FBX
        /// imported into each slot, never by slot index.
        ///
        /// The submesh order flips between Blender and Unity: kimono_fit.py splits the garment as
        /// {0: cloth 10940 tris, 1: belt 1060}, and both models come back out of the FBX import as
        /// submesh 0 = Kimono_Belt (1060), submesh 1 = Kimono_Cloth (10940). Writing
        /// new[] { kimono, belt } would therefore paint the belt in the cloth colour and the cloth
        /// in the belt colour, and nothing in the suite would go red over it —
        /// Models_KimonoHasClothAndBeltSubmeshes only counts submeshes, and a swapped pair still
        /// counts two. The placeholder names travel with the mesh, so they survive a reorder.
        ///
        /// It has to be the array setter either way: Renderer.sharedMaterial only ever touches
        /// submesh 0, leaving the other slot on whatever Unity auto-imported.</summary>
        static void SetKimonoMaterials(GameObject root, Material kimono, Material belt)
        {
            var child = root.transform.Find("Kimono_low");
            if (child == null)
                throw new System.InvalidOperationException(
                    root.name + " has no child named Kimono_low after the swap");
            var renderer = child.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null)
                throw new System.InvalidOperationException("Kimono_low has no SkinnedMeshRenderer");

            var slots = renderer.sharedMaterials;
            if (slots.Length != 2)
                throw new System.InvalidOperationException(
                    "Kimono_low has " + slots.Length + " material slot(s), expected cloth + belt");
            for (int i = 0; i < slots.Length; i++)
            {
                var placeholder = slots[i] == null ? null : slots[i].name;
                if (placeholder == "Kimono_Cloth") slots[i] = kimono;
                else if (placeholder == "Kimono_Belt") slots[i] = belt;
                else
                    throw new System.InvalidOperationException(
                        "Kimono_low slot " + i + " holds " + (placeholder ?? "nothing")
                        + ", expected Kimono_Cloth or Kimono_Belt");
            }
            renderer.sharedMaterials = slots;
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
