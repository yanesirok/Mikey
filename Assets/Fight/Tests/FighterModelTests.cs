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
        public const string ModelPath = "Assets/Fight/character/KimonoFighter.fbx";

        [Test]
        public void Model_ImportsAsHumanoid()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.IsNotNull(importer, ModelPath + " is not in the project");
            Assert.AreEqual(ModelImporterAnimationType.Human, importer.animationType);
        }

        [Test]
        public void Model_AvatarIsValidHuman()
        {
            var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<Avatar>().FirstOrDefault();
            Assert.IsNotNull(avatar, "model carries no avatar");
            Assert.IsTrue(avatar.isValid, "avatar is invalid");
            Assert.IsTrue(avatar.isHuman, "avatar is not human");
        }

        /// <summary>A squashed avatar is the failure this project already hit once: the editor
        /// preview looked right and the running game showed a flattened fighter. Height is the
        /// cheapest signal that the rig scale survived the Blender round trip.</summary>
        [Test]
        public void Model_IsHumanHeight()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.IsNotNull(go);
            var instance = Object.Instantiate(go);
            try
            {
                var renderers = instance.GetComponentsInChildren<Renderer>();
                Assert.IsNotEmpty(renderers, "model has no renderers");
                var bounds = renderers[0].bounds;
                foreach (var r in renderers)
                    bounds.Encapsulate(r.bounds);
                Assert.That(bounds.size.y, Is.InRange(1.6f, 1.95f),
                    "fighter is " + bounds.size.y + " m tall");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Body and cloth are separate meshes and must not share a material: the body
        /// shows only where the kimono does not cover it — head, neck, hands, feet — so one
        /// material for both would paint bare skin in gi colours.</summary>
        [Test]
        public void Model_HasSeparateBodyAndClothMeshes()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.IsNotNull(go);
            var skins = go.GetComponentsInChildren<SkinnedMeshRenderer>();
            Assert.AreEqual(2, skins.Length,
                "expected body and kimono, got " + string.Join(", ", skins.Select(s => s.name)));
        }

        [Test]
        public void Materials_ExistOnTheCharacterShader()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/Fight/character/Character.shader");
            Assert.IsNotNull(shader, "Character.shader is missing");

            foreach (var path in new[]
                     {
                         "Assets/Fight/character/M_Fighter_Skin.mat",
                         "Assets/Fight/character/M_Player_Kimono.mat",
                         "Assets/Fight/character/M_Enemy_Kimono.mat",
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

        /// <summary>Fighter.cs drives position itself, so a clip that carries root motion walks
        /// the fighter out of the spot the code put them in. These clips are video mocap and do
        /// carry root translation, so the importer has to bake it into the pose; this asserts
        /// that setting was actually applied.</summary>
        [Test]
        public void EveryClip_StaysInPlace()
        {
            foreach (var s in States())
            {
                var clip = s.motion as AnimationClip;
                if (clip == null)
                    continue;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.propertyName != "RootT.x" && binding.propertyName != "RootT.z")
                        continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    float min = float.MaxValue, max = float.MinValue;
                    foreach (var key in curve.keys)
                    {
                        min = Mathf.Min(min, key.value);
                        max = Mathf.Max(max, key.value);
                    }
                    Assert.Less(max - min, 0.15f,
                        clip.name + " drifts " + (max - min) + " m on " + binding.propertyName);
                }
            }
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
}
