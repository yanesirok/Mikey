using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Mikey.Fight;

/// <summary>
/// One-click builder for the FightSandbox scene: configures the Quaternius imports
/// (Universal Base Character + Universal Animation Library, both Humanoid so the
/// animations retarget onto the character), generates the fighter AnimatorController,
/// and places a light-suited player vs a dark-suited AI opponent with touch controls.
/// Re-runnable — regenerates the controller/materials and replaces the fighters.
/// Menu: Mikey ▸ Setup Fight Scene. Also callable via -executeMethod FightSceneSetup.Setup.
/// </summary>
public static class FightSceneSetup
{
    private const string CharacterPath = "Assets/Characters/Karate/Superhero_Male_FullBody.fbx";
    private const string AnimationsPath = "Assets/Characters/Karate/UAL1_Standard.fbx";
    private const string Animations2Path = "Assets/Characters/Karate/UAL2_Standard.fbx";
    private const string ControllerPath = "Assets/Fight/Fighter.controller";
    private const string ScenePath = "Assets/Scenes/FightSandbox.unity";

    private static readonly Color PlayerSuit = new Color(0.16f, 0.19f, 0.40f); // deep indigo
    private static readonly Color EnemySuit = new Color(0.44f, 0.12f, 0.12f);  // dark crimson

    [MenuItem("Mikey/Setup Fight Scene")]
    public static void Setup()
    {
        ConfigureImport(CharacterPath);
        ConfigureImport(AnimationsPath);
        ConfigureImport(Animations2Path);
        var normalImporter = (TextureImporter)AssetImporter.GetAtPath("Assets/Characters/Karate/T_Superhero_Male_Normal.png");
        if (normalImporter != null && normalImporter.textureType != TextureImporterType.NormalMap)
        {
            normalImporter.textureType = TextureImporterType.NormalMap;
            normalImporter.SaveAndReimport();
        }
        AnimatorController controller = BuildController();
        if (controller == null)
            return; // clips missing — error already logged, scene untouched
        PopulateScene(controller);
        Debug.Log("FightSceneSetup: done. Open FightSandbox and press Play (A/D move, J punch, K kick, hold S block; on device — touch).");
    }

    /// <summary>Humanoid rig (for retargeting) and looped *_Loop clips.</summary>
    private static void ConfigureImport(string fbxPath)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        if (importer == null)
        {
            Debug.LogError($"FightSceneSetup: no importer at {fbxPath} — is the model in place?");
            return;
        }

        importer.animationType = ModelImporterAnimationType.Human;

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        foreach (ModelImporterClipAnimation clip in clips)
            clip.loopTime = clip.name.EndsWith("_Loop");
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    /// <summary>Find a clip by name, tolerating Blender's "Armature|" take prefix.</summary>
    private static AnimationClip Clip(string fbxPath, string name)
    {
        return AssetDatabase.LoadAllAssetsAtPath(fbxPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == name || c.name.EndsWith("|" + name));
    }

    private static AnimatorController BuildController()
    {
        AnimationClip idleClip = Clip(AnimationsPath, "Idle_Loop");
        AnimationClip walkClip = Clip(AnimationsPath, "Walk_Loop");
        AnimationClip jabClip = Clip(AnimationsPath, "Punch_Jab");
        AnimationClip crossClip = Clip(AnimationsPath, "Punch_Cross");
        AnimationClip hitClip = Clip(AnimationsPath, "Hit_Chest");
        AnimationClip deathClip = Clip(AnimationsPath, "Death01");
        // ponytail: the free UAL2 has no unarmed block — shield-guard hold + sword parry
        // read fine as an unarmed guard; swap for Mixamo block anims when they arrive.
        AnimationClip blockingClip = Clip(Animations2Path, "Idle_Shield_Loop");
        AnimationClip blockHitClip = Clip(Animations2Path, "Sword_Block");

        var missing = new System.Collections.Generic.List<string>();
        if (idleClip == null) missing.Add("Idle_Loop");
        if (walkClip == null) missing.Add("Walk_Loop");
        if (jabClip == null) missing.Add("Punch_Jab");
        if (crossClip == null) missing.Add("Punch_Cross");
        if (hitClip == null) missing.Add("Hit_Chest");
        if (deathClip == null) missing.Add("Death01");
        if (blockingClip == null) missing.Add("Idle_Shield_Loop");
        if (blockHitClip == null) missing.Add("Sword_Block");
        if (missing.Count > 0)
        {
            Debug.LogError($"FightSceneSetup: animation clips not found: {string.Join(", ", missing)}. If Unity is still importing, wait and run Mikey ▸ Setup Fight Scene again.");
            return null;
        }

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

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        AnimatorState idle = sm.AddState("Idle");
        idle.motion = idleClip;
        AnimatorState walk = sm.AddState("Walk");
        walk.motion = walkClip;
        AnimatorState punch = sm.AddState("Punch");
        punch.motion = jabClip;
        AnimatorState punchB = sm.AddState("PunchB");
        punchB.motion = crossClip;
        // ponytail: no kick anim in the free CC0 library — Punch_Cross stands in until
        // a Mixamo karate kick (mae/mawashi geri) is imported, then just swap the motion.
        AnimatorState kick = sm.AddState("Kick");
        kick.motion = crossClip;
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

        foreach (string stale in new[] { "Fighter_Player", "Fighter_Enemy", "Player", "Enemy", "TouchControls", "Backdrop", "FX_Leaves", "FX_Embers", "Torii", "MapleTree", "MapleTree_Mid", "MapleTree_Far", "Vegetation", "Terrain", "Arena", "ArenaProps" })
        {
            GameObject old = GameObject.Find(stale);
            if (old != null)
                Object.DestroyImmediate(old);
        }

        CreateSky();
        CreateTerrain();
        float floorY = CreateProps();
        CreateEffects();

        Fighter player = SpawnFighter("Player", -1.2f, controller, PlayerSuit, floorY);
        Fighter enemy = SpawnFighter("Enemy", 1.2f, controller, EnemySuit, floorY);
        PlayerFighterInput input = player.gameObject.AddComponent<PlayerFighterInput>();
        enemy.gameObject.AddComponent<EnemyFighterAI>();
        player.Opponent = enemy;
        enemy.Opponent = player;
        input.Touch = CreateTouchControls();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
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

    /// <summary>Real 3D golden-hour sky: Unity's procedural skybox, sun bound to the scene light.</summary>
    private static void CreateSky()
    {
        const string matPath = "Assets/Fight/Arena/M_Sky.mat";
        var sky = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (sky == null)
        {
            sky = new Material(Shader.Find("Skybox/Procedural"));
            AssetDatabase.CreateAsset(sky, matPath);
        }
        sky.SetFloat("_SunSize", 0.06f);
        sky.SetFloat("_AtmosphereThickness", 1.35f);        // heavier atmosphere = warm dusk
        sky.SetColor("_SkyTint", new Color(0.9f, 0.55f, 0.35f));
        sky.SetColor("_GroundColor", new Color(0.35f, 0.28f, 0.20f));
        sky.SetFloat("_Exposure", 1.25f);
        RenderSettings.skybox = sky;

        GameObject sun = GameObject.Find("Directional Light");
        if (sun != null)
            RenderSettings.sun = sun.GetComponent<Light>();
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Real 3D ground: a 200×200 Unity Terrain — flat golden fighting plateau in the middle,
    /// Perlin hills further out, and a mountain ring on the horizon that the bluish fog
    /// fades into GoT-style misty ranges. Replaces both the old floor plane and the photo backdrop.
    /// </summary>
    private static void CreateTerrain()
    {
        const int res = 257;
        const float sizeXZ = 200f;
        const float sizeY = 35f;

        var data = new TerrainData();
        data.heightmapResolution = res;
        data.size = new Vector3(sizeXZ, sizeY, sizeXZ);

        var heights = new float[res, res];
        for (int zi = 0; zi < res; zi++)
        {
            for (int xi = 0; xi < res; xi++)
            {
                // World offsets from terrain corner; arena centre sits at terrain centre.
                float wx = xi / (float)(res - 1) * sizeXZ - sizeXZ / 2f;
                float wz = zi / (float)(res - 1) * sizeXZ - sizeXZ / 2f;

                // Flat fighting plateau around the origin.
                float flat = Mathf.Max(Mathf.Abs(wx) - 24f, wz - 16f, -wz - 12f, 0f);
                float t = Mathf.Clamp01(flat / 30f); // 0 = plateau, 1 = far country

                float hills = Mathf.PerlinNoise(wx * 0.02f + 3.7f, wz * 0.02f + 8.1f) * 0.12f * t;
                float dist = Mathf.Sqrt(wx * wx + wz * wz);
                float mountains = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((dist - 70f) / 30f))
                    * (0.35f + Mathf.PerlinNoise(wx * 0.03f, wz * 0.03f) * 0.5f);

                heights[zi, xi] = hills + mountains;
            }
        }
        data.SetHeights(0, 0, heights);

        // Single golden-grass layer from a tiny generated texture.
        string texPath = "Assets/Fight/Arena/T_TerrainGold.png";
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) == null)
        {
            var tex = new Texture2D(4, 4);
            var gold = new Color(0.72f, 0.58f, 0.32f);
            for (int i = 0; i < 16; i++)
                tex.SetPixel(i % 4, i / 4, gold);
            tex.Apply();
            System.IO.File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);
        }
        string layerPath = "Assets/Fight/Arena/TL_Gold.terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
        if (layer == null)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, layerPath);
        }
        layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        layer.tileSize = new Vector2(50f, 50f);
        data.terrainLayers = new[] { layer };

        AssetDatabase.CreateAsset(data, "Assets/Fight/Arena/ArenaTerrain.asset");

        GameObject terrain = Terrain.CreateTerrainGameObject(data);
        terrain.name = "Terrain";
        terrain.transform.position = new Vector3(-sizeXZ / 2f, 0f, -sizeXZ / 2f); // plateau surface = y 0
        AssetDatabase.SaveAssets();
    }

    /// <summary>Real 3D mid-ground: dojo, fighting platform, post lanterns, school banners,
    /// cedar fence, bokken rack, mossy boulders, red maples and swaying pampas grass —
    /// Higgsfield-generated GLB props imported via glTFast. Missing props are skipped with
    /// a warning. Returns the platform-top world Y (0 if no platform) — the fighters' floor.</summary>
    private static float CreateProps()
    {
        Transform propsRoot = new GameObject("ArenaProps").transform;

        // Legacy untextured meshes get a flat painterly tint; the new dojo-set GLBs are
        // generated with baked textures, so they pass tint: null and keep their materials.
        var crimson = new Color(0.55f, 0.10f, 0.08f); // maples
        var stone = new Color(0.42f, 0.44f, 0.40f);   // mossy granite

        // Platform first: its top becomes the fighters' floor. 10 m wide so the
        // ±FightRules.ArenaHalfWidth (4 m) movement range keeps a margin from the edge.
        GameObject platform = PlaceProp("platform.glb", "Platform", Vector3.zero, 10f, 0f, null, propsRoot, normalizeByWidth: true);
        float floorY = PropTopY(platform);

        PlaceProp("dojo.glb", "Dojo", new Vector3(2f, 0f, 18f), 8f, 180f, null, propsRoot);
        PlaceProp("lantern.glb", "Lantern_L", new Vector3(-6.5f, 0f, 2.5f), 2.6f, 15f, null, propsRoot);
        PlaceProp("lantern.glb", "Lantern_R", new Vector3(6.5f, 0f, 2.5f), 2.6f, -15f, null, propsRoot);

        // School banners at the platform corners, poles just off the deck.
        PlaceProp("banner.glb", "Banner_0", new Vector3(-5.6f, 0f, -4.6f), 3.5f, 20f, null, propsRoot);
        PlaceProp("banner.glb", "Banner_1", new Vector3(5.6f, 0f, -4.6f), 3.5f, -20f, null, propsRoot);
        PlaceProp("banner.glb", "Banner_2", new Vector3(-5.6f, 0f, 5.2f), 3.5f, 160f, null, propsRoot);
        PlaceProp("banner.glb", "Banner_3", new Vector3(5.6f, 0f, 5.2f), 3.5f, -160f, null, propsRoot);

        // Fence flanks a clear central path from the platform to the dojo doors.
        PlaceProp("fence.glb", "Fence_0", new Vector3(-13f, 0f, 9f), 1.2f, 8f, null, propsRoot);
        PlaceProp("fence.glb", "Fence_1", new Vector3(-8.5f, 0f, 9.5f), 1.2f, 4f, null, propsRoot);
        PlaceProp("fence.glb", "Fence_2", new Vector3(8.5f, 0f, 9.5f), 1.2f, -4f, null, propsRoot);
        PlaceProp("fence.glb", "Fence_3", new Vector3(13f, 0f, 9f), 1.2f, -8f, null, propsRoot);

        PlaceProp("bokken_rack.glb", "BokkenRack", new Vector3(6.5f, 0f, 14.5f), 1.6f, 200f, null, propsRoot);

        PlaceProp("boulder.glb", "Boulder_0", new Vector3(-16f, 0f, 7f), 1.8f, 0f, stone, propsRoot);
        PlaceProp("boulder.glb", "Boulder_1", new Vector3(18f, 0f, 11f), 1.4f, 120f, stone, propsRoot);
        PlaceProp("boulder.glb", "Boulder_2", new Vector3(-21f, 0f, 15f), 2.2f, 250f, stone, propsRoot);

        PlaceProp("maple.glb", "MapleTree", new Vector3(-12f, 0f, 10f), 7f, -10f, crimson, propsRoot);
        PlaceProp("maple.glb", "MapleTree_Mid", new Vector3(16f, 0f, 20f), 5f, 140f, crimson, propsRoot);
        PlaceProp("maple.glb", "MapleTree_Far", new Vector3(-24f, 0f, 26f), 4f, 60f, crimson, propsRoot);

        var grassPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Fight/Arena/Props/grass.glb");
        if (grassPrefab == null)
        {
            Debug.LogWarning("FightSceneSetup: Props/grass.glb not found — skipping vegetation.");
            return floorY;
        }

        var vegetation = new GameObject("Vegetation");
        var gold = new Color(0.78f, 0.65f, 0.38f);

        // Near rows framing the fight, then a scattered mid-ground field.
        // All positions deterministic (pseudo-random from index) so re-runs are reproducible.
        int n = 0;
        void Clump(float x, float z, float height)
        {
            var clump = (GameObject)PrefabUtility.InstantiatePrefab(grassPrefab, vegetation.transform);
            clump.name = $"Grass_{n++}";
            NormalizeHeight(clump, height);
            clump.transform.position = new Vector3(x, 0f, z);
            clump.transform.rotation = Quaternion.Euler(0f, (n * 73f) % 360f, 0f);
            GroundToFloor(clump);
            Tint(clump, gold);
        }

        for (int i = 0; i < 12; i++) // front row, small, pushed behind the platform edge (z=5)
            Clump(-14f + i * 2.5f + (i * 7 % 3) * 0.3f, 6.5f + (i % 3) * 0.6f, 1.0f + (i % 3) * 0.15f);
        for (int i = 0; i < 12; i++) // second row
            Clump(-15f + i * 2.7f + (i * 5 % 4) * 0.3f, 8f + (i % 4) * 0.8f, 1.3f + (i % 2) * 0.2f);
        for (int i = 0; i < 30; i++) // scattered field toward the hills
        {
            float px = -35f + (i * 137f % 70f);
            float pz = 9f + (i * 89f % 26f);
            Clump(px, pz, 1.2f + (i * 31f % 10f) / 10f);
        }
        vegetation.AddComponent<GrassSway>();
        return floorY;
    }

    /// <summary>World Y of the top of an object's combined renderer bounds; 0 for null/empty.</summary>
    private static float PropTopY(GameObject go)
    {
        if (go == null)
            return 0f;
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return 0f;
        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers)
            b.Encapsulate(r.bounds);
        return b.max.y;
    }

    /// <summary>Uniformly scale an object so its renderer-bounds height equals the target.</summary>
    private static void NormalizeHeight(GameObject go, float targetHeight)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return;
        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers)
            b.Encapsulate(r.bounds);
        if (b.size.y > 0.001f)
            go.transform.localScale = go.transform.localScale * (targetHeight / b.size.y);
    }

    private static GameObject PlaceProp(string file, string name, Vector3 position, float size, float yaw, Color? tint, Transform parent = null, bool normalizeByWidth = false)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Fight/Arena/Props/{file}");
        if (prefab == null)
        {
            Debug.LogWarning($"FightSceneSetup: Props/{file} not found — skipping {name}.");
            return null;
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;

        // Normalize unknown GLB scale: measure, then scale so the chosen dimension
        // (height, or horizontal footprint for flat props like the platform) equals size.
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers)
                b.Encapsulate(r.bounds);
            float measured = normalizeByWidth ? Mathf.Max(b.size.x, b.size.z) : b.size.y;
            if (measured > 0.001f)
                go.transform.localScale = Vector3.one * (size / measured);
        }

        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        GroundToFloor(go);
        if (tint.HasValue)
            Tint(go, tint.Value);
        return go;
    }

    /// <summary>Assign a solid URP Lit tint to every renderer (generated GLBs come untextured).</summary>
    private static void Tint(GameObject go, Color color)
    {
        string matPath = $"Assets/Fight/Arena/M_Tint_{ColorUtility.ToHtmlStringRGB(color)}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.SetColor("_BaseColor", color);
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                mats[i] = mat;
            r.sharedMaterials = mats;
        }
    }

    /// <summary>Ghost-of-Tsushima mood: drifting crimson maple leaves, warm ember
    /// fireflies, and a light warm fog for depth. All built as scene particle systems.</summary>
    private static void CreateEffects()
    {
        // Wind-blown maple leaves falling across the arena.
        ParticleSystem leaves = NewParticles("FX_Leaves", new Vector3(6f, 8f, 3f));
        ParticleSystem.MainModule lm = leaves.main;
        lm.startLifetime = new ParticleSystem.MinMaxCurve(7f, 12f);
        lm.startSpeed = 0f;
        lm.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
        lm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        lm.maxParticles = 150;
        ParticleSystem.EmissionModule le = leaves.emission;
        le.rateOverTime = 10f;
        ParticleSystem.ShapeModule ls = leaves.shape;
        ls.shapeType = ParticleSystemShapeType.Box;
        ls.scale = new Vector3(24f, 1f, 8f);
        ParticleSystem.VelocityOverLifetimeModule lv = leaves.velocityOverLifetime;
        lv.enabled = true;
        lv.x = new ParticleSystem.MinMaxCurve(-1.6f, -0.7f); // steady wind to the left
        lv.y = new ParticleSystem.MinMaxCurve(-0.9f, -0.4f);
        lv.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
        ParticleSystem.RotationOverLifetimeModule lr = leaves.rotationOverLifetime;
        lr.enabled = true;
        lr.z = new ParticleSystem.MinMaxCurve(Mathf.Deg2Rad * 45f, Mathf.Deg2Rad * 160f);
        ParticleSystem.NoiseModule ln = leaves.noise;
        ln.enabled = true;
        ln.strength = 0.35f;
        ln.frequency = 0.4f;
        var leafTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Fight/Arena/fx_leaf.png");
        SetParticleMaterial(leaves, "Assets/Fight/Arena/M_FxLeaf.mat", leafTex, Color.white);

        // Warm ember fireflies drifting low, GoT dusk mood.
        ParticleSystem embers = NewParticles("FX_Embers", new Vector3(0f, 1f, 2f));
        ParticleSystem.MainModule em = embers.main;
        em.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f);
        em.startSpeed = 0f;
        em.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
        em.maxParticles = 60;
        ParticleSystem.EmissionModule ee = embers.emission;
        ee.rateOverTime = 6f;
        ParticleSystem.ShapeModule es = embers.shape;
        es.shapeType = ParticleSystemShapeType.Box;
        es.scale = new Vector3(18f, 2.5f, 6f);
        ParticleSystem.VelocityOverLifetimeModule ev = embers.velocityOverLifetime;
        ev.enabled = true;
        ev.x = new ParticleSystem.MinMaxCurve(-0.5f, -0.1f);
        ev.y = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
        ParticleSystem.NoiseModule en = embers.noise;
        en.enabled = true;
        en.strength = 0.25f;
        en.frequency = 0.6f;
        ParticleSystem.ColorOverLifetimeModule ec = embers.colorOverLifetime;
        ec.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.75f, 0.35f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.15f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        ec.color = grad;
        SetParticleMaterial(embers, "Assets/Fight/Arena/M_FxEmber.mat", null, new Color(1f, 0.7f, 0.35f));

        // Golden-hour key light.
        GameObject sun = GameObject.Find("Directional Light");
        if (sun != null)
        {
            var light = sun.GetComponent<Light>();
            light.color = new Color(1f, 0.82f, 0.6f);
            light.intensity = 1.15f;
            sun.transform.rotation = Quaternion.Euler(28f, -35f, 0f); // low warm sun
        }

        // Cool distance haze: near field stays golden, far ridges fade into misty blue (GoT).
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 25f;
        RenderSettings.fogEndDistance = 130f;
        RenderSettings.fogColor = new Color(0.62f, 0.66f, 0.74f);
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

    private static Fighter SpawnFighter(string name, float x, AnimatorController controller, Color suitColor, float floorY)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = name;
        go.transform.position = new Vector3(x, 0, 0);

        Material suit = SuitMaterial(name, suitColor);
        foreach (SkinnedMeshRenderer r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] == null || !mats[i].name.Contains("Eye"))
                    mats[i] = suit;
            r.sharedMaterials = mats;
        }

        Animator animator = go.GetComponent<Animator>();
        if (animator == null)
            animator = go.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        GroundToFloor(go);
        go.transform.position += Vector3.up * floorY; // stand on the platform deck, not the terrain
        return go.AddComponent<Fighter>();
    }

    /// <summary>Solid fabric color + the body normal map — reads as a fitted suit, not bare skin.</summary>
    private static Material SuitMaterial(string fighterName, Color color)
    {
        string matPath = $"Assets/Characters/Karate/M_Suit_{fighterName}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", 0.3f);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Characters/Karate/T_Superhero_Male_Normal.png");
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.EnableKeyword("_NORMALMAP");
        }
        AssetDatabase.SaveAssets();
        return mat;
    }

    /// <summary>Lift an object so the bottom of its combined renderer bounds sits on the ground —
    /// the terrain surface if one exists, else y=0. Model pivots vary (the UBC body pivots at
    /// the hips, generated GLBs at their centre), so never trust the pivot.</summary>
    private static void GroundToFloor(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return;
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers)
            bounds.Encapsulate(r.bounds);

        float groundY = 0f;
        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null)
            groundY = terrain.SampleHeight(go.transform.position) + terrain.transform.position.y;

        Vector3 p = go.transform.position;
        p.y += groundY - bounds.min.y;
        go.transform.position = p;
    }
}
