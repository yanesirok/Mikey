# Arena Dojo Assets (GoT style) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **NOTE:** Tasks 1–4 are interactive Higgsfield MCP generation with a user-approval gate after every image — they cannot be delegated to subagents and must run inline in the main session. Only Task 5 (code) is a normal coding task.

**Goal:** Replace the torii gate with a dojo-themed prop set (7 Higgsfield photo→3D GLB assets) and place them in the FightSandbox arena, Ghost of Tsushima style, zero religious elements.

**Architecture:** Two master concept images lock the art direction; each prop is then generated as an isolated ¾-view photo on a neutral background and converted to GLB via `generate_3d`. `FightSceneSetup.CreateProps` places everything deterministically; the fighting platform's top becomes the fighters' floor.

**Tech Stack:** Higgsfield MCP (`generate_image`, `generate_3d`, `models_explore`), Unity 6 + glTFast, C# editor scripting.

## Global Constraints

- **No religion, no statues** — every image prompt ends with the negative block: `no religious symbols, no shrine elements, no statues, no torii, no shimenawa ropes, no text, no people`
- **Style block** (embed verbatim in every *master* prompt): `golden hour sunset light, warm amber rim lighting, soft atmospheric haze, muted painterly realism in the style of Ghost of Tsushima, weathered dark cedar wood, windswept`
- **Asset framing block** (embed verbatim in every *asset* prompt): `three-quarter view, centered, entire object visible, plain neutral gray studio background, soft even diffuse lighting, painterly realism in the style of Ghost of Tsushima, game asset concept render`
- **Approval gate:** after EVERY generated image, show it to the user and wait for explicit "да/ок" before spending credits on the next step (regenerate on "нет" with the user's correction). Never batch-generate ahead of approvals.
- GLB files go to `Assets/Fight/Arena/Props/` with exactly these names: `dojo.glb`, `lantern.glb`, `fence.glb`, `bokken_rack.glb`, `banner.glb`, `platform.glb`, `boulder.glb`
- Unity imports GLB via glTFast automatically on refresh; each `.glb` gets a `.meta` — commit both.
- Palette anchors: terrain gold `C7A661`, crimson `8C1A14`, player indigo `(0.16, 0.19, 0.40)`.

---

### Task 1: Style lock — master concept shots M1 + M2

**Files:**
- Create: `docs/superpowers/specs/refs/arena-m1.png` (downloaded master shot)
- Create: `docs/superpowers/specs/refs/arena-m2.png`

**Interfaces:**
- Produces: approved art direction; M1/M2 images referenced by eye when judging asset consistency in Tasks 2–4. No code interfaces.

- [ ] **Step 1: Pick the image model**

Call `models_explore(action: 'recommend')` with goal: "photorealistic painterly game-environment concept art, text-to-image, high detail" — use the recommended model for ALL image generations in this plan. If the recommendation errors, use the Higgsfield default `generate_image` model.

- [ ] **Step 2: Generate M1 (wide shot)**

`generate_image` with prompt:

> Wide cinematic shot of a fighting arena on a golden grass plateau at sunset: a low square wooden fighting platform in the center, a single-story traditional Japanese dojo building with a simple tiled roof and engawa veranda in the background, tall indigo fabric banners with a simple geometric diamond crest on bamboo poles at the platform corners, glowing wooden post lanterns, low cedar fence sections flanking a path to the dojo, red maple trees, tall pampas grass swaying in the wind, misty blue mountain ridges on the horizon, golden hour sunset light, warm amber rim lighting, soft atmospheric haze, muted painterly realism in the style of Ghost of Tsushima, weathered dark cedar wood, windswept, no religious symbols, no shrine elements, no statues, no torii, no shimenawa ropes, no text, no people

- [ ] **Step 3: Show M1 to the user — approval gate**

Show the image. If rejected: ask what to change, regenerate, repeat. Do not proceed until approved.

- [ ] **Step 4: Generate M2 (material close-up)**

`generate_image` with prompt:

> Medium shot of the corner of a traditional single-story Japanese wooden dojo at sunset: engawa veranda with worn dark cedar planks, wide sliding wooden doors, simple tiled roof with no ornaments and no finials, a glowing paper-and-wood lantern mounted on a cedar post, a wooden rack holding five bokken practice swords, golden grass in the foreground, golden hour sunset light, warm amber rim lighting, soft atmospheric haze, muted painterly realism in the style of Ghost of Tsushima, weathered dark cedar wood, windswept, no religious symbols, no shrine elements, no statues, no torii, no shimenawa ropes, no text, no people

- [ ] **Step 5: Show M2 to the user — approval gate** (same loop as Step 3)

- [ ] **Step 6: Download both approved images and commit**

Download each approved image URL with PowerShell:
```powershell
New-Item -ItemType Directory -Force docs/superpowers/specs/refs
Invoke-WebRequest -Uri "<M1_URL>" -OutFile "docs/superpowers/specs/refs/arena-m1.png"
Invoke-WebRequest -Uri "<M2_URL>" -OutFile "docs/superpowers/specs/refs/arena-m2.png"
```

```bash
git add docs/superpowers/specs/refs
git commit -m "assets: arena master concept shots (GoT style lock)"
```

---

### Task 2: Dojo asset (highest risk — building geometry)

**Files:**
- Create: `Assets/Fight/Arena/Props/dojo.glb` (+ auto `.meta`)

**Interfaces:**
- Produces: `dojo.glb` loaded by Task 5's `PlaceProp("dojo.glb", ...)`.

- [ ] **Step 1: Generate the dojo photo**

`generate_image` with prompt:

> single one-story traditional Japanese dojo building, simple hip-and-gable dark tiled roof with no ornaments and no finials, engawa veranda around the perimeter, wide sliding wooden doors, weathered dark cedar wood walls, three-quarter view, centered, entire object visible, plain neutral gray studio background, soft even diffuse lighting, painterly realism in the style of Ghost of Tsushima, game asset concept render, no religious symbols, no shrine elements, no statues, no torii, no shimenawa ropes, no text, no people

- [ ] **Step 2: Approval gate** — show the user; regenerate with corrections until approved. Check style against M2 (same wood tone, same roof simplicity).

- [ ] **Step 3: Convert to 3D**

`generate_3d` from the approved image → GLB result.

- [ ] **Step 4: Show the 3D result to the user — approval gate.** If the mesh is mangled (holes, melted roof), regenerate the *image* with a cleaner silhouette (e.g. add "orthographic look, no perspective distortion") and repeat.

- [ ] **Step 5: Download GLB and commit**

```powershell
Invoke-WebRequest -Uri "<GLB_URL>" -OutFile "Assets/Fight/Arena/Props/dojo.glb"
```

```bash
git add Assets/Fight/Arena/Props/dojo.glb*
git commit -m "assets: dojo building GLB (Higgsfield photo-to-3D)"
```

---

### Task 3: Fighting platform asset

**Files:**
- Create: `Assets/Fight/Arena/Props/platform.glb` (+ auto `.meta`)

**Interfaces:**
- Produces: `platform.glb` loaded by Task 5's `PlaceProp("platform.glb", ..., normalizeByWidth: true)`; its top surface becomes the fighters' floor.

- [ ] **Step 1: Generate the platform photo**

`generate_image` with prompt:

> single low square wooden fighting platform, 8 by 8 meters, made of worn dark cedar planks, one low step running along each edge, flat clean top surface, slightly weathered, three-quarter view from slightly above, centered, entire object visible, plain neutral gray studio background, soft even diffuse lighting, painterly realism in the style of Ghost of Tsushima, game asset concept render, no religious symbols, no shrine elements, no statues, no torii, no shimenawa ropes, no text, no people

- [ ] **Step 2: Approval gate** (image)
- [ ] **Step 3: `generate_3d`** from the approved image
- [ ] **Step 4: Approval gate** (mesh) — the top MUST be flat; a bumpy top breaks fighter grounding. If bumpy, regenerate the image emphasizing "perfectly flat top surface".
- [ ] **Step 5: Download to `Assets/Fight/Arena/Props/platform.glb`, commit**

```bash
git add Assets/Fight/Arena/Props/platform.glb*
git commit -m "assets: fighting platform GLB"
```

---

### Task 4: Small props — lantern, fence, bokken rack, banner, boulder

**Files:**
- Create: `Assets/Fight/Arena/Props/lantern.glb`, `fence.glb`, `bokken_rack.glb`, `banner.glb`, `boulder.glb` (+ auto `.meta` each)

**Interfaces:**
- Produces: the five GLBs loaded by Task 5's `PlaceProp` calls (exact filenames above).

For EACH of the five props run the same loop: generate image → user approval → `generate_3d` → user approval → download → commit (`git add Assets/Fight/Arena/Props/<name>.glb*` + `git commit -m "assets: <name> GLB"`). Every prompt below already contains the framing and negative blocks.

- [ ] **Step 1: `lantern.glb`**

> single wooden post lantern: a warm glowing paper-and-wood lantern box with a small wooden roof cap, mounted on top of a simple dark cedar post, NOT a stone lantern, three-quarter view, centered, entire object visible, plain neutral gray studio background, soft even diffuse lighting, painterly realism in the style of Ghost of Tsushima, game asset concept render, no religious symbols, no shrine elements, no statues, no torii, no shimenawa ropes, no text, no people

- [ ] **Step 2: `fence.glb`**

> single low wooden fence section made of dark weathered cedar, about 3 meters long and 1.2 meters tall, two simple horizontal rails on sturdy posts, rustic Japanese countryside style, three-quarter view, centered, entire object visible, plain neutral gray studio background, soft even diffuse lighting, painterly realism in the style of Ghost of Tsushima, game asset concept render, no religious symbols, no shrine elements, no statues, no torii, no shimenawa ropes, no text, no people

- [ ] **Step 3: `bokken_rack.glb`**

> single free-standing wooden weapon rack holding five wooden bokken practice swords resting horizontally on pegs, dark weathered cedar, simple sturdy joinery, three-quarter view, centered, entire object visible, plain neutral gray studio background, soft even diffuse lighting, painterly realism in the style of Ghost of Tsushima, game asset concept render, no religious symbols, no shrine elements, no statues, no torii, no shimenawa ropes, no text, no people

- [ ] **Step 4: `banner.glb`**

> single tall vertical nobori banner: deep indigo fabric with one simple geometric diamond crest, mounted on a bamboo pole with a small top crossbar, fabric gently curved as if in light wind, three-quarter view, centered, entire object visible, plain neutral gray studio background, soft even diffuse lighting, painterly realism in the style of Ghost of Tsushima, game asset concept render, no kanji, no lettering, no religious symbols, no shrine elements, no statues, no torii, no shimenawa ropes, no text, no people

- [ ] **Step 5: `boulder.glb`**

> single large granite boulder with natural rounded weathered shape and patches of green moss, three-quarter view, centered, entire object visible, plain neutral gray studio background, soft even diffuse lighting, painterly realism in the style of Ghost of Tsushima, game asset concept render, no religious symbols, no shrine elements, no statues, no torii, no shimenawa ropes, no text, no people

---

### Task 5: Scene code — remove torii, place the dojo set, fighters on the platform

**Files:**
- Modify: `Assets/Editor/FightSceneSetup.cs` (doc comment ~line 10–17, `PopulateScene` ~line 192–218, `CreateProps` ~line 330–377, `PlaceProp` ~line 392–418, `SpawnFighter` ~line 552–577)
- Delete: `Assets/Fight/Arena/Props/torii.glb`, `Assets/Fight/Arena/Props/torii.glb.meta`

**Interfaces:**
- Consumes: the 7 GLB filenames from Tasks 2–4.
- Produces: `CreateProps()` now returns `float` (platform-top world Y); `PlaceProp` gains `Transform parent = null, bool normalizeByWidth = false` params and returns `GameObject`; `SpawnFighter` gains a `float floorY` param; new helper `float PropTopY(GameObject go)`.

There is no runtime logic here (editor-only deterministic placement, no branching worth a unit test — verification is visual in Task 6), so no TDD cycle; the "test" is compilation + Task 6's checklist.

- [ ] **Step 1: Delete the torii asset**

```bash
git rm Assets/Fight/Arena/Props/torii.glb Assets/Fight/Arena/Props/torii.glb.meta
```

- [ ] **Step 2: Update `PopulateScene`**

Replace the stale-names array and the `CreateProps`/`SpawnFighter` call block (keep old names so scenes built before this change still get cleaned):

```csharp
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
```

- [ ] **Step 3: Replace `CreateProps` entirely**

```csharp
/// <summary>Real 3D mid-ground: dojo, fighting platform, post lanterns, school banners,
/// cedar fence, bokken rack, mossy boulders, red maples and swaying pampas grass —
/// Higgsfield-generated GLB props imported via glTFast. Missing props are skipped with
/// a warning. Returns the platform-top world Y (0 if no platform) — the fighters' floor.</summary>
private static float CreateProps()
{
    Transform propsRoot = new GameObject("ArenaProps").transform;

    var cedar = new Color(0.26f, 0.19f, 0.13f);   // weathered dark cedar
    var indigo = new Color(0.16f, 0.19f, 0.40f);  // school banners, matches the player suit
    var stone = new Color(0.42f, 0.44f, 0.40f);   // mossy granite
    var crimson = new Color(0.55f, 0.10f, 0.08f); // maples

    // Platform first: its top becomes the fighters' floor. 10 m wide so the
    // ±FightRules.ArenaHalfWidth (4 m) movement range keeps a margin from the edge.
    GameObject platform = PlaceProp("platform.glb", "Platform", Vector3.zero, 10f, 0f, cedar, propsRoot, normalizeByWidth: true);
    float floorY = PropTopY(platform);

    PlaceProp("dojo.glb", "Dojo", new Vector3(2f, 0f, 18f), 8f, 180f, cedar, propsRoot);
    PlaceProp("lantern.glb", "Lantern_L", new Vector3(-6.5f, 0f, 2.5f), 2.6f, 15f, cedar, propsRoot);
    PlaceProp("lantern.glb", "Lantern_R", new Vector3(6.5f, 0f, 2.5f), 2.6f, -15f, cedar, propsRoot);

    // School banners at the platform corners, poles just off the deck.
    PlaceProp("banner.glb", "Banner_0", new Vector3(-5.6f, 0f, -4.6f), 3.5f, 20f, indigo, propsRoot);
    PlaceProp("banner.glb", "Banner_1", new Vector3(5.6f, 0f, -4.6f), 3.5f, -20f, indigo, propsRoot);
    PlaceProp("banner.glb", "Banner_2", new Vector3(-5.6f, 0f, 5.2f), 3.5f, 160f, indigo, propsRoot);
    PlaceProp("banner.glb", "Banner_3", new Vector3(5.6f, 0f, 5.2f), 3.5f, -160f, indigo, propsRoot);

    // Fence flanks a clear central path from the platform to the dojo doors.
    PlaceProp("fence.glb", "Fence_0", new Vector3(-13f, 0f, 9f), 1.2f, 8f, cedar, propsRoot);
    PlaceProp("fence.glb", "Fence_1", new Vector3(-8.5f, 0f, 9.5f), 1.2f, 4f, cedar, propsRoot);
    PlaceProp("fence.glb", "Fence_2", new Vector3(8.5f, 0f, 9.5f), 1.2f, -4f, cedar, propsRoot);
    PlaceProp("fence.glb", "Fence_3", new Vector3(13f, 0f, 9f), 1.2f, -8f, cedar, propsRoot);

    PlaceProp("bokken_rack.glb", "BokkenRack", new Vector3(6.5f, 0f, 14.5f), 1.6f, 200f, cedar, propsRoot);

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

    // Near rows framing the fight (pushed behind the platform edge at z=5),
    // then a scattered mid-ground field.
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

    for (int i = 0; i < 12; i++) // front row, small, just behind the platform
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
```

- [ ] **Step 4: Replace `PlaceProp`**

```csharp
private static GameObject PlaceProp(string file, string name, Vector3 position, float size, float yaw, Color tint, Transform parent = null, bool normalizeByWidth = false)
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
    Tint(go, tint);
    return go;
}
```

- [ ] **Step 5: Update `SpawnFighter` to stand on the platform**

Change the signature and add one line after `GroundToFloor(go);`:

```csharp
private static Fighter SpawnFighter(string name, float x, AnimatorController controller, Color suitColor, float floorY)
{
    // ... existing body unchanged until the end ...
    GroundToFloor(go);
    go.transform.position += Vector3.up * floorY; // stand on the platform deck, not the terrain
    return go.AddComponent<Fighter>();
}
```

- [ ] **Step 6: Update the class doc comment**

In the summary at the top of the file, replace the sentence mentioning the torii mid-ground (if any) so the description matches: dojo, platform, lanterns, banners, fence, rack, boulders, maples, grass.

- [ ] **Step 7: Compile check**

Trigger a Unity script compile (open the editor, or run the existing edit-mode test suite if a Unity CLI path is configured). Expected: zero compile errors, `FightRulesTests` still pass.

- [ ] **Step 8: Commit**

```bash
git add Assets/Editor/FightSceneSetup.cs
git commit -m "feat: dojo arena prop set replaces torii; fighters spawn on platform"
```

---

### Task 6: Rebuild the scene and verify visually

**Files:**
- Modify: `Assets/Scenes/FightSandbox.unity` (regenerated by the setup tool)

**Interfaces:**
- Consumes: everything above.

- [ ] **Step 1: Run the setup**

In the Unity editor: menu **Mikey ▸ Setup Fight Scene** (or `-executeMethod FightSceneSetup.Setup` in batch mode if running headless). Expected console output ends with `FightSceneSetup: done.` and NO `— skipping` warnings for the seven new props.

- [ ] **Step 2: Visual checklist (Scene/Game view, user confirms)**

- No torii anywhere; nothing religious, no statues.
- Dojo visible behind the fighters, facing the camera.
- Both fighters stand ON the platform deck (feet not sunken, not floating).
- Walking to the movement limits (A/D to x = ±4) keeps fighters on the platform.
- 4 banners at platform corners, 2 lanterns, 4 fence sections with a clear path to the dojo, rack near the dojo, 3 boulders, maples and grass intact.
- Nothing floats above or sinks into terrain/platform.
- Style coherent with master shot M1 (warm dusk, indigo/crimson accents, misty ridges).

If placement looks off, tweak positions/yaws/sizes in `CreateProps` and re-run the menu item (it is re-runnable).

- [ ] **Step 3: Commit the scene**

```bash
git add Assets/Scenes/FightSandbox.unity
git commit -m "feat: FightSandbox rebuilt with dojo arena set"
```

- [ ] **Step 4: Update the old design doc status**

In `docs/superpowers/specs/2026-07-11-fight-arena-2.5d-design.md` the torii is listed as a placed prop — add one line noting it was replaced by the dojo set per `2026-07-13-arena-dojo-assets-design.md`. Commit:

```bash
git add docs/superpowers/specs/2026-07-11-fight-arena-2.5d-design.md
git commit -m "docs: note torii replaced by dojo arena set"
```
