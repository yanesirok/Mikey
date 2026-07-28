using System.Collections.Generic;
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
///   bridge  x ±8, z ±1.1, deck top at y 0 rising <see cref="Sag"/> toward both ends
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

    private const float HalfLength = 8f;   // bridge spans x ±8, so its ends stay just out of frame
    private const float HalfWidth = 1.1f;  // and z ±1.1
    private const float Sag = 0.18f;       // the ends ride this much higher than mid-span
    private const float ShoreX = 6.8f;     // water's edge; the bridge lands 1.2 units onto the bank
    private const float ChannelEndZ = 26f; // the channel closes here, deep enough to be pure fog
    private const int Seed = 20260727;

    /// <summary>Where the piles stand. Shared with the water builder, which bakes a foam mask
    /// around each one — knowing the positions at build time is what lets the foam skip the
    /// depth texture entirely.</summary>
    private static readonly float[] PileX = { -5.4f, -1.9f, 1.9f, 5.4f };
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
    /// makes it read as something slung between two banks.</summary>
    public static float DeckHeight(float x)
    {
        float t = Mathf.Clamp(x / HalfLength, -1f, 1f);
        return DeckY + Sag * t * t;
    }

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

        BuildBridge(timber, foliage);
        BuildProps(timber, foliage);
        BuildBamboo(bamboo, cards);
        BuildBanks(ground);
        BuildScanFoliage(scan);
        BuildUndergrowth(foliage, cards);

        Mesh timberMesh = timber.ToMesh("ArenaTimber");
        Mesh bambooMesh = bamboo.ToMesh("ArenaBamboo");
        Mesh cardsMesh = cards.ToMesh("ArenaBambooCards");
        Mesh foliageMesh = foliage.ToMesh("ArenaFoliage");
        Mesh groundMesh = ground.ToMesh("ArenaGround");
        Mesh scanMesh = scan.ToMesh("ArenaScan");

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

        var root = new GameObject("Arena");
        GameObject timberGo = AddMesh(root, "Timber", timberMesh, TimberMaterial(wood), castShadows: true);
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
        Debug.Log($"BambooArena: timber {timberMesh.triangles.Length / 3} tris, " +
                  $"bamboo {bambooTris}, cards {cardTris}, vegetation {foliageTris}, " +
                  $"ground {groundMesh.triangles.Length / 3}, scan {scanTris}.");

        // The grove is the frame's budget, and the ceiling was raised to ~115k for the arena
        // deliberately, to buy three times the culms. These numbers catch a regression; they do
        // not police the art decision. If a phone disagrees, cut the tier-2 and tier-3 cards
        // first — they buy the least picture per shaded pixel — and the near foliage last.
        // The card mesh carries bank grass as well as the far tiers' foliage now, so its ceiling
        // is no longer the one set when it held only bamboo.
        if (bambooTris > 95000 || cardTris > 16000 || foliageTris > 30000)
            Debug.LogError($"BambooArena: grove over budget — {bambooTris} tris of culm " +
                           $"(max 95000), {cardTris} of card (max 16000), {foliageTris} of " +
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
        Color plankPale = new Color(1.15f, 1.12f, 1.06f);
        Color plankMid = new Color(0.9f, 0.88f, 0.84f);
        Color plankDark = new Color(0.62f, 0.6f, 0.56f);
        Color beam = new Color(0.5f, 0.48f, 0.44f);
        Color wet = new Color(0.3f, 0.32f, 0.32f);

        // Boards laid across the direction of travel: the repeating edge is what gives the
        // bridge its rhythm and tells the eye how big a fighter is against it.
        const float pitch = 0.27f;   // 25 cm board, 2 cm gap — the gap shows water through it
        const float thickness = 0.055f;
        int index = 0;
        for (float x = -HalfLength; x <= HalfLength; x += pitch, index++)
        {
            float width = Random.Range(0.22f, 0.25f);
            float top = DeckHeight(x);

            // Unevenness is not decoration. A deck of identical boards reads as a printed
            // texture; three or four sitting proud or sunk is what makes it read as built.
            float roll = Random.value;
            if (roll < 0.06f) top += Random.Range(0.012f, 0.03f);      // proud
            else if (roll < 0.12f) top -= Random.Range(0.01f, 0.022f); // sunk

            float halfSpan = HalfWidth + 0.06f;
            float zOffset = Random.Range(-0.03f, 0.03f);
            if (roll > 0.94f)
                halfSpan -= Random.Range(0.15f, 0.35f); // one board short, leaving a gap at the edge

            Color tint = Color.Lerp(plankDark, plankPale, Random.Range(0.25f, 1f));
            tint = Color.Lerp(tint, plankMid, 0.3f);

            var centre = new Vector3(x + width * 0.5f, top - thickness * 0.5f, zOffset);
            var size = new Vector3(width, thickness, halfSpan * 2f);
            // Grain runs down the length of the board, and each board gets its own patch of the
            // map: without the per-board offset the repeat is visible across the whole deck.
            bake.Box(centre, size, tint, new Vector2(0.4f, 3f),
                     new Vector2(Random.value, Random.value), Random.Range(-1.2f, 1.2f));
        }

        // Two longitudinal bearers under the boards, seen through the gaps and at the edges.
        foreach (float z in new[] { -0.72f, 0.72f })
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
            for (int i = -1; i <= 1; i += 2)
            {
                var head = new Vector3(x, DeckHeight(x) - 0.2f, i * 0.62f);
                var foot = new Vector3(x + i * 0.12f, WaterY - 0.85f, i * 0.86f);
                Vector3 At(float y) => Vector3.Lerp(head, foot, Mathf.InverseLerp(head.y, foot.y, y));

                Vector3 damp = At(WaterY + 0.15f);
                Vector3 line = At(WaterY);
                bake.Tube(head, damp, 0.075f, 0.078f, beam, 6, 1);
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

        BuildRailing(bake, foliage, plankPale, plankMid, plankDark);
        BuildAbutments(bake);
    }

    /// <summary>
    /// Far railing only. The near side stays open: a rail between the camera and the fighters
    /// would cut their legs off, which this framing cannot afford.
    ///
    /// Everything here exists to stop it reading as a row of identical black sticks. Nothing is
    /// square to anything: posts lean a degree or two each their own way, differ in height and
    /// thickness, and stand at an uneven step. Rails sag between them, run through the posts
    /// rather than butting against them, and stick out past the last one. One bay is broken.
    /// </summary>
    private static void BuildRailing(Bake bake, Bake foliage, Color pale, Color mid, Color dark)
    {
        float railZ = HalfWidth + 0.06f;
        // Lowered from 0.86. In world terms that was already hip height, but the rail stands
        // 1.16 further from the camera than the fighters do, and seen from an eye at 1.15 it
        // projected across their chests and cut them with a horizontal line.
        const float topRail = 0.72f;
        const float lowRail = 0.38f;

        // 2.4 / 2.9 / 2.6 repeating: a constant step is the loudest thing announcing a loop.
        var spacing = new[] { 2.4f, 2.9f, 2.6f };
        var posts = new List<float>();
        for (float x = -HalfLength + 0.55f, i = 0; x <= HalfLength - 0.55f; x += spacing[(int)i % 3], i++)
            posts.Add(x);

        int brokenBay = 2; // the one span with its top rail gone

        for (int p = 0; p < posts.Count; p++)
        {
            float x = posts[p];
            float baseY = DeckHeight(x);
            // Ends five centimetres above the top rail. At 1.0 the posts overshot it by twenty
            // and the railing read as a row of spikes rather than as a fence.
            float height = 0.86f + Random.Range(-0.05f, 0.05f);
            float thick = 0.1f * Random.Range(0.88f, 1.12f);
            // A post beside the broken bay took the same knock the rail did.
            // Kept small. At ±4° every post leaned visibly and the whole run read as damaged
            // rather than as weathered; the one bay that is meant to be broken has to be the
            // only thing that looks broken.
            float lean = p == brokenBay || p == brokenBay + 1
                ? Random.Range(3.5f, 5.5f) * (p == brokenBay ? -1f : 1f)
                : Random.Range(-2f, 2f);
            Quaternion tilt = Quaternion.Euler(0f, Random.Range(-3f, 3f), lean);

            bake.Box(new Vector3(x, baseY + height * 0.40f, railZ), new Vector3(thick, height, thick),
                     pale * Random.Range(1.0f, 1.18f), new Vector2(0.4f, 3f),
                     new Vector2(Random.value, Random.value), tilt);

            // Moss at the foot, on the shaded side. Goes in the foliage mesh because the timber
            // material is the wood map and moss has no business being wood-coloured.
            if (Random.value < 0.7f)
                Blades(foliage, new Vector3(x + Random.Range(-0.07f, 0.07f), baseY - 0.02f, railZ + 0.05f),
                       Random.Range(0.1f, 0.2f), 1.4f, 0.5f, 5,
                       Srgb(0.16f, 0.22f, 0.11f) * Random.Range(0.8f, 1.2f), Random.value, 0f, 1f);
        }

        // Rails. Three sub-segments per bay so the sag is a curve rather than a kink, and the
        // run overshoots the end posts by 7 cm — a through tenon, not two boxes meeting.
        foreach ((float y, float thickness, bool top) rail in
                 new[] { (topRail, 0.075f, true), (lowRail, 0.065f, false) })
        {
            for (int bay = 0; bay < posts.Count - 1; bay++)
            {
                if (rail.top && bay == brokenBay)
                    continue; // this span is what is missing

                float x0 = posts[bay] - (bay == 0 ? 0.07f : 0f);
                float x1 = posts[bay + 1] + (bay == posts.Count - 2 ? 0.07f : 0f);
                for (int s = 0; s < 3; s++)
                {
                    float a = Mathf.Lerp(x0, x1, s / 3f);
                    float b = Mathf.Lerp(x0, x1, (s + 1) / 3f);
                    float mid01 = ((s + 0.5f) / 3f - 0.5f) * 2f;
                    float sag = 0.02f * (1f - mid01 * mid01); // 2 cm at the middle of the span
                    float centreX = (a + b) * 0.5f;
                    bake.Box(new Vector3(centreX, DeckHeight(centreX) + rail.y - sag, railZ),
                             new Vector3(b - a + 0.01f, rail.thickness, rail.thickness),
                             pale * 1.08f, new Vector2(0.4f, 3f),
                             new Vector2(Random.value, Random.value), Random.Range(-0.6f, 0.6f));
                }
            }

            // Worn strip along the top of the handrail: lighter where hands have polished it.
            // The single most noticeable wear detail on a railing and the one most often missed.
            if (!rail.top)
                continue;
            for (int bay = 0; bay < posts.Count - 1; bay++)
            {
                if (bay == brokenBay)
                    continue;
                float centreX = (posts[bay] + posts[bay + 1]) * 0.5f;
                float span = posts[bay + 1] - posts[bay];
                bake.Box(new Vector3(centreX, DeckHeight(centreX) + rail.y + 0.032f, railZ),
                         new Vector3(span * 0.92f, 0.012f, rail.thickness * 0.55f),
                         pale * 1.06f, new Vector2(0.4f, 3f),
                         new Vector2(Random.value, Random.value), 0f, 0.004f);
            }
        }

        // The broken span: two splintered stubs left in the posts either side.
        for (int side = 0; side <= 1; side++)
        {
            float from = posts[brokenBay + side];
            float dir = side == 0 ? 1f : -1f;
            float length = Random.Range(0.35f, 0.6f);
            bake.Box(new Vector3(from + dir * length * 0.5f, DeckHeight(from) + topRail + 0.03f, railZ),
                     new Vector3(length, 0.06f, 0.06f), dark * 0.9f, new Vector2(0.4f, 3f),
                     new Vector2(Random.value, Random.value), Quaternion.Euler(0f, dir * 4f, dir * 9f));
        }
    }

    /// <summary>Stone steps where the bridge lands on each bank. Without them the deck ends in
    /// mid-air, which is visible the moment the camera pans to either extreme.</summary>
    private static void BuildAbutments(Bake bake)
    {
        Color stone = new Color(0.95f, 0.97f, 0.98f);
        Color stoneDark = new Color(0.55f, 0.58f, 0.6f);

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
        public readonly float RadiusMin;
        public readonly float RadiusMax;
        public readonly float HeightMin;
        public readonly float HeightMax;

        public Tier(int sides, float nodeSwell, int rings, int branchPairs, float leafLength,
                    int cards, float cardSize, float radiusMin, float radiusMax,
                    float heightMin, float heightMax)
        {
            Sides = sides;
            NodeSwell = nodeSwell;
            Rings = rings;
            BranchPairs = branchPairs;
            LeafLength = leafLength;
            Cards = cards;
            CardSize = cardSize;
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
    //                                          sides swell rings pairs leaf  cards size   rMin    rMax    hMin  hMax
    private static readonly Tier EdgeTier = new Tier(8, 0.09f,   0,    2, 0.315f, 0, 0f,    0.0975f, 0.146f, 9f,  14f);
    private static readonly Tier NearTier = new Tier(6, 0f,      6,    2, 0.46f,  0, 0f,    0.039f, 0.107f,  4.5f, 8f);
    private static readonly Tier MidTier  = new Tier(6, 0f,      2,    0, 0f,     3, 2.25f, 0.068f, 0.117f,  6f,  11f);
    private static readonly Tier FarTier  = new Tier(6, 0f,      1,    0, 0f,     1, 3.4f,  0.068f, 0.117f,  8f,  15f);
    private static readonly Tier ShootTier = new Tier(6, 0f,     6,    1, 0.34f,  0, 0f,    0.029f, 0.049f,  1.5f, 3f);

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

    // ponytail: the grove is the frame's main cost. If a real phone disagrees, cut the tier-3
    // cluster count first — fog is already doing most of that tier's work.
    private static void BuildBamboo(Bake bake, Bake cards)
    {
        // Four steps of brightness, and now of saturation too. Depth here comes from the tonal
        // step between tiers, not from blur: a background that is merely out of focus still reads
        // flat, while separated values read as separate planes even when sharp.
        Color edge = Culm(0.561f, 0.659f, 0.235f, 0.114f, 1.30f);  // #8FA83C at the near-black level
        // 0.45, not the 0.32 this tier used to sit at. Once the crowns filled with foliage the
        // near grove became most of the left and right thirds of the frame, and at the old value
        // it read as one black curtain with no step between it and the frame-edge culms. This is
        // the one place the tonal ladder was genuinely wrong and not just desaturated.
        Color near = Culm(0.659f, 0.690f, 0.290f, 0.45f, 1.35f);   // #A8B04A, the mature culm
        // The far tiers are pulled toward the reference hue further than before, but their
        // saturation is barely touched: they stand where the fog does, and saturated green in the
        // haze flattens the depth the ladder exists to build. The distance has to end in sky.
        Color mid = Color.Lerp(Srgb(0.46f, 0.50f, 0.35f),
                               Culm(0.659f, 0.690f, 0.290f, 0.481f, 1.15f), 0.70f);
        Color far = Color.Lerp(Srgb(0.62f, 0.66f, 0.55f),
                               Culm(0.659f, 0.690f, 0.290f, 0.644f, 1.05f), 0.35f);

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

    /// <summary>Bank growth, one step below the near tier in luminance. Brighter than the culms
    /// standing over it, not darker: the ground it stands on is the darkest surface in the scene,
    /// and grass pitched below the grove disappeared into it.</summary>
    private static Color Turf => Culm(0.561f, 0.659f, 0.235f, 0.52f, 1.25f);

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
            // A tier down to two cards is far enough that individual leaves are sub-pixel, so it
            // gets the dense blob; the nearer card tier gets the three readable fans.
            int cell = tier.Cards <= 2 ? 3 : Random.Range(0, 3);
            cards.Card(Along(t) + Random.insideUnitSphere * (height * 0.025f),
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
                index[i, j] = bake.Push(new Vector3(x, y, z), normal, new Vector2(x * 0.25f, z * 0.25f),
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

    private static void BuildUndergrowth(Bake bake, Bake cards)
    {
        Color reed = Srgb(0.30f, 0.36f, 0.17f);
        Color fern = Srgb(0.20f, 0.28f, 0.13f);
        // Was Srgb(0.22, 0.29, 0.15), darkened on purpose to keep the water dark. The water has
        // since been lifted, and against it a pad measured (16, 21, 21) to the water's (70, 87,
        // 94) — four times darker and with no green left after the grade. At that ratio a leaf
        // stops being a leaf and becomes a hole. Two to one is the most it can take.
        Color pad = Srgb(0.30f, 0.42f, 0.19f);
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
        // 0.52 — brighter than the culms standing over it (0.45), not darker. The bank lies in the
        // shadow the bridge throws, and at the value the ground itself has, grass painted a step
        // below the grove disappears into it. In the reference the bank growth is the lightest
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

        // Lily pads, three size classes rather than one continuous range — real ones grow in
        // distinct stages, and a smooth spread of sizes reads as noise.
        for (int i = 0; i < 240; i++)
        {
            float x = Random.Range(-13f, 13f);
            float z = Mathf.Lerp(-10f, 26f, Random.value * Random.value);
            if (Ground(x, z) > WaterY - 0.05f)
                continue;
            float roll = Random.value;
            float radius = roll < 0.5f ? Random.Range(0.1f, 0.16f)
                         : roll < 0.85f ? Random.Range(0.2f, 0.28f)
                         : Random.Range(0.34f, 0.46f);
            var centre = new Vector3(x, WaterY + 0.02f, z);
            LilyPad(bake, centre, radius, pad * Random.Range(0.75f, 1.25f));
            if (Random.value < 0.05f)
                Blades(bake, centre + new Vector3(0f, 0.05f, 0f), 0.11f, 0.6f, -0.2f, 6, bloom,
                       Random.value, 0.3f, 0.5f);
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
                uv = new Vector2(p.x * 0.25f, p.z * 0.25f); // the bank's own projection
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

        // Near white, and deliberately not the turf tint the drawn grass uses. These plants carry
        // their colour in a photograph; multiplying that by a green tint multiplies two greens and
        // sends them black. The vertex colour here is a multiplier, nothing more — the same
        // mistake the decking and the bank each cost a build to learn.
        Color lit = new Color(1.05f, 1.08f, 0.98f);

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
                colors[v] = new Color(FoamAt(xs[i], zs[j]), 0f, 0f, 1f);
            }
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

        return Mathf.Clamp01(Mathf.Max(shore * 0.9f, piles));
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

    private static void LilyPad(Bake bake, Vector3 centre, float radius, Color color)
    {
        const int sides = 7;
        float phase = Random.value;
        float notch = Random.value * Mathf.PI * 2f;
        var wind = new Vector2(phase, 0.05f);
        int hub = bake.Push(centre, Vector3.up, new Vector2(0.5f, 0.5f), wind, color * 0.85f);

        var rim = new int[sides + 1];
        for (int i = 0; i <= sides; i++)
        {
            float a = notch + 0.4f + i / (float)sides * (Mathf.PI * 2f - 0.8f);
            var p = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (radius * Random.Range(0.86f, 1.1f));
            p.y += 0.012f;
            rim[i] = bake.Push(p, Vector3.up, new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f),
                               wind, color);
        }
        for (int i = 0; i < sides; i++)
            bake.Tri(hub, rim[i], rim[i + 1]);
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

    private static Material TimberMaterial(ArenaTextures.Surface wood)
    {
        Material mat = ArenaMaterial("M_ArenaWood", CullMode.Back, 0f, 1f, 0f, 1f, 1.1f);
        mat.SetTexture("_BaseMap", wood.Albedo);
        mat.SetTexture("_BumpMap", wood.Normal);
        mat.SetTexture("_MaskMap", wood.Mask);
        mat.SetFloat("_BumpScale", 1.3f);
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

    private static Material WaterMaterial()
    {
        Shader shader = Shader.Find("Mikey/Water");
        if (shader == null)
        {
            Debug.LogError("BambooArena: shader Mikey/Water not found (compile error?).");
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }
        Material mat = LoadOrCreate("M_ArenaWater", shader);
        mat.SetColor("_DeepColor", new Color(0.039f, 0.102f, 0.094f));
        // The sky colour, not a dark tint. At a 5-10° view angle Fresnel puts 60-90% of the
        // surface into reflection, so water darker than the sky above it is physically
        // impossible — and a 0.27 sky against a 0.78 fog is exactly why it read as a grey
        // sheet painted across the bottom of the frame.
        mat.SetColor("_SkyColor", new Color(0.784f, 0.824f, 0.800f));
        mat.SetColor("_FoamColor", new Color(0.769f, 0.812f, 0.788f));
        mat.SetFloat("_FresnelPower", 4.5f);
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
            for (int i = 0; i < 8; i++)
                Tri(hub, rim[i], rim[(i + 1) % 8]);
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

                for (int s = 0; s < sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2f;
                    Vector3 dir = side * Mathf.Cos(a) + forward * Mathf.Sin(a);
                    Push(centre + dir * radius, dir, new Vector2(s / (float)sides, v),
                         new Vector2(phase, height), tint);
                }
            }

            for (int r = 0; r < loopT.Count - 1; r++)
                for (int s = 0; s < sides; s++)
                {
                    int i0 = start + r * sides + s;
                    int i1 = start + r * sides + (s + 1) % sides;
                    Tri(i0, i0 + sides, i1);
                    Tri(i1, i0 + sides, i1 + sides);
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
