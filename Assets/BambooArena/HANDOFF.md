# Bamboo Arena — scene handoff

Fight-stage backdrop for Mikey: a bamboo grove with a boardwalk over a green channel.
Unity 6000.5.7f1, URP 17.5, Linear colour space.

## What to open

Unzip `BambooArena_scene.zip` over a fresh URP project, then open `Assets/Scenes/SampleScene.unity`.

The `.meta` files travel with every asset, so all GUIDs are preserved and the scene wires itself up
with no manual reassignment. `ProjectSettings/GraphicsSettings.asset` and `QualitySettings.asset`
are included because they bind the pipeline assets and the quality tiers; drop them in only if the
target project has no pipeline setup of its own.

## Scene cost, measured

| | value |
|---|---|
| triangles in scene | 127,166 |
| triangles submitted per frame | ~748,000 |
| vertices | 189,456 |
| textures | 45.5 MB |
| draw calls / SetPass | 82 / 50 |
| renderers / materials | 13 / 11 |
| overdraw | about 5 layers per pixel |

The per-frame triangle figure is 5.2x the scene because everything is drawn for the camera, twice
more for the two shadow cascades, and once more for the water reflection.

## The camera

Position `(-0.68, 2.60, 3.219)`, rotation `(5.0, 180.5, 0)`, FOV 45.75. It is meant to travel
laterally along the bridge. Every visibility cut in this scene was computed across 13 camera
positions spanning x -6 to +6 plus the mirrored water-reflection view, so panning inside that range
will not open holes in the foliage. **Going wider than that range can.** If the shot needs a longer
travel, re-run the visibility pass rather than trusting what is here.

## Things that will bite you

**The water shader is load-bearing for depth priming.** `RiverWater.shader` is unlit and now carries
`DepthOnly` and `DepthNormals` passes. Without them the river disappears completely: depth priming
runs the forward pass at `ZTest Equal`, the water contributes nothing to the prepass, every pixel
fails the test, and the terrain shows through. If you rewrite that shader, keep those two passes.

**Planar reflection renders the whole scene a second time.** `WaterReflection` carries
`PlanarReflection.cs`, which mirrors the camera about y=0 and uses an oblique near clip plane so
nothing below the waterline is drawn. Resolution scale is 0.385. This is the single largest cost in
the frame and the first thing to cut if a device struggles.

**The arena root is an instance of `BambooArena.glb`.** Mesh assignments are stored as prefab
overrides, not on the objects, which is why the scene file looks half empty when read as text.

**Foliage is alpha-tested and double-sided.** That is 42% of the geometry and it is the reason
overdraw is the binding cost rather than triangle count. Anything that adds more leaf cards should
be checked against the overdraw figure, not the triangle counter.

## Pipeline settings, and one that is deliberately unset

Two quality tiers exist. `GraphicsSettings` currently binds `PC_RPAsset`; the Android build will
use `Mobile_RPAsset` via the "Mobile" quality level.

| | PC | Mobile |
|---|---|---|
| shadow map | 2048 | 1024 |
| cascades | 2 | 2 |
| shadow distance | 45 m | 50 m |
| HDR | off | off |
| render scale | 1.0 | 0.8 |
| soft shadows | on | off |
| depth priming | Forced | **off** |

Depth priming is on for PC because it was measured there: with the water shader's depth passes in
place the frame is identical to 0.04/255 while each pixel is shaded roughly once instead of six
times. It is **off on Mobile** because that tier uses a different rendering path (Forward, not
Forward+) and I never measured it there. Turn it on, compare a frame, and keep it only if the image
is unchanged. Do not assume the PC result transfers.

## What was already optimised

Starting point to final, all measured:

- triangles 288,526 to 127,166
- vertices 382,805 to 189,456 (193,349 of those were dead: vertices no triangle referenced, left
  behind by edits that rewrote index buffers without rebuilding vertex buffers)
- textures 109.7 MB to 45.5 MB, the biggest single item being a 2048 deck map that shipped
  uncompressed inside the .glb at 37.3 MB
- shadow map 4096 to 2048, 32 MB to 8 MB
- foliage invisible from every camera position removed, proved by an ID-render pass rather than a
  frustum test, because 65% of leaf cards were inside the frustum but hidden behind the front layer
- geometry below the ground surface removed; frame difference 0.143/255

## Known open issues

1. **The river is shaded by the culms, not the leaves.** Measured: turning off culm shadows raises
   the river from 40.5 to 56.7, leaves raise it by 0.2. Any attempt to get more light onto the water
   has to thin culms in the sun corridor. Leaf cuts do nothing.
2. **Depth priming untested on the Mobile path.** See above.
3. **Reflection cost unquantified.** Editor-side timing produced a physically impossible result
   (disabling a whole pass appeared to make the frame slower), so it was discarded. Needs a device
   profile.
4. **91 mesh assets exist in the source project, 13 are used.** The rest are intermediate states
   from iteration. They are not in this package.

## Not included, on purpose

- `Assets/Screenshots` — 251 MB of debug renders
- `Assets/Editor` — 15 one-shot tool scripts that auto-run on load and would rewrite the scene the
  first time the project opens
- 78 unused mesh assets
