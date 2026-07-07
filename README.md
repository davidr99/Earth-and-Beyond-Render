# Earth & Beyond W3D Render

A Unity project for loading and viewing **Westwood 3D (`.w3d`)** models from
*Earth & Beyond* (Westwood Studios / EA, 2002). The importer parses the binary
W3D format directly at runtime and rebuilds the models as Unity GameObjects
with materials, hierarchies, and animations — aiming to match what the
original DirectX 8 game client renders.

The chunk layouts are based on a reverse-engineered format specification
derived from decompiles of the E&B game client and Westwood's official
W3D viewer, so parsing follows what the game actually accepts rather than
third-party documentation.

## Features

**Geometry & scene structure**
- Meshes (`0x000`): vertices, normals, UVs, u32-index triangles, per-face
  material binding, AABTrees, skinning via vertex influences
- Hierarchies (`0x100`) with legacy (`Version < 3.0`) synthetic-root handling
- HLOD (`0x700`), LOD models (`0x400`), aggregates (`0x600`),
  collision boxes (`0x740`), ShdMesh (`0xB00`), dazzle capture (`0x900`)
- Billboard geometry types (camera-aligned / camera-oriented)

**Materials**
- Modern material sets (`0x28`/`0x29`/`0x2A`/`0x30`/`0x38`) and legacy
  MATERIALS3 (`0x15`) with per-triangle bindings
- PRELIT container selection from mesh-header attribute bits
- Blend classification from the 16-byte W3D shader struct (opaque, alpha,
  additive, screen, multiplicative, alpha-test), matching the client
- Emissive-driven surfaces (black diffuse/ambient) render unlit ×
  emissive, like the DX8 fixed-function gradient
- UV mappers: linear offset / grid / rotate / zigzag scroll animation from
  vertex-material mapper args
- E&B `[Effect_Pass_Description]` / `[Surface_Description]` INI blocks from
  mesh user text (UV scrolling, detail maps, two-sided/hidden flags)

**Animation**
- Uncompressed animation (`0x200`): translation + quaternion channels,
  packed-bit visibility channels
- Compressed animation (`0x280`): TimeCoded (interleaved packets with
  bit-31 hold flags) and AdaptiveDelta (full nibble-delta decode with the
  client's 256-entry filter table)
- Automatic discovery of external animations that target the loaded
  hierarchy from sibling `.w3d` files

**Emitters**
- Particle emitters (`0x500`) including INFOV2, keyframed properties, and
  rotation/frame/blur keyframe chunks, mapped to Unity particle systems

## Requirements

- **Unity 6000.4.x** (developed on 6000.4.2f1) with **HDRP**
- Earth & Beyond game assets (`.w3d` models and `.tga` textures) extracted
  from the game's MIX files — **not included in this repository**

## Usage

1. Open the project in Unity and enter Play mode in a scene containing the
   `W3DViewerUI` component (see `OutdoorsScene.unity`).
2. Enter or browse to a `.w3d` file path and press **Load**. Textures are
   resolved from the model's directory.
3. Camera controls:
   - **Orbit** mode: drag to rotate, wheel to zoom, **Frame** (or `F`) to
     re-center on the model
   - **Fly** mode (`F2` to toggle): `WASD` + `Q`/`E` for up/down, `Shift`
     for speed boost, `Esc` to release the cursor
4. Lighting: the viewer drives the W3D preview shaders with an adjustable
   directional light + ambient, or can follow the strongest Unity scene
   light ("Use Scene Light For W3D Shading").

The viewer isolates its camera from scene post-processing (volumes, fog),
and the project's default HDRP volume profile uses fixed exposure with
tonemapping and bloom disabled so output matches the original game's LDR
look — emissive glow strips and additive beams render at their authored
brightness instead of blooming.

## Project layout

```
Assets/W3DImporter/
  Runtime/
    IO/         chunk reader / binary reader / chunk type registry
    Parsers/    file, mesh, hierarchy, animation, emitter, HLOD, aggregate
    Structs/    parsed data model (POCOs, no Unity scene objects)
    Builders/   scene/mesh/material/animation/emitter -> Unity objects
    W3DViewerUI.cs   runtime viewer (load, orbit/fly camera, lighting)
  Shaders/      W3D/Opaque, Transparent, Additive, Particle (+ vertex color)
  Editor/       ScriptedImporter for .w3d assets
```

## Known gaps

- Morph animations (`0x2C0`, HMorphAnim) are recognized but not yet played
- HMODEL (`0x300`), COLLECTION (`0x420`), and SOUNDROBJ (`0xA00`) are
  skipped
- AdaptiveDelta decoding assumes well-formed files; truncated channels are
  cut short rather than rejected

## Legal

This is a fan preservation/rendering project. *Earth & Beyond* and the W3D
format are property of Electronic Arts. No game assets are distributed with
this repository.
