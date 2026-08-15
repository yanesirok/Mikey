using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Mikey.FightEditor
{
    /// <summary>Wires the fighter's animator states to the project's own karate mocap.
    ///
    /// Done in code rather than by hand because the clips need two import settings that are
    /// easy to lose and invisible when lost: without Bake Into Pose on the root's XZ the
    /// fighter walks away from the position Fighter.cs holds for them, and without Loop Time
    /// the three looping states play once and freeze.
    /// </summary>
    public static class FighterClipSetup
    {
        const string ControllerPath = "Assets/Fight/Fighter.controller";
        const string Mocap = "Assets/Fight/animations/";
        const string Ual1 = "Assets/Characters/Karate/UAL1_Standard.fbx";

        /// <summary>state name -> (model asset, clip name inside it).
        ///
        /// The two UAL1 clips carry an "Armature|" prefix and the mocap clips do not: the mocap
        /// files were renamed at import time, the CC0 pack was left as its exporter wrote it.
        /// These are the names of the actual AnimationClip assets — verified against the
        /// importers' own clip lists — and a mismatch here throws rather than silently skipping.
        /// </summary>
        static readonly (string State, string Model, string Clip)[] Wiring =
        {
            ("Idle",     Mocap + "video_2026-08-06_08-08-32_BoyFBX.fbx", "FightIdle"),
            ("Walk",     Ual1,                                           "Armature|Walk_Loop"),
            ("Punch",    Mocap + "video_2026-08-06_08-08-18_BoyFBX.fbx", "OiZuki"),
            ("PunchB",   Mocap + "video_2026-08-06_08-08-25_BoyFBX.fbx", "Uraken_Swing"),
            ("Kick",     Mocap + "video_2026-08-06_08-08-14_BoyFBX.fbx", "YokoGeri_High"),
            ("Hit",      Ual1,                                           "Armature|Hit_Chest"),
            ("BlockHit", Mocap + "video_2026-08-06_08-08-22_BoyFBX.fbx", "AgeUke"),
            ("Blocking", Mocap + "video_2026-08-06_08-08-22_BoyFBX.fbx", "AgeUke"),
            ("Death",    Mocap + "video_2026-08-06_08-08-28_BoyFBX.fbx", "Knockdown_GetUp"),
        };

        /// <summary>Only these three are states the fighter can sit in; the rest fire once on a
        /// trigger and hand control back.</summary>
        static readonly HashSet<string> Looping = new HashSet<string>
        {
            "FightIdle", "Armature|Walk_Loop", "AgeUke",
        };

        [MenuItem("Mikey/Setup Fighter Clips")]
        public static void Run()
        {
            foreach (var model in Wiring.Select(w => w.Model).Distinct())
                SetUpClips(model);

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                throw new System.IO.FileNotFoundException(ControllerPath + " is not in the project");

            var states = controller.layers
                .SelectMany(l => l.stateMachine.states)
                .Select(c => c.state)
                .ToDictionary(s => s.name);

            foreach (var (stateName, model, clipName) in Wiring)
            {
                if (!states.TryGetValue(stateName, out var state))
                    throw new System.InvalidOperationException(
                        "controller has no state named " + stateName);

                var clip = AssetDatabase.LoadAllAssetsAtPath(model)
                    .OfType<AnimationClip>()
                    .FirstOrDefault(c => c.name == clipName);
                if (clip == null)
                    throw new System.InvalidOperationException(
                        "no clip named " + clipName + " inside " + model);

                state.motion = clip;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("FighterClipSetup: done");
        }

        /// <summary>clipAnimations is empty until something writes it — until then the importer
        /// serves defaultClipAnimations, which cannot be edited in place. Copying, editing and
        /// assigning back is the supported way to change per-clip settings.</summary>
        static void SetUpClips(string modelPath)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null)
                throw new System.IO.FileNotFoundException(modelPath + " is not in the project");

            var clips = importer.clipAnimations.Length > 0
                ? importer.clipAnimations
                : importer.defaultClipAnimations;

            var wanted = new HashSet<string>(
                Wiring.Where(w => w.Model == modelPath).Select(w => w.Clip));

            foreach (var clip in clips)
            {
                if (!wanted.Contains(clip.name))
                    continue;
                // Fighter.cs drives position itself; root translation in the clip fights it.
                clip.lockRootPositionXZ = true;
                clip.keepOriginalPositionXZ = true;
                clip.loopTime = Looping.Contains(clip.name);
            }

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }
    }
}
