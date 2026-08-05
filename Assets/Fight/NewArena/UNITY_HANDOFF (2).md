# Bamboo Arena — Unity Handoff

Everything needed to bring this scene into Unity URP, what transfers automatically, what must
be rebuilt by hand, and why.

**Source of truth:** `C:\Users\Ras\blender\bamboo_grove\bamboo_grove.blend`
**Reference frame:** `renders\full.png` — the Unity result should match this.

---

## 1. What you are importing

| | |
|---|---|
| Triangles | 185,253 |
| Objects / draw calls | 9 |
| Materials | 9 |
| Largest dimension | 130 m (fog domain; actual playable area ~40 m) |
| Units | metric, scale 1.0 |

| object | tris | material | notes |
|---|---:|---|---|
| `Merged_fern_02` | 67,544 | fern_02 | 540 plants merged into one mesh |
| `BambooLeaves` | 57,654 | M_ArenaLeafCard | alpha cards, needs clip |
| `BambooWall` | 28,591 | M_ArenaBamboo | culms, 6-sided tubes + billboards |
| `Merged_boulder_01` | 12,657 | boulder_01 | 41 rocks, LOD baked per distance |
| `Terrain` | 11,290 | Bank | |
| `Bridge` | 5,813 | weathered_planks | the fight stage |
| `Water` | 1,690 | Water | |
| `FogDomain` | 12 | Mist | **do not import** |
| `Backdrop` | 2 | BackdropMat | **see section 6** |

---

## 2. Export from Blender

```
File > Export > glTF 2.0 (.glb)

Include   : Selected Objects (exclude FogDomain)
Transform : +Y Up            <- REQUIRED, Blender is Z-up and Unity is Y-up
Data      : Mesh, UVs, Normals, Materials
            Vertex Colors ON (harmless, and needed if you add wind later)
Compression: off for the first pass, so you can inspect it
```

Do not export `FogDomain`. It is a Cycles volume; in Unity it would appear as a 130 m box.

---

## 3. Baked textures — use these, not the .blend materials

Pre-baked in `textures\unity\`. Each one already contains the procedural colour work that
glTF cannot carry, so in Unity you assign a plain **URP/Lit** material and nothing more.

| file | for | contains |
|---|---|---|
| `bamboo_bark_albedo.png` | BambooWall | T_Bark multiplied by the culm green (0.096, 0.140, 0.047). The source bark is greyscale; without this the culms import white. |
| `bamboo_leaf_albedo.png` | BambooLeaves | leaf card tinted green, alpha preserved from the source PNG |
| `fern_albedo.png` | Merged_fern_02 | value lift 1.15 / saturation 1.20, plus a real alpha channel |
| `ground_albedo.png` | Terrain | T_Ground tinted and darkened, with the vegetation bake composited over it by alpha |
| `rock_albedo.png` | Merged_boulder_01 | boulder diffuse darkened ×0.52 (it read chalk-white otherwise) |

Normal and roughness maps come across with the glTF export unchanged — only the albedos
needed baking.

### Why these were baked rather than rebuilt

They are per-texel operations (multiply, hue/value, alpha-from-luminance), so they were
computed directly on the pixel data. That was deliberate: the ferns, leaves and rocks all
share one atlas UV space, so a conventional Blender bake would have needed unique
non-overlapping UVs that these meshes do not have.

---

## 4. Material settings in Unity

| material | Surface Type | Notes |
|---|---|---|
| `fern_02` | **Opaque** | See below. Do NOT set alpha clip. |
| `M_ArenaLeafCard` | Transparent → **Alpha Clip**, cutoff 0.35 | Must be clip, not blend |
| `M_ArenaBamboo` | Opaque | |
| `Bank` | Opaque | |
| `boulder_01` | Opaque | Needs a moss shader, section 5 |
| `weathered_planks` | Opaque | |
| `Water` | Transparent or a URP water shader, section 5 | |

### The fern is Opaque, and this matters

`fern_02` has its leaf shapes as **geometry**, not as an alpha cut-out — 259 triangles per
plant, modelled fronds. The baked alpha channel is 99.8% opaque, which confirms it.

Setting it Opaque removes the largest single source of overdraw in the scene: the ferns are
36% of all triangles, and alpha-tested surfaces break early-Z so the GPU cannot discard
occluded pixels behind them. Marking them Opaque is free performance with no visual cost.

### Alpha clip, never alpha blend, for the leaves

Alpha-blended cards do not write depth reliably. That breaks sorting between overlapping
leaf cards AND kills the dappled leaf shadows, since the shadow pass needs depth. Use clip.

---

## 5. What you must rebuild by hand

### 5.1 Fog — required

The Blender fog is a Cycles volume and does not export. Without it the grove has no depth and
the scene reads flat.

```
Window > Rendering > Lighting > Environment
  Fog: enabled
  Mode: Exponential
  Density: 0.0125
  Color: sample the horizon from renders\full.png (a warm pale cream)
```

Blender's measured falloff, to match against:

| distance | how much the fog has washed out |
|---|---|
| 10 m | 12% |
| 25 m | 27% |
| 45 m | 43% |

### 5.2 Lighting — required

Two lights, both directional.

| | Rotation (Unity, X/Y/Z) | Intensity | Colour | Notes |
|---|---|---|---|---|
| Key sun | X **40**, Y **52**, Z 0 | ~3.0 | warm, ~3200 K (1.0, 0.78, 0.52) | 40° elevation is what puts leaf shadows in frame |
| Fill | X 18, Y **-208**, Z 0 | ~0.4 | cool (0.82, 0.87, 1.0) | lifts camera-facing surfaces; without it the bridge is a silhouette |

Blender's key sits at 12 W with a 3.5° disc against a 0.62 sky. The ratio matters more than
the absolute numbers — roughly **20:1 key to ambient**. Softness (3.5°) is deliberate; a hard
sun blew out the upper left.

Set ambient/environment to a low warm value rather than pure black, or the shadowed grove
goes solid dark.

### 5.3 Rock moss — optional but recommended

The only material trick that could NOT be baked, because it is driven by **world normal**, not
by UV. All 41 rocks share one atlas, so baking would have given every rock identical moss
regardless of which way its faces point.

Shader Graph, about six nodes:

```
Normal Vector (World) -> Split -> take G (the up component)
  -> Remap (0.25 .. 0.85 to 0 .. 1)         # upward faces only
  -> Multiply by Simple Noise (scale ~5.5)  # break up the edge
  -> Lerp( rock_albedo , green 0.055/0.135/0.030 )
  -> Base Color
```

Skip it if you are short on time; the rocks are already darkened and will read as stone.

### 5.4 Water

Blender's water is a dark, low-roughness surface with a noise bump. Two options:

- **Simple:** URP/Lit, Smoothness ~0.95, base colour (0.004, 0.011, 0.008), a scrolling normal
  map for ripples.
- **Better:** the URP water shader or a planar-reflection setup. The reflection is what makes
  this shot work, and it is view-dependent so it cannot be baked.

---

## 6. The backdrop — read before you use it

`Backdrop` is a flat quad at **27.3 m** with a pre-rendered image, standing in for the grove
past the river bend.

**It is only correct from the camera's home position.** The camera is specified to pan 2–4 m
laterally. At 4 m of pan the angular error against the plate is `atan(4 / 27.3) ≈ 8.4°`, and
because a flat image has no internal parallax, the distant bamboo will visibly slide relative
to the near grove.

Options:

1. **Drop it and let the real grove show.** Costs about 49k triangles to restore the geometry
   past 24 m. Safest.
2. **Push it back to 45 m+.** Halves the parallax error. Needs re-rendering the plate.
3. **Curve it into a cylinder** centred on the rail. Correct for lateral pan specifically.
4. **Keep it flat** only if the camera ends up genuinely locked.

This was flagged in the design spec as a hard dependency: the rail range must be fixed before
plates are baked, because the bake depends on it.

---

## 7. Camera

Match exactly, or the composition and the backdrop both break.

```
position   (-0.68, 2.60, -3.219)     # Blender (x, z, -y) -> Unity (x, y, z)
rotation    X 5.0, Y 0.5, Z 0        # 5 deg downward pitch
Field of View  45.7                  # Unity's FOV is VERTICAL
                                     # (Blender: 24 mm, 36 mm sensor, horizontal fit)
Physical camera: 24 mm focal, 36 mm sensor width, if you prefer to set it that way
resolution 1920x1080
near clip  0.05      far clip  500
```

Depth of field in the reference is f/2.8 focused at **6.18 m** (the bridge). In Unity use the
URP Depth of Field post-process override, Bokeh mode, focus distance 6.18, aperture 2.8.

### The framing rule this camera encodes

The bridge deck sits along the bottom edge and everything nearer is hidden. That is geometric,
not luck: the frame's bottom edge sits 27.9° below horizontal, the bridge's near edge is at
33.6° (below the frame, hidden) and its far edge at 20.1° (in frame). If you move the camera
vertically, recheck this or the strip in front of the deck will become visible — and that strip
was deleted.

---

## 8. Performance notes

Measured in Blender before export:

| metric | value | verdict |
|---|---:|---|
| Triangles | 185,253 | fine for mid-range and up |
| Draw calls | 9 | excellent (was 332 before merging) |
| Materials | 9 | excellent |
| Alpha-tested tris | 160,196 (82%) | **the real bottleneck** |

**Triangle count is not this scene's problem.** On a tile-based mobile GPU the cost is
overdraw: alpha-tested surfaces break early-Z, so every layer of foliage between the camera
and the background gets shaded.

Biggest remaining win, in order:

1. Set `fern_02` to **Opaque** (section 4). Removes 36% of triangles from the alpha path.
2. Keep leaf cards on **clip**, never blend.
3. If frames are still short, reduce leaf-card layers by depth rather than polygon count.

### What was already done, so you do not redo it

- 332 objects merged to 9 by material
- Rocks on distance LODs (1200 / 600 / 260 faces), buried geometry removed — 43,800 → 12,657
- Bridge geometry below water and inside the bank removed — 10,300 → 5,813 (mostly buried
  foundation from the source arena, not piles)
- Water buried under the banks removed — 2,970 → 845
- Everything outside the camera frustum removed, keeping shadow casters and anything reflected
  in the water

---

## 9. Known issues carried over

- **Backdrop parallax** — section 6, unresolved by design pending the final rail range.
- **No light shafts.** Five attempts, none worked: the camera looks down an open corridor so
  the mist is lit uniformly with nothing to stripe it, and the bridge is only 3.4–6.2 m away,
  too little depth for in-scattering to build a visible beam. Do them in Unity as a
  post-process if wanted.
- **Water reflection is flat** compared with earlier renders, from an over-aggressive darkening
  pass. A proper URP water shader fixes this.
- **Blendkit materials never applied** — the two grass materials you supplied were never
  fetched; the download endpoint returns 403 without an API key.
- **Textures live in the .blend.** 28 of 33 are packed with dead filepaths pointing at Poly
  Haven temp folders. They extract correctly on glTF export, but there is no on-disk copy to
  fall back to. The five baked albedos in `textures\unity\` are real files.

---

## 10. Rebuilding from scratch

The scene is procedural. `build\common.py` defines the river; every other script regenerates
from it. If the geometry needs changing, edit `common.py` and re-run in this order:

```
rebuild_terrain.py     terrain + water from the river definition
uv_planar.py           planar UVs (required before any texture bake)
culms_v3.py            bamboo culms and canopy
rebuild_rail_aware.py  props, culled against the whole camera rail
merge_by_material.py   collapse to 9 draw calls
bake_albedo.py         regenerate the Unity textures
```

All scripts are driven through `build\bmcp.ps1`, which talks to a running Blender over the
BlenderMCP socket on port 9876.
