# Jade Water Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Executed 2026-07-29.** All six tasks done, every self-check silent, measurements in the spec's
> "Результат проверок". Four things went differently from the way they are written here, and the
> spec says what and why: the palette colours are written raw rather than through `Srgb()` (a
> double gamma conversion the measurement caught), `_BedTint` became a Vector, the shadow plumbing
> from Task 5 was pulled forward into Task 4 because an unlit bed was invisible, and Task 6's first
> check was mis-specified and was replaced by a three-frame `WaterProbe`.
>
> Two files this work touches — `Assets/Editor/BambooArena.cs` and `Assets/Editor/ArenaTextures.cs`
> — were being edited by a second Claude session at the same time, so their changes are **not
> committed** here. See the closing note.

**Goal:** Give the river a visible body — a jade colour that comes from light absorbed and scattered along the path through the water, with the riverbed and its caustics visible under it — without moving the water out of the opaque queue.

**Architecture:** No depth texture and no opaque copy. The bed depth is baked into the water mesh's vertex colours, the path length is computed from it and the view ray, and the bed is drawn by the water shader itself using the same texture and the same world-space UV projection the banks already use. Caustics ride in the free `B` channel of the existing water noise map. Fresnel is deliberately cheated down so the body shows at all at a 5–10° camera.

**Tech Stack:** Unity 6000.3.18f1, URP 17.x, linear colour space, HLSL (`Assets/Fight/Arena/Water.shader`), C# editor generators under `Assets/Editor` (no asmdef — everything compiles into `Assembly-CSharp-Editor`). Python 3.11 + PIL/numpy for capture measurement.

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-07-29-jade-water-design.md`. Every number below comes from it.
- **Verification is batchmode self-checks and measured captures, not NUnit.** `Assets/Editor` has no asmdef, so no test assembly reaches this code. The repo convention is a `Debug.LogError` self-check inside the build path: write the check first, watch it fire, then make it stop firing.
- **Unity must be closed** for any batchmode run — the editor holds the project lock and batchmode exits 1.
- **Rebuild command:**
  `"C:/Program Files/Unity/Hub/Editor/6000.3.18f1/Editor/Unity.exe" -batchmode -quit -projectPath C:/Users/user/Mikey -executeMethod FightSceneSetup.RebuildArena -logFile build.log`
  Never pass `-nographics` — there would be no GPU context.
- **Capture command:** same executable, `-executeMethod FightCapture.Shoot -captureOut <file.png> -captureSize 1600x900 -logFile cap.log`. Rebuild and capture cannot share one `-quit` invocation.
- **Two frames from two Unity runs are not comparable.** Ripple alone moves brightness 12–25 units between runs. Every before/after comparison in this plan is either a single-run pair or a calculation outside Unity.
- **Camera and water geometry, fixed:** eye at `y = 1.15`, tilt 2.5°, water plane at `WaterY = -0.6`, channel bed at `WaterY - 0.7`. Eye sits 1.75 m above the surface.
- **Colour convention.** `BambooArena.Srgb(r, g, b)` returns `new Color(r, g, b).linear` and is how every hand-tuned colour constant in the arena is written. The two new palette colours go through it. Note the inconsistency you will see next to them: `_SkyColor` and the old `_DeepColor` are set as raw numbers with no conversion. Do not "fix" that in this work — it is the value the reflection was tuned against. If the measured water in Task 6 lands far off the predicted table, this conversion is the first suspect and the spec says so.
- **Shader properties after this work:** nine new — `_Absorption`, `_ScatterDensity`, `_ShallowColor`, `_BedMap`, `_BedUvScale`, `_BedTint`, `_RefractStrength`, `_FresnelTilt`, `_Caustics` — plus `_DeepColor` repurposed from a flat tint to the deep end of the palette.
- **`viewWS` in this shader points from the surface toward the camera.** The camera is above the water, so `viewWS.y` is the positive sine of the view elevation and the ray travelling *into* the water is `-viewWS`. Every formula below is written in that convention; the spec's prose uses the opposite sign for the same quantity.
- **The arena reshuffles on any change to the `Random` draw order.** This work touches none of it: no task adds, removes or reorders a `Random` call. If a capture shows the grove rearranged, something went wrong.

---

### Task 1: Bed depth in the water mesh, and one UV constant for bed and bank

The water mesh already carries a foam mask in vertex `r`. The bed depth goes into `g`, which is free. The bank's world-space UV projection is the literal `0.25f` in two places; the water shader needs the same number, so it becomes a named constant now rather than a third copy later.

The shore foam weight drops in the same task: it is one constant in the same file, it needs the same rebake, and the reason it drops is that the body of water now does its job.

**Files:**
- Modify: `Assets/Editor/BambooArena.cs` — `Ground` neighbourhood (add `BedDepthAt`), `BuildWater:1284-1313`, `FoamAt:1307-1320`, bank UV at `:973` and `:1186`
- No test file: verification is a self-check inside `BuildWater`

**Interfaces:**
- Produces: `BambooArena.BedDepthAt(float x, float z)` → metres of water above the bed at that point, clamped to `[0, 1]`. `BambooArena.BankUvScale` → `const float` = `0.25f`, the world-space UV scale shared by the bank mesh and the water shader. Task 4 writes `BankUvScale` into the water material.

- [ ] **Step 1: Write the failing self-check**

In `BuildWater`, immediately after the vertex loop that fills `verts` and `colors` (after line 1299):

```csharp
        // The shader reads the bed depth out of vertex g and divides by the view ray's vertical
        // component to get the path length through the water. Zero there is not a dark river, it
        // is no river at all: L collapses, the body term vanishes and the surface goes back to
        // being a mirror. Two points pin it — mid-channel, where the bed is a known 0.7 below the
        // surface, and the bank top, where there is no water above the ground at all.
        float midChannel = colors[IndexOf(xs, zs, 0f, 0f)].g;
        float bankTop = colors[IndexOf(xs, zs, 12f, 0f)].g;
        if (Mathf.Abs(midChannel - 0.7f) > 0.01f || bankTop > 0.001f)
            Debug.LogError($"BambooArena: water vertex g carries no bed depth — mid-channel " +
                           $"{midChannel:F3} (want 0.700), bank top {bankTop:F3} (want 0.000).");
```

And the index helper, next to `GridAxis`:

```csharp
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
```

- [ ] **Step 2: Run the rebuild and watch it fail**

Run:
```bash
"C:/Program Files/Unity/Hub/Editor/6000.3.18f1/Editor/Unity.exe" -batchmode -quit \
  -projectPath C:/Users/user/Mikey -executeMethod FightSceneSetup.RebuildArena -logFile build.log
grep -n "water vertex g" build.log
```
Expected: the error fires, `mid-channel 0.000 (want 0.700)`.

- [ ] **Step 3: Bake the depth, name the UV scale, and cut the shore foam**

Next to `Ground`, after line 81:

```csharp
    /// <summary>Metres of water standing above the bed at a point, zero on dry land. Clamped to a
    /// metre because that is the range the vertex colour byte has to span: the channel bottoms out
    /// 0.7 below the surface, and a byte over a metre quantises to 4 mm, which is finer than the
    /// grid this is sampled on.</summary>
    public static float BedDepthAt(float x, float z) =>
        Mathf.Clamp01(WaterY - Ground(x, z));

    /// <summary>World-space UV scale of the bank's planar projection. The water shader draws the
    /// riverbed with the same texture and must use the same number: the bed is the bank
    /// continuing under the water, and two copies of this constant would part company exactly on
    /// the waterline, which is where the eye is.</summary>
    public const float BankUvScale = 0.25f;
```

In `BuildWater`, line 1298:

```csharp
                colors[v] = new Color(FoamAt(xs[i], zs[j]), BedDepthAt(xs[i], zs[j]), 0f, 1f);
```

In `FoamAt`, line 1319:

```csharp
        // 0.4, down from 0.9. The shore band of foam was a prop: it hid the hard line where the
        // water plane cuts the bank. The body of water now dissolves that line properly — depth
        // goes to zero at the edge and the water becomes the bank — so what is left here is only
        // as much scum as a slow river actually collects against a shore.
        return Mathf.Clamp01(Mathf.Max(shore * 0.4f, piles));
```

At `:973` and `:1186`, replace the literal:

```csharp
                index[i, j] = bake.Push(new Vector3(x, y, z), normal,
                                        new Vector2(x * BankUvScale, z * BankUvScale),
```

```csharp
                uv = new Vector2(p.x * BankUvScale, p.z * BankUvScale); // the bank's own projection
```

- [ ] **Step 4: Rebuild and confirm the check is silent**

Run:
```bash
"C:/Program Files/Unity/Hub/Editor/6000.3.18f1/Editor/Unity.exe" -batchmode -quit \
  -projectPath C:/Users/user/Mikey -executeMethod FightSceneSetup.RebuildArena -logFile build.log
grep -n "water vertex g\|error CS" build.log; echo "exit=$?"
```
Expected: no match for either pattern. `grep` exiting 1 with no output is the pass.

- [ ] **Step 5: Commit**

```bash
git add Assets/Editor/BambooArena.cs Assets/Fight/Arena/M_ArenaWater.mesh Assets/Fight/Arena/M_ArenaGround.mesh
git commit -m "assets: глубина дна в вершинном цвете воды, тайлинг берега — одна константа"
```

---

### Task 2: Caustics in the free B channel of the water noise map

`T_WaterNoise` is 256², `R` foam, `G` gusts, `B` and `A` untouched. Caustics go into `B` as a Voronoi edge field: the network of cell boundaries is what gives thin sharp veins, which is exactly what fbm cannot give at any octave count.

**Files:**
- Modify: `Assets/Editor/ArenaTextures.cs` — `Noise():316-343`, and a new `Veins` helper next to `Fbm:737`

**Interfaces:**
- Produces: `T_WaterNoise` with `B` = caustic veins, values in `[0, 1]`, tiling with period 1 in both axes. Task 5 samples it.

- [ ] **Step 1: Write the failing self-check**

At the top of `Noise()`, before the pixel loop:

```csharp
        // The map tiles across 80 by 54 units of river, so a seam in it is a straight line drawn
        // across the water. Value noise tiles because Hash wraps its cell index; the vein field
        // has to wrap its own cell grid the same way, and this catches the case where it does not.
        for (int i = 0; i < 4; i++)
        {
            float v = i * 0.23f;
            if (Mathf.Abs(Veins(0f, v) - Veins(1f, v)) > 1e-4f ||
                Mathf.Abs(Veins(v, 0f) - Veins(v, 1f)) > 1e-4f)
                Debug.LogError($"ArenaTextures: caustic veins do not tile at v={v:F2} — " +
                               $"{Veins(0f, v):F4} vs {Veins(1f, v):F4} across x, " +
                               $"{Veins(v, 0f):F4} vs {Veins(v, 1f):F4} across y.");
        }
```

And a stub that deliberately does not tile, so the check has something to catch:

```csharp
    private static float Veins(float u, float v) => u + v;
```

- [ ] **Step 2: Run the rebuild and watch it fail**

Run:
```bash
"C:/Program Files/Unity/Hub/Editor/6000.3.18f1/Editor/Unity.exe" -batchmode -quit \
  -projectPath C:/Users/user/Mikey -executeMethod FightSceneSetup.RebuildArena -logFile build.log
grep -n "caustic veins do not tile" build.log
```
Expected: four errors, one per sampled `v`.

- [ ] **Step 3: Implement the vein field and write it into B**

Replace the stub, next to `Fbm`:

```csharp
    /// <summary>Voronoi cell boundaries — the light network a rippled surface throws on a riverbed.
    ///
    /// The value is the gap between the two nearest feature points, which is zero exactly on a cell
    /// boundary and grows toward the middle of a cell. Inverted, that is a web of thin bright lines
    /// rather than a field of blobs, and thin bright lines are the whole reason caustics read as
    /// caustics. An fbm cannot produce them at any octave count: it has no zero set.
    ///
    /// The cell index wraps modulo <paramref name="cells"/>, so the field tiles with period 1 the
    /// same way <see cref="Noise"/> does.</summary>
    private static float Veins(float u, float v, int cells = 6, int seed = 61)
    {
        float px = u * cells, py = v * cells;
        int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py);
        float first = 99f, second = 99f;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int gx = cx + dx, gy = cy + dy;
                float fx = gx + Hash(gx, gy, cells, cells, seed);
                float fy = gy + Hash(gx, gy, cells, cells, seed + 977);
                float d = new Vector2(px - fx, py - fy).magnitude;
                if (d < first) { second = first; first = d; }
                else if (d < second) second = d;
            }
        // 0.35 of a cell is the width the boundary is felt across. Narrower and the veins alias on
        // a 256 map; wider and they thicken into the blobs this exists to avoid.
        return 1f - Mathf.SmoothStep(0f, 0.35f, second - first);
    }
```

In the pixel loop, replace the `Color32` construction:

```csharp
                float caustic = Veins(u, v);
                pixels[y * size + x] = new Color32(
                    (byte)(Mathf.Clamp01(foam) * 255f),
                    (byte)(Mathf.Clamp01(gust) * 255f),
                    (byte)(Mathf.Clamp01(caustic) * 255f), 255);
```

And extend the summary comment above `Noise()`:

```csharp
    ///   B — Voronoi cell boundaries, sampled twice against itself for the caustic net on the bed
```

- [ ] **Step 4: Rebuild and confirm silence, then eyeball the map**

Run:
```bash
"C:/Program Files/Unity/Hub/Editor/6000.3.18f1/Editor/Unity.exe" -batchmode -quit \
  -projectPath C:/Users/user/Mikey -executeMethod FightSceneSetup.RebuildArena -logFile build.log
grep -n "caustic veins do not tile\|error CS" build.log
```
Expected: no matches.

Then look at `Assets/Fight/Arena/T_WaterNoise.asset`'s blue channel: a connected web of bright lines with dark cell interiors. A field of round blobs means `second - first` was replaced by `first` somewhere.

- [ ] **Step 5: Commit**

```bash
git add Assets/Editor/ArenaTextures.cs Assets/Fight/Arena/T_WaterNoise.asset
git commit -m "assets: каустика в свободный канал B шумовой карты воды"
```

---

### Task 3: The body of the water

Path length from the baked depth, absorption and scattering along it, and the Fresnel cheat that makes any of it visible. The bed is a flat placeholder colour in this task — the texture arrives in Task 4 — so that the colour model can be judged on its own.

**Files:**
- Modify: `Assets/Fight/Arena/Water.shader` — properties, `Varyings`, `CBUFFER`, `vert`, `frag`
- Modify: `Assets/Editor/BambooArena.cs` — `WaterMaterial():1728-1760`

**Interfaces:**
- Consumes: `input.color.g` = bed depth in metres, from Task 1.
- Produces: `float3 body` in the fragment stage, and `float h`, `float L`, `float3 viewWS` in scope for Tasks 4 and 5 to build on.

- [ ] **Step 1: Write the failing self-check**

In `WaterMaterial`, at the end before `return mat`:

```csharp
        // The body of the water is what makes it jade, and it is switched on by exactly one thing:
        // a non-zero absorption vector. A material deserialised from before this work has the
        // property at (0,0,0), transmittance comes out 1 everywhere, and the water silently goes
        // back to being a mirror over a flat colour. That is a hard failure to spot by eye and a
        // trivial one to spot here.
        if (mat.GetVector("_Absorption").sqrMagnitude < 1e-6f)
            Debug.LogError("BambooArena: M_ArenaWater has no absorption — the water has no body " +
                           "and will render as a mirror over the bed colour.");
```

- [ ] **Step 2: Run the rebuild and watch it fail**

Run:
```bash
"C:/Program Files/Unity/Hub/Editor/6000.3.18f1/Editor/Unity.exe" -batchmode -quit \
  -projectPath C:/Users/user/Mikey -executeMethod FightSceneSetup.RebuildArena -logFile build.log
grep -n "has no absorption" build.log
```
Expected: the error fires — the property does not exist yet, and `GetVector` on a missing property returns zero.

- [ ] **Step 3: Add the properties and the body to the shader**

In `Properties`, replacing the `_DeepColor` line and adding below it:

```hlsl
        // The two ends of the jade palette. Everything between them is the formula's job, not a
        // gradient texture's: a texture would be right at one depth and wrong at every other, and
        // wrong in particular at the shore and under the bridge where the eye checks it.
        _DeepColor ("Deep Water", Color) = (0.002, 0.050, 0.061, 1)
        _ShallowColor ("Shallow Water", Color) = (0.274, 0.808, 0.715, 1)
        // Per metre, per channel. Red is absorbed seven times faster than green — that ratio *is*
        // why deep water is green, and it is not a stylistic choice.
        _Absorption ("Absorption per metre", Vector) = (1.15, 0.16, 0.28, 0)
        _ScatterDensity ("Scatter Density", Range(0.05, 2)) = 0.45
        _BedTint ("Bed Tint", Color) = (0.55, 0.50, 0.45, 1)
        // Tilts the normal toward the camera for the Fresnel term only. 0.22 is a 12° lie. Without
        // it a 5-10° camera sees 60-90% reflection and there is no body to look at.
        _FresnelTilt ("Fresnel Tilt", Range(0, 0.5)) = 0.22
```

In `CBUFFER_START(UnityPerMaterial)`:

```hlsl
                float4 _ShallowColor;
                float4 _Absorption;
                float4 _BedTint;
                float  _ScatterDensity;
                float  _FresnelTilt;
```

In `Varyings`, after `float foam`:

```hlsl
                float  bedDepth   : TEXCOORD3;
```

In `vert`, after `o.foam = input.color.r;`:

```hlsl
                o.bedDepth = input.color.g;
```

In `frag`, replacing the `deep` / `color` / `fresnel` block at lines 196-201:

```hlsl
                // Path length through the water, along the *unrefracted* view ray.
                //
                // Real refraction squeezes everything into Snell's window: an 85° incidence
                // leaves the surface at 48° and the path comes out 1.05 m, against 0.92 m at 30°.
                // Physically honest, and it means no gradient anywhere in the frame — a 70 cm
                // channel would be one flat colour from the deck to the mist.
                //
                // Taken along the view ray instead, the same channel spans 1.4 m at the bottom of
                // the frame and 7.8 m at the far clamp: exactly the 1-8 m range the reference
                // palette is drawn over. The grazing angle that killed refraction is what pays
                // for the depth gradient. The clamp is 5°, past which fog owns the pixel anyway.
                float h = input.bedDepth;
                float L = h / max(viewWS.y, 0.09);

                float3 bed = _BedTint.rgb;   // the bank's texture takes over in the next task
                float3 transmittance = exp(-_Absorption.rgb * L);
                // Scatter that fades along the path as well as accumulating along it. Without the
                // lerp the colour converges on the scatter tint, so distance makes the water
                // *brighter* and more saturated — the opposite of every reference photograph.
                float scatter = 1.0 - exp(-_ScatterDensity * L);
                float3 body = bed * transmittance
                            + lerp(_ShallowColor.rgb, _DeepColor.rgb, scatter) * scatter;

                // Fresnel against a normal tilted toward the camera. The tilt is a lie told once,
                // here, and nowhere else: the reflection is sampled and the glint is lit with the
                // real normal, or the whole reflected grove slides up the screen with the lie.
                float3 fresnelN = normalize(normalWS + viewWS * _FresnelTilt);
                float fresnel = _FresnelBias
                              + (1.0 - _FresnelBias) * pow(1.0 - saturate(dot(fresnelN, viewWS)), _FresnelPower);

                float3 sky = _SkyColor.rgb * (0.85 + 0.15 * normalWS.y);
                float3 color = lerp(body, sky, saturate(fresnel));
```

And at line 224, drop the reflection floor:

```hlsl
                // Weighted by Fresnel alone. The old 0.12 floor put a ninth of a bright reflection
                // into water viewed straight down, where physics says two percent — and that floor
                // was most of why the near water read as a pale sheet with no colour in it.
                color = lerp(color, reflection.rgb, saturate(_ReflectionStrength * fresnel));
```

- [ ] **Step 4: Set the values on the material**

In `WaterMaterial`, replacing the `_DeepColor` line at `:1737` and adding after it:

```csharp
        // Srgb(), like every other tuned colour constant in this file. The two raw values below it
        // — _SkyColor, _FoamColor — are deliberately not converted: they were tuned raw against
        // the fog, and matching them would be changing a measured result to satisfy a convention.
        mat.SetColor("_DeepColor", Srgb(0.027f, 0.247f, 0.271f));      // #073F45
        mat.SetColor("_ShallowColor", Srgb(0.561f, 0.910f, 0.863f));   // #8FE8DC
        mat.SetVector("_Absorption", new Vector4(1.15f, 0.16f, 0.28f, 0f));
        mat.SetFloat("_ScatterDensity", 0.45f);
        mat.SetColor("_BedTint", Srgb(0.55f, 0.50f, 0.45f));           // the bank's silt multiplier
        mat.SetFloat("_FresnelTilt", 0.22f);
```

And change the existing Fresnel line at `:1744`:

```csharp
        // 3.0, down from the 4.5 that a physical surface wants. Together with _FresnelTilt this
        // takes the reflection from 16/46/64% at 3/10/20 m to 5/24/33%, and what the reflection
        // gives up, the body of the water takes.
        mat.SetFloat("_FresnelPower", 3f);
```

- [ ] **Step 5: Rebuild, confirm silence, and capture**

Run:
```bash
"C:/Program Files/Unity/Hub/Editor/6000.3.18f1/Editor/Unity.exe" -batchmode -quit \
  -projectPath C:/Users/user/Mikey -executeMethod FightSceneSetup.RebuildArena -logFile build.log
grep -n "has no absorption\|error CS\|Shader error" build.log
"C:/Program Files/Unity/Hub/Editor/6000.3.18f1/Editor/Unity.exe" -batchmode -quit \
  -projectPath C:/Users/user/Mikey -executeMethod FightCapture.Shoot \
  -captureOut C:/Users/user/Mikey/issues/water_body.png -captureSize 1600x900 -logFile cap.log
```
Expected: no matches in `build.log`; `issues/water_body.png` shows green in the water, strongest at the bottom of the frame. A flat colour with no distance gradient means `bedDepth` is not reaching the fragment stage — check that `Varyings` got the new member and `vert` writes it.

- [ ] **Step 6: Commit**

```bash
git add Assets/Fight/Arena/Water.shader Assets/Editor/BambooArena.cs Assets/Fight/Arena/M_ArenaWater.mat
git commit -m "feat: тело воды — поглощение и рассеяние по лучу, Френель сдвинут ради него"
```

---

### Task 4: The riverbed, drawn by the water shader

The bed already exists as geometry and texture: `ArenaGround` runs down to `WaterY − 0.7` wearing `T_Ground.jpg`. It is invisible only because the water is opaque. The shader draws the same texture with the same projection, offset by real refraction, so the bed sits under the surface instead of on it — and so the waterline has no seam.

**Files:**
- Modify: `Assets/Fight/Arena/Water.shader` — properties, samplers, `frag`
- Modify: `Assets/Editor/BambooArena.cs` — `WaterMaterial()`

**Interfaces:**
- Consumes: `BambooArena.BankUvScale` (Task 1), `float h`, `float L`, `float3 viewWS` (Task 3).
- Produces: `float2 bedUV` — the world XZ of the point on the bed the ray reaches. Task 5 samples caustics at it.

- [ ] **Step 1: Write the failing self-check**

In `WaterMaterial`, next to the absorption check:

```csharp
        // The bed must be the same map the bank wears, loaded from the same call. Anything else
        // here is not a missing decoration: the water would draw its bed as flat tint and the
        // waterline would grow a seam exactly where the eye goes, between a textured bank and an
        // untextured river.
        Texture2D bankMap = LoadTexture("T_Ground.jpg");
        if (mat.GetTexture("_BedMap") != bankMap)
            Debug.LogError("BambooArena: M_ArenaWater bed map is not the bank's T_Ground.jpg — " +
                           "the waterline will show a seam.");
```

Place it after the `mat.SetTexture("_NoiseMap", ...)` line and before `return mat`, so that in Step 4 the assignment lands above it and the check reads the value that was actually set.

- [ ] **Step 2: Run the rebuild and watch it fail**

Run:
```bash
"C:/Program Files/Unity/Hub/Editor/6000.3.18f1/Editor/Unity.exe" -batchmode -quit \
  -projectPath C:/Users/user/Mikey -executeMethod FightSceneSetup.RebuildArena -logFile build.log
grep -n "bed map is not the bank" build.log
```
Expected: the error fires.

- [ ] **Step 3: Sample the bed in the shader**

In `Properties`:

```hlsl
        _BedMap ("Riverbed (the bank's own map)", 2D) = "grey" {}
        // Written from BambooArena.BankUvScale. Not a free-standing number: the bank projects its
        // UVs at exactly this scale, and the bed is the bank continuing under the water.
        _BedUvScale ("Bed UV Scale", Range(0.05, 1)) = 0.25
        _RefractStrength ("Refraction Strength", Range(0, 0.2)) = 0.035
```

In `CBUFFER_START`:

```hlsl
                float  _BedUvScale;
                float  _RefractStrength;
```

Next to the other texture declarations at line 119:

```hlsl
            TEXTURE2D(_BedMap);        SAMPLER(sampler_BedMap);
```

In `frag`, replacing `float3 bed = _BedTint.rgb;`:

```hlsl
                // Where the ray actually lands on the bed, by Snell — and this one is *not*
                // cheated, unlike the path length above.
                //
                // The two want different things. The path length is a scalar feeding an
                // exponential; all it needs is a gradient, and the long ray supplies one. This is
                // a position on the ground. Computed from the same long ray it would swing metres
                // as the camera pans along the fight line, and the riverbed would swim under the
                // surface. Snell caps the offset inside a 48.6° cone, so it never exceeds 1.13*h
                // — eighty centimetres at the deepest point of the channel.
                float  sinI  = saturate(length(viewWS.xz));
                float  sinT  = sinI / 1.333;
                float2 bedUV = input.positionWS.xz
                             - normalize(viewWS.xz + 1e-6) * (h * sinT * rsqrt(max(1.0 - sinT * sinT, 1e-4)));
                // Ripple wobble. No shallow-water damping term is needed: the offset is already
                // proportional to h, and h goes to zero at the shore on its own.
                bedUV += slope * _RefractStrength;

                float3 bed = SAMPLE_TEXTURE2D(_BedMap, sampler_BedMap, bedUV * _BedUvScale).rgb
                           * _BedTint.rgb;
```

- [ ] **Step 4: Bind it on the material**

In `WaterMaterial`, after the `_NoiseMap` line and **above** the check written in Step 1, so it
reuses the same `bankMap` the check compares against — move the `Texture2D bankMap = ...`
declaration up to here:

```csharp
        Texture2D bankMap = LoadTexture("T_Ground.jpg");
        mat.SetTexture("_BedMap", bankMap);
        mat.SetFloat("_BedUvScale", BankUvScale);
        mat.SetFloat("_RefractStrength", 0.035f);
```

- [ ] **Step 5: Rebuild, confirm silence, capture**

Run:
```bash
"C:/Program Files/Unity/Hub/Editor/6000.3.18f1/Editor/Unity.exe" -batchmode -quit \
  -projectPath C:/Users/user/Mikey -executeMethod FightSceneSetup.RebuildArena -logFile build.log
grep -n "bed map is not the bank\|error CS\|Shader error" build.log
"C:/Program Files/Unity/Hub/Editor/6000.3.18f1/Editor/Unity.exe" -batchmode -quit \
  -projectPath C:/Users/user/Mikey -executeMethod FightCapture.Shoot \
  -captureOut C:/Users/user/Mikey/issues/water_bed.png -captureSize 1600x900 -logFile cap.log
```
Expected: no matches; ground texture visible through the shallow water near the banks, fading to colour toward the middle and the distance.

- [ ] **Step 6: Commit**

```bash
git add Assets/Fight/Arena/Water.shader Assets/Editor/BambooArena.cs Assets/Fight/Arena/M_ArenaWater.mat
git commit -m "feat: дно под водой рисует сам шейдер, смещение по Снеллу"
```

---

### Task 5: Caustics, and the shadow the water never had

Two counter-moving samples of the vein channel, joined with `min()`. The water shader currently calls `GetMainLight()` with no shadow coordinate at all, so it does not know the deck is over it — and a bright light net on the bed directly under a bridge is the kind of mistake that is visible from across a room.

**Files:**
- Modify: `Assets/Fight/Arena/Water.shader` — pragmas, `Varyings`, `vert`, `frag`
- Modify: `Assets/Editor/BambooArena.cs` — `WaterMaterial()`

**Interfaces:**
- Consumes: `float2 bedUV`, `float3 bed`, `float L` (Tasks 3-4); `T_WaterNoise` channel `B` (Task 2).

- [ ] **Step 1: Write the failing self-check**

In `WaterMaterial`, next to the other two checks:

```csharp
        // Caustics at zero are not "off", they are a silent regression: the shader still pays for
        // two texture fetches and the bed still loses the only thing that made it read as being
        // under water rather than painted on the surface.
        if (mat.GetFloat("_Caustics") <= 0f)
            Debug.LogError("BambooArena: M_ArenaWater has no caustics intensity — the bed will " +
                           "render flat.");
```

- [ ] **Step 2: Run the rebuild and watch it fail**

Run:
```bash
"C:/Program Files/Unity/Hub/Editor/6000.3.18f1/Editor/Unity.exe" -batchmode -quit \
  -projectPath C:/Users/user/Mikey -executeMethod FightSceneSetup.RebuildArena -logFile build.log
grep -n "no caustics intensity" build.log
```
Expected: the error fires.

- [ ] **Step 3: Add shadows and caustics to the shader**

In `Properties`:

```hlsl
        _Caustics ("Caustics", Range(0, 4)) = 1.4
```

In `CBUFFER_START`:

```hlsl
                float  _Caustics;
```

After `#pragma multi_compile_fog`:

```hlsl
            // The water has never sampled a shadow. It could get away with it while the surface
            // was a mirror — a mirror does not care what light falls on it. A lit riverbed does,
            // and the deck is directly over the middle of it.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
```

Add to the includes at line 69:

```hlsl
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
```

In `frag`, replace `Light mainLight = GetMainLight();` at line 230 and move it **above** the `bed` sampling block, immediately after `float L = ...`:

```hlsl
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
```

Then, after the `bed` sample in Task 4's block:

```hlsl
                // Caustics. Two samples of the same vein field, moving against each other, joined
                // with min() rather than added: the minimum of two webs is the sharp intersection
                // of them, the sum is a bruise. The coordinate is the point on the *bed*, so the
                // net lies on the ground and stays there when the camera pans.
                //
                // 0.35 per metre puts a Voronoi cell at roughly half a metre, which is the scale
                // a rippled surface actually focuses light at in water this shallow.
                float2 kUV = bedUV * 0.35;
                float k1 = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap,
                                            kUV + _Time.y * float2(0.020, 0.015)).b;
                float k2 = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap,
                                            kUV * 1.31 - _Time.y * float2(0.017, 0.023)).b;
                // Fades along the path, and dies wherever the sun does not reach — under the deck
                // most of all.
                float caustics = min(k1, k2) * exp(-L * 0.55) * mainLight.shadowAttenuation;
                bed += mainLight.color * caustics * _Caustics;
```

- [ ] **Step 4: Set the value on the material**

In `WaterMaterial`:

```csharp
        mat.SetFloat("_Caustics", 1.4f);
```

- [ ] **Step 5: Rebuild, confirm silence, capture**

Run:
```bash
"C:/Program Files/Unity/Hub/Editor/6000.3.18f1/Editor/Unity.exe" -batchmode -quit \
  -projectPath C:/Users/user/Mikey -executeMethod FightSceneSetup.RebuildArena -logFile build.log
grep -n "no caustics intensity\|error CS\|Shader error" build.log
"C:/Program Files/Unity/Hub/Editor/6000.3.18f1/Editor/Unity.exe" -batchmode -quit \
  -projectPath C:/Users/user/Mikey -executeMethod FightCapture.Shoot \
  -captureOut C:/Users/user/Mikey/issues/water_caustics.png -captureSize 1600x900 -logFile cap.log
```
Expected: no matches; a light net on the bed in the open water, absent under the deck. A net that is present under the deck means the shadow coordinate is not reaching — check that the `Shadows.hlsl` include was added and the pragmas are inside the same `HLSLPROGRAM` block.

- [ ] **Step 6: Commit**

```bash
git add Assets/Fight/Arena/Water.shader Assets/Editor/BambooArena.cs Assets/Fight/Arena/M_ArenaWater.mat
git commit -m "feat: каустика на дне, и тень настила, которой у воды не было"
```

---

### Task 6: Measure it

Five checks from the spec. Four are numeric, one is a calculation done outside Unity because the previous water work established that pixels cannot answer it.

**Files:**
- Create: `tools/water_probe.py`
- Create: `docs/superpowers/specs/2026-07-29-jade-water-design.md` — append a "Результат проверок" section (the file exists; this adds to it)

**Interfaces:**
- Consumes: a 1600×900 PNG from `FightCapture.Shoot`.
- Produces: `python tools/water_probe.py <capture.png> [--overlay out.png]` printing mean linear RGB, G/R ratio and inter-row variance per band.

- [ ] **Step 1: Write the probe**

```python
"""
Measures the water bands of a fight capture.

Three bands of water and one control, as fractions of frame size — picked against a 1600x900
capture with the camera in its default framing. Run with --overlay first and look at the file:
if a band has drifted onto the deck or the bank, the numbers below it are meaningless.

Run:
  python tools/water_probe.py issues/water_caustics.png --overlay issues/water_probe.png
"""
import sys
import numpy as np
from PIL import Image, ImageDraw

# name -> (x0, y0, x1, y1) in fractions of width/height
BANDS = {
    "near (under the deck, ~3 m)":  (0.10, 0.90, 0.35, 0.98),
    "mid  (~10 m)":                 (0.05, 0.62, 0.25, 0.68),
    "far  (~16 m, before fog)":     (0.30, 0.555, 0.45, 0.595),
    "shadow (bed under the deck)":  (0.45, 0.90, 0.62, 0.97),
    "control (sky, static)":        (0.42, 0.10, 0.58, 0.18),
}


def srgb_to_linear(a):
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)


def main():
    path = sys.argv[1]
    img = Image.open(path).convert("RGB")
    w, h = img.size
    px = srgb_to_linear(np.asarray(img, dtype=np.float64) / 255.0)

    print(f"{path}  {w}x{h}")
    print(f"{'band':32s} {'R':>8s} {'G':>8s} {'B':>8s} {'G/R':>7s} {'rowvar':>8s}")
    for name, (x0, y0, x1, y1) in BANDS.items():
        crop = px[int(y0 * h):int(y1 * h), int(x0 * w):int(x1 * w)]
        mean = crop.reshape(-1, 3).mean(axis=0)
        # Luminance variance between rows: the flicker measure the ripple work used.
        rows = crop.mean(axis=(1, 2))
        print(f"{name:32s} {mean[0]:8.4f} {mean[1]:8.4f} {mean[2]:8.4f} "
              f"{mean[1] / max(mean[0], 1e-6):7.2f} {rows.var() * 1e4:8.3f}")

    if "--overlay" in sys.argv:
        out = sys.argv[sys.argv.index("--overlay") + 1]
        vis = img.copy()
        draw = ImageDraw.Draw(vis)
        for name, (x0, y0, x1, y1) in BANDS.items():
            draw.rectangle([x0 * w, y0 * h, x1 * w, y1 * h], outline=(255, 0, 0), width=3)
        vis.save(out)
        print(f"overlay -> {out}")


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: Run it against the capture and confirm the bands land on water**

Run:
```bash
python tools/water_probe.py issues/water_caustics.png --overlay issues/water_probe.png
```
Then open `issues/water_probe.png`. Move any rectangle in `BANDS` that has drifted onto the deck, a fighter or the bank, and re-run. The numbers are only worth reading once the boxes are on water.

- [ ] **Step 3: Check the gradient prediction**

From the printed table:

- `G/R ≥ 2` in near, mid and far.
- Mean luminance falls monotonically from near to far.

If luminance **rises** with distance, the model is wrong rather than mistuned — the `lerp` inside the scatter term is the thing that makes it fall, so check that it is present and that `_DeepColor` is the dark end and not the old `#0A1A18` flat tint.

Record the three rows verbatim in the spec.

- [ ] **Step 4: Check the shadow and the shore**

- `shadow` band mean luminance is below the `near` band, and its `rowvar` is not higher — a caustic net firing in shadow shows up as extra row variance, not just as brightness.
- Shore seam: crop 40 px across the waterline at three points along the left bank in an image viewer and confirm no step appears that is larger than the variation within the bank itself. A visible line means `_BedUvScale` and `BankUvScale` have parted company, or `_BedTint` does not match the bank's silt multiplier.

- [ ] **Step 5: Check that the bed does not swim — by calculation, not pixels**

The previous water work established that cross-correlating a periodic pattern in perspective reports a confident zero at any shift, so this is checked outside Unity:

```bash
python - <<'PY'
import numpy as np
# Snell offset, exactly as the shader computes it, for two camera positions a metre apart in X.
eye_y, water_y, bed = 1.15, -0.6, 0.7
for cam_x in (0.0, 1.0):
    p = np.array([[x, z] for x in np.linspace(-8, 8, 33) for z in np.linspace(-6, 14, 41)])
    v = np.column_stack([p[:, 0] - cam_x, np.full(len(p), eye_y - water_y), p[:, 1] + 6.0])
    v /= np.linalg.norm(v, axis=1)[:, None]
    sin_i = np.clip(np.linalg.norm(v[:, [0, 2]], axis=1), 0, 1)
    sin_t = sin_i / 1.333
    off = bed * sin_t / np.sqrt(np.maximum(1 - sin_t**2, 1e-4))
    print(f"cam_x={cam_x}: offset max {off.max():.3f} m (bound {1.13 * bed:.3f})")
PY
```
Expected: both maxima at or below 0.791 m. A value above the bound means the `rsqrt` guard or the `saturate` on `sinI` was dropped.

- [ ] **Step 6: Check the histogram and record everything**

Run the ten-band histogram the way `2026-07-27-water-and-railing-design.md` records it and compare the dark end against the 8.5% / 11.2% figures there. The water taking a colour instead of a pale sheet should not drain the dark end.

Append a `## Результат проверок` section to `docs/superpowers/specs/2026-07-29-jade-water-design.md` with: the three-band table, the shadow comparison, the offset bound, the histogram, and — separately and honestly — anything that could not be measured.

- [ ] **Step 7: Commit**

```bash
git add tools/water_probe.py docs/superpowers/specs/2026-07-29-jade-water-design.md issues/water_probe.png
git commit -m "docs: замеры нефритовой воды — градиент, тень, стык и граница смещения дна"
```

---

## Notes for the reviewer

- **Tasks 3, 4 and 5 each leave the water in a shippable state**, and each is worth judging on its own: the body without the bed, the bed without the caustics, the caustics without the measurements. If Task 3's capture does not read as water, do not proceed to Task 4 — the bed will hide the problem rather than fix it.
- **Nothing here touches the `Random` draw order**, so the grove must come out identical between captures. If it reshuffles, a change strayed outside this plan.
- **The reflection loses roughly half its strength in the near field.** That is the intended trade and it is in the spec, not a regression to fix.
