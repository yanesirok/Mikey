using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Mikey.Fight;

/// <summary>
/// One-click builder for the FightSandbox scene: configures the Mixamo imports (character mesh
/// plus animation-only FBX files, all Humanoid against the character's own avatar so the clips
/// retarget cleanly), generates the fighter AnimatorController, and places a cool-tinted player
/// against a warm-tinted AI opponent with touch controls.
/// Re-runnable — regenerates the controller/materials and replaces the fighters.
/// Menu: Mikey ▸ Setup Fight Scene. Also callable via -executeMethod FightSceneSetup.Setup.
/// </summary>
public static class FightSceneSetup
{
    private const string CharacterPath = "Assets/Fight/character/Ch15_nonPBR.fbx";
    private const string AnimDir = "Assets/Fight/animations/";
    private const string ControllerPath = "Assets/Fight/Fighter.controller";

    /// <summary>How fast the river runs, and the only place that decides it. The shader moves the
    /// surface, this moves everything floating on it, and the two disagreeing is instantly visible
    /// — a ring sliding across its own ripples reads worse than no current at all. The value is
    /// pushed onto the water material in <see cref="CreateEffects"/> rather than left to the
    /// shader's default, because a material serialises whatever it was last given and then the
    /// default never reaches it again.</summary>
    private const float RiverFlowSpeed = 0.5f;
    private const string ScenePath = "Assets/Scenes/FightSandbox.unity";

    /// <summary>Mixamo names the clip inside every FBX "mixamo.com", so the file name is the only
    /// identifier there is — each clip is renamed on import to the name we look it up by.</summary>
    private static readonly (string File, string Clip, bool Loop)[] MixamoClips =
    {
        ("X Bot@Punching.fbx",                     "Punch",    false),
        ("X Bot@Kicking.fbx",                      "Kick",     false),
        ("X Bot@Martelo 2.fbx",                    "KickB",    false),
        ("X Bot@Chapa-Giratoria.fbx",              "KickSpin", false),
        ("X Bot@Body Block.fbx",                   "Block",    true),
        ("X Bot@Receive Uppercut To The Face.fbx", "Hit",      false),
        ("Ch15_nonPBR@Big Kidney Hit.fbx",         "HitBody",  false),
        ("Ch15_nonPBR@Big Side Hit.fbx",           "HitHeavy", false),
        ("Ch15_nonPBR@Light Hit To Head.fbx",      "HitLight", false),
        ("Ch15_nonPBR@Slow Jog Backwards.fbx",     "Jog",      true),
    };

    // Multiplied over the character's own albedo rather than replacing it, so the pair reads as
    // two fighters without losing the texture. Above 1 on purpose: this character is black
    // tactical gear whose armour diffuse has a median of 10/255, which under a backlit sun comes
    // out as a flat silhouette. The lift puts it back in the range of ordinary dark cloth.
    // The player is the warm one now that the arena is a cold overcast grove: the environment is
    // graded down to almost no saturation, so anything warm in frame is the eye's first stop.
    // The opponent stays cold and dark and reads by silhouette against the mist instead.
    // Matched in luminance, not just in feel: 0.2126R + 0.7152G + 0.0722B lands within 2% for
    // both. Picked by eye they were a factor of two apart, and on an albedo this dark that read
    // as one fighter lit and the other a black cut-out. Hue separates them; brightness must not.
    private static readonly Color PlayerSuit = new Color(0.95f, 0.70f, 0.52f); // warm ochre
    private static readonly Color EnemySuit = new Color(0.62f, 0.75f, 0.98f);  // cold slate

    [MenuItem("Mikey/Setup Fight Scene")]
    public static void Setup()
    {
        Avatar rig = ConfigureCharacter();
        if (rig == null)
            return; // no valid humanoid avatar — error already logged, scene untouched
        foreach ((string file, string clip, bool loop) in MixamoClips)
            ConfigureAnimation(AnimDir + file, clip, loop, rig);

        ApplyQualitySettings();

        AnimatorController controller = BuildController();
        if (controller == null)
            return; // clips missing — error already logged, scene untouched
        PopulateScene(controller);
        Debug.Log("FightSceneSetup: done. Open FightSandbox and press Play (A/D move, J punch, K kick, hold S block; on device — touch).");
    }

    /// <summary>Rebuild only the environment — atmosphere, arena, particles, grade and camera
    /// framing — leaving the fighters, the controller and the FBX imports alone. Setup re-imports
    /// eleven Mixamo files every run, which is minutes; look iteration needs seconds.
    /// Menu: Mikey ▸ Rebuild Arena. Also callable via -executeMethod FightSceneSetup.RebuildArena.
    /// </summary>
    [MenuItem("Mikey/Rebuild Arena")]
    public static void RebuildArena()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath);
        var owned = new System.Collections.Generic.HashSet<string>
        {
            "Arena", "Water", "Terrain", "Grass", "FX_Mist", "FX_Leaves", "FX_Motes", "FX_Drips",
            "FX_Steam", "FX_Embers", "PostProcessing",
        };
        foreach (GameObject root in scene.GetRootGameObjects())
            if (owned.Contains(root.name))
                Object.DestroyImmediate(root);

        EnsureReflectedLayer();
        CreateAtmosphere();
        BambooArena.Build();
        CreateEffects();
        CreatePostProcessing();

        GameObject playerGo = GameObject.Find("Player");
        GameObject enemyGo = GameObject.Find("Enemy");
        if (playerGo != null && enemyGo != null)
            AimCamera(playerGo.GetComponent<Fighter>(), enemyGo.GetComponent<Fighter>());

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("FightSceneSetup: arena rebuilt.");
    }

    /// <summary>
    /// Project-wide texture settings the arena depends on. Both are the difference between a
    /// 2048 character map being used and being quietly thrown away: a mipmap limit above zero
    /// halves every texture at load, and without forced anisotropy the deck — which the camera
    /// sees at a very grazing angle — collapses into mip mush a couple of metres out.
    /// </summary>
    private static void ApplyQualitySettings()
    {
        int current = QualitySettings.GetQualityLevel();
        for (int level = 0; level < QualitySettings.names.Length; level++)
        {
            QualitySettings.SetQualityLevel(level, false);
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.globalTextureMipmapLimit = 0; // full resolution
        }
        QualitySettings.SetQualityLevel(current, false);
        AssetDatabase.SaveAssets();
    }

    /// <summary>Humanoid rig for the character mesh. Its avatar is the single reference every
    /// animation is then described against; letting each clip file generate its own avatar is
    /// what folded the previous character into a seated pose with its hips below ground.</summary>
    private static Avatar ConfigureCharacter()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(CharacterPath);
        if (importer == null)
        {
            Debug.LogError($"FightSceneSetup: no importer at {CharacterPath} — is the model in place?");
            return null;
        }

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = false; // a Mixamo character download is a bare T-pose
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        importer.SaveAndReimport();

        // Mixamo embeds its textures inside the FBX, where no material can reference them — the
        // generated materials come out with _BaseMap unset and the fighter renders as flat colour.
        // Extracting once puts them on disk, and the reimport binds them.
        string charDir = Path.GetDirectoryName(CharacterPath).Replace('\\', '/');
        string texDir = charDir + "/Textures";
        if (!AssetDatabase.IsValidFolder(texDir))
        {
            AssetDatabase.CreateFolder(charDir, "Textures");
            if (importer.ExtractTextures(texDir))
            {
                AssetDatabase.Refresh();
                importer.SaveAndReimport();
            }
        }

        // Mixamo ships 4096² across eight maps — a hundred megabytes of source art for a phone.
        // Halved rather than quartered: the camera now sits 6 units out and a fighter fills half
        // the frame height, where 1024 was visibly soft on the armour panels. 2048 is the budget.
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { texDir }))
        {
            string texPath = AssetDatabase.GUIDToAssetPath(guid);
            var texImporter = (TextureImporter)AssetImporter.GetAtPath(texPath);
            if (texImporter == null || texImporter.maxTextureSize <= 2048)
                continue;
            texImporter.maxTextureSize = 2048;
            texImporter.SaveAndReimport();
        }

        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(CharacterPath).OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isValid)
        {
            Debug.LogError($"FightSceneSetup: {CharacterPath} produced no valid humanoid avatar.");
            return null;
        }
        return avatar;
    }

    /// <summary>Import one animation-only FBX against the character's avatar and rename its take,
    /// since Mixamo calls every clip "mixamo.com" regardless of what it animates.</summary>
    private static void ConfigureAnimation(string fbxPath, string clipName, bool loop, Avatar rig)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        if (importer == null)
        {
            Debug.LogError($"FightSceneSetup: no importer at {fbxPath} — is the animation in place?");
            return;
        }

        importer.animationType = ModelImporterAnimationType.Human;
        // Mixamo names a clip file "<source character>@<clip>", so the prefix says whose skeleton
        // it was exported on. Copying the character's avatar is only valid when that skeleton is
        // the same one: on a different body Unity extracts muscle values against the wrong bone
        // lengths, which skews the legs — 32-77 mm of error, and the fighter folds at the knees.
        // Anything else goes through its own avatar and retargets in muscle space, as intended.
        string clipRig = Path.GetFileNameWithoutExtension(fbxPath).Split('@')[0];
        bool sameRig = clipRig == Path.GetFileNameWithoutExtension(CharacterPath);
        importer.avatarSetup = sameRig
            ? ModelImporterAvatarSetup.CopyFromOther
            : ModelImporterAvatarSetup.CreateFromThisModel;
        importer.sourceAvatar = sameRig ? rig : null;
        importer.materialImportMode = ModelImporterMaterialImportMode.None; // skeleton only, no mesh

        ModelImporterClipAnimation[] takes = importer.defaultClipAnimations;
        if (takes.Length == 0)
        {
            Debug.LogError($"FightSceneSetup: {fbxPath} contains no animation take.");
            return;
        }
        takes[0].name = clipName;
        takes[0].loopTime = loop;
        importer.clipAnimations = new[] { takes[0] };
        importer.SaveAndReimport();
    }

    /// <summary>Load a clip by the name <see cref="MixamoClips"/> renamed it to on import.</summary>
    private static AnimationClip Clip(string clipName)
    {
        (string File, string Clip, bool Loop) entry = MixamoClips.FirstOrDefault(c => c.Clip == clipName);
        if (entry.File == null)
            return null;
        return AssetDatabase.LoadAllAssetsAtPath(AnimDir + entry.File)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == clipName);
    }

    private static AnimatorController BuildController()
    {
        string[] missing = MixamoClips.Where(c => Clip(c.Clip) == null).Select(c => c.Clip).ToArray();
        if (missing.Length > 0)
        {
            Debug.LogError($"FightSceneSetup: animation clips not found: {string.Join(", ", missing)}. If Unity is still importing, wait and run Mikey ▸ Setup Fight Scene again.");
            return null;
        }

        // ponytail: three clips are not downloaded yet, so the guard pose stands in for the
        // fighting idle, the backwards jog runs in reverse as the walk, and the heavy hit holds
        // as the death. Swapping in Fighting Idle / Walking / Dying is a one-line change each.
        AnimationClip idleClip = Clip("Block");
        AnimationClip walkClip = Clip("Jog");
        AnimationClip punchClip = Clip("Punch");
        AnimationClip kickClip = Clip("Kick");
        AnimationClip hitClip = Clip("HitLight");
        AnimationClip deathClip = Clip("HitHeavy");
        AnimationClip blockingClip = Clip("Block");
        AnimationClip blockHitClip = Clip("HitBody");

        AssetDatabase.DeleteAsset(ControllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        controller.AddParameter("MoveSpeed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Punch", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("PunchB", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Kick", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("BlockHit", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Blocking", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);

        // Without the IK pass on the layer Unity never calls OnAnimatorIK, and FootIK is dead
        // code that reports nothing. Layers are returned by value, so the array has to go back.
        AnimatorControllerLayer[] layers = controller.layers;
        layers[0].iKPass = true;
        controller.layers = layers;

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        AnimatorState idle = sm.AddState("Idle");
        idle.motion = idleClip;
        AnimatorState walk = sm.AddState("Walk");
        walk.motion = walkClip;
        walk.speed = -1f; // the only locomotion clip jogs backwards; reversed it advances
        AnimatorState punch = sm.AddState("Punch");
        punch.motion = punchClip;
        AnimatorState punchB = sm.AddState("PunchB");
        punchB.motion = punchClip;
        punchB.mirror = true; // second punch for free: same clip, other hand
        AnimatorState kick = sm.AddState("Kick");
        kick.motion = kickClip;
        AnimatorState hit = sm.AddState("Hit");
        hit.motion = hitClip;
        AnimatorState death = sm.AddState("Death");
        death.motion = deathClip;
        AnimatorState blocking = sm.AddState("Blocking");
        blocking.motion = blockingClip;
        AnimatorState blockHit = sm.AddState("BlockHit");
        blockHit.motion = blockHitClip;
        sm.defaultState = idle;

        foreach (AnimatorState from in new[] { idle, walk })
            Instant(from, blocking).AddCondition(AnimatorConditionMode.If, 0, "Blocking");
        Instant(blocking, idle).AddCondition(AnimatorConditionMode.IfNot, 0, "Blocking");
        Instant(blocking, blockHit).AddCondition(AnimatorConditionMode.If, 0, "BlockHit");
        AnimatorStateTransition backToGuard = AfterClip(blockHit, blocking);
        backToGuard.AddCondition(AnimatorConditionMode.If, 0, "Blocking");
        Instant(blockHit, idle).AddCondition(AnimatorConditionMode.IfNot, 0, "Blocking");

        Instant(idle, walk).AddCondition(AnimatorConditionMode.Greater, 0.1f, "MoveSpeed");
        Instant(walk, idle).AddCondition(AnimatorConditionMode.Less, 0.1f, "MoveSpeed");

        foreach (AnimatorState from in new[] { idle, walk })
        {
            Instant(from, punch).AddCondition(AnimatorConditionMode.If, 0, "Punch");
            Instant(from, punchB).AddCondition(AnimatorConditionMode.If, 0, "PunchB");
            Instant(from, kick).AddCondition(AnimatorConditionMode.If, 0, "Kick");
        }
        AfterClip(punch, idle);
        AfterClip(punchB, idle);
        AfterClip(kick, idle);

        AnimatorStateTransition anyToHit = sm.AddAnyStateTransition(hit);
        anyToHit.AddCondition(AnimatorConditionMode.If, 0, "Hit");
        anyToHit.duration = 0.05f;
        anyToHit.canTransitionToSelf = false;
        AfterClip(hit, idle);

        AnimatorStateTransition anyToDeath = sm.AddAnyStateTransition(death);
        anyToDeath.AddCondition(AnimatorConditionMode.If, 0, "Dead");
        anyToDeath.duration = 0.05f;
        anyToDeath.canTransitionToSelf = false;

        AssetDatabase.SaveAssets();
        return controller;
    }

    private static AnimatorStateTransition Instant(AnimatorState from, AnimatorState to)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.hasExitTime = false;
        t.duration = 0.1f;
        return t;
    }

    private static AnimatorStateTransition AfterClip(AnimatorState from, AnimatorState to)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.hasExitTime = true;
        t.exitTime = 0.9f;
        t.duration = 0.1f;
        return t;
    }

    private static void PopulateScene(AnimatorController controller)
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath);

        // Sweep every matching root, not the first one GameObject.Find happens to return: with
        // one removal per name per run, a duplicate is never caught up with, and three fighters
        // had quietly accumulated in the scene. Find also ignores inactive objects; this does not.
        var stale = new System.Collections.Generic.HashSet<string>
        {
            "Fighter_Player", "Fighter_Enemy", "Player", "Enemy", "TouchControls", "Backdrop",
            "FX_Leaves", "FX_Embers", "FX_Mist", "FX_Drips", "FX_Motes", "Torii", "MapleTree",
            "MapleTree_Mid", "MapleTree_Far", "Vegetation", "Grass", "Terrain", "Water", "Arena",
            "ArenaProps", "PostProcessing", "FightRound", "FX_Steam",
        };
        int removed = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (!stale.Contains(root.name) && root.GetComponentInChildren<Fighter>(true) == null)
                continue;
            Object.DestroyImmediate(root);
            removed++;
        }
        Debug.Log($"FightSceneSetup: removed {removed} stale objects.");

        EnsureReflectedLayer(); // the arena builder puts the bridge on it as it is created
        CreateAtmosphere();
        BambooArena.Build();
        CreateEffects();

        Fighter player = SpawnFighter("Player", -1.2f, controller, PlayerSuit);
        Fighter enemy = SpawnFighter("Enemy", 1.2f, controller, EnemySuit);
        PlayerFighterInput input = player.gameObject.AddComponent<PlayerFighterInput>();
        enemy.gameObject.AddComponent<EnemyFighterAI>();
        player.Opponent = enemy;
        enemy.Opponent = player;

        var roundGo = new GameObject("FightRound");
        FightRound round = roundGo.AddComponent<FightRound>();
        round.Player = player;
        round.Enemy = enemy;
        input.Touch = CreateTouchControls();
        AimCamera(player, enemy);
        CreatePostProcessing(); // after AimCamera: it needs Camera.main to already be set up

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>Put the scene camera on the fight line and hand it the two fighters to track.
    /// Snapped immediately so the saved scene frames the fight without entering play mode.</summary>
    private static void AimCamera(Fighter player, Fighter enemy)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("FightSceneSetup: no camera tagged MainCamera in the scene — camera not set up.");
            return;
        }

        // Replaced rather than reused: a component that survives from an earlier run keeps its
        // serialized distance/height/tilt, so every framing change made in code silently never
        // reached the scene. Re-adding it forces the current defaults in, the same way the rest
        // of this builder regenerates what it owns.
        FightCamera stale = cam.GetComponent<FightCamera>();
        if (stale != null)
            Object.DestroyImmediate(stale);

        FightCamera follow = cam.gameObject.AddComponent<FightCamera>();
        follow.Player = player.transform;
        follow.Opponent = enemy.transform;
        follow.Snap();

        // There is no skybox: the background is a flat clear colour identical to the fog, so
        // the far grove does not fade *toward* the sky, it becomes it.
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = SkyColor;

        // Without this the grass and the ridge line render as raw stair-steps — the "filmed
        // on an old camera" look. MSAA alone cannot help the grass, whose silhouette is one
        // alpha-free strip per blade, so a post-process resolve is what actually cleans it up.
        UniversalAdditionalCameraData camData = cam.GetUniversalAdditionalCameraData();
        camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        camData.antialiasingQuality = AntialiasingQuality.High;
    }

    private static FightTouchControls CreateTouchControls()
    {
        var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI/MikeyPanelSettings.asset");
        if (settings == null)
            Debug.LogError("FightSceneSetup: Assets/UI/MikeyPanelSettings.asset not found — touch UI will not render.");

        var go = new GameObject("TouchControls");
        var doc = go.AddComponent<UIDocument>();
        doc.panelSettings = settings;

        FightTouchControls controls = go.AddComponent<FightTouchControls>();
        controls.PunchButton = Icon("btn_punch");
        controls.KickButton = Icon("btn_kick");
        controls.BlockButton = Icon("btn_block");
        return controls;
    }

    /// <summary>Sky, fog and camera background are one colour on purpose: anything far enough
    /// away has to vanish into it completely, which is what separates the planes here.</summary>
    // #C8D2CC — not white and not grey. Pure white fog reads as milk; the slight green is what
    // makes it read as air in a forest.
    private static readonly Color SkyColor = new Color(0.784f, 0.824f, 0.800f);

    /// <summary>
    /// Overcast light, gradient ambient and the fog that holds the arena together. This is the
    /// easy case for a phone: a misty bamboo grove after rain has no hard shadows and no sharp
    /// reflections, so almost all of it is ambient plus fog. The fog is doing composition work
    /// rather than just hiding the far plane — everything past the bridge is meant to wash out
    /// to exactly the colour of the sky, and that pale band is what the fighters are read
    /// against. Put them in front of dark bamboo instead and they disappear.
    /// </summary>
    private static void CreateAtmosphere()
    {
        // No skybox at all. In the reference the sky is a blown-out white with no gradient in
        // it, so a flat clear colour is not a shortcut, it is the right answer — and it
        // guarantees the background and the fog are the same colour to the last bit.
        RenderSettings.skybox = null;

        // A real key light with real shadows, not an overcast wash. The single biggest thing
        // separating this from a fighting game that looks expensive is that the light has a
        // direction you can name and casts a shadow you can see.
        GameObject sun = GameObject.Find("Directional Light");
        if (sun != null)
        {
            var light = sun.GetComponent<Light>();
            light.color = new Color(1f, 0.97f, 0.92f);
            light.intensity = 1.3f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.58f;
            light.shadowBias = 0.03f;
            light.shadowNormalBias = 0.25f;
            // From above, in front and to one side. Placed behind the fighters it rimmed them
            // beautifully and left everything facing the camera in total darkness — two black
            // cut-outs. The camera-facing side has to be the lit side; the rim comes from the
            // character shader instead.
            //
            // Yaw 300 rather than 335, and this is what put a fighter's shadow back on the deck.
            // Every shadow setting in the project was already right — soft shadows, strength 0.58,
            // a 25 m shadow distance, an arena shader that samples the main light and a character
            // shader that casts — and there was still no shadow to see, because of where this
            // vector pointed rather than because of any of them.
            //
            // At 335 the light travelled (-0.30, -0.71, +0.64): 0.42 along -x for every metre of
            // height, and 0.91 along +z. The deck ends at z 1.1, so everything above 1.21 m threw
            // its shadow clean off the far edge into the river, and the metre-wide strip that was
            // left lay directly behind the fighter — seen almost edge-on from a camera at 1.15 and
            // occluded by the fighter's own legs.
            //
            // At 300 it travels (-0.61, -0.71, +0.35): 0.87 along -x and 0.50 along +z. A 1.7 m
            // figure lays its shadow to (-1.47, +0.85), which is on the boards with 25 cm to spare
            // and broadside to the lens. The constraint that chose 335 is untouched — the light
            // still arrives from -z, so the lit side is still the side facing the camera. What
            // changes is the azimuth off the camera axis, 25° to 60°, which is an ordinary
            // three-quarter key and models a figure better than a near-frontal one did.
            sun.transform.rotation = Quaternion.Euler(45f, 300f, 0f);
            RenderSettings.sun = light;
        }

        // Warm bounce from low and in front, so the shadow side of a fighter is dark rather
        // than black. Deliberately weak — it is a fill, and a fill that competes with the key
        // flattens exactly what the key was for.
        GameObject fill = GameObject.Find("FillLight");
        if (fill == null)
            fill = new GameObject("FillLight");
        var fillLight = fill.GetComponent<Light>();
        if (fillLight == null)
            fillLight = fill.AddComponent<Light>();
        fillLight.type = LightType.Directional;
        fillLight.color = new Color(1f, 0.72f, 0.45f);
        fillLight.intensity = 0.35f;
        fillLight.shadows = LightShadows.None; // a fill that casts shadows is a second key light
        fill.transform.rotation = Quaternion.Euler(-14f, 150f, 0f); // opposite the key, from below

        // Ambient dropped hard from what an overcast scene wanted. Strong ambient is what had
        // flattened the frame into the top half of the histogram: with no dark end there is no
        // contrast to grade, and every surface reads as the same plastic.
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.58f, 0.62f, 0.64f);
        RenderSettings.ambientEquatorColor = new Color(0.42f, 0.46f, 0.41f);
        RenderSettings.ambientGroundColor = new Color(0.14f, 0.15f, 0.13f);

        // Linear, and starting well past the fighters. Exponential fog touches everything from
        // the first metre, which is what put haze on the arena itself and cost it all its
        // contrast. Linear gives an exact answer to "where does the mist begin": nothing nearer
        // than 16 units — the fighters, the deck, the near water — receives any fog at all, and
        // the mist becomes a job the background does.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 16f;
        RenderSettings.fogEndDistance = 50f;
        RenderSettings.fogColor = SkyColor;
    }

    /// <summary>
    /// Add a volume override that survives a save. VolumeProfile.Add builds the component in
    /// memory only; without AddObjectToAsset it is dropped the moment the profile reloads —
    /// which is exactly why an entire grading setup silently did nothing for several rounds,
    /// leaving a profile on disk holding nothing but its own name.
    /// </summary>
    private static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
    {
        T component = profile.Add<T>(true);
        component.hideFlags = HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }

    /// <summary>
    /// Global post-processing volume. Raw sRGB output is the single biggest thing separating
    /// this from a graded game frame: the grade pulls saturation down, cools the whole image
    /// and pushes the shadows toward cyan, and a little bloom and vignette bind it together.
    /// </summary>
    private static void CreatePostProcessing()
    {
        const string profilePath = "Assets/Fight/Arena/PP_Fight.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
        }

        // Rebuild from scratch so re-running Setup never stacks duplicate overrides.
        foreach (VolumeComponent stale in profile.components.ToArray())
            Object.DestroyImmediate(stale, true);
        profile.components.Clear();

        // ACES. Its filmic curve is what puts a real toe and shoulder on the image; Neutral
        // left the frame sitting flat in the top half of the histogram with no black point.
        Tonemapping tone = AddOverride<Tonemapping>(profile);
        tone.mode.overrideState = true;
        tone.mode.value = TonemappingMode.ACES;

        ColorAdjustments grade = AddOverride<ColorAdjustments>(profile);
        grade.postExposure.overrideState = true;
        grade.postExposure.value = 1.35f; // ACES pulls midtones down; this puts them back
        grade.contrast.overrideState = true;
        grade.contrast.value = 14f;
        // Only a little. Draining the environment to near-monochrome was the overcast plan;
        // a frame with this much contrast keeps its colour separation without help.
        grade.saturation.overrideState = true;
        grade.saturation.value = -8f;
        grade.colorFilter.overrideState = true;
        grade.colorFilter.value = new Color(0.99f, 1f, 0.99f);

        WhiteBalance balance = AddOverride<WhiteBalance>(profile);
        balance.temperature.overrideState = true;
        balance.temperature.value = -10f;

        // The shadow end is pulled down as well as toward cyan. This is what returns black to
        // the frame: without it the darkest pixel in the image sat around 55% grey and nothing
        // in the scene could read as being in shadow at all.
        ShadowsMidtonesHighlights split = AddOverride<ShadowsMidtonesHighlights>(profile);
        split.shadows.overrideState = true;
        split.shadows.value = new Vector4(0.80f, 0.86f, 0.93f, 0f);
        split.midtones.overrideState = true;
        split.midtones.value = new Vector4(0.98f, 1.0f, 1.0f, 0f);
        split.highlights.overrideState = true;
        split.highlights.value = new Vector4(1.0f, 1.0f, 1.0f, 0f);

        Bloom bloom = AddOverride<Bloom>(profile);
        bloom.intensity.overrideState = true;
        bloom.intensity.value = 0.35f;
        bloom.threshold.overrideState = true;
        bloom.threshold.value = 1.0f;

        Vignette vignette = AddOverride<Vignette>(profile);
        vignette.intensity.overrideState = true;
        vignette.intensity.value = 0.25f;
        vignette.color.overrideState = true;
        vignette.color.value = new Color(0.04f, 0.06f, 0.08f);
        vignette.smoothness.overrideState = true;
        vignette.smoothness.value = 0.55f;

        // Gaussian rather than Bokeh: on a phone the cheap blur buys the same "fighters sharp,
        // background soft" read for a fraction of the cost. The fighters are 6-8.5 m out and
        // the near forest starts at 14, so the blur begins in the gap between them.
        DepthOfField dof = AddOverride<DepthOfField>(profile);
        dof.mode.overrideState = true;
        dof.mode.value = DepthOfFieldMode.Gaussian;
        dof.gaussianStart.overrideState = true;
        dof.gaussianStart.value = 12f;
        dof.gaussianEnd.overrideState = true;
        dof.gaussianEnd.value = 30f;

        // Cheapest effect on the list with the best return: hides banding in the sky and fog
        // gradients, which is exactly where 8-bit output falls apart.
        FilmGrain grain = AddOverride<FilmGrain>(profile);
        grain.type.overrideState = true;
        grain.type.value = FilmGrainLookup.Thin1;
        grain.intensity.overrideState = true;
        grain.intensity.value = 0.15f;

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        var go = new GameObject("PostProcessing");
        Volume volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;
        go.AddComponent<FightBootstrap>(); // lifts the mobile frame cap off 30

        Camera cam = Camera.main;
        if (cam != null)
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
    }

    /// <summary>
    /// The cheapest work in the scene with the highest return: a still frame is dead however
    /// good the geometry is. Drifting mist banks, falling bamboo leaves, motes hanging in the
    /// air and rings spreading on the water, all off two generated sprites.
    /// </summary>
    private static void CreateEffects()
    {
        Texture2D puff = SoftSprite("fx_puff", r => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 0.05f, r)));
        Texture2D ring = SoftSprite("fx_ring", r => (1f - Mathf.SmoothStep(0f, 0.16f, Mathf.Abs(r - 0.8f)))
                                                    * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 0.85f, r)));

        // Mist banks crossing the frame. Alpha is deliberately tiny: a dozen sprites this size
        // overlap several deep, so what looks like nothing on one particle is a white sheet over
        // the whole frame once they stack. Judge this in a build, never in an editor render —
        // particle systems do not simulate outside play mode, so an editor capture shows a
        // scene with no mist in it at all and invites exactly that mistake.
        // Kept behind the fight line as well: mist between the lens and the fighters is haze on
        // the subject, which is the one place this scene cannot afford it.
        // Sitting in the gap between the bridge and the near grove — z 6 to 15 — which is
        // exactly where the global fog no longer reaches. The arena is out of the mist now, so
        // the mist has to be put back deliberately, behind it, or the planes stop separating.
        ParticleSystem mist = NewParticles("FX_Mist", new Vector3(0f, 0.6f, 10f));
        ParticleSystem.MainModule mm = mist.main;
        mm.startLifetime = new ParticleSystem.MinMaxCurve(22f, 38f);
        mm.startSpeed = 0f;
        mm.startSize = new ParticleSystem.MinMaxCurve(6f, 12f);
        mm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        mm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.86f, 0.90f, 0.88f, 0.05f));
        mm.maxParticles = 12;
        mm.prewarm = true;
        mm.loop = true;
        ParticleSystem.EmissionModule me = mist.emission;
        me.rateOverTime = 0.45f;
        ParticleSystem.ShapeModule ms = mist.shape;
        ms.shapeType = ParticleSystemShapeType.Box;
        ms.scale = new Vector3(30f, 4f, 11f);
        ParticleSystem.VelocityOverLifetimeModule mv = mist.velocityOverLifetime;
        mv.enabled = true;
        // All three axes, always. Unity requires the velocity curves to share a mode, and
        // leaving z at its default constant against two ranges logs an error every frame per
        // system — thousands of lines in the player log for a value that was meant to be zero.
        mv.x = new ParticleSystem.MinMaxCurve(-0.35f, -0.08f);
        mv.y = new ParticleSystem.MinMaxCurve(-0.01f, 0.04f);
        mv.z = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
        Fade(mist, 0.25f);
        SetParticleMaterial(mist, "Assets/Fight/Arena/M_FxMist.mat", puff, Color.white);

        // Bamboo leaves coming down. Slow, spinning, drifting with the same wind the shader uses.
        ParticleSystem leaves = NewParticles("FX_Leaves", new Vector3(0f, 5f, 1.5f));
        ParticleSystem.MainModule lm = leaves.main;
        lm.startLifetime = new ParticleSystem.MinMaxCurve(9f, 15f);
        lm.startSpeed = 0f;
        lm.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.2f);
        lm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        lm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.60f, 0.34f, 1f));
        lm.maxParticles = 60;
        lm.prewarm = true;
        ParticleSystem.EmissionModule le = leaves.emission;
        le.rateOverTime = 4f;
        ParticleSystem.ShapeModule ls = leaves.shape;
        ls.shapeType = ParticleSystemShapeType.Box;
        ls.scale = new Vector3(15f, 3f, 8f);
        ParticleSystem.VelocityOverLifetimeModule lv = leaves.velocityOverLifetime;
        lv.enabled = true;
        lv.x = new ParticleSystem.MinMaxCurve(-0.9f, -0.3f);
        lv.y = new ParticleSystem.MinMaxCurve(-0.55f, -0.25f);
        lv.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        ParticleSystem.RotationOverLifetimeModule lr = leaves.rotationOverLifetime;
        lr.enabled = true;
        lr.z = new ParticleSystem.MinMaxCurve(Mathf.Deg2Rad * 40f, Mathf.Deg2Rad * 150f);
        ParticleSystem.NoiseModule ln = leaves.noise;
        ln.enabled = true;
        ln.strength = 0.4f;
        ln.frequency = 0.35f;
        var leafTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Fight/Arena/fx_leaf.png");
        SetParticleMaterial(leaves, "Assets/Fight/Arena/M_FxLeaf.mat", leafTex, Color.white);

        // Motes hanging in the damp air just in front of the lens. Nearly still — they read as
        // depth rather than as motion.
        ParticleSystem motes = NewParticles("FX_Motes", new Vector3(0f, 1.3f, -2f));
        ParticleSystem.MainModule om = motes.main;
        om.startLifetime = new ParticleSystem.MinMaxCurve(6f, 12f);
        om.startSpeed = 0f;
        om.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.035f);
        om.startColor = new ParticleSystem.MinMaxGradient(new Color(0.95f, 0.97f, 1f, 0.5f));
        om.maxParticles = 40;
        om.prewarm = true;
        ParticleSystem.EmissionModule oe = motes.emission;
        oe.rateOverTime = 5f;
        ParticleSystem.ShapeModule os = motes.shape;
        os.shapeType = ParticleSystemShapeType.Box;
        os.scale = new Vector3(11f, 3f, 5f);
        ParticleSystem.VelocityOverLifetimeModule ov = motes.velocityOverLifetime;
        ov.enabled = true;
        ov.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.02f);
        ov.y = new ParticleSystem.MinMaxCurve(-0.04f, 0.08f);
        ov.z = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
        Fade(motes, 0.3f);
        SetParticleMaterial(motes, "Assets/Fight/Arena/M_FxMote.mat", puff, Color.white);

        // The river's speed, written where the surface reads it. Downstream is world +Z: the
        // channel runs along Z and the camera looks down it, so that is also away from the camera.
        var water = AssetDatabase.LoadAssetAtPath<Material>("Assets/Fight/Arena/M_ArenaWater.mat");
        if (water != null)
        {
            water.SetFloat("_FlowSpeed", RiverFlowSpeed);
            water.SetVector("_FlowDir", new Vector4(0f, 1f, 0f, 0f));
            EditorUtility.SetDirty(water);
            AssetDatabase.SaveAssets();
        }
        else
        {
            Debug.LogError("FightSceneSetup: no water material — the river will run at whatever the shader defaults to.");
        }

        // Rings spreading where drips hit the water. Horizontal billboards, not camera-facing
        // ones: a ring that turns to face a near-level camera stops looking like it is lying on
        // the surface, which is the entire point of it.
        ParticleSystem drips = NewParticles("FX_Drips", new Vector3(0f, BambooArena.WaterY + 0.02f, 6f));
        ParticleSystem.MainModule dm = drips.main;
        dm.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
        dm.startSpeed = 0f;
        dm.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
        dm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.82f, 0.86f, 0.87f, 0.55f));
        dm.maxParticles = 30;
        ParticleSystem.EmissionModule de = drips.emission;
        de.rateOverTime = 4f;
        ParticleSystem.ShapeModule ds = drips.shape;
        ds.shapeType = ParticleSystemShapeType.Box;
        // A band along the bridge rather than the whole channel: rings spreading where the piles
        // stand read as the water reacting to them, and rings out in the middle of nowhere read
        // as rain that is not falling anywhere else.
        ds.scale = new Vector3(12f, 0f, 3.5f);
        ParticleSystem.SizeOverLifetimeModule dsz = drips.sizeOverLifetime;
        dsz.enabled = true;
        dsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.15f, 1f, 1f));
        // Carried downstream. A ring that spreads but stays put says the water is standing; the
        // surface itself is advected in the shader, but at this view angle a current running away
        // from the camera barely projects, so the things lying on it have to carry the reading.
        ParticleSystem.VelocityOverLifetimeModule dv = drips.velocityOverLifetime;
        dv.enabled = true;
        dv.space = ParticleSystemSimulationSpace.World;
        dv.z = new ParticleSystem.MinMaxCurve(RiverFlowSpeed);
        Fade(drips, 0.12f);
        drips.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        SetParticleMaterial(drips, "Assets/Fight/Arena/M_FxRipple.mat", ring, Color.white);

        // Steam lying flat on the water. Horizontal billboards again, and very wide and thin:
        // it has to look like it is on the surface, not standing up out of it.
        ParticleSystem steam = NewParticles("FX_Steam", new Vector3(0f, BambooArena.WaterY + 0.1f, 4f));
        ParticleSystem.MainModule sm = steam.main;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(14f, 24f);
        sm.startSpeed = 0f;
        sm.startSize = new ParticleSystem.MinMaxCurve(3f, 7f);
        sm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        sm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.84f, 0.88f, 0.86f, 0.07f));
        sm.maxParticles = 10;
        sm.prewarm = true;
        ParticleSystem.EmissionModule se = steam.emission;
        se.rateOverTime = 0.6f;
        ParticleSystem.ShapeModule ss = steam.shape;
        ss.shapeType = ParticleSystemShapeType.Box;
        ss.scale = new Vector3(20f, 0f, 16f);
        ParticleSystem.VelocityOverLifetimeModule sv = steam.velocityOverLifetime;
        sv.enabled = true;
        sv.space = ParticleSystemSimulationSpace.World;
        // The sideways drift is wind and stays; the new z is the river dragging the sheet along
        // with it. Wind and current are not required to agree, and it reads better when they do
        // not — the steam then slides diagonally across a surface going straight downstream.
        sv.x = new ParticleSystem.MinMaxCurve(-0.25f, -0.05f);
        sv.y = new ParticleSystem.MinMaxCurve(0f, 0.015f);
        sv.z = new ParticleSystem.MinMaxCurve(RiverFlowSpeed * 0.7f, RiverFlowSpeed * 1.1f);
        Fade(steam, 0.3f);
        steam.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        SetParticleMaterial(steam, "Assets/Fight/Arena/M_FxSteam.mat", puff, Color.white);
    }

    /// <summary>Fade a system in over the first <paramref name="attack"/> of its life and out
    /// again at the end, so nothing ever pops into or out of frame.</summary>
    private static void Fade(ParticleSystem system, float attack)
    {
        ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, attack),
                new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f),
            });
        fade.color = gradient;
    }

    /// <summary>A generated round sprite. Mist puffs and ripple rings are two radial gradients;
    /// painting, importing and shipping textures for that would be work in exchange for nothing.
    /// </summary>
    private static Texture2D SoftSprite(string file, System.Func<float, float> alphaByRadius)
    {
        const int size = 64;
        string path = $"Assets/Fight/Arena/{file}.asset";
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex == null)
        {
            tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            AssetDatabase.CreateAsset(tex, path);
        }

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.Clamp01(alphaByRadius(Mathf.Sqrt(dx * dx + dy * dy)));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(pixels);
        tex.Apply();
        EditorUtility.SetDirty(tex);
        AssetDatabase.SaveAssets();
        return tex;
    }

    private static ParticleSystem NewParticles(string name, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        return go.AddComponent<ParticleSystem>();
    }

    private static void SetParticleMaterial(ParticleSystem ps, string matPath, Texture2D texture, Color tint)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.SetFloat("_Surface", 1f); // transparent
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.SetColor("_BaseColor", tint);
        if (texture != null)
            mat.SetTexture("_BaseMap", texture);
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
    }

    private static Texture2D Icon(string name)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Fight/Icons/{name}.png");
        if (tex == null)
            Debug.LogError($"FightSceneSetup: icon {name}.png not found in Assets/Fight/Icons.");
        return tex;
    }

    private static Fighter SpawnFighter(string name, float x, AnimatorController controller, Color suitColor)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = name;
        go.transform.position = new Vector3(x, 0, 0);

        foreach (SkinnedMeshRenderer r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                mats[i] = TintedMaterial(mats[i], name, suitColor);
            r.sharedMaterials = mats;
        }

        Animator animator = go.GetComponent<Animator>();
        if (animator == null)
            animator = go.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        // Default culling stops writing bone transforms whenever the renderer is judged invisible,
        // which freezes a fighter mid-pose while its state machine keeps running. Two characters
        // that are on screen for the whole match are not worth that risk.
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // The reflection camera renders this layer and nothing else, which is what keeps a
        // planar reflection down to three renderers instead of a second pass over the grove.
        int reflected = EnsureReflectedLayer();
        if (reflected >= 0)
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = reflected;

        GroundToFloor(go);
        // The start position names a place along the bridge; the height comes from the deck it
        // stands on, which sags. Runtime does the same on every move — this is the edit-mode
        // scene agreeing with it, so a capture shows what a player sees.
        go.transform.position += new Vector3(0f, FightRules.DeckHeight(x), 0f);
        AddBlobShadow(go);
        go.AddComponent<FootIK>();

        // One fighter kept rendering white while both suit materials were provably present in
        // the scene. Report what each renderer actually ended up with rather than guess again.
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            string mats = string.Join(", ", r.sharedMaterials.Select(m =>
                m == null ? "<null>"
                : m.HasProperty("_BaseColor") ? $"{m.name}#{ColorUtility.ToHtmlStringRGB(m.GetColor("_BaseColor"))}"
                : $"{m.name}<no _BaseColor>"));
            Debug.Log($"FightSceneSetup: {name}/{r.name} [{r.GetType().Name}] -> {mats}");
        }

        return go.AddComponent<Fighter>();
    }

    /// <summary>Per-fighter material carrying the character's own albedo, tinted so the two sides
    /// read apart. Built on a stock URP Lit rather than copied from the FBX material: the imported
    /// one renders as error magenta on the armour submesh and carries a white _EMISSION the
    /// character has no business having. Only the base map is worth keeping from it.</summary>
    private static Material TintedMaterial(Material source, string fighterName, Color tint)
    {
        if (source == null)
            return null;

        // Mikey/Character rather than URP/Lit for one reason: the rim. Against a background of
        // pale mist a lit silhouette edge is what separates a fighter from it, and separating
        // the fighters from the background is the renderer's most important job here.
        Shader shader = Shader.Find("Mikey/Character");
        if (shader == null)
        {
            Debug.LogError("FightSceneSetup: shader Mikey/Character not found (compile error?) — " +
                           "falling back to URP/Lit, so the fighters will have no rim light.");
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }

        string matPath = $"{Path.GetDirectoryName(CharacterPath).Replace('\\', '/')}/M_{fighterName}_{source.name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.shader = shader;
        mat.SetColor("_RimColor", new Color(0.86f, 0.93f, 1f));
        mat.SetFloat("_RimPower", 3.2f);
        mat.SetFloat("_RimStrength", 0.8f);
        mat.SetFloat("_SpecStrength", 0.25f);
        mat.SetFloat("_BumpScale", 1f);
        // 0.5, not 0.35. Lifted too hard the suit's whole tonal range compresses into the top
        // of the curve and the fighters come out as flat pastel plastic — readable, but toys.
        // This keeps them mid-dark tactical gear that still reads against the mist.
        mat.SetFloat("_AlbedoGamma", 0.62f);
        mat.SetFloat("_Smoothness", 0.08f);
        mat.DisableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;

        Texture baseMap = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : null;
        if (baseMap == null && source.HasProperty("_MainTex"))
            baseMap = source.GetTexture("_MainTex");
        mat.SetTexture("_BaseMap", baseMap);
        Texture2D normal = NormalMapBeside(baseMap);
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.EnableKeyword("_NORMALMAP");
        }
        mat.SetColor("_BaseColor", tint);
        // Cloth and skin, not lacquer. At 0.3 the broad specular from a warm sun swamped the
        // albedo entirely and the player rendered as a neutral white mannequin.
        mat.SetFloat("_Smoothness", 0.06f);
        Debug.Log($"FightSceneSetup: {matPath} <- {source.name} baseMap={(baseMap == null ? "<none>" : baseMap.name)}");
        AssetDatabase.SaveAssets();
        return mat;
    }

    /// <summary>The extracted set is named "&lt;mesh&gt;_&lt;id&gt;_Diffuse/Normal/Specular", so the normal map
    /// is the diffuse's neighbour. Worth the lookup: without it the character is a flat silhouette
    /// under a low sun, which is most of what read as "unprofessional".</summary>
    private static Texture2D NormalMapBeside(Texture baseMap)
    {
        if (baseMap == null)
            return null;
        string diffusePath = AssetDatabase.GetAssetPath(baseMap);
        if (string.IsNullOrEmpty(diffusePath) || !diffusePath.Contains("_Diffuse"))
            return null;

        string normalPath = diffusePath.Replace("_Diffuse", "_Normal");
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        if (normal == null)
            return null;

        var importer = (TextureImporter)AssetImporter.GetAtPath(normalPath);
        if (importer != null && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
        return normal;
    }

    /// <summary>
    /// A soft dark oval on the boards under each fighter, on top of the real cast shadow. The
    /// cast shadow alone leaves a gap directly beneath the body when the key is steep, and that
    /// gap is exactly what makes a character read as hovering. Static, not a component: nothing
    /// in this game leaves the ground, so a child quad at deck level stays correct for free.
    /// </summary>
    private static void AddBlobShadow(GameObject fighter)
    {
        const string matPath = "Assets/Fight/Arena/M_BlobShadow.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f); // alpha blend
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 50; // under the water ripples
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Fight/Arena/T_Blob.asset"));
        mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0.55f));

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "BlobShadow";
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
        quad.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        quad.transform.SetParent(fighter.transform, false);
        // Lifted a centimetre off the boards: coplanar with the deck it z-fights, and the
        // fighter's own transform sits wherever GroundToFloor left it, so this is computed in
        // world space and converted back rather than assumed to be zero.
        quad.transform.position = new Vector3(
            fighter.transform.position.x,
            BambooArena.DeckHeight(fighter.transform.position.x) + 0.012f,
            fighter.transform.position.z);
        quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        quad.transform.localScale = new Vector3(1.15f, 0.8f, 1f);
        // Deliberately left on Default rather than following the fighter into the reflected
        // layer. This is a cheat tuned for one viewing angle — the main camera's. Mirrored, it
        // becomes a black oval hanging under the deck with nothing casting it.
    }

    /// <summary>Both reflection layers, created here rather than asked of the user so a fresh
    /// clone of the project sets itself up. Two of them because the mobile tier reflects only
    /// the near one.</summary>
    private static int EnsureReflectedLayer()
    {
        EnsureLayer(Mikey.Fight.WaterReflection.FarLayerName);
        return EnsureLayer(Mikey.Fight.WaterReflection.LayerName);
    }

    private static int EnsureLayer(string name)
    {
        int existing = LayerMask.NameToLayer(name);
        if (existing >= 0)
            return existing;

        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets.Length == 0)
        {
            Debug.LogError($"FightSceneSetup: cannot open TagManager.asset — layer '{name}' not created.");
            return -1;
        }

        var tagManager = new SerializedObject(assets[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++) // 0-7 are Unity's own
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(layer.stringValue))
                continue;
            layer.stringValue = name;
            tagManager.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log($"FightSceneSetup: created layer {i} '{name}'.");
            return i;
        }

        Debug.LogError($"FightSceneSetup: no free user layer for '{name}'.");
        return -1;
    }

    /// <summary>Lift an object so the bottom of its combined renderer bounds sits on the ground —
    /// the terrain surface if one exists, else y=0. Model pivots vary (the UBC body pivots at
    /// the hips, generated GLBs at their centre), so never trust the pivot.</summary>
    private static void GroundToFloor(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return;

        // Renderer.bounds on a skinned mesh is a padded box sized for any pose the clips might
        // reach, not for the mesh as it stands. On this character it hangs 12 cm below the soles,
        // and grounding against it floated both fighters by exactly that. Bake the bind pose and
        // measure real vertices instead.
        float lowest = float.PositiveInfinity;
        foreach (Renderer r in renderers)
        {
            if (r is SkinnedMeshRenderer skin && skin.sharedMesh != null)
            {
                var baked = new Mesh();
                skin.BakeMesh(baked); // no scale — the transform below applies it
                foreach (Vector3 v in baked.vertices)
                    lowest = Mathf.Min(lowest, skin.transform.TransformPoint(v).y);
                Object.DestroyImmediate(baked);
            }
            else
            {
                lowest = Mathf.Min(lowest, r.bounds.min.y);
            }
        }
        if (float.IsInfinity(lowest))
            return;

        float groundY = 0f;
        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null)
            groundY = terrain.SampleHeight(go.transform.position) + terrain.transform.position.y;

        Vector3 p = go.transform.position;
        p.y += groundY - lowest;
        go.transform.position = p;
    }
}

