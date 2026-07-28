using UnityEditor;
using UnityEngine;

/// <summary>
/// Generated texture maps for the arena — weathered board, bamboo bark, and the blob shadow
/// sprite. Written rather than sourced because every other surface in this scene is generated:
/// a photogrammetry plank dropped next to procedural bamboo reads as borrowed, which is the
/// usual reason an indie scene looks assembled out of other people's work.
///
/// The noise is periodic value noise rather than <see cref="Mathf.PerlinNoise"/>, which does
/// not tile — a deck is nothing but the same map repeated a hundred and forty times, so a seam
/// or a drifting low frequency would be visible immediately.
///
/// Normal maps are saved as plain colour assets, not imported as normal maps: a texture created
/// through AssetDatabase has no importer to set the type on, so the shader decodes them by hand
/// (rgb * 2 - 1) rather than through UnpackNormal, which would expect the DXT5nm swizzle.
/// </summary>
public static class ArenaTextures
{
    private const string Dir = "Assets/Fight/Arena/";

    public struct Surface
    {
        public Texture2D Albedo;
        public Texture2D Normal;
        public Texture2D Mask; // g = occlusion, a = smoothness
    }

    /// <summary>
    /// Weathered decking. Grain runs along U, which the bridge maps down the length of each
    /// board, so the fibre reads across the bridge the way sawn boards do. Three things carry
    /// it: the fibre itself, dark streaks where the grain is coarse, and broad wet patches
    /// pushed into the smoothness channel — the wet patches are what sell "after rain", far
    /// more than the colour does.
    /// </summary>
    public static Surface Wood()
    {
        const int size = 512;
        var albedo = new Color32[size * size];
        var normal = new Color32[size * size];
        var mask = new Color32[size * size];
        var height = new float[size * size];

        Color dark = new Color(0.38f, 0.34f, 0.29f).linear;
        Color mid = new Color(0.55f, 0.51f, 0.44f).linear;
        Color pale = new Color(0.70f, 0.66f, 0.58f).linear;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                float v = y / (float)size;
                int i = y * size + x;

                // Stretched 1:22 so the fibre runs the length of the board rather than blotching.
                float fibre = Fbm(u * 2f, v * 44f, 3, 2, 44, 11);
                float coarse = Fbm(u * 2f, v * 14f, 2, 2, 14, 23);
                float grain = fibre * 0.65f + coarse * 0.35f;

                // Ridged: a few hard dark lines rather than an even gradient. Sawn timber has
                // sharp grain boundaries, and a smooth blend is what makes wood read as plastic.
                float ridge = 1f - Mathf.Abs(grain * 2f - 1f);
                float streak = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.80f, 0.99f, ridge));

                // Deliberately weak. One board is mapped to roughly one tile, so any noise at a
                // low frequency becomes patches the size of a hand — the first pass came out
                // looking like camouflage rather than timber. Broad variation belongs between
                // boards, where the per-board vertex tint puts it, not inside one.
                float knot = Fbm(u * 3f, v * 3f, 2, 3, 3, 41);
                float tone = Mathf.Clamp01(grain * 0.92f + knot * 0.08f);

                Color c = tone < 0.5f
                    ? Color.Lerp(dark, mid, tone * 2f)
                    : Color.Lerp(mid, pale, (tone - 0.5f) * 2f);
                c = Color.Lerp(c, dark, streak * 0.22f);

                height[i] = grain * 0.7f + streak * 0.3f;
                albedo[i] = ToColor32(c);

                // Broad damp patches. Smoothness is the whole point of this channel: dry
                // weathered timber is matt, and the wet strip beside it catches the sky.
                float wet = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.70f,
                    Fbm(u * 3f, v * 3f, 2, 3, 3, 67)));
                float smooth = Mathf.Lerp(0.06f, 0.5f, wet);
                // Grain valleys hold dirt and stay dark; that is where occlusion lives.
                float occ = Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(grain * 1.2f)) * (1f - streak * 0.35f);
                mask[i] = new Color32(0, (byte)(occ * 255f), 0, (byte)(smooth * 255f));
            }
        }

        // 1.4, not 2.2: once the boards gained chamfered edges the strong grain normal on top of
        // the specular turned every plank into harsh black stripes.
        BuildNormal(height, size, 1.4f, normal);
        return new Surface
        {
            Albedo = Save("T_Wood", size, albedo),
            Normal = Save("T_Wood_N", size, normal, linear: true),
            Mask = Save("T_Wood_M", size, mask),
        };
    }

    /// <summary>Metres of culm covered by one vertical tile of <see cref="Bark"/>: exactly
    /// <see cref="BarkNodesPerTile"/> internodes, so the painted rings line up with the geometric
    /// ones on the frame-edge culms instead of drifting into a second, offset row of nodes.</summary>
    public const float BarkNodeSpacing = 0.34f;
    /// <summary>Three, not the six the drawn map used: the reference strip carries exactly three
    /// node rings and tiles seamlessly on all three, so cutting it down to one internode would
    /// discard the natural variation in pitch and buy a cross-faded seam for it.</summary>
    public const int BarkNodesPerTile = 3;
    public const float BarkTileHeight = BarkNodeSpacing * BarkNodesPerTile;

    /// <summary>Metres of culm circumference under one horizontal repeat of the bark strip.
    /// The drawn map pre-stretched its noise 8.7:1 (<c>Fbm(u * 26, v * 3)</c>) so that one wrap
    /// looked right on any culm; a photograph carries its own fibre scale and cannot. Our culms
    /// run from 0.25 m of circumference at the shoots to 0.92 m at the frame edge, so the repeat
    /// count comes from the culm instead — see <c>Bake.Tube</c>.</summary>
    public const float BarkWrapMetres = 0.25f;

    /// <summary>
    /// Fraction of its own height the reference strip is rolled by, to bring its printed node
    /// rings onto the geometric ones. The strip has them at v 0.2366 / 0.6062 / 0.9631 and the
    /// contract wants (k + 0.5) / 3.
    ///
    /// About 0.09 of an internode of error survives any roll and always will: the photographed
    /// internodes are 731, 757 and 560 rows tall while the contract spaces them evenly. Three
    /// centimetres on a 34 cm internode is not visible; half an internode would be, which is
    /// what <see cref="CheckRingPhase"/> is set to catch.
    /// </summary>
    private const float BarkRingRoll = 0.1020f;

    /// <summary>Mean the bark albedo is renormalised to, in the same gamma space the procedural
    /// generator authored in — its own was 0.72 of base plus about 0.11 of fibre.</summary>
    private const float BarkTargetValue = 0.83f;

    /// <summary>
    /// Bamboo bark. Carries the fine vertical fibre, the blotching of age, and — for every culm
    /// past the frame edge — the node itself: a dark ring with the pale waxy band beneath it.
    ///
    /// Only the six frame-edge culms can afford node rings as geometry; at eight units the swell
    /// is under a pixel and it is the *banding* the eye reads, not the bump. So the ring lives
    /// here, phased so that a ring sits at the tile boundary — which is where
    /// <c>Bake.Tube</c> puts its geometric swell as well.
    /// </summary>
    public static Surface Bark()
    {
        Texture2D srcColour = LoadRef("JA14_Bark_BC");
        Texture2D srcNormal = LoadRef("JA14_Bark_N");
        Texture2D srcRough = LoadRef("JA14_Bark_R");
        if (srcColour == null || srcNormal == null || srcRough == null)
            return default;

        int width = srcColour.width, height = srcColour.height;
        if (srcNormal.width != width || srcNormal.height != height ||
            srcRough.width != width || srcRough.height != height)
        {
            Debug.LogError($"ArenaTextures: the three bark references disagree on size — " +
                           $"BC {srcColour.width}x{srcColour.height}, " +
                           $"N {srcNormal.width}x{srcNormal.height}, " +
                           $"R {srcRough.width}x{srcRough.height}.");
            return default;
        }

        Color32[] srcC = srcColour.GetPixels32();
        Color32[] srcN = srcNormal.GetPixels32();
        Color32[] srcR = srcRough.GetPixels32();

        var albedo = new Color32[width * height];
        var normal = new Color32[width * height];
        var mask = new Color32[width * height];

        // Rec.709 in the photograph's own gamma space. The arena authors every map in gamma and
        // lets ToColor32(c.linear) do the one conversion; matching that convention matters more
        // than being right about it, because every colour constant here was tuned against it.
        var luma = new float[width * height];
        float sum = 0f;
        for (int i = 0; i < luma.Length; i++)
        {
            luma[i] = (0.2126f * srcC[i].r + 0.7152f * srcC[i].g + 0.0722f * srcC[i].b) / 255f;
            sum += luma[i];
        }
        // Straight to the value the drawn map had. A 1:1 swap would land the culms at 0.39 of
        // their old linear brightness and take the frame-edge tier, whose vertex colour is
        // already 0.012, into black.
        float gain = BarkTargetValue / Mathf.Max(sum / luma.Length, 1e-4f);

        // GetPixels32 and SetPixels32 both run bottom to top, so y is v and the roll moves the
        // rings down in v by taking each destination row from further up the source.
        int roll = Mathf.RoundToInt(BarkRingRoll * height);

        for (int y = 0; y < height; y++)
        {
            int sy = (y + roll) % height;
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x, s = sy * width + x;

                float value = Mathf.Clamp01(luma[s] * gain);
                albedo[i] = ToColor32(new Color(value, value, value).linear);

                // DirectX green. At every one of the three rings the reference reads below 128
                // above the ring and above 128 below it, which is the Y-down polarity for a
                // raised ridge; Unity wants Y-up.
                normal[i] = new Color32(srcN[s].r, (byte)(255 - srcN[s].g), srcN[s].b, 255);

                // g = occlusion, a = smoothness. There is no AO map in the pack, so occlusion
                // comes from the albedo's own luminance, ranged to the 0.64..1 the drawn map
                // authored. The node scar is the darkest band in the photograph, so the dip
                // lands on the node for free — which is what the drawn map did by formula.
                float occlusion = Mathf.Lerp(0.64f, 1f, value);
                // The pack's R map is roughness: 155-162 over the sheath scar, 106-113 over the
                // waxy collar below it. Dark is smooth, which is the correct polarity.
                float smooth = 1f - srcR[s].r / 255f;
                mask[i] = new Color32(0, (byte)(occlusion * 255f), 0, (byte)(smooth * 255f));
            }
        }

        CheckRingPhase(albedo, width, height);

        return new Surface
        {
            Albedo = Save("T_Bark", width, height, albedo),
            Normal = Save("T_Bark_N", width, height, normal, linear: true),
            Mask = Save("T_Bark_M", width, height, mask),
        };
    }

    /// <summary>A third-party reference map, or null with a loud error. These live outside the
    /// player build: nothing but this file and the crown baker ever references them.</summary>
    private static Texture2D LoadRef(string name)
    {
        string path = Dir + "Ref/" + name + ".png";
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex == null)
            Debug.LogError($"ArenaTextures: {path} is missing — run tools/ja14_extract.py.");
        return tex;
    }

    /// <summary>
    /// Fails loudly when the printed node rings drift out of phase with the geometric swell.
    /// <c>Bake.Tube</c> puts its swollen loop at v = (k + 0.5) / <see cref="BarkNodesPerTile"/>
    /// and the map has to paint its ring in the same place; half an internode out and a
    /// frame-edge culm carries two rows of nodes, one modelled and one printed.
    ///
    /// The tolerance is 0.2 of an internode, not something tighter: about 0.09 is irreducible
    /// because the photographed internodes are unequal, and the failure worth catching — a lost
    /// or remeasured roll — is half an internode or more.
    /// </summary>
    private static void CheckRingPhase(Color32[] albedo, int width, int height)
    {
        var rowMean = new float[height];
        for (int y = 0; y < height; y++)
        {
            float sum = 0f;
            for (int x = 0; x < width; x++)
                sum += albedo[y * width + x].r;
            rowMean[y] = sum / width;
        }

        for (int k = 0; k < BarkNodesPerTile; k++)
        {
            // Row index is v: GetPixels32 hands the texture over bottom to top.
            float target = (k + 0.5f) / BarkNodesPerTile;
            int centre = Mathf.RoundToInt(target * height);
            int span = height / (BarkNodesPerTile * 2);
            int darkest = centre % height;
            for (int d = -span; d <= span; d++)
            {
                int y = ((centre + d) % height + height) % height;
                if (rowMean[y] < rowMean[darkest])
                    darkest = y;
            }
            float actual = (darkest + 0.5f) / height;
            float error = Mathf.Abs(actual - target) * BarkNodesPerTile;
            if (error > 0.2f)
                Debug.LogError($"ArenaTextures: bark ring {k} sits {error:F3} of an internode " +
                               $"from its geometric node (v {actual:F4}, want {target:F4}). " +
                               $"BarkRingRoll needs remeasuring against the reference strip.");
        }
    }

    /// <summary>
    /// One tiling noise map doing two jobs for the water, packed into two channels because both
    /// want the same kind of data at different scales and a second texture would buy nothing:
    ///   R — mid frequency, sampled twice in opposite directions to make the foam edge breathe
    ///   G — large and smooth, sampled once very slowly to decide where the surface is glassy
    ///       and where the wind has ruffled it
    /// </summary>
    public static Texture2D Noise()
    {
        const int size = 256;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                float v = y / (float)size;
                float foam = Fbm(u * 8f, v * 8f, 4, 8, 8, 3);
                // Two octaves only, so the gust field stays broad: a high-frequency gust mask
                // reads as noise on the water rather than as weather crossing it.
                float gust = Fbm(u * 3f, v * 3f, 2, 3, 3, 29);
                pixels[y * size + x] = new Color32(
                    (byte)(Mathf.Clamp01(foam) * 255f),
                    (byte)(Mathf.Clamp01(gust) * 255f), 0, 255);
            }
        return Save("T_WaterNoise", size, pixels);
    }

    /// <summary>Soft dark oval for the contact shadow under each fighter. A real shadow alone
    /// leaves a gap when the key is steep; the blob is what actually welds feet to boards.</summary>
    public static Texture2D Blob()
    {
        const int size = 128;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 0.15f, r));
                pixels[y * size + x] = new Color32(0, 0, 0, (byte)(a * 255f));
            }
        return Save("T_Blob", size, pixels);
    }

    /// <summary>
    /// Four leaf-cluster silhouettes in one 512 atlas, laid out 2×2, for the tiers too far away
    /// to pay for geometry. Cells 0 and 1 hang downward, cell 2 sweeps sideways, cell 3 is a
    /// dense mass with no readable individual leaves — that one carries tier 3, where a leaf is
    /// a pixel and only the blob matters.
    ///
    /// Drawn with <see cref="LeafHalfWidth"/>, the same lanceolate profile the geometric blades
    /// use, because the moment the far tiers use a different leaf shape they read as a different
    /// species standing behind the first one.
    /// </summary>
    /// <param name="radii">Per cell, the reach of the drawn cluster in each of eight directions,
    /// in cell-UV units. The card mesh is cut to this instead of being a quad: a quad around a
    /// cluster this shape spends most of its fill rate on transparent pixels, and on a phone it
    /// is fill rate, not triangles, that a grove runs out of.</param>
    public static Texture2D LeafAtlas(out float[,] radii)
    {
        const int size = LeafAtlasGrid * 256;
        const int cell = 256;
        var pixels = new Color32[size * size];

        // Near-neutral, like every other map here: albedo is texture × vertex colour, and the
        // vertex colour already carries the tier's tint. Painting the reference leaf green in
        // here as well would multiply two greens together and land the far tiers in the dark.
        // What this does carry is the value break between the shaded base of a leaf and its
        // lit tip, which is the part a flat tint cannot produce.
        Color shade = new Color(0.62f, 0.68f, 0.58f);
        Color lit = new Color(0.94f, 1.0f, 0.88f);

        // Fill RGB everywhere, including fully transparent pixels: bilinear filtering blends
        // colour across the alpha edge, and an unwritten black surround draws a dark outline
        // around every leaf at distance.
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = ToColor32(Color.Lerp(shade, lit, 0.5f).linear);
        var alpha = new float[size * size];

        var rng = new System.Random(20260727);
        float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        for (int c = 0; c < LeafAtlasCells; c++)
        {
            int ox = c % LeafAtlasGrid * cell, oy = c / LeafAtlasGrid * cell;

            // Cells 4 and up are bank grass, not bamboo: many thin arching blades rather than
            // twigs carrying leaves. They live in the same atlas so the whole scene keeps one
            // clipped material and one draw call.
            if (c >= GrassCellFirst)
            {
                int blades = rng.Next(46, 62);
                var root = new Vector2(0.5f, 0.06f);
                for (int b = 0; b < blades; b++)
                {
                    // Steeply up, then arching over: a blade that leaves the ground sideways
                    // reads as a fallen stalk, and one that stays straight reads as a reed.
                    float angle = Mathf.PI * 0.5f + Rand(-0.85f, 0.85f);
                    var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    float len = Rand(0.42f, 0.82f);
                    float bend = Rand(0.5f, 1.15f);
                    var from = root + new Vector2(Rand(-0.10f, 0.10f), Rand(-0.02f, 0.05f));
                    float lift = Rand(0f, 1f);

                    for (int s = 0; s <= 160; s++)
                    {
                        float t = s / 160f;
                        Vector2 p = from + dir * (len * t) + Vector2.down * (bend * len * t * t);
                        // Thin, but not thinner than alpha clipping can carry. At 0.007 a blade
                        // was a pixel and a half wide, its alpha never reached 1 anywhere, and
                        // mip maps plus a 0.35 cutoff erased the entire cell — the cards were in
                        // the mesh and invisible on screen. Three pixels of solid core is the
                        // floor for anything that gets clipped.
                        float halfWidth = 0.018f * len * Mathf.Sin(Mathf.Pow(t, 0.4f) * Mathf.PI);
                        Color tint = Color.Lerp(shade, lit, Mathf.Clamp01(lift * 0.5f + t * 0.6f));
                        Stamp(pixels, alpha, size, ox, oy, cell, p, halfWidth, tint);
                    }
                }
                continue;
            }

            bool sideways = c == 2;
            bool dense = c == 3;
            // A cell is a whole branch mass, not one fan: several twigs, each carrying its own
            // small leaves. Drawn as a handful of big leaves instead, a 1.5 m card puts metre-long
            // leaves in the frame — which is exactly what the first pass did, and it read as
            // shrubbery hanging in the sky rather than as bamboo at thirty units.
            int twigs = dense ? 9 : 6;
            var stem = sideways ? new Vector2(0.09f, 0.74f) : new Vector2(0.5f, 0.88f);

            for (int b = 0; b < twigs; b++)
            {
                float twigAngle = (sideways ? 0f : -Mathf.PI * 0.5f)
                                + (sideways ? Rand(-0.5f, 0.8f) : Rand(-1.05f, 1.05f));
                var twigDir = new Vector2(Mathf.Cos(twigAngle), Mathf.Sin(twigAngle));
                float twigLen = dense ? Rand(0.22f, 0.34f) : Rand(0.26f, 0.40f);
                Vector2 tip = stem + twigDir * twigLen + Vector2.down * (twigLen * 0.25f);

                // The twig itself, one pixel wide: at this size it is a hairline, and its absence
                // is why the leaves would otherwise float unattached.
                for (int s = 0; s <= 220; s++)
                {
                    float t = s / 220f;
                    Vector2 p = Vector2.Lerp(stem, tip, t) + Vector2.down * (twigLen * 0.1f * t * (1f - t));
                    Stamp(pixels, alpha, size, ox, oy, cell, p, 0.004f, shade);
                }

                int leaves = dense ? rng.Next(7, 11) : rng.Next(6, 10);
                for (int l = 0; l < leaves; l++)
                {
                    float angle = twigAngle + Rand(-0.9f, 0.9f);
                    var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    float len = dense ? Rand(0.07f, 0.12f) : Rand(0.09f, 0.16f);
                    float droop = Rand(0.35f, 0.7f);
                    float lift = Rand(0f, 1f);
                    Vector2 from = Vector2.Lerp(stem, tip, Rand(0.45f, 1f));

                    const int steps = 90;
                    for (int s = 0; s <= steps; s++)
                    {
                        float t = s / (float)steps;
                        Vector2 p = from + dir * (len * t) + Vector2.down * (droop * len * t * t);
                        float halfWidth = LeafHalfWidth(t) * len;
                        // Darker at the base, lighter toward the tip, and each leaf sitting at
                        // its own level — a fan of leaves all one value is a green blob.
                        Color tint = Color.Lerp(shade, lit, Mathf.Clamp01(lift * 0.6f + t * 0.5f));
                        Stamp(pixels, alpha, size, ox, oy, cell, p, halfWidth, tint);
                    }
                }
            }
        }

        for (int i = 0; i < pixels.Length; i++)
            pixels[i].a = (byte)(Mathf.Clamp01(alpha[i]) * 255f);

        // Measure what was actually drawn rather than assuming a shape. 1.0824 = 1/cos(22.5°),
        // which pushes the octagon's vertices out far enough that its *edges* still clear the
        // furthest pixel between two of them.
        radii = new float[LeafAtlasCells, 8];
        for (int c = 0; c < LeafAtlasCells; c++)
        {
            int ox = c % LeafAtlasGrid * cell, oy = c / LeafAtlasGrid * cell;
            for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    if (alpha[(oy + y) * size + ox + x] < 0.35f)
                        continue;
                    float dx = (x + 0.5f) / cell - 0.5f, dy = (y + 0.5f) / cell - 0.5f;
                    int octant = Mathf.RoundToInt(Mathf.Atan2(dy, dx) / (Mathf.PI * 0.25f) + 8f) % 8;
                    radii[c, octant] = Mathf.Max(radii[c, octant], Mathf.Sqrt(dx * dx + dy * dy));
                }
            for (int d = 0; d < 8; d++)
                radii[c, d] = Mathf.Clamp(radii[c, d] * 1.0824f, 0.08f, 0.5f);
        }

        // ponytail: mip maps thin the alpha out, so a far card can shrink under the cutoff. The
        // clusters are solid enough in the middle that it does not show at these distances; if a
        // tier ever fades out, rescale each mip's alpha to preserve coverage in Save.
        // MIKEY_ATLAS_PREVIEW=<path> writes the atlas out as a PNG. Kept because this map is the
        // one asset whose failure is invisible from the rendered frame: blades drawn a pixel and
        // a half wide left every card in the mesh and nothing on screen, and the only way to tell
        // that apart from a placement bug was to look at the map itself.
        Texture2D atlas = Save("T_LeafCard", size, pixels);
        string preview = System.Environment.GetEnvironmentVariable("MIKEY_ATLAS_PREVIEW");
        if (!string.IsNullOrEmpty(preview))
            System.IO.File.WriteAllBytes(preview, atlas.EncodeToPNG());
        return atlas;
    }

    /// <summary>Atlas layout: a square grid of 256 px cells. Cells 0–3 are bamboo foliage,
    /// <see cref="GrassCellFirst"/> and up are bank grass. One atlas rather than two keeps the
    /// whole clipped-foliage scene on a single material.</summary>
    public const int LeafAtlasGrid = 3;
    public const int GrassCellFirst = 4;
    public const int LeafAtlasCells = 7;

    /// <summary>Half-width of a lanceolate leaf at <paramref name="t"/> along its length, as a
    /// fraction of that length. Widest a third of the way along and drawn to a point: a leaf
    /// widest in the middle reads as grass. 1:8 overall, which is the real proportion — the
    /// earlier 1:20 is why the grove read as sedge.</summary>
    public static float LeafHalfWidth(float t) => 0.06f * Mathf.Sin(Mathf.Pow(t, 0.5f) * Mathf.PI);

    // ------------------------------------------------------------------ helpers

    /// <summary>Soft-edged disc of leaf into one atlas cell. Coverage accumulates as a max, not
    /// a sum, so overlapping leaves stay opaque instead of building a halo.</summary>
    private static void Stamp(Color32[] pixels, float[] alpha, int size, int ox, int oy, int cell,
                              Vector2 p, float radius, Color tint)
    {
        float r = radius * cell;
        if (r < 0.5f)
            r = 0.5f;
        int cx = Mathf.RoundToInt(p.x * cell), cy = Mathf.RoundToInt(p.y * cell);
        int span = Mathf.CeilToInt(r) + 1;
        // 3 px of margin inside the cell so bilinear filtering never samples the neighbour.
        for (int y = cy - span; y <= cy + span; y++)
        {
            if (y < 3 || y >= cell - 3)
                continue;
            for (int x = cx - span; x <= cx + span; x++)
            {
                if (x < 3 || x >= cell - 3)
                    continue;
                float d = Mathf.Sqrt((x - p.x * cell) * (x - p.x * cell) + (y - p.y * cell) * (y - p.y * cell));
                float a = Step(r, r - 1.5f, d);
                if (a <= 0f)
                    continue;
                int i = (oy + y) * size + ox + x;
                if (a <= alpha[i])
                    continue;
                alpha[i] = a;
                pixels[i] = ToColor32(tint.linear);
            }
        }
    }

    /// <summary>HLSL <c>smoothstep</c>. Unity's <see cref="Mathf.SmoothStep"/> is a different
    /// function — it interpolates <em>between</em> its first two arguments — and using it as an
    /// edge function silently produces a ramp that never reaches either end.</summary>
    private static float Step(float edge0, float edge1, float x) =>
        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, x));

    /// <summary>Sobel the height field into a tangent-space normal map. Wrapped sampling, so
    /// the normal tiles exactly like the albedo it came from.</summary>
    private static void BuildNormal(float[] height, int size, float strength, Color32[] output)
    {
        float At(int x, int y) => height[((y % size + size) % size) * size + ((x % size + size) % size)];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = At(x + 1, y) - At(x - 1, y);
                float dy = At(x, y + 1) - At(x, y - 1);
                Vector3 n = new Vector3(-dx * strength * size / 64f, -dy * strength * size / 64f, 1f).normalized;
                output[y * size + x] = new Color32(
                    (byte)((n.x * 0.5f + 0.5f) * 255f),
                    (byte)((n.y * 0.5f + 0.5f) * 255f),
                    (byte)((n.z * 0.5f + 0.5f) * 255f), 255);
            }
    }

    private static Texture2D Save(string name, int size, Color32[] pixels, bool linear = false) =>
        Save(name, size, size, pixels, linear);

    /// <summary>
    /// Writes a generated map as a native Texture2D asset.
    ///
    /// <paramref name="linear"/> must be true for anything carrying data rather than colour. It
    /// was false for every map here including the normals, which meant a flat texel
    /// (0.5, 0.5, 1.0) reached the shader as (0.214, 0.214, 0.920) and tilted every surface by
    /// about 37°. Colour space is a constructor argument, so a texture whose flag changed has to
    /// be recreated — reusing the asset silently keeps the old one.
    /// </summary>
    private static Texture2D Save(string name, int width, int height, Color32[] pixels,
                                  bool linear = false)
    {
        string path = Dir + name + ".asset";
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex == null || tex.width != width || tex.height != height || tex.isDataSRGB == linear)
        {
            AssetDatabase.DeleteAsset(path);
            tex = new Texture2D(width, height, TextureFormat.RGBA32, true, linear);
            AssetDatabase.CreateAsset(tex, path);
        }
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Trilinear;
        tex.anisoLevel = 8; // decking seen at a grazing angle is mip mush without it
        tex.SetPixels32(pixels);
        tex.Apply(true);
        EditorUtility.SetDirty(tex);
        return tex;
    }

    private static Color32 ToColor32(Color c) => new Color32(
        (byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f),
        (byte)(Mathf.Clamp01(c.b) * 255f), 255);

    /// <summary>Periodic value noise. The period is per-axis so a map can be stretched — wood
    /// grain is 1:14 — and still tile in both directions.</summary>
    private static float Noise(float x, float y, int periodX, int periodY, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float xf = x - xi, yf = y - yi;
        float u = xf * xf * (3f - 2f * xf);
        float v = yf * yf * (3f - 2f * yf);
        float a = Hash(xi, yi, periodX, periodY, seed);
        float b = Hash(xi + 1, yi, periodX, periodY, seed);
        float c = Hash(xi, yi + 1, periodX, periodY, seed);
        float d = Hash(xi + 1, yi + 1, periodX, periodY, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }

    private static float Fbm(float x, float y, int octaves, int periodX, int periodY, int seed)
    {
        float sum = 0f, amp = 1f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Noise(x, y, periodX, periodY, seed + i * 17) * amp;
            norm += amp;
            amp *= 0.5f;
            x *= 2f; y *= 2f; periodX *= 2; periodY *= 2;
        }
        return sum / norm;
    }

    private static float Hash(int x, int y, int periodX, int periodY, int seed)
    {
        x = (x % periodX + periodX) % periodX;
        y = (y % periodY + periodY) % periodY;
        unchecked
        {
            int h = x * 73856093 ^ y * 19349663 ^ seed * 83492791;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0x7fffffff) / 2147483647f;
        }
    }
}
