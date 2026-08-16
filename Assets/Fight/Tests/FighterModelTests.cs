using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Mikey.Fight.Tests
{
    /// <summary>The fighter is retargeted mocap, so a model that imports as anything but
    /// Humanoid silently plays nothing at all. These are asset tests, not logic tests: they
    /// fail when an import setting is lost, which is exactly how the previous fighter broke.</summary>
    public class FighterModelTests
    {
        public const string PlayerModelPath = "Assets/Fight/character/KimonoFighter_Player.fbx";
        public const string EnemyModelPath = "Assets/Fight/character/KimonoFighter_Enemy.fbx";

        static readonly string[] Models = { PlayerModelPath, EnemyModelPath };

        [Test]
        public void Models_ImportAsHumanoid()
        {
            foreach (var path in Models)
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                Assert.IsNotNull(importer, path + " is not in the project");
                Assert.AreEqual(ModelImporterAnimationType.Human, importer.animationType, path);
            }
        }

        [Test]
        public void Models_AvatarsAreValidHumans()
        {
            foreach (var path in Models)
            {
                var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
                Assert.IsNotNull(avatar, path + " carries no avatar");
                Assert.IsTrue(avatar.isValid, path + " avatar is invalid");
                Assert.IsTrue(avatar.isHuman, path + " avatar is not human");
            }
        }

        /// <summary>Both fighters are normalised to the same height in Blender because they fight
        /// each other — one arriving at 3.78 m and the other at 1.77 m would look absurd.</summary>
        [Test]
        public void Models_AreTheSameHumanHeight()
        {
            var heights = new System.Collections.Generic.List<float>();
            foreach (var path in Models)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(go, path + " is not in the project");
                var instance = Object.Instantiate(go);
                try
                {
                    var renderers = instance.GetComponentsInChildren<Renderer>();
                    Assert.IsNotEmpty(renderers, path + " has no renderers");
                    var bounds = renderers[0].bounds;
                    foreach (var r in renderers)
                        bounds.Encapsulate(r.bounds);
                    heights.Add(bounds.size.y);
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
            foreach (var h in heights)
                Assert.That(h, Is.InRange(1.6f, 1.95f), "fighter is " + h + " m tall");
            Assert.That(Mathf.Abs(heights[0] - heights[1]), Is.LessThan(0.1f),
                "fighters differ in height by " + Mathf.Abs(heights[0] - heights[1]) + " m");
        }

        /// <summary>The cloth mesh carries two submeshes — cloth then belt — so the belt can take
        /// its own colour instead of borrowing the rim, which is a silhouette effect and cannot
        /// represent a belt at all.</summary>
        [Test]
        public void Models_KimonoHasClothAndBeltSubmeshes()
        {
            foreach (var path in Models)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var kimono = go.GetComponentsInChildren<SkinnedMeshRenderer>()
                    .FirstOrDefault(s => s.name.Contains("Kimono"));
                Assert.IsNotNull(kimono, path + " has no Kimono mesh");
                Assert.AreEqual(2, kimono.sharedMesh.subMeshCount,
                    path + " kimono should be cloth + belt");
            }
        }

        /// <summary>Do not delete this test. The body arrives from Mixamo with its own diffuse,
        /// and that texture is the single reason the body source was changed at all — the
        /// generated body gave flat-colour heads. A body material without a diffuse hands that
        /// failure straight back, and nothing else in this file would notice: the FBX still
        /// imports, the avatar is still human, the height still checks out, the kimono still has
        /// its two submeshes. That is how a textureless body passed two reviews.
        ///
        /// The maps ride inside the FBX rather than as files under Assets/, so they become
        /// project assets only when FighterImportSetup calls ModelImporter.ExtractTextures. Red
        /// here means either that step did not run or the export lost the embedded media.
        ///
        /// The body is picked by name and not by vertex count: Ch28_Hair carries 15540 vertices
        /// against Ch28_Body's 9466, so "the heaviest mesh" would guard the hair and let a bare
        /// body through. Hair and eyelashes are deliberately not asserted on — they may be
        /// untextured legitimately.</summary>
        [Test]
        public void Models_BodyMeshCarriesItsDiffuse()
        {
            foreach (var path in Models)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var body = go.GetComponentsInChildren<SkinnedMeshRenderer>()
                    .FirstOrDefault(s => !s.name.Contains("Kimono") && s.name.Contains("Body"));
                Assert.IsNotNull(body, path + " has no body mesh");
                Assert.IsNotNull(body.sharedMaterial, path + " body mesh has no material");
                Assert.IsNotNull(body.sharedMaterial.mainTexture,
                    path + " body material " + body.sharedMaterial.name + " has no diffuse");
            }
        }

        [Test]
        public void Materials_ExistOnTheCharacterShader()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/Fight/character/Character.shader");
            Assert.IsNotNull(shader, "Character.shader is missing");

            foreach (var path in new[]
                     {
                         "Assets/Fight/character/M_Player_Kimono.mat",
                         "Assets/Fight/character/M_Enemy_Kimono.mat",
                         "Assets/Fight/character/M_Player_Belt.mat",
                         "Assets/Fight/character/M_Enemy_Belt.mat",
                     })
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.IsNotNull(mat, path + " is missing");
                Assert.AreEqual(shader, mat.shader, path + " is on the wrong shader");
            }
        }

        /// <summary>The kimono has no albedo texture at all — its five materials are flat
        /// colours — so the baked AO doubles as the base map and the normal map carries the
        /// folds. Losing either reduces the garment to a flat silhouette, which is the whole
        /// failure this asset pipeline exists to avoid.</summary>
        [Test]
        public void KimonoMaterials_CarryTheBakedMaps()
        {
            foreach (var path in new[]
                     {
                         "Assets/Fight/character/M_Player_Kimono.mat",
                         "Assets/Fight/character/M_Enemy_Kimono.mat",
                     })
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.IsNotNull(mat, path + " is missing");
                Assert.IsNotNull(mat.GetTexture("_BumpMap"), path + " has no normal map");
                Assert.IsNotNull(mat.GetTexture("_BaseMap"), path + " has no base map");
            }
        }

        /// <summary>A normal map imported as a plain colour texture reads as coloured noise on
        /// the surface instead of relief — a silent, purely visual failure that no other test
        /// here would catch.</summary>
        [Test]
        public void NormalMap_IsImportedAsNormalMap()
        {
            var importer = AssetImporter.GetAtPath(
                "Assets/Fight/character/kimono/T_Kimono_Normal.png") as TextureImporter;
            Assert.IsNotNull(importer, "normal map is not in the project");
            Assert.AreEqual(TextureImporterType.NormalMap, importer.textureType);
        }

        /// <summary>bake() writes T_Kimono_AO.png with is_data=True — linear values. Importing
        /// it as sRGB (Unity's default for a new PNG) decodes it a second time and crushes the
        /// midtones before _AlbedoGamma even applies — a silent, purely visual failure.</summary>
        [Test]
        public void AoMap_IsImportedAsLinear()
        {
            var importer = AssetImporter.GetAtPath(
                "Assets/Fight/character/kimono/T_Kimono_AO.png") as TextureImporter;
            Assert.IsNotNull(importer, "AO map is not in the project");
            Assert.IsFalse(importer.sRGBTexture, "AO map is imported as sRGB");
        }
    }

    /// <summary>The controller is the contract between Fighter.cs and the art. A state with a
    /// null motion plays the bind pose and looks like a frozen fighter, not like an error — so
    /// it has to fail here rather than in someone's play session. Every clip reference in this
    /// controller was dangling when these tests were written; that is the failure they lock out.</summary>
    public class FighterClipsTests
    {
        const string ControllerPath = "Assets/Fight/Fighter.controller";

        static UnityEditor.Animations.AnimatorState[] States()
        {
            var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
                ControllerPath);
            Assert.IsNotNull(ac, ControllerPath + " is missing");
            return ac.layers
                .SelectMany(l => l.stateMachine.states)
                .Select(c => c.state)
                .ToArray();
        }

        static UnityEditor.Animations.AnimatorState State(string name)
        {
            var s = States().FirstOrDefault(x => x.name == name);
            Assert.IsNotNull(s, "no state named " + name);
            return s;
        }

        [Test]
        public void EveryState_HasMotion()
        {
            foreach (var s in States())
                Assert.IsNotNull(s.motion, "state " + s.name + " has no motion");
        }

        /// <summary>Kick used to play Punch_Cross and Blocking used to borrow UAL2's shield
        /// stance, because the CC0 pack had neither a kick nor an unarmed block. Both are paid
        /// off by the project's own karate mocap. UAL1 is deliberately still allowed — Walk and
        /// Hit legitimately come from it; UAL2 was only ever the weapon-stance stopgap.</summary>
        [Test]
        public void NoState_StillUsesTheWeaponStopgaps()
        {
            foreach (var s in States())
            {
                var path = AssetDatabase.GetAssetPath(s.motion);
                Assert.IsFalse(path.Contains("UAL2_Standard"),
                    "state " + s.name + " still plays a weapon-pack stopgap: " + path);
            }
        }

        /// <summary>Asserted on the asset path and clip name rather than on a generic "not a
        /// punch" check: every technique here is a named karate move, so the test can say which
        /// one it expects. Yoko geri is the kick the project kept — spec 2026-07-29 drops mae
        /// geri, so a Kick that plays MaeGeri is a regression, not a near miss.</summary>
        [Test]
        public void Kick_PlaysYokoGeri()
        {
            var motion = State("Kick").motion;
            Assert.IsNotNull(motion, "Kick has no motion");
            Assert.IsTrue(motion.name.Contains("YokoGeri"),
                "Kick plays " + motion.name + " instead of yoko geri");
        }

        [Test]
        public void Blocking_PlaysAgeUke()
        {
            var motion = State("Blocking").motion;
            Assert.IsNotNull(motion, "Blocking has no motion");
            Assert.IsTrue(motion.name.Contains("AgeUke"),
                "Blocking plays " + motion.name + " instead of age uke");
        }
    }

    /// <summary>Fighter.cs owns the fighters' positions and moves them by writing
    /// transform.position directly. That only works while the Animator is not also moving them.
    /// The mocap clips carry real captured translation — up to 0.8 m — so the moment someone
    /// ticks Apply Root Motion, both fighters start sliding around the arena and the arena's
    /// bridge-deck height logic stops lining up with where they actually are.</summary>
    public class FightSceneTests
    {
        const string ScenePath = "Assets/Scenes/FightSandbox.unity";

        [Test]
        public void Fighters_DoNotApplyRootMotion()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                var animators = scene.GetRootGameObjects()
                    .SelectMany(go => go.GetComponentsInChildren<Animator>(true))
                    .Where(a => a.GetComponent<Fighter>() != null)
                    .ToArray();

                Assert.IsNotEmpty(animators, "no fighters found in " + ScenePath);
                foreach (var animator in animators)
                    Assert.IsFalse(animator.applyRootMotion,
                        animator.name + " applies root motion; Fighter.cs already owns position");
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
