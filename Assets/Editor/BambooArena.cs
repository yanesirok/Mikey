using System.Collections.Generic;
using Mikey.Fight;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

/// <summary>
/// Procedural "bamboo bridge" arena: the fight happens on a plank bridge slung low over dark
/// water in a bamboo grove. Everything here is generated geometry carrying its colour — and its
/// baked occlusion — in the vertex stream, textured with maps <see cref="ArenaTextures"/>
/// generates, and baked into four meshes so the whole environment is a handful of draw calls.
///
/// Baked meshes rather than placed prefabs because the camera never leaves its rail: nothing
/// needs culling, LOD or instancing, and one mesh per material is the cheapest thing a mobile
/// GPU can be handed.
///
/// World layout. The fighters' floor is y = 0, which is the deck surface at mid-span:
///   bridge  x ±8, z ±1.1, deck top at y 0 rising toward both ends by FightRules.BridgeSag
///   water   y <see cref="WaterY"/> — 0.6 below the boards, close enough to reach
///   banks   a height field either side of |x| = <see cref="ShoreX"/>; the bamboo grows on it
///   bamboo  three tiers at distinct brightness steps, plus dark culms at the frame edges
///
/// Placement is driven by what the camera can actually see. FightCamera frames ±7.3 units around
/// the fight at full zoom-out, so nothing is built beyond that except what the fog is meant to
/// swallow. The foreground culms sit exactly where they can only ever cross the outer edge of
/// the frame — a stalk in front of a fighter is the one thing a side-on fight cannot afford.
/// </summary>
public static class BambooArena
{
    public const float DeckY = 0f;       // deck surface at mid-span = the fighters' floor
    public const float WaterY = -0.6f;   // low: in the reference the water is right at the boards

    private const float HalfLength = FightRules.BridgeHalfLength; // ±8, ends just out of frame
    private const float HalfWidth = 1.1f;  // and z ±1.1
    private const float ShoreX = 6.8f;     // water's edge; the bridge lands 1.2 units onto the bank
    private const float ChannelEndZ = 26f; // the channel closes here, deep enough to be pure fog
    private const int Seed = 20260727;

    /// <summary>Where the piles stand. Shared with the water builder, which bakes a foam mask
    /// around each one — knowing the positions at build time is what lets the foam skip the
    /// depth texture entirely.</summary>
    private static readonly float[] PileX = { -5.4f, -1.9f, 1.9f, 5.4f };

    /// <summary>The stringers the boards are nailed to, and what actually carries the span.
    /// Three at 72 cm rather than two at 144: a 2.3 m deck on two stringers has its whole middle
    /// unsupported, and that is visible through the gaps as a long empty shadow where a beam
    /// ought to be. Shared so the nail heads land on them rather than near them.</summary>
    private static readonly float[] BearerZ = { -0.72f, 0f, 0.72f };
    private const float PileWaterZ = 0.7f; // where a splayed leg crosses the surface

    private const string Dir = "Assets/Fight/Arena/";

    /// <summary>Silhouette reach of each leaf-atlas cell, filled in by
    /// <see cref="ArenaTextures.LeafAtlas"/> before any card is cut from it.</summary>
    private static float[,] LeafCardRadii;

    /// <summary>Top of the stretch of bark map that carries no node ring. Leaves and branches
    /// live in the same mesh as the culms and so share their texture; they have to sample inside
    /// this band, because the map tiles and a node ring printed across a leaf is instantly
    /// wrong.</summary>
    private const float BarkClearV = 0.05f;

    /// <summary>Deck surface height. A dead straight bridge reads as a box; the sag is what
    /// makes it read as something slung between two banks.
    ///
    /// The curve itself lives in <see cref="FightRules"/>: the boards are built from it here and
    /// the fighters stand on it at runtime, and two copies of a curve is two copies that drift.
    /// </summary>
    public static float DeckHeight(float x) => DeckY + FightRules.DeckHeight(x);

    /// <summary>Bank height field. Below the water line inside the channel, climbing to the bank
    /// tops outside it. The channel narrows to nothing past <see cref="ChannelEndZ"/> so the
    /// frame never contains a flat water horizon — the river bends into the mist instead.</summary>
    public static float Ground(float x, float z)
    {
        float shore = Mathf.Lerp(ShoreX, 0f, Mathf.InverseLerp(ChannelEndZ - 8f, ChannelEndZ + 4f, z));
        float inland = Mathf.Abs(x) - shore;
        float y = Mathf.Lerp(WaterY - 0.7f, 0.85f, Mathf.Clamp01((inland + 1.2f) / 3.4f));
        y += (Mathf.PerlinNoise(x * 0.13f + 4.3f, z * 0.13f + 7.9f) - 0.35f) * 1.5f
             * Mathf.Clamp01(inland / 2.5f);
        return y;
    }

    /// <summary>Metres of water standing above the bed at a point, zero on dry land. Clamped to a
    /// metre because that is the range the vertex colour byte has to span: the channel bottoms out
    /// 0.7 below the surface, and a byte over a metre quantises to 4 mm, finer than the grid this
    /// is sampled on.</summary>
    public static float BedDepthAt(float x, float z) => Mathf.Clamp01(WaterY - Ground(x, z));

    /// <summary>World-space UV scale of the bank's planar projection. The water shader draws the
    /// riverbed with the same texture and must use the same number: the bed is the bank continuing
    /// under the water, and two copies of this constant would part company exactly on the
    /// waterline, which is where the eye is.</summary>
    public const float BankUvScale = 0.25f;

    public static GameObject Build()
    {
        // Two invariants everything else is built on: mid-span deck is exactly the fighters'
        // floor, and the ground under the bridge stays below the water. Break the first and both
        // fighters float or sink; break the second and the bank surfaces through the river in
        // the middle of the fight line.
        if (!Mathf.Approximately(DeckHeight(0f), DeckY) || Ground(0f, 0f) > WaterY)
            Debug.LogError($"BambooArena: layout invariants broken — deck at mid-span is " +
                           $"{DeckHeight(0f)} (want {DeckY}), ground under it {Ground(0f, 0f)} " +
                           $"(want below {WaterY}).");

        CheckNodeSilhouette(); // before the seed, so the probe cannot shift the grove's randomness
        Random.InitState(Seed);
        ArenaTextures.Surface wood = ArenaTextures.Wood();
        ArenaTextures.Surface bark = ArenaTextures.Bark();
        Texture2D leafCard = ArenaTextures.LeafAtlas(out LeafCardRadii);
        ArenaTextures.Blob();

        var timber = new Bake();
        var bamboo = new Bake();
        var cards = new Bake();
        var foliage = new Bake();
        var ground = new Bake();
        var scan = new Bake();
        var shade = new Bake();

        BuildBridge(timber, foliage);
        BuildProps(timber, foliage);
        BuildBamboo(bamboo, cards);
        BuildBanks(ground);
        BuildScanFoliage(scan);
        BuildUndergrowth(foliage, cards, shade);

        Mesh timberMesh = timber.ToMesh("ArenaTimber");
        Mesh bambooMesh = bamboo.ToMesh("ArenaBamboo");
        Mesh cardsMesh = cards.ToMesh("ArenaBambooCards");
        Mesh foliageMesh = foliage.ToMesh("ArenaFoliage");
        Mesh groundMesh = ground.ToMesh("ArenaGround");
        Mesh scanMesh = scan.ToMesh("ArenaScan");
        Mesh shadeMesh = shade.ToMesh("ArenaLilyShade");

        // Occlusion is the single biggest thing separating generated geometry from modelled
        // geometry: without it planks, posts and props all sit in the same flat light and
        // nothing looks like it is resting on anything. Raycast the timber against the whole
        // arena; the foliage gets a cheaper height-based approximation while it is being built.
        // Foliage is deliberately not an occluder: cooking a collider for a hundred thousand
        // two-sided leaf strips costs seconds and shadows nothing a leaf could actually shadow.
        BakeOcclusion(timberMesh, new[] { timberMesh, bambooMesh });

        timberMesh.RecalculateTangents(); // normal-mapped, so it needs them
        groundMesh.RecalculateTangents();
        bambooMesh.RecalculateTangents(); // and so does the bark, which had none for its whole life

        // The bamboo material enables _NORMALMAP, and Arena.shader builds its basis from
        // input.tangentWS. On a mesh with no tangent stream that basis is undefined, and the bark
        // relief — the whole reason the culms carry a normal map — resolves to noise.
        if (bambooMesh.tangents.Length != bambooMesh.vertexCount)
            Debug.LogError($"BambooArena: bamboo mesh has {bambooMesh.tangents.Length} tangents " +
                           $"for {bambooMesh.vertexCount} vertices — M_ArenaBamboo enables " +
                           $"_NORMALMAP, so the bark relief is being read off an undefined basis.");

        // Tube duplicates the seam vertex so u runs 0..uTiles across a ring instead of wrapping
        // back to zero on the last facet. Without it that facet carries the whole strip squeezed
        // in backwards, which is invisible on a noise map and not on a photograph. u reaching its
        // far end somewhere in the mesh is exactly the invariant; before the fix it stopped at
        // (sides - 1) / sides.
        float maxU = 0f;
        foreach (Vector2 uv in bambooMesh.uv)
            maxU = Mathf.Max(maxU, uv.x);
        if (maxU < 0.999f)
            Debug.LogError($"BambooArena: bark u stops at {maxU:F3} on the bamboo mesh — the seam " +
                           $"ring is not being duplicated, so the strip runs backwards across one " +
                           $"facet of every culm.");

        var root = new GameObject("Arena");
        GameObject timberGo = AddMesh(root, "Timber", timberMesh, TimberMaterial(wood), castShadows: true);
        // The only collider in the arena, and it exists for one reason: foot IK has to find the
        // board a sole is standing on, and the boards sit proud of one another by up to three
        // centimetres in a pattern that only this mesh knows. Seven thousand triangles is nothing
        // to cook and nothing to raycast four times a frame — and no rigidbody ever touches it.
        timberGo.AddComponent<MeshCollider>().sharedMesh = timberMesh;
        // Bamboo does not cast. The frame-edge culms are twelve units tall and stand a couple of
        // units off camera, so under a 45° key they throw a shadow band clear across the deck —
        // it put one of the two fighters in total darkness while the other stood in the light.
        // A grove casting no shadow costs nothing here; a fighter lost to one costs the fight.
        GameObject bambooGo = AddMesh(root, "Bamboo", bambooMesh, BambooMaterial(bark), castShadows: false);
        GameObject cardsGo = AddMesh(root, "BambooCards", cardsMesh, LeafCardMaterial(leafCard),
                                     castShadows: false);
        GameObject foliageGo = AddMesh(root, "Vegetation", foliageMesh, FoliageMaterial(), castShadows: false);
        GameObject groundGo = AddMesh(root, "Ground", groundMesh, GroundMaterial(), castShadows: false);
        GameObject scanGo = AddMesh(root, "ScanFoliage", scanMesh, ScanFoliageMaterial(),
                                    castShadows: false);
        // The lily pads' contact shadow. Deliberately left on Default rather than following the
        // vegetation into a reflected layer, for the same reason the fighters' blob is: this is a
        // cheat tuned for the main camera's angle, and mirrored it becomes a dark patch floating
        // in the sky the water is showing.
        AddMesh(root, "LilyShade", shadeMesh, LilyShadeMaterial(), castShadows: false);
        BuildWater(root);

        // Two reflected layers, because the two halves cost very different amounts. The bridge
        // joins the fighters on the always-reflected one; the grove and the banks go on the
        // layer the mobile tier drops. At a grazing angle what the water shows is whatever
        // stands near the horizon, so it is the grove that produces the stretched vertical
        // streaks — but it is also 80k triangles of second pass.
        int reflected = LayerMask.NameToLayer(Mikey.Fight.WaterReflection.LayerName);
        if (reflected >= 0)
            timberGo.layer = reflected;

        int reflectedFar = LayerMask.NameToLayer(Mikey.Fight.WaterReflection.FarLayerName);
        if (reflectedFar >= 0)
        {
            bambooGo.layer = reflectedFar;
            cardsGo.layer = reflectedFar;
            foliageGo.layer = reflectedFar;
            groundGo.layer = reflectedFar;
            scanGo.layer = reflectedFar;
        }

        int bambooTris = bambooMesh.triangles.Length / 3;
        int cardTris = cardsMesh.triangles.Length / 3;
        int foliageTris = foliageMesh.triangles.Length / 3;
        int scanTris = scanMesh.triangles.Length / 3;
        int timberTris = timberMesh.triangles.Length / 3;
        int groundTris = groundMesh.triangles.Length / 3;
        int shadeTris = shadeMesh.triangles.Length / 3;
        int arenaTris = timberTris + bambooTris + cardTris + foliageTris + groundTris + scanTris
                        + shadeTris;
        Debug.Log($"BambooArena: timber {timberTris} tris, " +
                  $"bamboo {bambooTris}, cards {cardTris}, vegetation {foliageTris}, " +
                  $"ground {groundTris}, scan {scanTris}, lily shade {shadeTris}, " +
                  $"arena {arenaTris}.");

        // The ceiling is the whole arena, because that is what a phone draws. It was stated as
        // ~115k for a long time and enforced nowhere: the per-mesh caps permitted 141k between
        // them, never tested the timber or the ground, and computed the scanned foliage — a
        // quarter of the frame — purely in order to log it. The arena sat at 133 552 with no
        // error raised. Carding the near tier's crowns is what finally made the number meetable.
        //
        // These catch a regression; they do not police the art decision. If a phone disagrees,
        // cut the tier-2 and tier-3 cards first — they buy the least picture per shaded pixel —
        // and the near foliage last.
        if (arenaTris > 115000)
            Debug.LogError($"BambooArena: arena over budget — {arenaTris} tris (max 115000): " +
                           $"timber {timberTris}, bamboo {bambooTris}, cards {cardTris}, " +
                           $"vegetation {foliageTris}, ground {groundTris}, scan {scanTris}, " +
                           $"lily shade {shadeTris}.");
        // Per-mesh tripwires on the three a placement change can run away with. The card mesh
        // carries bank grass and the near tier's crowns as well as the far tiers' foliage now.
        if (bambooTris > 50000 || cardTris > 16000 || foliageTris > 30000)
            Debug.LogError($"BambooArena: grove over budget — {bambooTris} tris of culm " +
                           $"(max 50000), {cardTris} of card (max 16000), {foliageTris} of " +
                           $"vegetation (max 30000).");
        return root;
    }

    /// <summary>The silhouette test from the design, written down where it can fail loudly: on a
    /// frame-edge culm the node has to be a real bump, not a painted band. Measured off the built
    /// mesh rather than the parameters, so it still holds if <c>Tube</c> is rewritten.</summary>
    private static void CheckNodeSilhouette()
    {
        var probe = new Bake();
        var root = new Vector3(0f, 0f, 0f);
        Stalk(probe, null, root, 10f, 0.06f, Color.white, EdgeTier, Vector2.zero, 0f);
        Mesh mesh = probe.ToMesh("Probe");

        // One metre of culm, taken from below the branches. A band this short is the point:
        // measured over the whole stalk the taper alone would swing the radius by six per cent
        // and the test would pass on a smooth cone. Here the taper contributes well under one
        // per cent, so whatever variation is left is the node. The radius cap keeps a stray
        // branch out of the sample if BranchStart ever moves down.
        float widest = 0f, narrowest = float.MaxValue;
        int sampled = 0;
        foreach (Vector3 v in mesh.vertices)
        {
            float r = new Vector2(v.x, v.z).magnitude;
            if (v.y < 3f || v.y > 4f || r > 0.12f)
                continue;
            sampled++;
            widest = Mathf.Max(widest, r);
            narrowest = Mathf.Min(narrowest, r);
        }

        if (sampled == 0)
            Debug.LogError("BambooArena: node silhouette check sampled nothing — the probe culm " +
                           "no longer has geometry between 3 and 4 metres and the check is dead.");
        else if (widest < narrowest * 1.05f)
            Debug.LogError($"BambooArena: frame-edge culm has no node in its silhouette — " +
                           $"radius runs {narrowest:F4}..{widest:F4}. The nodes are texture only, " +
                           $"and at two units from the lens that reads as a smooth pipe.");
        Object.DestroyImmediate(mesh);
    }

    // ------------------------------------------------------------------ bridge

    private static void BuildBridge(Bake bake, Bake foliage)
    {
        // Multipliers on the wood map, not colours. The texture already says what timber looks
        // like; the vertex stream's job is board-to-board variation and baked occlusion. Written
        // as absolute sRGB colours they were converted to linear and multiplied into the map,
        // darkening every board twice over and turning the deck to charcoal. Multipliers are not
        // colours and must not go through the gamma conversion.
        // Pulled down by about a third once the wood map stopped arriving two stops dark. The
        // deck measured 125 against a 219 sky and read as bleached driftwood — brighter than
        // either fighter, which inverts what the frame is about. Target is 95..110: darker than
        // the water it sits over, lighter than the beams under it.
        //
        // Near-neutral again, and deliberately: the hue now lives in the wood map, where it
        // belongs, and these are back to being what they are called — multipliers that separate
        // one board from the next. Lifted about 15% because the warm map is darker than the grey
        // one it replaced, and the deck has to land back in the 100..115 it was measured at.
        //
        // An earlier pass warmed these by 12% instead and left the map grey. That was too timid
        // by half: a tenth of a hue difference under a grade that removes saturation is not a
        // warm bridge, it is the same silver one with an apology attached.
        Color plankPale = new Color(0.97f, 0.94f, 0.90f);
        Color plankMid = new Color(0.77f, 0.74f, 0.71f);
        Color plankDark = new Color(0.55f, 0.53f, 0.50f);
        Color beam = new Color(0.5f, 0.48f, 0.44f);
        Color wet = new Color(0.3f, 0.32f, 0.32f);
        // Multipliers again, so both read against the wood map they share: the nail cold and
        // dark, the lashing lighter and greyer than any board around it.
        Color nail = new Color(0.42f, 0.43f, 0.46f);
        Color rope = new Color(0.95f, 0.90f, 0.76f);

        // Boards laid across the direction of travel: the repeating edge is what gives the
        // bridge its rhythm and tells the eye how big a fighter is against it.
        // Laid one against the next with a fixed gap, rather than dropped on a fixed pitch. On a
        // 27 cm pitch a 22 cm board left a 5 cm gap and a 25 cm board left 2 cm: the widest read
        // as a missing board, and the run of them as a deck with its planks pulled up. Constant
        // gap, varying board is also the right way round — geometry regular, timber varied.
        //
        // 15 mm, not the 8 to 10 a real deck drains through. This camera puts 106 pixels on a
        // metre, so 9 mm is 0.95 of a pixel: correct, and invisible. Measured at 9 the deck came
        // back as one continuous sheet with faint scratches on it, and the board rhythm that
        // tells the eye how big a fighter is against the bridge was gone. 15 mm is 1.6 pixels —
        // a line that draws, still four times tighter than what read as a missing plank.
        const float gap = 0.015f;
        const float thickness = 0.055f;
        for (float x = -HalfLength; x <= HalfLength; )
        {
            float width = Random.Range(0.22f, 0.25f);
            float top = DeckHeight(x);

            // Unevenness is not decoration, but there are two kinds of it and only one belongs
            // here. A deck of identical boards reads as a printed texture; a deck with boards
            // three centimetres proud, boards cut short and boards missing reads as abandoned,
            // which is what this arena was accused of and what it was in fact built as. What is
            // left is the tolerance a hand plane leaves — millimetres, not centimetres — and the
            // board-to-board difference in width and tone, which is where the variation belongs.
            top += Random.Range(-0.004f, 0.004f);

            float halfSpan = HalfWidth + 0.06f - Random.Range(0f, 0.012f);
            float zOffset = Random.Range(-0.008f, 0.008f);

            Color tint = Color.Lerp(plankDark, plankPale, Random.Range(0.25f, 1f));
            tint = Color.Lerp(tint, plankMid, 0.3f);

            var centre = new Vector3(x + width * 0.5f, top - thickness * 0.5f, zOffset);
            var size = new Vector3(width, thickness, halfSpan * 2f);
            // Grain runs down the length of the board, and each board gets its own patch of the
            // map: without the per-board offset the repeat is visible across the whole deck.
            bake.Box(centre, size, tint, new Vector2(0.4f, 3f),
                     new Vector2(Random.value, Random.value), Random.Range(-0.4f, 0.4f));

            // What holds the board down. Two heads over the two bearers, one quad each: at this
            // camera a nail is two pixels across, so a box would spend forty-four triangles on
            // something a flat dot says just as well. Without them the boards are laid on the
            // beams and nothing explains why they stay there.
            foreach (float bearerZ in BearerZ)
                NailHead(bake, new Vector3(centre.x, top + 0.002f, bearerZ + Random.Range(-0.02f, 0.02f)),
                         Random.Range(0.018f, 0.026f), nail);

            x += width + gap;
        }

        // Two longitudinal bearers under the boards, seen through the gaps and at the edges.
        foreach (float z in BearerZ)
            for (int i = 0; i < 12; i++)
            {
                float x0 = Mathf.Lerp(-HalfLength, HalfLength, i / 12f);
                float x1 = Mathf.Lerp(-HalfLength, HalfLength, (i + 1) / 12f);
                var a = new Vector3(x0, DeckHeight(x0) - 0.14f, z);
                var b = new Vector3(x1, DeckHeight(x1) - 0.14f, z);
                bake.Box(Vector3.Lerp(a, b, 0.5f), new Vector3(x1 - x0, 0.13f, 0.14f), beam,
                         new Vector2(0.4f, 3f), new Vector2(Random.value, Random.value), 0f);
            }

        // Piles: short now that the deck is low, barely clearing the water. Split into three
        // segments because the interesting part is where they meet the surface.
        Color submerged = new Color(0.16f, 0.24f, 0.2f); // greener and much darker under water
        foreach (float x in PileX)
        {
            // The head beam tying each pair of piles, and the one member whose absence broke the
            // load path outright: two piles stood under the deck touching nothing but a stringer,
            // so the eye followed a post up and found it holding a plank. The lashings sit at
            // exactly this height, which now reads as the pile being tied to the beam it carries.
            bake.Box(new Vector3(x, DeckHeight(x) - 0.255f, 0f), new Vector3(0.2f, 0.1f, 1.45f),
                     beam * 0.94f, new Vector2(0.4f, 3f),
                     new Vector2(Random.value, Random.value), 0f);

            for (int i = -1; i <= 1; i += 2)
            {
                var head = new Vector3(x, DeckHeight(x) - 0.2f, i * 0.62f);
                var foot = new Vector3(x + i * 0.12f, WaterY - 0.85f, i * 0.86f);
                Vector3 At(float y) => Vector3.Lerp(head, foot, Mathf.InverseLerp(head.y, foot.y, y));

                Vector3 damp = At(WaterY + 0.15f);
                Vector3 line = At(WaterY);
                bake.Tube(head, damp, 0.075f, 0.078f, beam, 6, 1);
                // Lashing at the pile head, where the deck loads onto it. This is the joint the
                // eye goes to when it asks what holds the bridge up, and it was a pile ending in
                // mid-air under a board. Twelve triangles for the whole answer.
                bake.Tube(head + Vector3.up * 0.03f, head - Vector3.up * 0.055f,
                          0.089f, 0.087f, rope, 6, 1);
                // Moss where the pile meets the water, and nowhere else. It used to grow at the
                // foot of every railing post, on a deck people cross daily — that is not a
                // weathered bridge, that is an abandoned one. Down here it is just damp.
                if (Random.value < 0.75f)
                    Blades(foliage, damp + new Vector3(Random.Range(-0.05f, 0.05f), 0f, i * 0.06f),
                           Random.Range(0.08f, 0.16f), 1.4f, 0.5f, 5,
                           Srgb(0.16f, 0.22f, 0.11f) * Random.Range(0.8f, 1.2f), Random.value, 0f, 1f);
                // The wet band sits *above* the water, 15 cm of it, dark and glossier. It is a
                // separate thing from the foam and it is what says the water has been at this
                // height for a while.
                bake.Tube(damp, line, 0.078f, 0.08f, wet, 6, 1);
                // Below the surface: darker, greener, and stepped sideways. At a grazing angle
                // there is no refraction to see, but the eye still expects the underwater part
                // not to line up with the part above it, and a crude offset reads correctly.
                bake.Tube(line + new Vector3(0.035f * i, 0f, 0.01f), foot + new Vector3(0.035f * i, 0f, 0.01f),
                          0.082f, 0.088f, submerged, 6, 1);
            }
        }

        BuildRailing(bake, plankPale);
        BuildAbutments(bake);
    }

    /// <summary>
    /// A member laid along the bridge, tilted to the deck's own slope. A horizontal box on a
    /// curved deck steps at every joint, and thirty of them stepping against each other is half
    /// of what made the old railing read as thrown together rather than built.
    /// </summary>
    /// <param name="section">y is the member's thickness, z its width across the bridge.</param>
    private static void Beam(Bake bake, float x0, float x1, float above, float z,
                             Vector2 section, Color colour, float bevel = 0.015f)
    {
        float y0 = DeckHeight(x0) + above;
        float y1 = DeckHeight(x1) + above;
        float run = x1 - x0, rise = y1 - y0;
        bake.Box(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, z),
                 new Vector3(Mathf.Sqrt(run * run + rise * rise), section.x, section.y),
                 colour, new Vector2(0.4f, 3f), new Vector2(Random.value, Random.value),
                 Quaternion.Euler(0f, 0f, Mathf.Atan2(rise, run) * Mathf.Rad2Deg), bevel);
    }

    /// <summary>
    /// The sill that finishes both deck edges. Nothing stands on it.
    ///
    /// There was a balustrade here — sill, posts, lower rail, balusters at an even pitch, a flat
    /// handrail, heavier posts closing the run — and it was well built. It came out anyway, and
    /// the reason is worth keeping so nobody builds it back.
    ///
    /// This camera puts the far edge of the deck directly behind the fighters at waist height.
    /// Whatever stands there is not scenery, it is the backdrop the fight is read against, and a
    /// row of forty-five balusters is a rhythm at exactly the frequency a moving figure is: at
    /// the moment the two close, the legs and the balusters interleave and the eye has to pick
    /// the fighters out of a picket fence. Nothing about the railing was wrong except where it
    /// stood, and there is nowhere else on a bridge to put one.
    ///
    /// The sill stays. It is 8 cm at deck level, so it never crosses a figure, and it is the one
    /// member that turns a row of board ends into an edge.
    /// </summary>
    private static void BuildRailing(Bake bake, Color pale)
    {
        const float sillWidth = 0.14f;   // across the bridge
        const float sillHeight = 0.08f;

        float sillZ = HalfWidth + 0.06f - sillWidth * 0.5f; // outer face flush with the board ends

        // Both edges. Seven lengths a side, and odd for the same reason the posts are even: six
        // would butt two of them together at x = 0, putting a joint in the sill at the centre of
        // the frame. Seven is also what the curvature costs — a tilted chord over 2.3 m departs
        // from the sag by 4 mm, inside the tolerance the boards themselves keep.
        const int sillLengths = 7;
        foreach (int side in new[] { -1, 1 })
            for (int s = 0; s < sillLengths; s++)
                Beam(bake, Mathf.Lerp(-HalfLength, HalfLength, s / (float)sillLengths),
                     Mathf.Lerp(-HalfLength, HalfLength, (s + 1) / (float)sillLengths),
                     sillHeight * 0.5f, side * sillZ,
                     new Vector2(sillHeight, sillWidth), pale * 0.95f);
    }

    /// <summary>Stone steps where the bridge lands on each bank. Without them the deck ends in
    /// mid-air, which is visible the moment the camera pans to either extreme.</summary>
    private static void BuildAbutments(Bake bake)
    {
        // Cooled hard, because these ride the wood map like everything else on this material and
        // the map is now cedar rather than driftwood. A near-neutral multiplier over a warm map
        // is a warm result: the abutments would have come out as brown blocks. The 1.5 on blue
        // is not a colour, it is the inverse of the map's own hue.
        Color stone = new Color(0.95f, 1.14f, 1.48f);
        Color stoneDark = new Color(0.55f, 0.66f, 0.86f);

        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * (HalfLength - 0.5f);
            float deck = DeckHeight(x);
            // The block the deck rests on.
            bake.Box(new Vector3(x + side * 0.6f, deck - 0.45f, 0f), new Vector3(1.9f, 0.85f, 2.6f),
                     Color.Lerp(stoneDark, stone, 0.55f), new Vector2(0.6f, 0.6f),
                     new Vector2(Random.value, Random.value), side * 4f);
            // Two rough steps down to the bank.
            for (int s = 0; s < 2; s++)
                bake.Box(new Vector3(x + side * (1.5f + s * 0.75f), deck - 0.62f - s * 0.22f, Random.Range(-0.2f, 0.2f)),
                         new Vector3(0.8f, 0.3f + s * 0.2f, 2.2f - s * 0.3f),
                         Color.Lerp(stoneDark, stone, Random.Range(0.3f, 0.8f)),
                         new Vector2(0.6f, 0.6f), new Vector2(Random.value, Random.value), side * (6f + s * 5f));
            // Loose blocks at the foot, so the join is not a clean machined edge.
            for (int s = 0; s < 4; s++)
            {
                float bx = x + side * Random.Range(1.6f, 3.4f);
                float bz = Random.Range(-1.8f, 1.8f);
                float size = Random.Range(0.25f, 0.55f);
                bake.Box(new Vector3(bx, Ground(bx, bz) + size * 0.3f, bz),
                         new Vector3(size, size * 0.7f, size * Random.Range(0.7f, 1.3f)),
                         Color.Lerp(stoneDark, stone, Random.Range(0.2f, 0.9f)),
                         new Vector2(0.8f, 0.8f), new Vector2(Random.value, Random.value),
                         Random.Range(0f, 40f));
            }
        }
    }

    // ------------------------------------------------------------------ props

    /// <summary>What separates a location from a set of assets: every one of these says people
    /// cross this bridge. They sit on the banks at |x| 7-10, which the camera reaches only at
    /// the extremes of its pan — present, never competing with the fight.</summary>
    private static void BuildProps(Bake timber, Bake foliage)
    {
        Color stone = new Color(0.92f, 0.95f, 0.96f);
        Color stoneDark = new Color(0.5f, 0.53f, 0.55f);
        Color timberDark = new Color(0.55f, 0.5f, 0.42f);
        Color rope = new Color(0.85f, 0.76f, 0.55f);
        Color cloth = Srgb(0.62f, 0.56f, 0.44f); // foliage mesh: untextured, so a real colour

        // Stone lantern on the right bank: the classic marker that places the scene instantly.
        float lx = 8.2f, lz = 2.4f;
        float ly = Ground(lx, lz);
        BuildStoneLantern(timber, new Vector3(lx, ly, lz), stone, stoneDark);

        // Half-sunk punt against the left bank — the best mid-ground element there is, because
        // it reads as a silhouette even at full fog.
        float bx = -8.4f, bz = 3.2f;
        BuildPunt(timber, new Vector3(bx, WaterY - 0.12f, bz), timberDark);

        // Coiled rope and a bucket by the water.
        var coil = new Vector3(7.4f, Ground(7.4f, -1.2f) + 0.05f, -1.2f);
        for (int i = 0; i < 3; i++)
            timber.Torus(coil + Vector3.up * (i * 0.055f), 0.24f - i * 0.045f, 0.035f, rope, 10, 5);
        var bucketAt = new Vector3(-7.6f, Ground(-7.6f, -0.6f) + 0.14f, -0.6f);
        timber.Tube(bucketAt + Vector3.down * 0.14f, bucketAt + Vector3.up * 0.14f, 0.16f, 0.19f,
                    timberDark, 9, 2);
        timber.Torus(bucketAt + Vector3.up * 0.14f, 0.19f, 0.022f, rope, 9, 4);

        // Cloth on a bamboo pole, animated by the same wind the foliage uses.
        float px = 7.9f, pz = -2.6f;
        float py = Ground(px, pz);
        timber.Tube(new Vector3(px, py, pz), new Vector3(px, py + 2.4f, pz), 0.05f, 0.042f, timberDark, 6, 3);
        for (int i = 0; i < 5; i++)
        {
            float t0 = i / 5f, t1 = (i + 1) / 5f;
            var a = new Vector3(px + 0.04f, py + 2.3f - t0 * 1.1f, pz);
            var b = new Vector3(px + 0.04f, py + 2.3f - t1 * 1.1f, pz);
            foliage.Quad(a + Vector3.forward * -0.01f, a + Vector3.forward * 0.26f,
                         b + Vector3.forward * 0.26f, b + Vector3.forward * -0.01f,
                         Vector3.right, cloth * Random.Range(0.9f, 1.1f), 0.4f,
                         Mathf.Lerp(0.3f, 0.9f, t0), Mathf.Lerp(0.3f, 0.9f, t1));
        }
    }

    private static void BuildStoneLantern(Bake bake, Vector3 baseAt, Color stone, Color stoneDark)
    {
        bake.Tube(baseAt, baseAt + Vector3.up * 0.55f, 0.19f, 0.15f, stoneDark, 8, 1);       // shaft
        bake.Box(baseAt + Vector3.up * 0.66f, new Vector3(0.62f, 0.12f, 0.62f), stone,
                 new Vector2(0.7f, 0.7f), new Vector2(0.2f, 0.4f), 0f);                       // platform
        bake.Box(baseAt + Vector3.up * 0.92f, new Vector3(0.44f, 0.42f, 0.44f), stone,
                 new Vector2(0.7f, 0.7f), new Vector2(0.6f, 0.1f), 0f);                       // light box
        bake.Box(baseAt + Vector3.up * 1.2f, new Vector3(0.74f, 0.14f, 0.74f), stoneDark,
                 new Vector2(0.7f, 0.7f), new Vector2(0.35f, 0.8f), 0f);                      // roof
        bake.Tube(baseAt + Vector3.up * 1.27f, baseAt + Vector3.up * 1.45f, 0.09f, 0.02f, stoneDark, 6, 1);
    }

    /// <summary>A flat-bottomed punt, bow up and stern under the surface. Built as cross boards
    /// plus a gunwale rail either side, all yawed together — the hull only has to read as a
    /// silhouette, since half of it is under water and the rest is in fog.</summary>
    private static void BuildPunt(Bake bake, Vector3 at, Color timber)
    {
        const float yaw = 24f;
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

        Vector3 Hull(float t, float across, float lift) =>
            at + rot * new Vector3(across, t * 0.34f + lift, (t - 0.5f) * 3.2f);

        for (int i = 0; i <= 6; i++)
        {
            float t = i / 6f;
            float half = Mathf.Sin(t * Mathf.PI) * 0.42f + 0.14f;
            bake.Box(Hull(t, 0f, 0f), new Vector3(half * 2f, 0.08f, 0.5f),
                     timber * Random.Range(0.8f, 1.15f), new Vector2(0.4f, 3f),
                     new Vector2(Random.value, Random.value), yaw);
        }

        for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 6; i++)
            {
                float t0 = i / 6f, t1 = (i + 1) / 6f;
                float h0 = Mathf.Sin(t0 * Mathf.PI) * 0.42f + 0.14f;
                float h1 = Mathf.Sin(t1 * Mathf.PI) * 0.42f + 0.14f;
                bake.Tube(Hull(t0, side * h0, 0.13f), Hull(t1, side * h1, 0.13f),
                          0.05f, 0.05f, timber * 0.8f, 5, 1);
            }
    }

    // ------------------------------------------------------------------ bamboo

    /// <summary>
    /// How much detail one tier of the grove is allowed to buy. The whole budget argument lives
    /// in this table: a culm at z 12 paying for geometric node rings is paying for something no
    /// pixel ever shows, and that money is what the foliage was missing.
    /// </summary>
    private readonly struct Tier
    {
        public readonly int Sides;
        public readonly float NodeSwell;   // 0 = the node comes from the bark texture alone
        public readonly int Rings;         // loops for the bow, when the nodes are not geometry
        public readonly int BranchPairs;
        public readonly float LeafLength;  // metres, not a fraction of the culm
        public readonly int Cards;
        public readonly float CardSize;
        /// <summary>Which atlas cells this tier's cards may draw, as a first cell and a count.
        /// A property of the tier, not something to infer from the card count: the old rule —
        /// two cards or fewer means the dense far-distance blob — would hand the near tier a
        /// thirty-metre silhouette at six.</summary>
        public readonly int CellFirst;
        public readonly int CellCount;
        public readonly float RadiusMin;
        public readonly float RadiusMax;
        public readonly float HeightMin;
        public readonly float HeightMax;

        public Tier(int sides, float nodeSwell, int rings, int branchPairs, float leafLength,
                    int cards, float cardSize, int cellFirst, int cellCount, float radiusMin,
                    float radiusMax, float heightMin, float heightMax)
        {
            Sides = sides;
            NodeSwell = nodeSwell;
            Rings = rings;
            BranchPairs = branchPairs;
            LeafLength = leafLength;
            Cards = cards;
            CardSize = cardSize;
            CellFirst = cellFirst;
            CellCount = cellCount;
            RadiusMin = radiusMin;
            RadiusMax = radiusMax;
            HeightMin = heightMin;
            HeightMax = heightMax;
        }
    }

    /// <summary>Fraction of a culm that stays bare. Bamboo branches from the top half and only
    /// from the top half; foliage running down toward the root is what turns it into a bush.
    /// </summary>
    private const float BranchStart = 0.5f;

    // Heights are set by what the camera can see, not by what bamboo does. At tier 1 the frame
    // spans roughly y −2.3 to 4.6, so a fifteen-metre culm carries every leaf it has above the
    // top of the picture: the first pass built a full grove and showed bare poles. Tier 1 is
    // therefore a stand of young culms, tall enough to leave the frame but low enough that its
    // crown is inside it.
    // Culms run 8–21 cm where the reference photographs are 3–12, and the leaves are longer still.
    // Deliberately out of reference: the grove reads as old and heavy rather than as young growth,
    // and at the distance the camera keeps, life-size bamboo reads as reeds.
    // Crowns pay for that and for the tripled culm count — two branch pairs instead of five, four
    // to six leaves instead of five to nine. A leaf 1.5× longer covers 2.25× the area, so two pairs
    // of the larger leaf fill about the crown five pairs of the smaller one did, for 40% of the
    // triangles. Height is the one dimension left alone: it is set by where the frame ends, not by
    // what bamboo does, and scaling it would put the crowns back above the top of the picture.
    // The near tier carries no branches any more. Its crowns were 168 triangles a culm across 195
    // culms — 32 760, forty-seven per cent of the whole grove — of twig and geometric blade, eight
    // to eighteen metres from the lens, standing next to a photographed bark map that made every
    // one of those blades read as a painted stroke. Two cards from the atlas replace them for
    // 3 120. The frame edge keeps its geometry: there the crown is 12.7% of the culm's cost and it
    // is four metres away, and EdgeTier.Rings is 0 only because NodeSwell is not — drop the swell
    // without raising the rings and those six culms vanish from the mesh.
    //                                          sides swell rings pairs leaf  cards size  cell0 cells rMin    rMax    hMin  hMax
    private static readonly Tier EdgeTier = new Tier(8, 0.09f,   0,    2, 0.315f, 0, 0f,    0, 0, 0.0975f, 0.146f, 9f,  14f);
    private static readonly Tier NearTier = new Tier(6, 0f,      6,    0, 0f,     2, 2.0f,  7, 2, 0.039f, 0.107f,  4.5f, 8f);
    private static readonly Tier MidTier  = new Tier(6, 0f,      2,    0, 0f,     3, 2.25f, 0, 3, 0.068f, 0.117f,  6f,  11f);
    private static readonly Tier FarTier  = new Tier(6, 0f,      1,    0, 0f,     1, 3.4f,  3, 1, 0.068f, 0.117f,  8f,  15f);
    private static readonly Tier ShootTier = new Tier(6, 0f,     6,    1, 0.34f,  0, 0f,    0, 0, 0.029f, 0.049f,  1.5f, 3f);

    /// <summary>The reference bamboo hue carried to a luminance the tier already had. Only the
    /// hue and the saturation were wrong: the tonal ladder between tiers is what carries depth
    /// in a scene this foggy, and it was tuned against the fighters.</summary>
    private static Color Culm(float r, float g, float b, float luminance, float saturation = 1f)
    {
        float l = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        float k = luminance / Mathf.Max(l, 1e-4f);
        r *= k;
        g *= k;
        b *= k;
        // Pushed away from its own luminance, so purity rises and the tier's place on the tonal
        // ladder does not move. Scaling the colour instead would have brightened it, and the
        // ladder between tiers is the only thing carrying depth in a scene this foggy.
        if (saturation != 1f)
        {
            r = Mathf.Max(0f, luminance + (r - luminance) * saturation);
            g = Mathf.Max(0f, luminance + (g - luminance) * saturation);
            b = Mathf.Max(0f, luminance + (b - luminance) * saturation);
        }
        return Srgb(r, g, b);
    }

    /// <summary>The ladder these tints exist to build, checked rather than trusted. Every retune of
    /// the grove is done by eye against a frame, and each rule below has already cost a build once:
    /// an equal saturation cut across the tiers leaves the farthest plane the purest green in frame,
    /// turf pitched under the grove vanishes into the bank's cast shadow, and a near tier dropped to
    /// the edge culms' value merges with them into one black curtain. All three are arithmetic, so
    /// the bake can say so instead of the next screenshot.</summary>
    private static void CheckFoliageLadder(Color edge, Color near, Color mid, Color far)
    {
        float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        float Sat(Color c)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            return max <= 1e-4f ? 0f : (max - Mathf.Min(c.r, Mathf.Min(c.g, c.b))) / max;
        }

        if (Sat(near) <= Sat(mid) || Sat(mid) <= Sat(far))
            Debug.LogError($"BambooArena: foliage saturation must fall with distance, got near " +
                           $"{Sat(near):P0}, mid {Sat(mid):P0}, far {Sat(far):P0} — the haze reads " +
                           "inverted and the farthest plane is the purest green in frame.");

        if (Lum(Turf) <= Lum(near))
            Debug.LogError($"BambooArena: turf {Lum(Turf):F3} must stay brighter than the near " +
                           $"grove {Lum(near):F3}, or the bank loses it to the bridge's shadow.");

        if (Lum(edge) >= Lum(near))
            Debug.LogError($"BambooArena: frame-edge culms {Lum(edge):F3} must stay darker than " +
                           $"the near grove {Lum(near):F3}, or the two merge into one black curtain.");

        // Printed every bake, not just on failure: every spec in docs/ argues from a table of
        // measured luminance, and the numbers being in the log is what makes the next retune an
        // argument about evidence rather than about what the frame felt like.
        //
        // These are linear, because Culm() ends in Srgb() and that is what the vertex stream
        // carries. They read darker and far purer than the sRGB figures the spec tabulates — near
        // logs 0.108/70% where the eye is shown 0.360/45%. Comparing a number here against a
        // number there is the mistake this sentence exists to stop.
        Debug.Log($"BambooArena: foliage ladder, linear — " +
                  $"edge {Lum(edge):F3}/{Sat(edge):P0}, near {Lum(near):F3}/{Sat(near):P0}, " +
                  $"turf {Lum(Turf):F3}/{Sat(Turf):P0}, mid {Lum(mid):F3}/{Sat(mid):P0}, " +
                  $"far {Lum(far):F3}/{Sat(far):P0} (luminance/saturation).");
    }

    // ponytail: the grove is the frame's main cost. If a real phone disagrees, cut the tier-3
    // cluster count first — fog is already doing most of that tier's work.
    private static void BuildBamboo(Bake bake, Bake cards)
    {
        // Four steps of brightness, and now of saturation too. Depth here comes from the tonal
        // step between tiers, not from blur: a background that is merely out of focus still reads
        // flat, while separated values read as separate planes even when sharp.
        //
        // Saturation is what made the grove read as painted rather than photographed. A leaf under
        // open sky has sky falling into its shade, so its blue channel never collapses; at 1.30-1.35
        // these tiers held blue at a seventh of green and landed at 77-82% saturation, where foliage
        // in soft light sits at 30-45%. The multipliers are below one now, and in Culm() that lifts
        // blue toward the tier's own luminance without moving the tier along the ladder.
        Color edge = Culm(0.561f, 0.659f, 0.235f, 0.100f, 0.76f);  // #8FA83C at the near-black level
        // 0.360, down from the 0.45 this tier was lifted to once the crowns filled with foliage.
        // That lift was right against a grove at 77% saturation: the tier was carrying the left and
        // right thirds of the frame and had nothing but value to separate it from the frame-edge
        // culms. With the green cut back it reads as a plane at a darker value, and the step out to
        // the far tiers grows rather than shrinks. What must not come back is 0.32, where this tier
        // and the edge culms merged into one black curtain.
        Color near = Culm(0.659f, 0.690f, 0.290f, 0.360f, 0.77f);  // #A8B04A, the mature culm
        // The far tiers keep their luminance almost exactly — they stand where the fog does, and
        // the distance has to end in sky. Their saturation moves least of the four in absolute
        // terms, and that is the point rather than an oversight: haze had already taken most of
        // their chroma, so they have the least left to give. Cutting every tier by the same amount
        // is the obvious move and it inverts the ladder — the two planes that started cleanest end
        // up the purest green in frame. What has to survive is the order, 45% near / 32% mid /
        // 21% far, not the size of any single cut.
        Color mid = Color.Lerp(Srgb(0.46f, 0.50f, 0.35f),
                               Culm(0.659f, 0.690f, 0.290f, 0.386f, 0.56f), 0.70f);
        Color far = Color.Lerp(Srgb(0.62f, 0.66f, 0.55f),
                               Culm(0.659f, 0.690f, 0.290f, 0.636f, 0.48f), 0.35f);

        CheckFoliageLadder(edge, near, mid, far);

        // Culms at the frame edges. At z -3.5..-2 the frame is under 9.6 units wide however the
        // camera pans, so |x| >= 3.4 can only ever cross its outer edge. Rooted below the water
        // line, which at that depth is beneath the bottom of the frame, so they read as stalks
        // passing the lens rather than bamboo growing out of a river. These six are the only
        // culms whose nodes are worth building out of triangles, and the only ones dark enough
        // to anchor the bottom of the tonal range — bright foreground bamboo would out-read the
        // fighters, which is the one thing the frame cannot afford.
        for (int i = 0; i < 6; i++)
        {
            float x = Random.Range(3.4f, 5.2f) * (i % 2 == 0 ? 1f : -1f);
            var root = new Vector3(x, WaterY - 1.2f, Random.Range(-3.5f, -2f));
            Stalk(bake, cards, root, Random.Range(EdgeTier.HeightMin, EdgeTier.HeightMax),
                  Random.Range(EdgeTier.RadiusMin, EdgeTier.RadiusMax),
                  edge * Random.Range(0.85f, 1.2f), EdgeTier, RandomLean(0.14f), 0.05f);
        }

        // Tier 1, the near grove, on the banks either side of the channel. Deliberately absent
        // from the middle of the frame — behind the fighters there has to be pale empty mist, or
        // they sink into the forest.
        // The strip is wider than the frame reaches on purpose now: three times the culms packed
        // into the old x 6.6..11 band came out as a solid wall, and a grove differs from a wall in
        // that you can see through it.
        for (int c = 0; c < 78; c++)
        {
            float sign = c % 2 == 0 ? 1f : -1f;
            float cx = sign * Random.Range(6.4f, 13f);
            float cz = Random.Range(6.5f, 16f);
            Cluster(bake, cards, cx, cz, Random.Range(1, 5), near, NearTier);
        }

        // Tier 2: culms are plain tubes, the foliage is cards, a clear step lighter.
        for (int c = 0; c < 78; c++)
        {
            float sign = c % 2 == 0 ? 1f : -1f;
            float cx = sign * Random.Range(6f, 26f);
            float cz = Random.Range(18f, 30f);
            Cluster(bake, cards, cx, cz, Random.Range(2, 6), mid, MidTier);
        }

        // Tier 3: almost the colour of the sky. Past the point where the channel closes they
        // cross the middle too, so the corridor of water ends in bamboo rather than in an open
        // horizon.
        for (int c = 0; c < 90; c++)
        {
            float cz = Random.Range(34f, 50f);
            float limit = cz > ChannelEndZ ? 0f : ShoreX;
            float cx = Random.Range(limit, 30f) * (c % 2 == 0 ? 1f : -1f);
            Cluster(bake, cards, cx, cz, Random.Range(3, 7), far, FarTier);
        }

        BuildShoots(bake, near);
    }

    /// <summary>
    /// Low young shoots, filling the bottom of the grove where the tall culms are bare.
    ///
    /// There were leaning fallen culms here too, on the theory that a diagonal crossing the
    /// verticals reads as natural. It does not: bamboo stands straight, and a stick lying across
    /// the grove at sixty degrees reads as a stick, not as bamboo. Tripling them for density made
    /// it unmissable. The shoots that remain are nearly upright for the same reason — a two-metre
    /// stalk with twenty centimetres of bend in it is a twig.
    /// </summary>
    private static void BuildShoots(Bake bake, Color color)
    {
        for (int i = 0; i < 36; i++)
        {
            float x = (i % 2 == 0 ? 1f : -1f) * Random.Range(6.6f, 12.5f);
            float z = Random.Range(6.5f, 15f);
            Stalk(bake, null, new Vector3(x, Ground(x, z), z),
                  Random.Range(ShootTier.HeightMin, ShootTier.HeightMax),
                  Random.Range(ShootTier.RadiusMin, ShootTier.RadiusMax),
                  color * Random.Range(1.1f, 1.3f), ShootTier, RandomLean(0.09f), 0.035f);
        }
    }

    /// <summary>A clump rather than an even spacing. Two close together, a gap, three, a bigger
    /// gap — an even step is what turns bamboo into a picket fence.</summary>
    private static void Cluster(Bake bake, Bake cards, float cx, float cz, int count, Color color,
                                in Tier tier)
    {
        float spread = 0.7f + count * 0.32f;
        for (int i = 0; i < count; i++)
        {
            float x = cx + Random.Range(-spread, spread);
            float z = cz + Random.Range(-spread, spread);
            // Young culms are yellow-green, old ones go to ochre and straw. A grove of one
            // colour is the single most obvious tell that it was generated.
            Color tint = color * Random.Range(0.82f, 1.18f);
            float age = Random.value;
            tint = new Color(tint.r * (1f + age * 0.28f), tint.g * (1f + age * 0.1f),
                             tint.b * (1f - age * 0.3f), 1f);
            // Thickness stepped across the clump rather than drawn from the range three times:
            // three independent draws land close together often enough that half the clumps came
            // out uniform, which is exactly the tell this is meant to remove.
            float radius = Mathf.Lerp(tier.RadiusMin, tier.RadiusMax, i % 3 / 2f)
                         * Random.Range(0.9f, 1.12f);
            Stalk(bake, cards, new Vector3(x, Ground(x, z), z),
                  Random.Range(tier.HeightMin, tier.HeightMax), radius,
                  tint, tier, RandomLean(0.14f), 0.06f);
        }
    }

    /// <summary>Bank growth, one step above the near tier in luminance. Brighter than the culms
    /// standing over it, not darker: the ground it stands on is the darkest surface in the scene,
    /// and grass pitched below the grove disappeared into it. The +0.070 gap to the near tier is
    /// the invariant, not either endpoint — both moved down together when the green came out, and
    /// <see cref="CheckFoliageLadder"/> fails the bake if the gap ever closes.</summary>
    private static Color Turf => Culm(0.561f, 0.659f, 0.235f, 0.430f, 0.65f);

    /// <summary>Wind phase taken from where a clump stands rather than at random. Grass moves in
    /// waves crossing the bank; a grove does not, which is why the culms keep their random phase.
    /// The same shader channel serves both — only where the number comes from differs.</summary>
    private static float GrassPhase(float x, float z) =>
        Mathf.Repeat(x * 0.085f + z * 0.055f, 1f);

    private static Vector2 RandomLean(float fraction)
    {
        float a = Random.value * Mathf.PI * 2f;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(0.3f, 1f) * fraction;
    }

    /// <summary>
    /// One culm, its branches and its foliage. Four things make a green tube read as bamboo and
    /// all of them are here: nodes the whole way up, near-constant diameter — bamboo does not
    /// taper the way a tree does — foliage carried on side branches rather than growing out of
    /// the culm, and a lower half that is completely bare.
    /// </summary>
    private static void Stalk(Bake bake, Bake cards, Vector3 root, float height, float radius,
                              Color color, in Tier tier, Vector2 lean, float bow)
    {
        float phase = Random.value;
        var top = new Vector3(root.x + lean.x * height, root.y + height, root.z + lean.y * height);
        Vector3 bowVec = new Vector3(lean.x, 0f, lean.y).normalized * (bow * height);
        // Node pitch varies between culms rather than along one, on every tier whose nodes are
        // painted: one texture cannot stretch a single internode without stretching them all, and
        // at eight units it is the rhythm between neighbouring culms that reads anyway.
        float pitch = ArenaTextures.BarkNodeSpacing * Random.Range(0.85f, 1.15f);

        // 0.88, not 0.5: a culm keeps almost the same diameter for its whole length. Tapering
        // it like a tree trunk is why an earlier pass read as saplings rather than bamboo.
        bake.Tube(root, top, radius, radius * 0.88f, color, tier.Sides, tier.Rings, bowVec,
                  pitch, tier.NodeSwell, phase, height);

        Vector3 Along(float t) => Vector3.Lerp(root, top, t) + bowVec * (4f * t * (1f - t));

        // Branches in pairs from a node, each pair turned 72° from the last, none below halfway.
        float turn = Random.value * Mathf.PI * 2f;
        for (int p = 0; p < tier.BranchPairs; p++)
        {
            float t = Mathf.Lerp(BranchStart, 0.97f, (p + Random.Range(0.15f, 0.85f)) / tier.BranchPairs);
            Vector3 node = Along(t);
            turn += 1.2566f; // 72°
            var outward = new Vector3(Mathf.Cos(turn), 0f, Mathf.Sin(turn));
            // One long, one short from the same node. The asymmetry is characteristic, and a
            // symmetric pair reads as a telegraph pole.
            float scale = Mathf.Lerp(0.75f, 1.3f, t);
            Branch(bake, node, outward, height * 0.085f * scale, tier.LeafLength, color, phase, t);
            Branch(bake, node, -outward, height * 0.055f * scale, tier.LeafLength * 0.85f, color, phase, t);
        }

        for (int c = 0; c < tier.Cards && cards != null; c++)
        {
            float t = Mathf.Lerp(BranchStart, 0.98f, (c + 0.5f) / tier.Cards);
            // Off the axis, by the reach the branches this replaces threw their foliage: a card
            // centred on the culm has the culm rendering straight through the middle of it, and
            // the jitter alone is ±0.16 m at the near tier's mean height.
            float cardTurn = Random.value * Mathf.PI * 2f;
            var outward = new Vector3(Mathf.Cos(cardTurn), 0f, Mathf.Sin(cardTurn))
                        * (height * 0.085f * Mathf.Lerp(0.75f, 1.3f, t));
            int cell = tier.CellFirst + Random.Range(0, tier.CellCount);
            cards.Card(Along(t) + outward + Random.insideUnitSphere * (height * 0.025f),
                       tier.CardSize * Random.Range(0.82f, 1.2f), cell, LeafCardRadii,
                       color * 1.12f, phase, t);
        }
    }

    /// <summary>A branch and the fan of leaves on its end. Bamboo carries its foliage on thin
    /// side branches and never straight off the culm; pucks of leaves stuck to the trunk is what
    /// makes a generated grove read as a bottle brush.</summary>
    private static void Branch(Bake bake, Vector3 node, Vector3 outward, float length,
                               float leafLength, Color color, float phase, float height)
    {
        Vector3 dir = (outward + Vector3.up * Random.Range(0.25f, 0.6f)).normalized;
        Vector3 droop = Vector3.down * (length * 0.3f);
        Vector3 tip = node + dir * length + droop;
        // windHeight, not the tube's own t: a branch has to sway with the point of the culm it
        // grows from, or its base sits still while its leaves fly off it.
        bake.Tube(node, tip, 0.014f, 0.006f, color * 0.8f, 3, 2, droop * 0.5f,
                  0f, 0f, phase, 0f, height, BarkClearV * 0.4f);
        Blades(bake, tip, leafLength, 0.15f, 0.75f, Random.Range(4, 7),
               color * 1.25f, phase, height, height);
    }

    // ------------------------------------------------------------------ banks and undergrowth

    private static void BuildBanks(Bake bake)
    {
        // Multipliers, not colours, and so deliberately not run through Srgb(): albedo here is
        // texture × vertex colour, and a multiplier that goes through gamma conversion darkens
        // twice. The bank used to be untextured — flat vertex colour and nothing else, which is
        // why it read as a dark shelf no amount of grass could climb out of.
        Color mud = new Color(0.55f, 0.50f, 0.45f);
        Color moss = new Color(0.95f, 1.00f, 0.85f);

        const float minX = -22f, maxX = 22f, minZ = -12f, maxZ = 44f;
        const float step = 1.1f;
        int cols = Mathf.CeilToInt((maxX - minX) / step) + 1;
        int rows = Mathf.CeilToInt((maxZ - minZ) / step) + 1;

        var index = new int[cols, rows];
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
            {
                float x = minX + i * step;
                float z = minZ + j * step;
                float y = Ground(x, z);
                var normal = new Vector3(Ground(x - 0.4f, z) - Ground(x + 0.4f, z), 0.8f,
                                         Ground(x, z - 0.4f) - Ground(x, z + 0.4f)).normalized;
                float wetness = Mathf.InverseLerp(WaterY + 1f, WaterY - 0.2f, y);
                Color tint = Color.Lerp(moss, mud, wetness) * Random.Range(0.82f, 1.12f);
                // Cheap crevice occlusion: hollows are darker than ridges.
                tint *= Mathf.Lerp(0.72f, 1f, Mathf.InverseLerp(-0.5f, 0.6f, y));
                index[i, j] = bake.Push(new Vector3(x, y, z), normal,
                                        new Vector2(x * BankUvScale, z * BankUvScale),
                                        Vector2.zero, tint);
            }
        for (int i = 0; i < cols - 1; i++)
            for (int j = 0; j < rows - 1; j++)
            {
                bake.Tri(index[i, j], index[i, j + 1], index[i + 1, j]);
                bake.Tri(index[i + 1, j], index[i, j + 1], index[i + 1, j + 1]);
            }

        // Pebbles only, and deliberately so. Scanned boulders stood here for one build and were
        // cut: placed thickly enough to be seen at all, they read as a continuous grey kerb along
        // the waterline rather than as scattered stones, and they cost 32k triangles for it. At
        // pebble size a box is indistinguishable from a stone anyway.
        for (int i = 0; i < 45; i++)
        {
            float x = Random.Range(-16f, 16f);
            float z = Random.Range(-10f, 26f);
            float y = Ground(x, z);
            if (y < WaterY - 0.3f || y > WaterY + 1.1f)
                continue;
            float size = Random.Range(0.1f, 0.24f);
            Color rock = new Color(0.72f, 0.72f, 0.68f) * Random.Range(0.6f, 1.2f);
            bake.Box(new Vector3(x, y + size * 0.25f, z),
                     new Vector3(size, size * Random.Range(0.5f, 0.8f), size * Random.Range(0.7f, 1.3f)),
                     rock, new Vector2(0.8f, 0.8f), new Vector2(Random.value, Random.value),
                     Random.Range(0f, 60f));
        }
    }

    private static void BuildUndergrowth(Bake bake, Bake cards, Bake shade)
    {
        Color reed = Srgb(0.30f, 0.36f, 0.17f);
        Color fern = Srgb(0.20f, 0.28f, 0.13f);
        // Albedo was never the problem. Lifting it four times over moved the pads from (1, 5, 0)
        // to (8, 20, 3) — exactly four times, and still a hole in the water. What was wrong was
        // the winding of the fan, fixed in LilyPad. Facing the sky the leaf takes eight times
        // the light it did, so the constant comes down rather than up: darker and greyer than
        // any value tried against the broken geometry, because now it is a lit leaf and the
        // grade this scene runs has saturation pulled down and everything else in frame with it.
        // At this value the dark green in the near water reads 78 against the water's 135. The
        // number is a band average that also catches the bridge's shadow, so it is evidence that
        // the pads are no longer holes, not a measurement of one leaf.
        Color pad = Srgb(0.20f, 0.26f, 0.13f);
        Color bloom = Srgb(0.90f, 0.84f, 0.82f);

        // Both bands were x ±18, z -10..34 — most of that is outside anything the camera frames,
        // and it was being paid for. Pulled in to the strip that shows, and given the narrow
        // profile a reed actually has instead of the bamboo leaf's.
        for (int i = 0; i < 220; i++)
        {
            float x = Random.Range(-15f, 15f);
            float z = Random.Range(-6f, 20f);
            float y = Ground(x, z);
            if (y < WaterY - 0.15f || y > WaterY + 0.8f)
                continue;
            Blades(bake, new Vector3(x, y - 0.05f, z), Random.Range(0.4f, 1f), 2.4f, 0.5f, 6,
                   reed * Random.Range(0.75f, 1.2f), Random.value, 0f, 1f, 0.014f);
        }

        for (int i = 0; i < 120; i++)
        {
            float x = Random.Range(-15f, 15f);
            float z = Random.Range(-6f, 20f);
            float y = Ground(x, z);
            if (y < WaterY + 0.5f)
                continue;
            Blades(bake, new Vector3(x, y - 0.05f, z), Random.Range(0.3f, 0.7f), 0.9f, 0.7f, 6,
                   fern * Random.Range(0.75f, 1.15f), Random.value, 0f, 1f, 0.03f);
        }

        // Bank growth under the grove. The reeds and ferns above are spread over x ±18 and z -10
        // to 34, which puts almost none of them in the strip the camera actually frames, and they
        // are darker than the culms now standing over them — so the ground under the grove read as
        // a dark empty shelf. This pass is dense, confined to that strip, and pitched one step
        // below the near tier in luminance: it has to read as ground, not as more bamboo.
        // z starts at 2, not at the bridge: at the bridge's own depth the bank is already past the
        // edge of the frame, and an earlier attempt spent more than half its clumps where nothing
        // sees them. This band sits under the part of the grove the camera actually frames.
        // 0.430 — brighter than the culms standing over it (0.360), not darker. The bank lies in
        // the shadow the bridge throws, and at the value the ground itself has, grass painted a
        // step below the grove disappears into it. In the reference the bank growth is the lightest
        // thing in the bottom half of the frame; here it has a cast shadow to climb out of first.
        Color turf = Turf;

        // The near edge used to be drawn blades. It is scanned grass now — see BuildScanFoliage —
        // which keeps this band's wind phase and placement rules and only changes the geometry.

        // Behind it, drawn grass as cards. Nothing on this bank is closer than eight units, and
        // at that range forty painted blades read as forty blades for the price of eight
        // triangles — the density in the reference photograph is not affordable any other way.
        for (int i = 0; i < 260; i++)
        {
            float x = (i % 2 == 0 ? 1f : -1f) * Random.Range(6.8f, 14f);
            float z = Random.Range(8f, 18f);
            float y = Ground(x, z);
            if (y < WaterY + 0.15f || y > WaterY + 2.2f)
                continue;
            float size = Random.Range(1.6f, 2.6f);
            // The drawn clump roots near the bottom of its cell, so the card's centre has to ride
            // above the ground by the distance from that root to the cell's middle.
            cards.Card(new Vector3(x, y + size * 0.44f, z), size,
                       ArenaTextures.GrassCellFirst + Random.Range(0, 3), LeafCardRadii,
                       turf * Random.Range(0.82f, 1.18f), GrassPhase(x, z), 0.45f);
        }

        // Lily pads. A water lily grows from a rhizome on the bed, so its leaves come out in a
        // clump around one point — which is why an even sowing across the whole channel is the
        // most visible placement mistake there is, and it is what stood here: 240 leaves at
        // uniform random, up to 92 cm across. A Nymphaea leaf is 10-28 cm. Fifteen clumps of four
        // to six at that size is roughly 75 leaves, which leaves the jade water visible between
        // them instead of paving it over.
        //
        // Rejection sampling rather than a lattice: the clumps have to keep 1.5 m apart *and* land
        // where the bed is deep enough, and the two conditions do not compose into a formula.
        var rhizomes = new List<Vector2>();
        for (int attempt = 0; attempt < 300 && rhizomes.Count < 15; attempt++)
        {
            var root = new Vector2(Random.Range(-12f, 12f),
                                   Mathf.Lerp(-10f, 26f, Random.value * Random.value));
            // Not on the shallows at the very edge: the old test was 5 cm of water, which put
            // leaves where the bank is about to surface through them.
            if (Ground(root.x, root.y) > WaterY - 0.15f)
                continue;
            bool crowded = false;
            foreach (Vector2 other in rhizomes)
                crowded |= (other - root).sqrMagnitude < 1.5f * 1.5f;
            if (crowded)
                continue;
            rhizomes.Add(root);
            LilyClump(bake, shade, root, pad, bloom);
        }
        if (rhizomes.Count < 12)
            Debug.LogError($"BambooArena: only {rhizomes.Count} lily clumps placed of 15 — the " +
                           $"spacing test or the depth test is rejecting nearly everything.");
    }

    /// <summary>
    /// One rhizome's worth of leaves, and at best one flower.
    ///
    /// Positions come from phyllotaxis — golden angle, distance growing as sqrt(k) — because that
    /// is literally how the plant lays its leaves out, and it packs a clump with no two leaves at
    /// the same distance and no gaps in the middle. The constant in front of it sets the crowding:
    /// at 2.0 mean radii neighbours overlap by about 15%, which is what a photograph of a real
    /// clump shows and what no amount of jittered scatter produces.
    /// </summary>
    private static void LilyClump(Bake bake, Bake shade, Vector2 root, Color pad, Color bloom)
    {
        // Three size classes, not a continuous range: real leaves grow in distinct stages, and a
        // smooth spread of sizes reads as noise. One mature leaf per clump, never two.
        var radii = new List<float> { Random.Range(0.11f, 0.14f) };
        for (int i = 0, n = Random.Range(2, 4); i < n; i++)
            radii.Add(Random.Range(0.07f, 0.11f));
        for (int i = 0, n = Random.Range(1, 3); i < n; i++)
            radii.Add(Random.Range(0.05f, 0.07f));

        const float golden = 2.399963f;
        float mean = 0f;
        foreach (float r in radii)
            mean += r / radii.Count;
        float spin = Random.value * Mathf.PI * 2f;

        for (int k = 0; k < radii.Count; k++)
        {
            float a = spin + k * golden;
            float d = 2f * mean * Mathf.Sqrt(k);
            float x = root.x + Mathf.Cos(a) * d;
            float z = root.y + Mathf.Sin(a) * d;
            if (Ground(x, z) > WaterY - 0.08f)
                continue;
            // The petiole enters the leaf at the sinus and runs down to the rhizome, so the notch
            // faces the middle of the clump. Getting this wrong is subtle and reads as wrong: a
            // ring of leaves with their notches pointing outward looks like a scatter of shapes
            // rather than like one plant. The leaf sitting on the rhizome has no direction to face.
            float notch = k == 0
                ? Random.value * Mathf.PI * 2f
                : Mathf.Atan2(root.y - z, root.x - x) + Random.Range(-0.7f, 0.7f);
            var centre = new Vector3(x, WaterY + 0.02f + Random.Range(-0.005f, 0.005f), z);
            LilyPad(bake, centre, radii[k], notch, pad * Random.Range(0.82f, 1.18f));
            LilyShade(shade, centre, radii[k]);
        }

        // Roughly one flower per nine leaves. The real ratio is one per five to eight; this lands
        // just under it because a white bloom is by a long way the brightest thing on this water,
        // and eight of them in frame is already where the eye goes first.
        if (Random.value < 0.55f)
        {
            float a = Random.value * Mathf.PI * 2f;
            float d = Random.Range(0.12f, 0.3f);
            float x = root.x + Mathf.Cos(a) * d;
            float z = root.y + Mathf.Sin(a) * d;
            if (Ground(x, z) < WaterY - 0.08f)
                LilyBloom(bake, new Vector3(x, WaterY + 0.045f, z), Random.Range(0.05f, 0.075f),
                          bloom);
        }
    }

    // ------------------------------------------------------------------ scanned props

    private const string ScanDir = Dir + "Scan/";

    /// <summary>First mesh inside an imported model, or null with a loud error.</summary>
    private static Mesh LoadScanMesh(string file)
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(ScanDir + file))
            if (o is Mesh m)
                return m;
        Debug.LogError($"BambooArena: no mesh inside {ScanDir}{file}.");
        return null;
    }

    /// <summary>
    /// Copies an imported mesh into one of our bakes, scaled, turned and placed — and writes our
    /// vertex stream over it rather than keeping the model's.
    ///
    /// This is the whole reason the scanned props cost nothing architecturally. Instantiated as
    /// prefabs they would each arrive with their own material and their own draw call, and with
    /// no uv1 they would stand dead still while everything around them moves. Grafted, they land
    /// in the same mesh as everything else, take the arena's colour and take its wind.
    ///
    /// Height, not width, sets the scale: <paramref name="size"/> is how tall the thing should
    /// stand, so whatever units the exporter used stop mattering.
    /// </summary>
    /// <param name="atlasCell">Cell in the scanned-foliage atlas, or −1 for a prop that takes the
    /// ground texture through a world-space projection — which is what makes a grafted boulder sit
    /// in the bank instead of on it.</param>
    /// <param name="weld">Grid, in metres of the placed prop, that vertices are snapped and merged
    /// onto before they enter the mesh. These are archviz models — a boulder arrives with three
    /// thousand triangles, which at twelve units is about ten triangles per pixel of silhouette.
    /// Welding is crude but exactly right for a blob: it collapses the interior and leaves the
    /// outline. Pass 0 for anything whose shape lives in its alpha, where welding would eat the
    /// cards themselves.</param>
    private static void Graft(Bake bake, Mesh mesh, Vector3 root, float size, float yaw,
                              Color tint, int atlasCell, float phase, float weld = 0f)
    {
        if (mesh == null)
            return;
        Vector3[] verts = mesh.vertices;
        Vector3[] norms = mesh.normals;
        Vector2[] uvs = mesh.uv;
        int[] tris = mesh.triangles;
        if (verts.Length == 0 || tris.Length == 0)
            return;

        Bounds b = mesh.bounds;
        float scale = size / Mathf.Max(b.size.y, 1e-4f);
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
        var pivot = new Vector3(b.center.x, b.min.y, b.center.z);
        var cell = new Vector2(atlasCell % 2, atlasCell / 2) * 0.5f;

        var map = new int[verts.Length];
        var welded = weld > 0f ? new Dictionary<Vector3Int, int>(verts.Length) : null;
        var cellKey = default(Vector3Int);
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 local = rot * ((verts[i] - pivot) * scale);
            if (welded != null)
            {
                cellKey = new Vector3Int(Mathf.RoundToInt(local.x / weld),
                                         Mathf.RoundToInt(local.y / weld),
                                         Mathf.RoundToInt(local.z / weld));
                if (welded.TryGetValue(cellKey, out int existing))
                {
                    map[i] = existing;
                    continue;
                }
                local = (Vector3)cellKey * weld;
            }
            Vector3 p = root + local;
            Vector3 n = norms.Length == verts.Length ? rot * norms[i] : Vector3.up;
            float h = Mathf.Clamp01(local.y / Mathf.Max(size, 1e-4f));

            Vector2 uv;
            Vector2 wind;
            Color c;
            if (atlasCell >= 0)
            {
                Vector2 src = uvs.Length == verts.Length ? uvs[i] : Vector2.zero;
                uv = cell + new Vector2(Mathf.Clamp01(src.x), Mathf.Clamp01(src.y)) * 0.5f;
                wind = new Vector2(phase, h);
                c = tint * Mathf.Lerp(0.55f, 1.15f, h); // same base-to-tip gradient the blades use
            }
            else
            {
                uv = new Vector2(p.x * BankUvScale, p.z * BankUvScale); // the bank's own projection
                wind = Vector2.zero;
                c = tint;
            }
            map[i] = bake.Push(p, n, uv, wind, c);
            if (welded != null)
                welded[cellKey] = map[i];
        }
        for (int t = 0; t + 2 < tris.Length; t += 3)
        {
            int a = map[tris[t]], b2 = map[tris[t + 1]], c2 = map[tris[t + 2]];
            // Welding collapses whole triangles onto a line or a point. Dropping them is the
            // decimation.
            if (a != b2 && b2 != c2 && a != c2)
                bake.Tri(a, b2, c2);
        }
    }

    /// <summary>
    /// Scanned grass, ferns and shrubs on the banks. Placed by the same rules the drawn grass
    /// used, and carrying the same wind phase taken from world position, so the wave still
    /// crosses the bank — the geometry changed, the motion did not.
    /// </summary>
    private static void BuildScanFoliage(Bake scan)
    {
        Mesh grass = LoadScanMesh("grass_medium_02.fbx");
        Mesh fern = LoadScanMesh("fern_02.fbx");
        Mesh shrub = LoadScanMesh("shrub_02.fbx");

        // Near neutral, and deliberately not the turf tint the drawn grass uses. These plants carry
        // their colour in a photograph; multiplying that by a green tint multiplies two greens and
        // sends them black. The vertex colour here is a multiplier, nothing more — the same
        // mistake the decking and the bank each cost a build to learn.
        //
        // Measured over the opaque texels of T_ScanFoliage.png, the photograph averages
        // 0.423 / 0.395 / 0.251 — red above green, an olive, at 42% median saturation. It was
        // already the most photographic green in the arena, and the old tint was the thing making
        // it shout: all three channels above one, with G the largest of them, brightening the plate
        // and pushing it green at the same time. Below one now, G smallest and B above it, which
        // lifts the shade the same way the culm tints do.
        //
        // The multiplier lands on an albedo the sampler has already decoded to linear, so it moves
        // value far more than it moves chroma: perceived luminance 0.403 to 0.363, median
        // saturation only 44.5% to 41.6%. That is the whole correction this plate needs. It was
        // never the thing shouting green — at 42% it was already the most photographic foliage in
        // the arena, and it only had to come down to meet the drawn turf that now sits at 43% and
        // 0.430. Landing just under the turf is right: this geometry shadows itself.
        Color lit = new Color(0.88f, 0.85f, 0.90f);

        // The window is z 9..18 at |x| 6.5..9.5, and it was measured rather than guessed: the
        // bank positions were projected through the fight camera (FOV 32, height 1.15, tilt 2.5°,
        // distance 6 to 8.5) to find where it actually lands on screen. Nearer than z 9 the bank
        // is hidden behind the deck edge and the railing, however wide the x band is. Three
        // separate passes of bank dressing were placed at z 2..10 and were invisible for this
        // reason alone.
        for (int i = 0; i < 16; i++)
        {
            float x = (i % 2 == 0 ? 1f : -1f) * Random.Range(6.7f, 9.2f);
            float z = Random.Range(9f, 17f);
            float y = Ground(x, z);
            if (y < WaterY + 0.15f || y > WaterY + 2.2f)
                continue;
            Graft(scan, grass, new Vector3(x, y - 0.05f, z), Random.Range(0.9f, 1.7f),
                  Random.Range(0f, 360f), lit * Random.Range(0.85f, 1.12f), 0, GrassPhase(x, z));
        }

        for (int i = 0; i < 10; i++)
        {
            float x = (i % 2 == 0 ? 1f : -1f) * Random.Range(6.7f, 9.5f);
            float z = Random.Range(9f, 17f);
            float y = Ground(x, z);
            if (y < WaterY + 0.2f || y > WaterY + 2.2f)
                continue;
            bool bushy = i % 3 == 0;
            Graft(scan, bushy ? shrub : fern, new Vector3(x, y - 0.05f, z),
                  bushy ? Random.Range(1.1f, 1.9f) : Random.Range(0.7f, 1.2f),
                  Random.Range(0f, 360f), lit * Random.Range(0.8f, 1.05f),
                  bushy ? 2 : 1, GrassPhase(x, z));
        }
    }

    // ------------------------------------------------------------------ water

    /// <summary>
    /// The water plane, with a foam mask baked into its vertex colours.
    ///
    /// Foam is normally read from the depth texture, comparing the scene behind the surface to
    /// the surface itself. That does not work here: the water is in the opaque queue, so
    /// _CameraDepthTexture already contains the water and the mask comes out zero. Moving it to
    /// the transparent queue fixes the mask and breaks the depth of field, which reads the same
    /// buffer. But every piece of this arena is generated and every position is known at build
    /// time — the piles are four pairs of coordinates and the shoreline is <see cref="Ground"/>
    /// — so the distance to the water's edge can simply be computed here and stored.
    ///
    /// The grid is deliberately non-uniform: 0.4 units across the stretch the camera sees the
    /// foam edge on, and metres wide out in the fog where nothing resolves.
    /// </summary>
    private static void BuildWater(GameObject root)
    {
        float[] xs = GridAxis(-40f, 40f, -10f, 10f, 0.4f, 2f);
        float[] zs = GridAxis(-14f, 40f, -8f, 10f, 0.4f, 1.8f);
        int cols = xs.Length, rows = zs.Length;

        var verts = new Vector3[cols * rows];
        var colors = new Color[cols * rows];
        var tris = new List<int>((cols - 1) * (rows - 1) * 6);
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
            {
                int v = i * rows + j;
                verts[v] = new Vector3(xs[i], 0f, zs[j]);
                colors[v] = new Color(FoamAt(xs[i], zs[j]), BedDepthAt(xs[i], zs[j]), 0f, 1f);
            }

        // The shader reads the bed depth out of vertex g and divides by the view ray's vertical
        // component to get the path length through the water. Zero there is not a dark river, it
        // is no river at all: the path collapses, the body term vanishes and the surface goes back
        // to being a mirror over a flat colour. Two points pin it — mid-channel, where the bed is a
        // known 0.7 below the surface, and the bank top, where there is no water above the ground.
        float midChannel = colors[IndexOf(xs, zs, 0f, 0f)].g;
        float bankTop = colors[IndexOf(xs, zs, 12f, 0f)].g;
        if (Mathf.Abs(midChannel - 0.7f) > 0.01f || bankTop > 0.001f)
            Debug.LogError($"BambooArena: water vertex g carries no bed depth — mid-channel " +
                           $"{midChannel:F3} (want 0.700), bank top {bankTop:F3} (want 0.000).");

        for (int i = 0; i < cols - 1; i++)
            for (int j = 0; j < rows - 1; j++)
            {
                int a = i * rows + j;
                tris.Add(a); tris.Add(a + 1); tris.Add(a + rows);
                tris.Add(a + rows); tris.Add(a + 1); tris.Add(a + rows + 1);
            }

        var mesh = new Mesh { name = "ArenaWater", indexFormat = IndexFormat.UInt32 };
        mesh.vertices = verts;
        mesh.colors = colors;
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        SaveMesh(mesh, "M_ArenaWater.mesh");

        var go = new GameObject("Water");
        go.transform.SetParent(root.transform, false);
        go.transform.position = new Vector3(0f, WaterY, 0f);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = WaterMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        go.AddComponent<Mikey.Fight.WaterReflection>();
    }

    /// <summary>Foam strength at a point on the surface: 1 hard against a pile or the shore,
    /// falling off over half a metre. The shader cuts this against scrolling noise, so the band
    /// it produces on screen is far narrower and far more ragged than the grid that carries it.
    /// </summary>
    private static float FoamAt(float x, float z)
    {
        float shore = 1f - Mathf.Clamp01((WaterY - Ground(x, z)) / 0.6f);

        float piles = 0f;
        foreach (float px in PileX)
            for (int i = -1; i <= 1; i += 2)
            {
                float d = new Vector2(x - px, z - i * PileWaterZ).magnitude;
                piles = Mathf.Max(piles, 1f - Mathf.Clamp01(d / 0.5f));
            }

        // 0.4, down from 0.9. The shore band of foam was a prop: it hid the hard line where the
        // water plane cuts the bank. The body of the water dissolves that line properly now —
        // depth goes to zero at the edge and the water becomes the bank — so what is left here is
        // only as much scum as a slow river actually collects against a shore.
        return Mathf.Clamp01(Mathf.Max(shore * 0.4f, piles));
    }

    /// <summary>Vertex index of the grid point nearest a world XZ position. Only the self-checks
    /// use it: the grid is non-uniform, so there is no arithmetic that maps a coordinate to an
    /// index.</summary>
    private static int IndexOf(float[] xs, float[] zs, float x, float z)
    {
        int i = 0, j = 0;
        for (int k = 1; k < xs.Length; k++)
            if (Mathf.Abs(xs[k] - x) < Mathf.Abs(xs[i] - x)) i = k;
        for (int k = 1; k < zs.Length; k++)
            if (Mathf.Abs(zs[k] - z) < Mathf.Abs(zs[j] - z)) j = k;
        return i * zs.Length + j;
    }

    /// <summary>Coordinates along one axis, fine through a window and coarse outside it.</summary>
    private static float[] GridAxis(float min, float max, float fineMin, float fineMax,
                                    float fine, float coarse)
    {
        var values = new List<float> { min };
        float p = min;
        while (p < max - 1e-3f)
        {
            p += p >= fineMin - fine && p <= fineMax ? fine : coarse;
            values.Add(Mathf.Min(p, max));
        }
        return values.ToArray();
    }

    // ------------------------------------------------------------------ occlusion

    /// <summary>
    /// Ray-traced ambient occlusion baked into the vertex colours. Not lightmaps: generated
    /// meshes have no UV2, an atlas would cost memory a phone does not have, and the whole arena
    /// already carries its colour per vertex — so occlusion rides along for free at runtime.
    ///
    /// Only the timber is traced. It is where occlusion reads: the shadow line between boards,
    /// under the rails, where a prop meets the ground. Foliage gets a height-based approximation
    /// while it is built, which is all a leaf strip can resolve anyway.
    /// </summary>
    private static void BakeOcclusion(Mesh target, Mesh[] occluders)
    {
        var holder = new GameObject("~AOColliders") { hideFlags = HideFlags.HideAndDontSave };
        foreach (Mesh m in occluders)
        {
            var child = new GameObject(m.name) { hideFlags = HideFlags.HideAndDontSave };
            child.transform.SetParent(holder.transform, false);
            child.AddComponent<MeshCollider>().sharedMesh = m;
        }
        Physics.SyncTransforms();

        // A fixed spiral of directions rather than random ones: the same mesh must bake to the
        // same colours every run, or every rebuild silently changes the look.
        const int rays = 14;
        const float radius = 0.9f;
        var directions = new Vector3[rays];
        for (int i = 0; i < rays; i++)
        {
            float t = (i + 0.5f) / rays;
            float phi = Mathf.Acos(1f - t);            // hemisphere, weighted toward the normal
            float theta = i * 2.39996323f;             // golden angle
            directions[i] = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi),
                                        Mathf.Sin(phi) * Mathf.Sin(theta));
        }

        Vector3[] positions = target.vertices;
        Vector3[] normals = target.normals;
        Color[] colors = target.colors;
        int occluded = 0;
        float deckSum = 0f, deckMin = 1f;
        int deckCount = 0;

        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 n = normals[i];
            Vector3 tangent = Vector3.Normalize(Vector3.Cross(n, Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up));
            Vector3 bitangent = Vector3.Cross(n, tangent);
            Vector3 origin = positions[i] + n * 0.012f;

            int hits = 0;
            for (int r = 0; r < rays; r++)
            {
                Vector3 d = directions[r];
                Vector3 world = tangent * d.x + n * d.y + bitangent * d.z;
                // Ignore anything closer than a chamfer's width. Once every box gained a 1.5 cm
                // bevel, each bevel vertex could see the faces of its own box a centimetre away
                // and counted them as occluders — the railing self-occluded to the floor value
                // and came out as a row of black sticks. The 2 cm plank gaps still register.
                if (Physics.Raycast(origin, world, out RaycastHit hit, radius) && hit.distance > 0.035f)
                    hits++;
            }
            // The deck's own occlusion, tracked separately and reported below. A plank is 25 cm
            // wide with no interior vertex, so every vertex it has sits on its perimeter two
            // centimetres from the next board — exactly where a tracer finds occluders. If the
            // mean here sits near the floor, the middle of every board is being shaded with the
            // value measured at its edge, and no albedo will make the deck read lit.
            if (normals[i].y > 0.9f && positions[i].y > DeckY - 0.02f
                && positions[i].y < DeckY + FightRules.BridgeSag + 0.06f)
            {
                float deckAo = Mathf.Lerp(1f, 0.6f, hits / (float)rays);
                deckSum += deckAo;
                deckMin = Mathf.Min(deckMin, deckAo);
                deckCount++;
            }

            if (hits == 0)
                continue;
            occluded++;
            // Floor at 0.6, not 0.25: fully enclosed vertices went so dark that the deck and the
            // rails read as charcoal against brightly lit fighters, inverting the frame's value
            // order. Occlusion is meant to describe contact, not to paint shadow.
            float ao = Mathf.Lerp(1f, 0.6f, hits / (float)rays);
            colors[i] *= ao;
            colors[i].a = 1f;
        }

        target.colors = colors;

        // The deck's own occlusion, reported separately. A plank is 25 cm wide with no interior
        // vertex, so every vertex it has sits on its perimeter two centimetres from the next
        // board — which is exactly where a tracer finds occluders. If the average here is well
        // below one, the middle of every board is being shaded with the value measured at its
        // edge, and no amount of albedo will make the deck read flat and lit.
        if (deckCount > 0)
            Debug.Log($"BambooArena: deck up-facing verts {deckCount}, occlusion mean " +
                      $"{deckSum / deckCount:F3}, min {deckMin:F3} (1.0 = unoccluded, floor 0.6).");
        Object.DestroyImmediate(holder);
        Debug.Log($"BambooArena: occlusion baked, {occluded} of {positions.Length} vertices darkened.");
    }

    // ------------------------------------------------------------------ primitives

    /// <param name="width">Half-width at the blade's widest point, as a fraction of its length.
    /// Defaults to the lanceolate bamboo leaf. Grass and reeds must pass something far narrower:
    /// this profile is 1:8, and applied to a metre-long grass blade it produces a twenty
    /// centimetre green plank. Below about 2 cm on screen a blade crawls even under MSAA, so
    /// that, and not botany, is the floor.</param>
    private static void Blades(Bake bake, Vector3 origin, float length, float lift, float droop,
                               int count, Color color, float phase, float heightBase, float heightTip,
                               float width = 0f)
    {
        for (int i = 0; i < count; i++)
        {
            float a = (i + Random.value) / count * Mathf.PI * 2f;
            var outDir = new Vector3(Mathf.Cos(a), lift * Random.Range(0.7f, 1.3f), Mathf.Sin(a)).normalized;
            var flat = new Vector3(outDir.x, 0f, outDir.z);
            flat = flat.sqrMagnitude < 1e-5f ? Vector3.right : flat.normalized;
            Vector3 side = Vector3.Cross(outDir, Vector3.up);
            side = side.sqrMagnitude < 1e-4f ? Vector3.Cross(outDir, Vector3.forward).normalized : side.normalized;

            Vector3 normal = (Vector3.up * 0.75f + flat * 0.5f).normalized;
            float len = length * Random.Range(0.7f, 1.3f);
            Color tint = color * Random.Range(0.85f, 1.15f);

            // Three, not four: at these sizes the extra segment buys no curve worth two triangles
            // per blade, and the grove pays for it a hundred thousand times over.
            const int segments = 3;
            int prevL = 0, prevR = 0;
            for (int s = 0; s <= segments; s++)
            {
                float t = s / (float)segments;
                Vector3 p = origin + outDir * (len * t) + Vector3.down * (droop * len * t * t);
                // Lanceolate profile, shared with the leaf atlas so the card tiers are the same
                // species as the geometry tiers.
                float w = len * (width > 0f
                    ? width * Mathf.Sin(Mathf.Pow(t, 0.5f) * Mathf.PI)
                    : ArenaTextures.LeafHalfWidth(t));
                float h = Mathf.Lerp(heightBase, heightTip, t);
                // Dark where the blade leaves the ground, light at the tip. This is the single
                // cheapest thing that separates a blade from a painted stripe: it is the contact
                // occlusion at the base and the thinning of the leaf toward its point, both in
                // the vertex colour that is already being written.
                Color shade = tint * Mathf.Lerp(0.55f, 1.15f, t);
                // v held inside the unringed band of the bark map — see BarkClearV. The reeds and
                // ferns that also come through here use an untextured material, so this costs
                // them nothing.
                float v = t * BarkClearV;
                int l = bake.Push(p - side * w, normal, new Vector2(0f, v), new Vector2(phase, h), shade);
                int r = bake.Push(p + side * w, normal, new Vector2(1f, v), new Vector2(phase, h), shade);
                if (s > 0)
                {
                    bake.Tri(prevL, l, prevR);
                    bake.Tri(prevR, l, r);
                }
                prevL = l;
                prevR = r;
            }
        }
    }

    /// <summary>A nail head: one quad lying on the board, two triangles. Wound counter to the
    /// obvious order because Unity takes the face normal from cross(b - a, c - a), and the
    /// obvious order puts it under the deck.</summary>
    private static void NailHead(Bake bake, Vector3 centre, float size, Color color)
    {
        float h = size * 0.5f;
        var uv = new Vector2(Random.value, Random.value);
        int a = bake.Push(centre + new Vector3(-h, 0f, -h), Vector3.up, uv, Vector2.zero, color);
        int b = bake.Push(centre + new Vector3(h, 0f, -h), Vector3.up, uv + new Vector2(0.02f, 0f),
                          Vector2.zero, color);
        int c = bake.Push(centre + new Vector3(h, 0f, h), Vector3.up, uv + new Vector2(0.02f, 0.02f),
                          Vector2.zero, color);
        int d = bake.Push(centre + new Vector3(-h, 0f, h), Vector3.up, uv + new Vector2(0f, 0.02f),
                          Vector2.zero, color);
        bake.Tri(a, d, c);
        bake.Tri(a, c, b);
    }

    /// <summary>
    /// A floating leaf: a radial mesh of three rings, not the seven-triangle fan this used to be.
    /// Seventy triangles instead of seven, and each of the three things the extra rings buy is
    /// something nothing else can produce.
    ///
    /// A circle instead of a heptagon. Fourteen segments is where the outline stops reading as a
    /// polygon at the size these leaves occupy in frame.
    ///
    /// A rim that turns up. The outer 10% of the leaf climbs from 6% to 14% of its radius, which
    /// is a 39° slope — steep enough that the edge takes visibly different light from the middle.
    /// That difference, and not detail, is what stops a flat leaf reading as a decal on the water.
    ///
    /// Somewhere to put a gradient. This material carries no texture at all — see
    /// <see cref="FoliageMaterial"/>, which nulls every map — so the only place a leaf can have a
    /// dark yellow middle, a lighter body, a rusty margin and venation is in the vertex stream,
    /// and a fan with one hub and one rim has nowhere to write them.
    ///
    /// The sinus — the wedge notch a water lily has and a lotus does not — is a gap left in the
    /// fan, not transparency in a map. There is no alpha anywhere in this material, which is why
    /// none of the standard lily-pad problems (mip-thinned cutouts, transparent sorting against
    /// the water, ZWrite off) exist here to be fixed.
    /// </summary>
    /// <param name="notch">Where the sinus opens, in the same XZ angle the rim is swept through.
    /// The clump aims it at the rhizome; see <see cref="LilyClump"/>.</param>
    private static void LilyPad(Bake bake, Vector3 centre, float radius, float notch, Color color)
    {
        const int sides = 14;
        const float gap = 0.25f; // half the sinus, so 0.5 rad — 29°, inside the botanical 25-30°

        // Ring radius, height and surface slope, all as fractions of the leaf's radius so a 5 cm
        // leaf curls in proportion to a 14 cm one. The slopes are what the vertex normals are
        // built from: each is the mean of the two facets meeting at that ring.
        float[] ringR = { 0.5f, 0.9f, 1f };
        float[] ringY = { 0f, 0.06f, 0.14f };
        float[] ringSlope = { 0.115f, 0.475f, 0.8f };
        // uv1.y, which the shader uses for *both* wind falloff and translucency strength. Kept low
        // on purpose: a leaf is held by a stem and barely moves, and at 0.25 the wind term works
        // out to a couple of millimetres at the rim. The gradient is there for the translucency —
        // the leaf is thinnest at its edge and that is where light comes through it.
        float[] ringHeight = { 0.10f, 0.18f, 0.25f };

        Color[] ringColor =
        {
            color * 0.95f,
            color * 1.15f,
            // The rusty margin a Nymphaea leaf carries, and the one part of it that is not a shade
            // of its own green. A fifth of the way to the brown: at two fifths the rim lit up as
            // an orange ring, which is a louder mistake than the flat colour it replaced.
            Color.Lerp(color * 0.85f, Srgb(0.54f, 0.35f, 0.18f), 0.2f),
        };

        float phase = Random.value;
        // Middle of the leaf: darker, and yellower rather than merely darker — the blue channel
        // comes down a further 28%. This is the opposite direction from the desaturation pass the
        // rest of the greenery got, and deliberately so: that pass fixed foliage lit by open sky,
        // and the middle of a lily pad is the one place on this water nothing blue reaches.
        var hubColor = new Color(color.r * 0.70f, color.g * 0.70f, color.b * 0.70f * 0.72f, color.a);
        int hub = bake.Push(centre + Vector3.down * (radius * 0.04f), Vector3.up,
                            new Vector2(0.5f, 0.5f), new Vector2(phase, 0f), hubColor);

        var ring = new int[3][];
        for (int r = 0; r < 3; r++)
        {
            ring[r] = new int[sides + 1];
            for (int i = 0; i <= sides; i++)
            {
                float a = notch + gap + i / (float)sides * (Mathf.PI * 2f - gap * 2f);
                float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                // Wobble on the outer ring only. On an inner ring the same jitter never reaches
                // the silhouette and shows up instead as a dent in the shading.
                float rr = radius * ringR[r] * (r == 2 ? Random.Range(0.9f, 1.06f) : 1f);
                var p = centre + new Vector3(cos * rr, radius * ringY[r], sin * rr);
                if (r == 2)
                    p.y += radius * Random.Range(-0.02f, 0.02f);
                float slope = ringSlope[r];
                // Veins: alternate vertices a step either side of the ring's value. The vertices
                // are shared between neighbouring segments, so this is not a set of hard wedges
                // but fourteen soft radial bands converging on the stem — which is what venation
                // amounts to at the size this leaf occupies on a phone.
                float vein = (i & 1) == 0 ? 1.08f : 0.94f;
                ring[r][i] = bake.Push(p, new Vector3(-slope * cos, 1f, -slope * sin).normalized,
                                       new Vector2(0.5f + cos * 0.5f, 0.5f + sin * 0.5f),
                                       new Vector2(phase, ringHeight[r]), ringColor[r] * vein);
            }
        }

        // Wound so the faces point the way their vertices claim to: (inner, outer[i+1], outer[i])
        // is the up-facing order for a rising angle in XZ. Taken the other way round,
        // cross(rim[i] - hub, rim[i+1] - hub) comes out as -Y and the pad's geometric normal
        // points at the riverbed. The material is Cull Off, so the pad still draws — but the
        // shader flips the shading normal by VFACE, and a back face whose vertex normal is already
        // reversed gets flipped away from the viewer rather than toward it. Sun contribution zero,
        // ground ambient only: 4% of the light an up-facing leaf takes, which is how a leaf on
        // bright water reads as a hole punched through it. Two earlier passes raised the albedo
        // against this bug and moved the pixels from 1 to 7.
        for (int i = 0; i < sides; i++)
        {
            bake.Tri(hub, ring[0][i + 1], ring[0][i]);
            for (int r = 0; r < 2; r++)
            {
                bake.Tri(ring[r][i], ring[r + 1][i + 1], ring[r + 1][i]);
                bake.Tri(ring[r][i], ring[r][i + 1], ring[r + 1][i + 1]);
            }
        }
    }

    /// <summary>
    /// The flower, and the reason to touch any of this: on every reference of water like this it
    /// is the blooms and not the leaves that carry the picture. What stood here before was a
    /// six-blade grass tuft painted white.
    ///
    /// Twenty-one petals in three rows of seven, two triangles each, over a hexagonal cone of
    /// stamens: 48 triangles, less than one chamfered box. Each row is shorter and steeper than
    /// the one outside it, and offset half a step around, so no petal sits directly on another.
    ///
    /// The glow is free. The material already runs _Translucency 1.2 and the shader scales it by
    /// uv1.y, so ramping that from 0.05 at the petal's base to 0.42 at its tip lights the petal
    /// through from behind exactly where it is thin — and a white water lily is mostly that glow.
    /// </summary>
    private static void LilyBloom(Bake bake, Vector3 centre, float radius, Color tip)
    {
        // A petal is greenish-yellow where it attaches and white at the tip. Missing that gradient
        // is what makes a white flower read as a paper cutout.
        Color root = Srgb(0.70f, 0.72f, 0.48f);
        Color heart = Srgb(0.86f, 0.68f, 0.16f);

        const int perRow = 7;
        float[] tilt = { 10f, 38f, 62f };
        float[] length = { 1f, 0.78f, 0.55f };
        float spin = Random.value * Mathf.PI * 2f;
        float phase = Random.value;

        for (int row = 0; row < 3; row++)
            for (int p = 0; p < perRow; p++)
            {
                float a = spin + (p + row * 0.5f) / perRow * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float t = tilt[row] * Mathf.Deg2Rad;
                Vector3 axis = dir * Mathf.Cos(t) + Vector3.up * Mathf.Sin(t);
                Vector3 side = Vector3.Cross(Vector3.up, dir); // horizontal, never degenerate
                Vector3 n = Vector3.Cross(axis, side).normalized;
                float len = radius * length[row] * Random.Range(0.92f, 1.08f);
                float wide = len * 0.3f;
                Color mid = Color.Lerp(root, tip, 0.5f);

                int b = bake.Push(centre + axis * (len * 0.1f), n, new Vector2(0.5f, 0f),
                                  new Vector2(phase, 0.05f), root);
                int sa = bake.Push(centre + axis * (len * 0.4f) + side * wide, n,
                                   new Vector2(1f, 0.4f), new Vector2(phase, 0.25f), mid);
                int sb = bake.Push(centre + axis * (len * 0.4f) - side * wide, n,
                                   new Vector2(0f, 0.4f), new Vector2(phase, 0.25f), mid);
                int tp = bake.Push(centre + axis * len, n, new Vector2(0.5f, 1f),
                                   new Vector2(phase, 0.42f), tip);
                bake.Tri(b, tp, sa);
                bake.Tri(b, sb, tp);
            }

        // Stamens. At ten to fifteen centimetres across, the middle of this flower is a yellow dot
        // a few pixels wide; a modelled bundle of filaments would be the same few pixels of the
        // same yellow for thirty times the triangles. A cone, not a disc, so it takes a highlight.
        int crown = bake.Push(centre + Vector3.up * (radius * 0.1f), Vector3.up,
                              new Vector2(0.5f, 0.5f), new Vector2(phase, 0.1f), heart * 1.15f);
        var skirt = new int[7];
        for (int i = 0; i <= 6; i++)
        {
            float a = spin + i / 6f * Mathf.PI * 2f;
            skirt[i] = bake.Push(
                centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (radius * 0.22f)
                       + Vector3.up * (radius * 0.02f),
                Vector3.up, new Vector2(0.5f, 0.5f), new Vector2(phase, 0.06f), heart);
        }
        for (int i = 0; i < 6; i++)
            bake.Tri(crown, skirt[i + 1], skirt[i]);
    }

    /// <summary>
    /// The soft darkening a floating leaf puts on the water beneath it — a leaf two centimetres
    /// off the surface occludes the sky over a patch a little wider than itself, and without that
    /// patch the pad reads as lying *on top of* the water rather than *on* it.
    ///
    /// Same texture, shader and queue as the fighters' blob (FightSceneSetup.AddBlobShadow). Its
    /// own mesh rather than part of the vegetation because that material is opaque and this one
    /// has to blend; two triangles a leaf, one draw call for the lot.
    /// </summary>
    private static void LilyShade(Bake shade, Vector3 centre, float radius)
    {
        // 1.35 radii of half-extent. T_Blob is opaque only inside 15% of its half-width and fades
        // to nothing at the edge, so what this actually puts on the water is a small dark core
        // under the middle of the leaf and a wide soft falloff — not a square.
        float s = radius * 1.35f;
        float y = WaterY + 0.005f;
        // Wound anticlockwise in XZ so the quad faces up; the particle shader culls back faces.
        shade.Quad(new Vector3(centre.x - s, y, centre.z - s),
                   new Vector3(centre.x - s, y, centre.z + s),
                   new Vector3(centre.x + s, y, centre.z + s),
                   new Vector3(centre.x + s, y, centre.z - s),
                   Vector3.up, Color.white, 0f, 0f, 0f);
    }

    // ------------------------------------------------------------------ assets

    private static GameObject AddMesh(GameObject parent, string name, Mesh mesh, Material material, bool castShadows)
    {
        SaveMesh(mesh, $"M_Arena{name}.mesh");

        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        // The timber and the culms cast now: hard board shadows across the deck are a large part
        // of what a directly lit fighting arena looks like. Leaves and reeds still do not — tens
        // of thousands of alpha-free strips buy speckle, not shadow.
        renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        return go;
    }

    private static void SaveMesh(Mesh mesh, string file)
    {
        AssetDatabase.DeleteAsset(Dir + file);
        AssetDatabase.CreateAsset(mesh, Dir + file);
    }

    private static Material ArenaMaterial(string name, CullMode cull, float wind, float windSpeed,
                                          float translucency, float smoothness, float spec)
    {
        Shader shader = Shader.Find("Mikey/Arena");
        if (shader == null)
        {
            Debug.LogError("BambooArena: shader Mikey/Arena not found (compile error?) — " +
                           "falling back to URP/Lit, so there will be no wind and no vertex colour.");
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }

        Material mat = LoadOrCreate(name, shader);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_SpecStrength", spec);
        mat.SetFloat("_Translucency", translucency);
        mat.SetFloat("_TranslucencyPower", 4f);
        mat.SetVector("_WindDir", new Vector4(-0.88f, 0.47f, 0f, 0f));
        mat.SetFloat("_WindStrength", wind);
        mat.SetFloat("_WindSpeed", windSpeed);
        mat.SetFloat("_Cull", (float)cull);
        return mat;
    }

    /// <summary>
    /// Specular strength 0.55, not 1.1. A multiplier above one is not a physical quantity, and it
    /// only ever looked defensible while the diffuse was arriving two stops dark — with nothing
    /// else to see, the specular was the surface. Restored, it drew a white outline along the
    /// chamfer of every board and rail, which is the "chromed pipe" the deck was accused of.
    /// </summary>
    private static Material TimberMaterial(ArenaTextures.Surface wood)
    {
        Material mat = ArenaMaterial("M_ArenaWood", CullMode.Back, 0f, 1f, 0f, 1f, 0.55f);
        mat.SetTexture("_BaseMap", wood.Albedo);
        mat.SetTexture("_BumpMap", wood.Normal);
        mat.SetTexture("_MaskMap", wood.Mask);
        // 0.55, down from 1.3: the normal carries the same fibre the albedo does, and at full
        // strength the two together made a corduroy rather than a board.
        mat.SetFloat("_BumpScale", 0.55f);
        // The fog's own colour on every upward edge. Kept low: this is meant to find the top of
        // a handrail and the chamfer of a board, not to wash the deck.
        mat.SetColor("_RimColor", Srgb(0.72f, 0.78f, 0.82f));
        mat.SetFloat("_RimStrength", 0.35f);
        mat.SetFloat("_RimPower", 3.5f);
        mat.EnableKeyword("_NORMALMAP");
        return mat;
    }

    private static Material BambooMaterial(ArenaTextures.Surface bark)
    {
        Material mat = ArenaMaterial("M_ArenaBamboo", CullMode.Off, 0.05f, 0.7f, 0.4f, 0.75f, 0.5f);
        mat.SetTexture("_BaseMap", bark.Albedo);
        mat.SetTexture("_BumpMap", bark.Normal);
        mat.SetTexture("_MaskMap", bark.Mask);
        mat.SetFloat("_BumpScale", 0.8f);
        mat.EnableKeyword("_NORMALMAP");
        return mat;
    }

    /// <summary>The far tiers' foliage: one atlas, clipped rather than blended. Blending would
    /// need these sorted against each other and against the culms they hang on, which no sort
    /// order gets right when they interpenetrate — and costs more on a phone besides.</summary>
    private static Material LeafCardMaterial(Texture2D atlas)
    {
        Material mat = ArenaMaterial("M_ArenaLeafCard", CullMode.Off, 0.28f, 1.15f, 1.2f, 0.2f, 0.2f);
        mat.SetTexture("_BaseMap", atlas);
        mat.SetTexture("_BumpMap", null);
        mat.SetTexture("_MaskMap", null);
        mat.DisableKeyword("_NORMALMAP");
        // 0.35, not the usual 0.5: mip maps thin a cluster's alpha out with distance, and these
        // cards are only ever seen at distance.
        mat.SetFloat("_Cutoff", 0.35f);
        mat.SetFloat("_AlphaToMask", 1f);
        mat.renderQueue = 2450; // AlphaTest: after the opaque grove, before anything transparent
        return mat;
    }

    /// <summary>
    /// The banks. The one surface in this arena that is not generated: a CC0 photographic mossy
    /// forest floor from Poly Haven, because there is nothing procedural about organic litter
    /// that a noise function reproduces convincingly, and this bank was flat vertex colour with
    /// no map at all — the darkest, emptiest thing in the frame.
    ///
    /// The bank is its own mesh and material rather than riding with the foliage: the foliage
    /// material has no base map by design, and the grass blades that share it sample a hand-picked
    /// sliver of UV space that would land somewhere arbitrary in a ground texture.
    /// </summary>
    private static Material GroundMaterial()
    {
        Material mat = ArenaMaterial("M_ArenaGround", CullMode.Back, 0f, 1f, 0f, 0.35f, 0.35f);
        mat.SetTexture("_BaseMap", LoadTexture("T_Ground.jpg"));
        mat.SetTexture("_BumpMap", LoadTexture("T_Ground_N.jpg"));
        mat.SetTexture("_MaskMap", LoadTexture("T_Ground_M.png"));
        mat.SetFloat("_BumpScale", 0.9f);
        mat.EnableKeyword("_NORMALMAP");
        return mat;
    }

    /// <summary>The scanned grass, ferns and shrubs. One material for all three, because their
    /// albedo and alpha were packed into a single atlas before import — three photographic plants
    /// are not worth three draw calls in a scene that renders its entire grove in one.</summary>
    private static Material ScanFoliageMaterial()
    {
        Material mat = ArenaMaterial("M_ArenaScan", CullMode.Off, 0.24f, 1.15f, 1.1f, 0.4f, 0.6f);
        mat.SetTexture("_BaseMap", LoadTexture("T_ScanFoliage.png"));
        mat.SetTexture("_BumpMap", null);
        mat.SetTexture("_MaskMap", null);
        mat.DisableKeyword("_NORMALMAP");
        mat.SetFloat("_Cutoff", 0.4f);
        mat.SetFloat("_AlphaToMask", 1f);
        mat.renderQueue = 2450;
        return mat;
    }

    /// <summary>Imported map from the arena folder. Null rather than an exception if it is
    /// missing, so a fresh clone without the downloaded textures still builds — it just builds a
    /// white bank, which is loud enough to notice.</summary>
    private static Texture2D LoadTexture(string file)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + file);
        if (tex == null)
            Debug.LogError($"BambooArena: {Dir}{file} is missing — the bank will render untextured.");
        return tex;
    }

    /// <summary>Grass, reeds and lilies. Smoothness and specular are well above what a matt leaf
    /// would take, because the silvered edge a blade catches at a grazing angle is half of what
    /// makes real grass look expensive — and this camera sees the bank almost edge-on.</summary>
    private static Material FoliageMaterial()
    {
        Material mat = ArenaMaterial("M_ArenaFoliage", CullMode.Off, 0.2f, 1.15f, 1.2f, 0.42f, 0.75f);
        mat.SetTexture("_BaseMap", null);
        mat.SetTexture("_BumpMap", null);
        mat.SetTexture("_MaskMap", null);
        mat.DisableKeyword("_NORMALMAP");
        return mat;
    }

    /// <summary>The lily pads' contact shadow. Same shader, texture and queue as the fighters'
    /// blob; a separate material only so the opacity can differ, because a fighter standing on the
    /// boards occludes far more sky than a leaf lying two centimetres off the water. Every float
    /// here is copied from what URP's own material inspector writes for a transparent particle —
    /// a freshly created material has none of them, and without the keyword it renders as an
    /// opaque black square.</summary>
    private static Material LilyShadeMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            Debug.LogError("BambooArena: URP particle shader not found — the lily pads will have " +
                           "no contact shadow.");
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        Material mat = LoadOrCreate("M_ArenaLilyShade", shader);
        mat.SetFloat("_Surface", 1f);   // transparent
        mat.SetFloat("_Blend", 0f);     // alpha
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", (float)CullMode.Back);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetShaderPassEnabled("DepthOnly", false);
        mat.SetShaderPassEnabled("SHADOWCASTER", false);
        // Under the water's ripple particles, over the water itself — which is Queue Geometry and
        // writes depth, so this lands on the surface and not through it.
        mat.renderQueue = (int)RenderQueue.Transparent - 50;
        mat.SetTexture("_BaseMap", LoadTexture("T_Blob.asset"));
        // 0.35 against the fighters' 0.55. A leaf is not opaque and does not sit on the surface;
        // at the fighters' value the water under a clump went to a flat dark mat.
        mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0.35f));
        mat.SetColor("_Color", new Color(0f, 0f, 0f, 0.35f));
        return mat;
    }

    private static Material WaterMaterial()
    {
        Shader shader = Shader.Find("Mikey/Water");
        if (shader == null)
        {
            Debug.LogError("BambooArena: shader Mikey/Water not found (compile error?).");
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }
        Material mat = LoadOrCreate("M_ArenaWater", shader);
        // The two ends of the jade palette, raw — the same convention _SkyColor and _FoamColor
        // below them already use, and now the measured one.
        //
        // Srgb() here was wrong and the probe caught it: a material serialises its Colors in gamma
        // and Unity converts them on upload, so a value that arrives already linear is converted a
        // second time. The body of the water came out four times too dark, with G/R at 5.1 where
        // the model says 3.5 — a nonlinear error, which is the signature of a double conversion
        // rather than a mistuned constant.
        mat.SetColor("_DeepColor", new Color(0.027f, 0.247f, 0.271f));  // #073F45
        // Dimmer than the reference palette's #8FE8DC, and deliberately. That hex is the colour a
        // photograph *comes out*, which already contains the bottom showing through and the sky
        // on top of it. Fed in as the source of scattering it counts that brightness twice: at
        // 0.81 linear green the water column outshone a lit riverbed by four to one and the bed
        // was invisible at any depth.
        mat.SetColor("_ShallowColor", new Color(0.373f, 0.655f, 0.612f)); // #5FA79C
        mat.SetVector("_Absorption", new Vector4(1.15f, 0.16f, 0.28f, 0f));
        // 0.22, down from 0.45. At 0.45 the scattering term was already at half strength 1.5 m
        // along the ray — which is the near band, where the bed is supposed to be legible.
        mat.SetFloat("_ScatterDensity", 0.22f);
        // The bank's own silt multiplier, and a multiplier is exactly what it stays: BuildBanks
        // writes (0.55, 0.50, 0.45) and says why — a multiplier run through gamma conversion
        // darkens twice. Set as a vector so that no conversion happens on the way in either.
        mat.SetVector("_BedTint", new Vector4(0.55f, 0.50f, 0.45f, 0f));
        mat.SetFloat("_FresnelTilt", 0.22f);
        // The sky colour, not a dark tint. At a 5-10° view angle Fresnel puts 60-90% of the
        // surface into reflection, so water darker than the sky above it is physically
        // impossible — and a 0.27 sky against a 0.78 fog is exactly why it read as a grey
        // sheet painted across the bottom of the frame.
        mat.SetColor("_SkyColor", new Color(0.784f, 0.824f, 0.800f));
        mat.SetColor("_FoamColor", new Color(0.769f, 0.812f, 0.788f));
        // 3.0, down from the 4.5 a physical surface wants. Together with _FresnelTilt this takes
        // the reflection from 16/46/64% at 3/10/20 metres to 5/24/33%, and what the reflection
        // gives up is exactly what the body of the water takes.
        mat.SetFloat("_FresnelPower", 3f);
        mat.SetFloat("_FresnelBias", 0.03f);
        mat.SetFloat("_RippleStrength", 0.9f);
        mat.SetFloat("_RippleScale", 1.4f);
        // 0.28: at 1.0 the layers cycled in 9.7 / 6.0 / 3.6 seconds, which is quick for water
        // with no current in it at all. This puts them at 35 / 21 / 13 — a pond, not a river.
        mat.SetFloat("_RippleSpeed", 0.28f);
        mat.SetFloat("_WaveAmp", 0.02f);
        mat.SetFloat("_Glint", 2f);
        mat.SetFloat("_GlintSharpness", 380f);
        mat.SetFloat("_ReflectionStrength", 0.9f);
        mat.SetFloat("_ReflectionDistortion", 0.022f);
        mat.SetFloat("_GustScale", 0.012f);
        mat.SetFloat("_FoamCutoff", 0.42f);
        mat.SetTexture("_NoiseMap", ArenaTextures.Noise());
        Texture2D bankMap = LoadTexture("T_Ground.jpg");
        mat.SetTexture("_BedMap", bankMap);
        mat.SetFloat("_BedUvScale", BankUvScale);
        mat.SetFloat("_RefractStrength", 0.035f);
        mat.SetFloat("_Caustics", 1.4f);

        // The body of the water is what makes it jade, and it is switched on by exactly one thing:
        // a non-zero absorption vector. A material deserialised from before this work has the
        // property at zero, transmittance comes out 1 everywhere, and the water silently goes back
        // to being a mirror over a flat colour — a hard failure to spot by eye and a trivial one
        // to spot here.
        if (mat.GetVector("_Absorption").sqrMagnitude < 1e-6f)
            Debug.LogError("BambooArena: M_ArenaWater has no absorption — the water has no body " +
                           "and will render as a mirror over the bed colour.");

        // The bed must be the same map the bank wears, from the same call. Anything else here is
        // not a missing decoration: the water would draw its bed as flat tint and the waterline
        // would grow a seam exactly where the eye goes, between a textured bank and an untextured
        // river.
        if (mat.GetTexture("_BedMap") != bankMap)
            Debug.LogError("BambooArena: M_ArenaWater bed map is not the bank's T_Ground.jpg — " +
                           "the waterline will show a seam.");

        // Caustics at zero are not "off", they are a silent regression: the shader still pays for
        // two texture fetches and the bed loses the only thing that made it read as being under
        // water rather than painted on the surface.
        if (mat.GetFloat("_Caustics") <= 0f)
            Debug.LogError("BambooArena: M_ArenaWater has no caustics intensity — the bed will " +
                           "render flat.");
        return mat;
    }

    private static Material LoadOrCreate(string name, Shader shader)
    {
        string path = Dir + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null || mat.shader != shader)
        {
            AssetDatabase.DeleteAsset(path);
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        return mat;
    }

    /// <summary>Vertex colours are handed to the GPU raw — unlike a material colour property,
    /// nothing converts them out of gamma. In a linear project that means every colour authored
    /// here has to be converted by hand or the whole arena comes out washed out.</summary>
    private static Color Srgb(float r, float g, float b) => new Color(r, g, b).linear;

    // ------------------------------------------------------------------ mesh baking

    /// <summary>
    /// Accumulates geometry into one mesh. Per-vertex channels match what Mikey/Arena reads:
    ///   normal  — spherised for foliage, radial for tubes, face normals for boxes
    ///   uv0     — texture coordinates
    ///   uv1.x   — per-instance wind phase, so nothing sways in lockstep
    ///   uv1.y   — 0 at the root, 1 at the tip: wind falloff and translucency
    ///   color   — albedo tint, multiplied by baked occlusion afterwards
    /// </summary>
    private sealed class Bake
    {
        private readonly List<Vector3> _positions = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<Vector2> _wind = new List<Vector2>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _triangles = new List<int>();

        public int Push(Vector3 position, Vector3 normal, Vector2 uv, Vector2 wind, Color color)
        {
            _positions.Add(position);
            _normals.Add(normal);
            _uvs.Add(uv);
            _wind.Add(wind);
            _colors.Add(color);
            return _positions.Count - 1;
        }

        public void Tri(int a, int b, int c)
        {
            _triangles.Add(a);
            _triangles.Add(b);
            _triangles.Add(c);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color color,
                         float phase, float heightAB, float heightCD)
        {
            int i0 = Push(a, normal, new Vector2(0f, 0f), new Vector2(phase, heightAB), color);
            int i1 = Push(b, normal, new Vector2(1f, 0f), new Vector2(phase, heightAB), color);
            int i2 = Push(c, normal, new Vector2(1f, 1f), new Vector2(phase, heightCD), color);
            int i3 = Push(d, normal, new Vector2(0f, 1f), new Vector2(phase, heightCD), color);
            Tri(i0, i1, i2);
            Tri(i0, i2, i3);
        }

        /// <summary>
        /// A chamfered box rotated about Y: six inset faces, twelve edge quads and eight corner
        /// triangles. Forty-four triangles instead of twelve, and worth every one of them — a
        /// sharp 90° edge catches no light at all and reads as a primitive from any distance,
        /// while a 1.5 cm chamfer puts a thin bright line down every edge. It is the cheapest
        /// thing that separates a model from a cube, and every board, rail, post, stone and prop
        /// in the arena goes through here.
        ///
        /// Winding is not reasoned about per face — each polygon is emitted and then flipped if
        /// its computed normal disagrees with the one it is supposed to have. Twenty-six
        /// polygons of sign juggling is exactly where an inside-out face hides.
        ///
        /// UVs are a planar projection onto the face's two tangent axes, with the texture's u
        /// following the longer of them so the generated grain runs along the piece rather than
        /// across it. <paramref name="uvOffset"/> gives each board its own patch of the map —
        /// without it a deck of a hundred boards shows one texture a hundred times.
        /// </summary>
        public void Box(Vector3 centre, Vector3 size, Color color, Vector2 uvScale, Vector2 uvOffset,
                        float yawDegrees, float bevel = 0.015f) =>
            Box(centre, size, color, uvScale, uvOffset, Quaternion.Euler(0f, yawDegrees, 0f), bevel);

        /// <summary>As above but freely oriented — railing posts lean a degree or two off
        /// vertical, which yaw alone cannot express.</summary>
        public void Box(Vector3 centre, Vector3 size, Color color, Vector2 uvScale, Vector2 uvOffset,
                        Quaternion rot, float bevel = 0.015f)
        {
            Vector3 h = size * 0.5f;
            float b = Mathf.Clamp(bevel, 0f, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.4f);
            var inner = new Vector3(h.x - b, h.y - b, h.z - b);

            var polygons = new List<(Vector3[] Points, Vector3 Normal)>(26);

            for (int axis = 0; axis < 3; axis++)
                for (int s = -1; s <= 1; s += 2)
                {
                    int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
                    Vector3 n = Vector3.zero;
                    n[axis] = s;
                    var quad = new Vector3[4];
                    var corners = new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) };
                    for (int k = 0; k < 4; k++)
                    {
                        var p = Vector3.zero;
                        p[axis] = s * h[axis];
                        p[a1] = corners[k].Item1 * inner[a1];
                        p[a2] = corners[k].Item2 * inner[a2];
                        quad[k] = p;
                    }
                    polygons.Add((quad, n));
                }

            for (int a = 0; a < 3; a++)
                for (int c = a + 1; c < 3; c++)
                {
                    int e = 3 - a - c;
                    for (int sa = -1; sa <= 1; sa += 2)
                        for (int sc = -1; sc <= 1; sc += 2)
                        {
                            Vector3 n = Vector3.zero;
                            n[a] = sa;
                            n[c] = sc;
                            n = n.normalized;
                            var quad = new Vector3[4];
                            for (int k = 0; k < 4; k++)
                            {
                                bool onA = k < 2;
                                float se = k == 0 || k == 3 ? -1f : 1f;
                                var p = Vector3.zero;
                                p[e] = se * inner[e];
                                p[a] = onA ? sa * h[a] : sa * inner[a];
                                p[c] = onA ? sc * inner[c] : sc * h[c];
                                quad[k] = p;
                            }
                            polygons.Add((quad, n));
                        }
                }

            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        polygons.Add((new[]
                        {
                            new Vector3(sx * h.x, sy * inner.y, sz * inner.z),
                            new Vector3(sx * inner.x, sy * h.y, sz * inner.z),
                            new Vector3(sx * inner.x, sy * inner.y, sz * h.z),
                        }, new Vector3(sx, sy, sz).normalized));

            foreach ((Vector3[] points, Vector3 normal) in polygons)
            {
                int dominant = Mathf.Abs(normal.x) >= Mathf.Abs(normal.y) && Mathf.Abs(normal.x) >= Mathf.Abs(normal.z) ? 0
                             : Mathf.Abs(normal.y) >= Mathf.Abs(normal.z) ? 1 : 2;
                int au = (dominant + 1) % 3, av = (dominant + 2) % 3;
                bool swap = size[av] > size[au];

                bool flip = Vector3.Dot(Vector3.Cross(points[1] - points[0], points[2] - points[0]), normal) < 0f;
                Vector3 worldNormal = rot * normal;

                var indices = new int[points.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 p = points[flip ? points.Length - 1 - i : i];
                    Vector2 uv = swap
                        ? new Vector2(p[av] * uvScale.x, p[au] * uvScale.y)
                        : new Vector2(p[au] * uvScale.x, p[av] * uvScale.y);
                    indices[i] = Push(centre + rot * p, worldNormal, uvOffset + uv, Vector2.zero, color);
                }
                for (int i = 2; i < indices.Length; i++)
                    Tri(indices[0], indices[i - 1], indices[i]);
            }
        }


        /// <summary>
        /// A leaf cluster as a single card, cut to the silhouette measured off the atlas rather
        /// than left as a quad — eight triangles buy back most of the transparent pixels a quad
        /// would shade, and fill rate is what a grove of these runs out of first.
        ///
        /// Not a billboard: this camera tracks along x and never orbits, so a fixed facing is
        /// indistinguishable from one that turns, and costs nothing per frame.
        /// </summary>
        public void Card(Vector3 centre, float size, int cell, float[,] radii, Color color,
                         float phase, float height)
        {
            const float grid = ArenaTextures.LeafAtlasGrid;
            var uvCell = new Vector2(cell % ArenaTextures.LeafAtlasGrid,
                                     cell / ArenaTextures.LeafAtlasGrid) / grid;
            var uvMid = new Vector2(0.5f, 0.5f) / grid;
            float yaw = Random.Range(-0.5f, 0.5f);
            var right = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
            Vector3 normal = Vector3.Cross(Vector3.up, right).normalized;
            var wind = new Vector2(phase, height);

            int hub = Push(centre, normal, uvCell + uvMid, wind, color);
            var rim = new int[8];
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                var offset = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radii[cell, i];
                rim[i] = Push(centre + (right * offset.x + Vector3.up * offset.y) * size, normal,
                              uvCell + uvMid + offset / grid, wind, color);
            }
            // Same reversal as the lily pads, and for the same reason: with this order
            // cross(rim[i] - hub, rim[i+1] - hub) is cross(right, up), the opposite of the
            // normal every vertex here was pushed with. The card faces the camera and shades as
            // though it were turned away from it, so a crown lit from the front got ambient and
            // nothing else.
            for (int i = 0; i < 8; i++)
                Tri(hub, rim[(i + 1) % 8], rim[i]);
        }

        /// <summary>
        /// Tapered tube with outward normals, wound so its faces front outward under Cull Back.
        ///
        /// <paramref name="nodeSpacing"/> above 0 puts the tube in bamboo mode: v is measured in
        /// internodes rather than in metres, so the ring painted into the bark texture lands on a
        /// node however long that internode physically is. <paramref name="nodeSwell"/> above 0
        /// additionally spends geometry on the node — a loop at the node scaled up by that
        /// fraction and a loop halfway between — which is worth paying only where the silhouette
        /// is a few units from the camera. Everything further away gets the node from the texture
        /// alone and a handful of loops for the bow.
        ///
        /// <paramref name="uvV"/> at or above 0 pins v to one line of the map, for the twigs that
        /// share the culm's texture but must not show its node rings.
        /// </summary>
        public void Tube(Vector3 from, Vector3 to, float radiusFrom, float radiusTo, Color color,
                         int sides = 6, int rings = 1, Vector3 bow = default, float nodeSpacing = 0f,
                         float nodeSwell = 0f, float phase = 0f, float heightSpan = 0f,
                         float windHeight = -1f, float uvV = -1f)
        {
            Vector3 axis = to - from;
            float length = axis.magnitude;
            if (length < 1e-4f)
                return;
            Vector3 up = axis / length;
            Vector3 side = Vector3.Cross(up, Mathf.Abs(up.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
            Vector3 forward = Vector3.Cross(side, up);

            // t along the tube, radius multiplier, and v in whatever unit this tube measures in.
            var loopT = new List<float>();
            var loopSwell = new List<float>();
            var loopV = new List<float>();

            if (nodeSwell > 0f && nodeSpacing > 0f)
            {
                // Walk internode by internode. Short at the base, long through the middle, short
                // again near the tip — an even pitch is the first thing that reads as generated,
                // and it is only here, a couple of units from the lens, that anyone can count them.
                float distance = 0f;
                int node = 0;
                while (distance < length)
                {
                    float t = distance / length;
                    loopT.Add(t);
                    loopSwell.Add(1f + nodeSwell);
                    // +0.5, because the bark map paints its ring in the middle of each cell and
                    // not at its edge. Line these up wrong and the culm carries two rows of
                    // nodes, one swollen and one painted, half an internode apart.
                    loopV.Add(node + 0.5f);

                    float spacing = nodeSpacing *
                                    Mathf.Lerp(0.55f, 1.25f, Mathf.Sin(Mathf.Pow(t, 0.85f) * Mathf.PI));
                    float mid = distance + spacing * 0.5f;
                    if (mid < length)
                    {
                        loopT.Add(mid / length);
                        loopSwell.Add(1f);
                        loopV.Add(node + 1f);
                    }
                    distance += spacing;
                    node++;
                }
                loopT.Add(1f);
                loopSwell.Add(1f + nodeSwell);
                loopV.Add(node + 0.5f);
            }
            else
            {
                for (int r = 0; r <= rings; r++)
                {
                    float t = r / (float)rings;
                    loopT.Add(t);
                    loopSwell.Add(1f);
                    // Same unit as the swelling path when there are nodes to line up with, plain
                    // metres otherwise: props and rope coils want their wood grain in metres.
                    loopV.Add(nodeSpacing > 0f ? t * length / nodeSpacing + 0.5f : t * length * 0.5f);
                }
            }

            int start = _positions.Count;
            for (int r = 0; r < loopT.Count; r++)
            {
                float t = loopT[r];
                Vector3 centre = Vector3.Lerp(from, to, t) + bow * (4f * t * (1f - t));
                float radius = Mathf.Lerp(radiusFrom, radiusTo, t) * loopSwell[r];
                Color tint = color;
                // Cheap contact darkening at the root, standing in for the occlusion bake the
                // foliage does not get.
                if (heightSpan > 0f)
                    tint *= Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(t * length / 1.2f));

                float v = uvV >= 0f
                    ? uvV
                    : nodeSpacing > 0f
                        ? loopV[r] / ArenaTextures.BarkNodesPerTile
                        : loopV[r];
                float height = windHeight >= 0f ? windHeight : (heightSpan > 0f ? t : 0f);

                // One horizontal repeat per BarkWrapMetres of circumference, so the photographed
                // fibre keeps roughly the same world size from a 3 cm shoot to a 29 cm frame-edge
                // culm. Integer, or the strip does not meet itself at the seam; capped at three,
                // because past that the fibre is finer than a pixel at any distance the camera
                // ever sees a culm from.
                float uTiles = nodeSpacing > 0f
                    ? Mathf.Clamp(Mathf.Round(Mathf.PI * (radiusFrom + radiusTo) /
                                              ArenaTextures.BarkWrapMetres), 1f, 3f)
                    : 1f;

                // sides + 1: the last vertex repeats the first position with u at the far end of
                // the strip instead of back at zero. It costs vertices only — the loop below
                // still closes `sides` quads per ring — and without it one facet of every culm
                // carries the whole texture squeezed into it backwards.
                for (int s = 0; s <= sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2f;
                    Vector3 dir = side * Mathf.Cos(a) + forward * Mathf.Sin(a);
                    Push(centre + dir * radius, dir, new Vector2(s / (float)sides * uTiles, v),
                         new Vector2(phase, height), tint);
                }
            }

            int stride = sides + 1;
            for (int r = 0; r < loopT.Count - 1; r++)
                for (int s = 0; s < sides; s++)
                {
                    int i0 = start + r * stride + s;
                    int i1 = i0 + 1;
                    Tri(i0, i0 + stride, i1);
                    Tri(i1, i0 + stride, i1 + stride);
                }
        }

        /// <summary>A ring of tube segments — rope coils and bucket bands.</summary>
        public void Torus(Vector3 centre, float radius, float thickness, Color color, int segments, int sides)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
                Tube(centre + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius,
                     centre + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius,
                     thickness, thickness, color, sides, 1);
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(_positions);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.SetUVs(1, _wind);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
