using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Renders bamboo foliage from the JA14 pack into square RGBA buffers for the leaf-card atlas.
///
/// A cell is built one of two ways. The far tier's cell is the whole scanned clump, seen from far
/// enough back that no individual leaf resolves — which is exactly what that tier wants. Every
/// nearer cell is a twig of photographed leaf clusters, assembled here.
///
/// The clump cannot supply the second kind and it is worth writing down why: an adult plant's
/// canopy is a continuous volume of leaves, so there is no camera distance that isolates a
/// readable branch out of it. Framing into it from three angles produced three grey walls at 62
/// to 77 per cent coverage. The pack's flat leaf sheet is a single three-blade cluster with a
/// clean alpha, and a handful of those on a twig is what a branch mass actually looks like.
///
/// The subject is staged a kilometre above the arena rather than on a private layer: the scene is
/// open and full of geometry while this runs, and a tight near/far plane at y = 10000 excludes
/// all of it without touching the layer table.
/// </summary>
public static class BambooCrownBake
{
    private const string RefDir = "Assets/Fight/Arena/Ref/";
    private const string ModelPath = RefDir + "JA14_Bamboo.fbx";

    /// <summary>Rendered at this multiple of the cell and box-filtered down. Supersampling rather
    /// than MSAA, because the matte below has to average cleanly and four resolved samples do not
    /// give enough levels for a leaf tip.</summary>
    private const int Supersample = 4;

    /// <summary>Fraction of the cell the baked silhouette is fitted to. Not 1: the drawn cells
    /// keep three pixels of margin so bilinear filtering never samples the neighbouring cell, and
    /// a baked one has to keep the same.</summary>
    private const float CellFit = 0.88f;

    /// <summary>Value range the baked cells are mapped onto, matching the shade-to-lit span the
    /// drawn ones used. Fixing both ends rather than the mean is what stops a high-contrast
    /// framing from coming out as black and white blotches.</summary>
    private const float LeafShade = 0.66f;
    private const float LeafLit = 0.98f;

    public enum Subject
    {
        /// <summary>The whole scanned plant, culm hidden. A dense mass at any distance.</summary>
        Clump,

        /// <summary>A twig carrying photographed leaf clusters, built here.</summary>
        Twig,
    }

    /// <summary>Where the camera stands and what it is looking at.</summary>
    public readonly struct Pose
    {
        public readonly int Cell;
        public readonly Subject Subject;
        public readonly float Yaw;    // degrees around the subject
        public readonly float Pitch;  // degrees, positive looks down
        /// <summary>Twig cells only: how many leaf clusters hang off it, and the seed that places
        /// them. Distinct seeds are the whole reason three mid-tier cells do not read as the same
        /// photograph three times.</summary>
        public readonly int Clusters;
        public readonly int Seed;
        /// <summary>Twig cells only: how far the clusters sweep from the stem, as a multiple of a
        /// cluster's own size. Low is a compact fan, high is a long arching branch.</summary>
        public readonly float Reach;

        public Pose(int cell, Subject subject, float yaw, float pitch, int clusters, int seed,
                    float reach)
        {
            Cell = cell;
            Subject = subject;
            Yaw = yaw;
            Pitch = pitch;
            Clusters = clusters;
            Seed = seed;
            Reach = reach;
        }
    }

    /// <summary>
    /// Cells 0-2 are the mid tier's branch masses, 3 is the far tier's dense blob, and 7 and 8 are
    /// the near tier's crowns. Cells 4-6 are bank grass and stay drawn.
    ///
    /// The near cells carry fewer, larger clusters than the mid ones: at six metres a card has to
    /// show leaves, and at twenty-five it has to show a mass.
    /// </summary>
    public static readonly Pose[] Poses =
    {
        new Pose(0, Subject.Twig, 8f, 6f, 9, 20260728, 2.1f),
        new Pose(1, Subject.Twig, -14f, 10f, 8, 20260729, 1.7f),
        new Pose(2, Subject.Twig, 20f, 2f, 10, 20260730, 2.6f),
        new Pose(3, Subject.Clump, 70f, 8f, 0, 0, 0f),
        new Pose(7, Subject.Twig, -6f, 4f, 6, 20260731, 1.5f),
        new Pose(8, Subject.Twig, 16f, 12f, 5, 20260732, 1.3f),
    };

    private static readonly Vector3 Stage = new Vector3(0f, 10000f, 0f);

    /// <summary>
    /// Renders one pose. <paramref name="rgb"/> receives unpremultiplied colour already carried
    /// into the arena's authoring convention; <paramref name="coverage"/> receives alpha in 0..1
    /// for the radii pass, which measures the CPU buffer and not the texture. Returns false with
    /// a logged error if anything is missing, so the atlas can leave the cell alone.
    /// </summary>
    public static bool Render(in Pose pose, int size, Color32[] rgb, float[] coverage)
    {
        Texture2D sheet = Composite();
        if (sheet == null)
            return false;

        GameObject subject = null;
        GameObject rig = null;
        Material material = null;
        RenderTexture target = null;
        Texture2D readback = null;
        RenderTexture previous = RenderTexture.active;
        bool fog = RenderSettings.fog;

        try
        {
            material = LeafMaterial(sheet);
            subject = pose.Subject == Subject.Clump ? BuildClump() : BuildTwig(pose);
            if (subject == null)
                return false;
            foreach (MeshRenderer renderer in subject.GetComponentsInChildren<MeshRenderer>())
                renderer.sharedMaterial = material;

            Bounds bounds = Encapsulate(subject);
            rig = new GameObject("BambooCrownBake") { hideFlags = HideFlags.HideAndDontSave };
            Camera cam = rig.AddComponent<Camera>();
            float distance = Frame(cam, bounds, pose, 0f);

            // The arena is a foggy grove and URP applies fog in the unlit pass too. Baked into a
            // card it would fog the leaves twice: once here, once by the scene the card sits in.
            RenderSettings.fog = false;

            int render = size * Supersample;
            target = new RenderTexture(render, render, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1, // supersampling does this job; MSAA here would double-resolve
            };
            readback = new Texture2D(render, render, TextureFormat.RGBA32, false);

            // Fitted on what actually landed on screen rather than on the mesh bounds. Foliage is
            // mostly air, so the bounds overstate it by a factor that changes with the angle, and
            // a hand-tuned distance would need retuning the moment a pose moves.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Color32[] black = Shoot(cam, target, readback, Color.black);
                Color32[] white = Shoot(cam, target, readback, Color.white);
                Matte(black, white, render, size, rgb, coverage);

                float extent = SilhouetteExtent(coverage, size);
                if (extent <= 0f)
                {
                    Debug.LogError($"BambooCrownBake: cell {pose.Cell} rendered nothing — the " +
                                   $"camera is not looking at the subject.");
                    return false;
                }
                if (Mathf.Abs(extent - CellFit) < 0.04f)
                    return true;
                if (attempt == 7)
                {
                    Debug.LogError($"BambooCrownBake: cell {pose.Cell} would not fit its cell — " +
                                   $"the silhouette spans {extent:P0} after eight attempts, want " +
                                   $"{CellFit:P0}. The subject is filling the frame at every " +
                                   $"distance, which means it has no silhouette to fit.");
                    return true;
                }
                // A silhouette that reaches the border is clipped, so its measured extent
                // understates how far the camera has to go; step by a fixed factor rather than by
                // a ratio that would creep out at fourteen per cent a time.
                distance = Frame(cam, bounds, pose,
                                 extent >= 0.995f ? distance * 1.6f : distance * extent / CellFit);
            }
            return true;
        }
        finally
        {
            RenderSettings.fog = fog;
            RenderTexture.active = previous;
            if (sheet != null) Object.DestroyImmediate(sheet);
            if (readback != null) Object.DestroyImmediate(readback);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (material != null) Object.DestroyImmediate(material);
            if (rig != null) Object.DestroyImmediate(rig);
            if (subject != null) Object.DestroyImmediate(subject);
        }
    }

    /// <summary>
    /// The scanned plant with its culm switched off.
    ///
    /// The FBX carries bark and leaves as two children named MI_LOD2 and MI_LOD2_001, and with
    /// materialImportMode None there are no material names to tell them apart. Vertex count does:
    /// the crown outnumbers the culm 4.6 to 1 on the adult LOD2 and 2 to 1 on the smallest mesh
    /// in the pack. A photographed culm inside the card would double up on the procedural one the
    /// card hangs from.
    /// </summary>
    private static GameObject BuildClump()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogError($"BambooCrownBake: {ModelPath} is missing — run tools/ja14_extract.py.");
            return null;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.hideFlags = HideFlags.HideAndDontSave;
        instance.transform.position = Stage;

        MeshFilter[] parts = instance.GetComponentsInChildren<MeshFilter>();
        if (parts.Length != 2)
        {
            Debug.LogError($"BambooCrownBake: {ModelPath} has {parts.Length} meshes, expected 2 " +
                           $"(one bark, one leaves).");
            Object.DestroyImmediate(instance);
            return null;
        }
        int leaves = parts[0].sharedMesh.vertexCount >= parts[1].sharedMesh.vertexCount ? 0 : 1;
        parts[1 - leaves].gameObject.SetActive(false);
        return instance;
    }

    /// <summary>
    /// A twig carrying leaf clusters, each one a quad of the photographed sheet.
    ///
    /// The sheet is a single three-blade cluster with its stem on the bottom edge, so a quad is
    /// attached by its bottom-centre and then turned. Bamboo hangs its foliage: the clusters roll
    /// past vertical as they go out along the twig, which is what stops a card reading as a bush.
    /// </summary>
    private static GameObject BuildTwig(in Pose pose)
    {
        var root = new GameObject($"Twig{pose.Cell}") { hideFlags = HideFlags.HideAndDontSave };
        root.transform.position = Stage;

        var rng = new System.Random(pose.Seed);
        float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        for (int i = 0; i < pose.Clusters; i++)
        {
            float t = pose.Clusters > 1 ? i / (pose.Clusters - 1f) : 0.5f;
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.hideFlags = HideFlags.HideAndDontSave;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(root.transform, false);

            // Along the twig, drooping as it goes: the sag is what a leafy branch does and a
            // straight line of clusters reads as a fence.
            float along = t * pose.Reach + Rand(-0.12f, 0.12f);
            var attach = new Vector3(along, -0.55f * along * along + Rand(-0.10f, 0.10f),
                                     Rand(-0.35f, 0.35f));

            float size = Rand(0.8f, 1.15f) * Mathf.Lerp(1.05f, 0.7f, t);
            // Past vertical, and further the further out it hangs.
            float roll = 180f + Mathf.Lerp(-38f, 38f, (float)rng.NextDouble()) + t * 22f;
            quad.transform.localRotation = Quaternion.Euler(Rand(-16f, 16f), Rand(-26f, 26f), roll);
            quad.transform.localScale = new Vector3(size, size, size);
            // The stem sits on the sheet's bottom edge, so the quad hangs from its own edge and
            // not from its middle.
            quad.transform.localPosition = attach + quad.transform.localRotation * Vector3.up * (size * 0.5f);
        }
        return root;
    }

    private static Bounds Encapsulate(GameObject subject)
    {
        var bounds = new Bounds(subject.transform.position, Vector3.zero);
        bool first = true;
        foreach (MeshRenderer renderer in subject.GetComponentsInChildren<MeshRenderer>())
        {
            if (first)
            {
                bounds = renderer.bounds;
                first = false;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        return bounds;
    }

    private static Material LeafMaterial(Texture2D sheet)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
        {
            hideFlags = HideFlags.HideAndDontSave,
        };
        material.SetTexture("_BaseMap", sheet);
        material.SetFloat("_Surface", 0f);   // opaque queue…
        material.SetFloat("_AlphaClip", 1f); // …with a clip, so the matte stays linear
        material.SetFloat("_Cutoff", 0.5f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.EnableKeyword("_ALPHATEST_ON");
        return material;
    }

    /// <summary>
    /// Points the camera at the subject and returns the distance it settled at.
    /// <paramref name="override"/> above 0 uses that distance instead of deriving one from the
    /// bounds, which is how the fit loop closes in on the cell.
    /// </summary>
    private static float Frame(Camera cam, Bounds bounds, in Pose pose, float @override)
    {
        const float fov = 24f;
        float span = Mathf.Max(bounds.size.magnitude, 0.01f);
        float distance = @override > 0f
            ? @override
            : span * 0.5f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);

        Quaternion rotation = Quaternion.Euler(pose.Pitch, pose.Yaw, 0f);
        cam.transform.position = bounds.center - rotation * Vector3.forward * distance;
        cam.transform.rotation = rotation;

        cam.enabled = false; // driven by hand; there is no player loop in batch mode anyway
        cam.fieldOfView = fov;
        cam.nearClipPlane = Mathf.Max(distance - span, 0.01f);
        cam.farClipPlane = distance + span * 2f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.allowMSAA = false;
        cam.allowHDR = false;

        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = false; // or the scene's grade tonemaps the atlas
        data.renderShadows = false;
        data.requiresColorOption = CameraOverrideOption.Off;
        data.requiresDepthOption = CameraOverrideOption.Off;
        return distance;
    }

    /// <summary>The silhouette's larger dimension as a fraction of the cell, so the fit loop can
    /// scale the distance by the ratio it is out by. Measured off the coverage the matte produced,
    /// which is the same buffer the radii are later measured from.</summary>
    private static float SilhouetteExtent(float[] coverage, int size)
    {
        int minX = size, minY = size, maxX = -1, maxY = -1;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if (coverage[y * size + x] < 0.35f)
                    continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        if (maxX < 0)
            return 0f;
        return Mathf.Max(maxX - minX + 1, maxY - minY + 1) / (float)size;
    }

    private static Color32[] Shoot(Camera cam, RenderTexture target, Texture2D readback, Color clear)
    {
        cam.backgroundColor = clear;
        cam.targetTexture = target;
        cam.Render();
        cam.targetTexture = null;

        RenderTexture.active = target;
        readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        readback.Apply();
        return readback.GetPixels32();
    }

    /// <summary>
    /// Box-filters both renders down to the cell and recovers the matte.
    ///
    /// Two backgrounds, not one alpha channel: both RP assets have m_AllowPostProcessAlphaOutput
    /// 0 and nothing guarantees usable alpha out of an ARGB32 target. Where the subject covers a
    /// texel both renders agree; where it does not they differ by exactly one, so the difference
    /// is the background's share of the texel.
    ///
    /// The division by coverage is not optional. Writing the over-black render as the colour
    /// leaves every rim texel premultiplied toward black, and the mip chain then pulls the whole
    /// leaf edge dark — the failure the fill-the-transparent-texels rule exists to prevent.
    /// </summary>
    private static void Matte(Color32[] black, Color32[] white, int render, int size,
                              Color32[] rgb, float[] coverage)
    {
        int step = render / size;
        float samples = step * step;
        var value = new float[size * size];
        int covered = 0;

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float br = 0f, bg = 0f, bb = 0f, diff = 0f;
                for (int sy = 0; sy < step; sy++)
                    for (int sx = 0; sx < step; sx++)
                    {
                        int s = (y * step + sy) * render + x * step + sx;
                        br += black[s].r;
                        bg += black[s].g;
                        bb += black[s].b;
                        diff += (white[s].r - black[s].r) + (white[s].g - black[s].g) +
                                (white[s].b - black[s].b);
                    }

                float a = Mathf.Clamp01(1f - diff / (samples * 3f * 255f));
                int i = y * size + x;
                coverage[i] = a;
                if (a < 0.004f)
                    continue;

                // Unpremultiplied, then straight to a value: the vertex colour carries the hue,
                // and a green photograph multiplied by a green tint crushes the far tiers.
                float r = br / (samples * 255f * a);
                float g = bg / (samples * 255f * a);
                float b = bb / (samples * 255f * a);
                value[i] = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                covered++;
            }

        // Both ends fixed, not just the mean. Scaling to a mean let a high-contrast framing clip
        // half its texels to white and leave the rest black; the drawn cells span a known shade to
        // lit range and the baked ones have to sit in the same place on the tonal ladder.
        // Percentiles rather than min and max, so one specular texel cannot set the exposure.
        var ranked = new float[Mathf.Max(covered, 1)];
        int n = 0;
        for (int i = 0; i < value.Length && n < ranked.Length; i++)
            if (coverage[i] >= 0.004f)
                ranked[n++] = value[i];
        System.Array.Sort(ranked);
        float low = ranked[Mathf.Clamp(n * 5 / 100, 0, Mathf.Max(n - 1, 0))];
        float high = ranked[Mathf.Clamp(n * 95 / 100, 0, Mathf.Max(n - 1, 0))];
        float spread = Mathf.Max(high - low, 1e-4f);

        for (int i = 0; i < value.Length; i++)
        {
            if (coverage[i] < 0.004f)
                continue;
            float v = Mathf.Lerp(LeafShade, LeafLit, Mathf.Clamp01((value[i] - low) / spread));
            // The same one gamma step every drawn cell goes through: the arena authors in gamma
            // and stores the .linear of it in an sRGB texture. Wrong, uniform, and every colour
            // constant in this scene was tuned against it.
            var b8 = (byte)(Mathf.Clamp01(new Color(v, v, v).linear.r) * 255f);
            rgb[i] = new Color32(b8, b8, b8, 255);
        }
    }

    /// <summary>
    /// JA14 keeps colour and opacity in separate files and no URP shader samples two maps for one
    /// alpha, so they are combined once into a throwaway RGBA texture.
    /// </summary>
    private static Texture2D Composite()
    {
        var colour = AssetDatabase.LoadAssetAtPath<Texture2D>(RefDir + "JA14_Leaves_BC.png");
        var opacity = AssetDatabase.LoadAssetAtPath<Texture2D>(RefDir + "JA14_Leaves_M.png");
        if (colour == null || opacity == null)
        {
            Debug.LogError($"BambooCrownBake: {RefDir}JA14_Leaves_BC.png or _M.png is missing — " +
                           $"run tools/ja14_extract.py.");
            return null;
        }
        if (colour.width != opacity.width || colour.height != opacity.height)
        {
            Debug.LogError($"BambooCrownBake: leaf colour is {colour.width}x{colour.height} but " +
                           $"its mask is {opacity.width}x{opacity.height}.");
            return null;
        }

        Color32[] pixels = colour.GetPixels32();
        Color32[] mask = opacity.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
            pixels[i].a = mask[i].r; // the mask is one channel triplicated; R, G and B are equal

        var sheet = new Texture2D(colour.width, colour.height, TextureFormat.RGBA32, true, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
        };
        sheet.SetPixels32(pixels);
        sheet.Apply(true);
        return sheet;
    }
}
