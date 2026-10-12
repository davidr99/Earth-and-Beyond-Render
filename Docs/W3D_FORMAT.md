# W3D File Format — Earth & Beyond Client (Consolidated Spec, v2)

**This is the canonical W3D format reference for building a C# viewer.**

> Note: evidence citations like `client.exe.c:1292623` refer to line numbers in
> Ghidra/IDA decompile outputs of the E&B client and Westwood's W3D viewer.
> Those source files are not included in this repository; the citations are
> kept so claims can be re-verified against a fresh decompile.

Reverse-engineered from three independent sources, cross-checked against each other:

1. `client.exe.c` — Ghidra decompile of the EnB client (primary; what the game actually accepts)
2. `client.exe.asm` — IDA disassembly / raw bytes (used where Ghidra dropped functions; several chunk readers exist *only* here)
3. `W3dview.exe.c` — Ghidra decompile of Westwood's official W3D viewer (authoritative for standard-format tiebreaks)

> **v2 (2026-07-04) supersedes the earlier version of this file and corrects major errors.**
> See [§16 Corrections](#16-corrections-vs-earlier-docs) before trusting any older doc
> (`ENB_W3D_FORMAT_EXTENSIONS.md`, `W3D_ENB_DELTA_FROM_OPENSAGE.md`, `W3D_UNITY_VIEWER_FORMAT.md`).
> Headline corrections:
> - Chunk **0x800 is NOT a W3D chunk** (it is the `splines.wsd` FX-curve database loader).
> - Chunk **0x15 is NOT vertex skinning** — it is the legacy Westwood **MATERIALS3** container.
> - Chunks **0x23–0x26 are NOT vertex color channels** — they are the standard **PRELIT** material containers.
> - **0x2C0 is NOT EnB-specific** — it is Westwood **morph animation** (HMorphAnim).
> - The claimed chunks **0x54/0x55/0x58/0x59 do not exist** (that code is a network-message factory).
> - All animation headers (0x201/0x281/0x2C1) previously had fields shifted by 4 bytes due to a
>   systematic Ghidra stack artifact — they are **standard 44/48-byte W3D layouts**.
> - Triangles (0x20) on disk are the **standard 32-byte W3dTriStruct with u32 indices** (client truncates
>   to u16 in memory). Pivots (0x102) are the **standard 60-byte layout** including (ignored) Euler angles.
>
> Big-picture: EnB is far closer to stock WW3D than previously believed. Most "EnB-specific" chunks
> were actually legacy W3D 1.x chunks (0x05, 0x0D, 0x15-family) that OpenSage-era docs no longer cover.

All integers/floats are **little-endian**. `u8/u16/u32/i32/f32` as usual; `vec3` = 3×f32; `quat` = 4×f32 in **x,y,z,w** order.

---

## 1. Container Rules

No file-level header; a `.w3d` file is a sequence of top-level chunks.

```
Chunk header (8 bytes):
  u32 ChunkType
  u32 ChunkSize        // payload bytes, EXCLUDING this header
                       // bit 31 (0x80000000) = "contains sub-chunks" hint
payload = ChunkSize & 0x7FFFFFFF bytes
```

Parser rules the client uses (do the same in C#):
- Skip unknown chunks by size; never fatal.
- Nested parsers enumerate children until the parent's payload end; always seek to chunk end after each child.
- Micro-chunks (u8 id + u8 size) exist in the chunk-IO class but are **not used by any mesh/animation code path** — you do not need them for a viewer.

Evidence: reader primitives `FUN_009d4310/43c0/43d0/4370/4540` (client.exe.c:1405177–1405319).

---

## 2. Top-Level Chunk Registry (definitive)

Handled **inline** by `WW3DAssetManager::Load_3D_Assets` (`FUN_0091d270`, client.exe.c:1300497):

| ID | Name | Path |
|---|---|---|
| 0x100 | HIERARCHY | HTree manager (`FUN_00964130`) |
| 0x200 | ANIMATION | anim manager (`FUN_00964930`) |
| 0x280 | COMPRESSED_ANIMATION | anim manager |
| 0x2C0 | **MORPH_ANIMATION** (HMorphAnim) | anim manager |

Handled by **registered prototype loaders** (registration: `FUN_0091c2c0` client.exe.c:1300222 + game-init at c:477966; Chunk_Type constants read from each loader's vtable slot 0 in the binary):

| ID | Name | Status | Notes |
|---|---|---|---|
| 0x000 | MESH | full spec §3 | |
| 0x300 | HMODEL | supported, layout not yet extracted | legacy hierarchical model |
| 0x400 | LODMODEL | supported, §11 | **real LOD-distance model** (0x401/0x402) |
| 0x420 | COLLECTION | supported, layout not yet extracted | |
| 0x500 | EMITTER | full spec §14 | |
| 0x600 | AGGREGATE | supported, §13 | |
| 0x700 | HLOD | full spec §12 | |
| 0x740 | BOX | supported, layout not yet extracted | standard W3D box assumed |
| 0x741 | (EnB) | supported, layout not yet extracted | parse fn 0x90DFE0 |
| 0x742 | (EnB) | supported, layout not yet extracted | parse fn 0x912F50 |
| 0x750 | NULL object | supported | renders nothing |
| 0x900 | DAZZLE | supported, layout not yet extracted | |
| 0xB00 | **SHDMESH** | §15 | shader-mesh (Generals-era); previously misdocumented as "0xB01 LOD model" |

**Not supported:** SOUNDROBJ (0xA00) — no loader registered. **0x800 is not a W3D chunk at all** (see §15).

---

## 3. Mesh (0x00)

Mesh sub-chunk dispatcher: `FUN_009bad80` (client.exe.c:1387182). Complete case list — anything not here is skipped:

| ID | Name | Layout |
|---|---|---|
| 0x02 | VERTICES | NumVertices × vec3 |
| 0x03 | VERTEX_NORMALS | NumVertices × vec3 |
| 0x04 | (normals alias) | identical parser to 0x03 |
| 0x05 | TEXCOORDS (legacy) | NumVertices × {f32 u, f32 v}; client stores **v' = 1 − v** |
| 0x0C | MESH_USER_TEXT | raw text blob; stored unparsed at load time (see §8) |
| 0x0D | VERTEX_COLORS (legacy) | NumVertices × {u8 r,g,b,pad}; packed ARGB, **alpha forced 0xFF** |
| 0x0E | VERTEX_INFLUENCES | NumVertices × 8 bytes: `{u16 BoneIdx; u8 pad[6]}` — only bone index used; sets skin flag |
| 0x15 | **MATERIALS3 (legacy)** | container, §5 |
| 0x1F | MESH_HEADER3 | 116 bytes, below (consumed before dispatcher) |
| 0x20 | TRIANGLES | NumTriangles × 32 bytes, below |
| 0x21 | PER_TRIANGLE_MATERIAL_IDS | NumTriangles × u16 → indexes MATERIALS3 entries, §5.2 |
| 0x22 | VERTEX_SHADE_INDICES | NumVertices × u32 |
| 0x23–0x26 | **PRELIT containers** | §7 |
| 0x28 | MATERIAL_INFO | 16 bytes: u32 PassCount, VertexMaterialCount, ShaderCount, TextureCount |
| 0x29 | SHADERS | ShaderCount × 16-byte shader struct, §4 |
| 0x2A | VERTEX_MATERIALS | container of 0x2B wrappers (wrapper id not even checked), §6 |
| 0x30 | TEXTURES | container of 0x31, §3.4 |
| 0x38 | MATERIAL_PASS | container, §3.3 |
| 0x90 | AABTREE | container, §3.5 |

Absent/skipped by the client: 0x3D, 0x3E (SCG), 0x3F, 0x40–0x47, 0x50–0x53 (shader materials), 0x58 (deform), 0x60/0x61 (tangents), 0x80 (PS2), 0xC00+.

### 3.1 Mesh Header (0x1F) — 116 bytes (standard W3dMeshHeader3Struct)

```
+0x00  u32   Version
+0x04  u32   Attributes
+0x08  char  MeshName[16]
+0x18  char  ContainerName[16]
+0x28  u32   NumTriangles
+0x2C  u32   NumVertices
+0x30  u32   NumMaterials
+0x34  u32   NumDamageStages
+0x38  i32   SortLevel
+0x3C  u32   PrelitVersion
+0x40  u32   FutureCounts
+0x44  u32   VertexChannels
+0x48  u32   FaceChannels
+0x4C  vec3  BoundingBoxMin
+0x58  vec3  BoundingBoxMax
+0x64  vec3  BoundingSphereCenter
+0x70  f32   BoundingSphereRadius
```

**Attributes bits** (as tested by W3dview loader, W3dview.exe.c:107098–107156; client tests are in an
unrecovered function but runtime behavior matches):

| Mask | Meaning |
|---|---|
| 0x00FF0000 | **Geometry type ENUM** (only when Version > 0x40000): 0x00=normal, 0x01=camera-aligned, 0x02=skin, 0x04=AA-box, 0x05=oriented box, 0x06=camera-oriented (tree billboard: Y-axis-only rotation) |
| 0x00001000 | hidden |
| 0x00002000 | two-sided |
| 0x00004000 | obsolete lightmapped |
| 0x00008000 | cast shadow |
| 0x00000FF0 | collision bits (not tested by viewer loader) |
| 0x0F000000 | **PRELIT mask** — selects which 0x23–0x26 container is loaded: 0x02000000→0x24 PRELIT_VERTEX, 0x04000000→0x25 LM_MULTIPASS, 0x08000000→0x26 MULTITEXTURE, else 0x23 PRELIT_UNLIT |

### 3.2 Triangles (0x20) — 32 bytes each, STANDARD layout

```
+0x00  u32   VertexIndex0     // client truncates to u16 in memory — on disk it's u32
+0x04  u32   VertexIndex1
+0x08  u32   VertexIndex2
+0x0C  u32   Attributes       // only low byte (surface type) kept
+0x10  vec3  PlaneNormal
+0x1C  f32   PlaneDist        // client stores NEGATED
```

Evidence: client `FUN_0092d130` (c:1308778–1308825); identical in W3dview (89210–89226).
⚠️ The Unity importer's "EnB triangle layout" (u16×3 at +0) **contradicts the client parser** — verify
against a real file and fix the importer if needed.

### 3.3 Material Pass (0x38) — `FUN_009bbd10` (c:1387866)

Only these sub-chunks are handled (0x3D–0x3F hit the default = skipped):

| ID | Name | Element | Count rule |
|---|---|---|---|
| 0x39 | VERTEX_MATERIAL_IDS | u32 | size==4 → one id for whole pass; else **per-vertex** |
| 0x3A | SHADER_IDS | u32 | size==4 → one shader for pass; else **per-face** |
| 0x3B | DCG (diffuse color) | u32 RGBA (r,g,b,a file order → packed ARGB) | per-vertex. 2nd occurrence in a PRELIT_VERTEX (0x24) container replaces **alpha only** |
| 0x3C | DIG (diffuse illumination) | u32 (rgb used, alpha forced 255) | per-vertex. 2nd occurrence **multiplies** into existing color |
| 0x48 | TEXTURE_STAGE | container | max 2 stages/pass |

Translucency heuristic: on pass 0, if shader DestBlend ≠ zero and no alpha-test and SortLevel==0, the mesh
gets its sort/translucent flag (c:1387989–1388008).

**Texture stage (0x48)** sub-chunks:

| ID | Name | Layout |
|---|---|---|
| 0x49 | TEXTURE_IDS | u32[]; size==4 → single texture for stage; else per-face; id 0xFFFFFFFF = none |
| 0x4A | STAGE_TEXCOORDS | (size/8) × {f32 u, f32 v}; **V flipped: v' = 1 − v**; CRC-deduped into ≤8 shared UV arrays |
| 0x05 | (alias) | accepted inside 0x48 as STAGE_TEXCOORDS |
| 0x4B | PER_FACE_TEXCOORD_IDS | size must equal NumTriangles×12, then **skipped — never used** |

### 3.4 Textures (0x30 → 0x31 → 0x32/0x33) — `FUN_0094ccc0` (c:1324726)

- 0x31 TEXTURE wrapper (id checked), containing:
- 0x32 TEXTURE_NAME: null-terminated string (≤255). Names are `.tga`; loaded verbatim via the texture manager.
- 0x33 TEXTURE_INFO, 12 bytes: `{u16 Attributes; u16 AnimType; u32 FrameCount; f32 FrameRate}`

Attributes bits used by the client: 0x0004 NO_LOD (1 mip), 0x00C0 mip count (0x40→2, 0x80→3, 0xC0→4),
0x1000 bump-map. **AnimType/FrameCount/FrameRate are read and completely ignored by this client**
(c:1324804–1324810) — texture animation in EnB comes from mapper args (§6) instead.

### 3.5 AABTree (0x90 → 0x91/0x92/0x93)

- 0x91 header, 32 bytes: `{u32 NodeCount; u32 PolyCount; u8 pad[24]}`
- 0x92 poly indices: PolyCount × u32 (triangle indices)
- 0x93 nodes: NodeCount × 32 bytes:

```
+0x00 vec3 Min
+0x0C vec3 Max
+0x18 u32  FrontOrPoly0      // LEAF if bit 0x80000000 set: low bits = first poly index
+0x1C u32  BackOrPolyCount   // leaf: poly count; interior: back child node index
```

Leaf: `FrontOrPoly0 = polyStart | 0x80000000`, `BackOrPolyCount = polyCount`.
Interior: `FrontOrPoly0`/`BackOrPolyCount` = front/back child node indices.
(From the W3dview tree builder, which writes the identical node format: 112036–112044.)
Note: skinned meshes (flag from 0x0E) **discard** the AABTree at load (c:1388545).

---

## 4. Shaders (0x29) — 16-byte W3dShaderStruct and runtime meaning

On-disk (one per ShaderCount):

```
+0  u8 DepthCompare      (D3DCMP-1 style: 0=never..7=always; default 3 = lessequal)
+1  u8 DepthMask         (0/1)
+2  u8 ColorMask         (obsolete)
+3  u8 DestBlend         0=zero 1=one 2=srcColor 3=invSrcColor 4=srcAlpha 5=invSrcAlpha 6=srcColorPrefog
+4  u8 FogFunc           (obsolete — recomputed at load from blend combo)
+5  u8 PriGradient       0=disable 1=modulate 2=add 3/4=bumpenv 5=modulate2x
+6  u8 SecGradient       (0/1)
+7  u8 SrcBlend          0=zero 1=one 2=srcAlpha 3=invSrcAlpha
+8  u8 Texturing         (0/1)
+9  u8 DetailColorFunc   0=disable 1=detail 2=scale 3=invScale 4=add 5=sub 6=subR 7=blend 8=detailBlend
+10 u8 DetailAlphaFunc   0=disable 1=detail 2=scale 3=invScale
+11 u8 ShaderPreset      (obsolete)
+12 u8 AlphaTest         (0/1)
+13 u8 PostDetailColorFunc   ← IGNORED by EnB client
+14 u8 PostDetailAlphaFunc   ← IGNORED by EnB client
+15 u8 pad
```

The client packs this into a 32-bit word (`FUN_009957e0`, c:1368292):

| Bits | Field |
|---|---|
| 0–2 | DepthCompare |
| 3 | DepthMask |
| 4 | (forced 1; runtime alpha-blend-enable test) |
| 5–7 | DestBlend |
| 8–9 | fog mode (derived post-load) |
| 10–12 | PriGradient |
| 13 | SecGradient |
| 14–15 | SrcBlend |
| 16 | Texturing |
| 18 | AlphaTest |
| 20–23 | DetailColorFunc |
| 24–26 | DetailAlphaFunc |

D3D application (`FUN_0093b510`, c:1314165): SRCBLEND/DESTBLEND via lookup tables; AlphaTest →
ALPHAREF 0x60 / GREATEREQUAL; PriGradient → stage-0 COLOROP (SELECTARG/MODULATE/ADD/BUMPENV/MODULATE2X);
DetailColorFunc → stage-1 COLOROP (MODULATE/MODULATE2X/ADD/ADDSIGNED/ADDSIGNED2X/SUBTRACT/ADDSMOOTH/
BLENDTEXTUREALPHA/BLENDCURRENTALPHA); DetailAlphaFunc → stage-1 ALPHAOP.

Useful blend classification for a viewer (0-based WW3D values):
Src=1,Dst=0 → opaque; Dst=1 → additive; Src=1,Dst=3 → screen; Src=srcAlpha,Dst=invSrcAlpha → alpha;
Dst=srcColor/invSrcColor → multiplicative; AlphaTest on opaque → alpha-clip.

---

## 5. Legacy MATERIALS3 (0x15) — how most EnB ship/station materials work

`FUN_009bb260` (c:1387354). Repeated **NumMaterials** (header +0x30) times:

```
0x16  MATERIAL3 (wrapper, id checked)
  0x17  MATERIAL3_NAME      null-terminated string
  0x18  MATERIAL3_INFO      exactly 44 (0x2C) bytes:
        +0x00 u32 Attributes        // bit0 = alpha-blend (→ srcAlpha/invSrcAlpha, depth-mask off)
        +0x04 u8[3]+pad DiffuseColor
        +0x08 u8[3]+pad SpecularColor
        +0x0C u8[3]+pad EmissiveColor
        +0x10 u8[3]+pad AmbientColor
        +0x14 u8[3]+pad DiffuseCoeffs   // final diffuse  = Diffuse  × these (per channel /255)
        +0x18 u8[3]+pad SpecularCoeffs  // final specular = Specular × these
        +0x1C f32 Shininess
        +0x20 f32 Opacity
        +0x24 f32 Translucency
        +0x28 f32 FogCoeff
  [optional] 0x19 MATERIAL3_DC_MAP    (diffuse-color texture)
        0x1A MAP3_FILENAME   string (< 0x200)
        0x1B MAP3_INFO       8 bytes = 2 × u32   (mapping type / frame info)
  [optional] 0x1E MATERIAL3_SI_MAP    (self-illumination map)
        // honored ONLY if DiffuseColor == (0,0,0); replaces the DC texture and forces the shader to
        // SrcBlend=ONE, DestBlend=ONE, gradient off, texturing on  → ADDITIVE glow pass
        0x1A + 0x1B  (same inner layout)
```

Each parsed material produces a (vertex-material, packed-shader, texture) triple appended to file-local
arrays, plus a 16-byte remap record.

### 5.2 Per-triangle material binding (0x21)

- If NumMaterials == 1: chunk skipped; single material applied to the whole mesh.
- Else: per triangle one **u16** index into the MATERIALS3 remap list. The client then assigns per-face
  texture, per-face shader, and per-corner vertex material. **Purely file-local — no global registry.**

For a C# viewer: group triangles by material index → one submesh per unique index.

---

## 6. Vertex Materials (0x2A → 0x2B → 0x2C/0x2D/0x2E/0x2F)

`FUN_0092f2a0` (c:1310385). Per material:

- 0x2C VERTEX_MATERIAL_NAME: string
- 0x2D VERTEX_MATERIAL_INFO — **exactly 32 bytes**:

```
+0x00 u32 Attributes
+0x04 u8[4] Ambient   (r,g,b,pad — bytes, NOT floats)
+0x08 u8[4] Diffuse
+0x0C u8[4] Specular
+0x10 u8[4] Emissive
+0x14 f32 Shininess
+0x18 f32 Opacity
+0x1C f32 Translucency   (read, unused)
```

- 0x2E MAPPER_ARGS0 / 0x2F MAPPER_ARGS1: raw text, parsed as INI `key=value` lines (wrapped in a
  synthetic `[Args]` section). Feed the mapper constructors.

**Attributes bits:**

| Mask | Meaning |
|---|---|
| 0x00000001 | USE_DEPTH_CUE |
| 0x00000004 | COPY_SPECULAR_TO_DIFFUSE |
| 0x00FF0000 | **Stage-0 mapper** (value = field >> 16) |
| 0x0000FF00 | **Stage-1 mapper** (value = field >> 8) — ⚠️ NOT 0xFF000000 |

Mapper enum (both stages, from W3dview 66185–66744; EnB client dispatch mirrors it):

| # | Mapper | # | Mapper |
|---|---|---|---|
| 0 | UV (default) | 10 | Step linear offset |
| 1 | Environment | 11 | ZigZag linear offset |
| 2 | Cheap environment | 12 | WS classic environment |
| 3 | Screen | 13 | WS environment |
| 4 | Linear offset | 14 | Grid classic environment |
| 5 | Silhouette (unhandled) | 15 | Grid environment |
| 6 | Scale | 16 | Random |
| 7 | Grid | 17 | Edge |
| 8 | Rotate | 18 | BumpEnv |
| 9 | Sine linear offset | | |

---

## 7. PRELIT Containers (0x23–0x26)

Each of the four IDs holds a **complete alternate material set** with the *same* grammar as the top level:
0x28 MATERIAL_INFO, 0x29 SHADERS, 0x2A VERTEX_MATERIALS, 0x30 TEXTURES, 0x38 MATERIAL_PASS.

- 0x23 PRELIT_UNLIT · 0x24 PRELIT_VERTEX · 0x25 PRELIT_LIGHTMAP_MULTI_PASS · 0x26 PRELIT_MULTI_TEXTURE
- The client accepts **exactly one**, chosen from the mesh-header PRELIT attribute bits (0x0F000000 mask);
  default (no prelit bits) = parse none of them (base materials outside the containers are used).
- Special rule: inside 0x24, a second DCG chunk replaces only the alpha channel of the existing colors.

For a viewer: pick the same container the header selects, or simply prefer the base (non-prelit) set +
0x23 if present.

---

## 8. Mesh User Text (0x0C) and the Effect-Pass INI system

The chunk is stored **raw** at load time (no parsing, c:1308858). The FX system later parses INI-style
sections from such text:

`[Effect_Pass_Description]` keys: `EFFECT_PASS_TYPE` (int; 2 and 3 build an extra textured render pass),
`TEXTURE` (string), `SHADER` (int 1..6 preset), `TWO_SIDED`, `HIDDEN` (bools), `CYCLE_TIME`,
`START_V_COORD`, `END_V_COORD` (floats; V-scroll animation for other pass types).

`[Surface_Description]` / lightmap keys: `COLOR_MAP`, `DETAIL_MAP`, `XDETAIL_SCALE`, `YDETAIL_SCALE`,
`FLIP_TGA`, `Patch_Grid_Width` (default 16), `LIGHTMAP_CHUNK` (default 8).

These create **additional passes layered on top of** the W3D materials — they do not replace them.
(Direct call chain from stored 0x0C text to the parser is via function pointer; treat as high-confidence.)

---

## 9. Hierarchy (0x100)

### 0x101 HIERARCHY_HEADER — 36 bytes

```
+0x00 u32  Version         // if < 0x30000: NumPivots += 1 and a synthetic "RootTransform"
                           // pivot 0 is prepended; all parent indices shift +1
+0x04 char Name[16]
+0x14 u32  NumPivots
+0x18 vec3 Center          // read, unused
```

### 0x102 PIVOTS — 60 bytes per record (STANDARD layout)

```
+0x00 char Name[16]
+0x10 i32  ParentIdx       // -1 = root
+0x14 vec3 Translation
+0x20 vec3 EulerAngles     // read and IGNORED by both EnB client and W3dview
+0x2C quat Rotation        // x, y, z, w  (W LAST)
```

Local transform = Translate(T) · QuatToMatrix(q) (row-major 3×4 in the engine).
Evidence: client `FUN_00989060` (c:1361501–1361652), quat→matrix `FUN_008d2c10` (c:1248834);
identical in W3dview (41289–41353).

### 0x103 PIVOT_FIXUPS — skipped entirely by the client.

---

## 10. Animation

> All three headers below were previously documented with a 4-byte field shift (Ghidra ESP artifact
> after an unmodeled vtable call). The layouts here are writer-roundtrip-proven and match stock W3D.

### 10.1 Uncompressed (0x200)

**0x201 ANIMATION_HEADER — 44 bytes**

```
+0x00 u32  Version          // unread
+0x04 char Name[16]         // animation name
+0x14 char HierarchyName[16]// used for HTree lookup; full key = "HIER.NAME"
+0x24 u32  NumFrames
+0x28 u32  FrameRate
```

**0x202 ANIMATION_CHANNEL** — 12-byte header + data:

```
+0x00 u16 FirstFrame
+0x02 u16 LastFrame
+0x04 u16 VectorLen         // 1 = scalar, 4 = quaternion
+0x06 u16 ChannelType       // see 10.4
+0x08 u16 Pivot
+0x0A u16 pad
+0x0C f32 Data[(LastFrame-FirstFrame+1) * VectorLen]   // frame-major: data[frame*VectorLen + comp]
```

Frames outside [First,Last] evaluate to 0 (identity quat 0,0,0,1 for type 6). Sampling: i0 = floor(frame),
lerp/slerp to i0+1 (wraps to frame 0 past NumFrames).

**0x203 BIT_CHANNEL** — 9-byte header + packed bits:

```
+0x00 u16 FirstFrame
+0x02 u16 LastFrame
+0x04 u16 ChannelType       // 0 = visibility (only type kept)
+0x06 u16 Pivot
+0x08 u8  DefaultVal
+0x09 u8  Bits[ (LastFrame-FirstFrame+8) >> 3 ]
```

**Bit packing is LSB-first:** `bit(f) = (Bits[(f-First)>>3] >> ((f-First)&7)) & 1`; out-of-range → DefaultVal;
channel absent → visible.

### 10.2 Compressed (0x280)

**0x281 COMPRESSED_ANIMATION_HEADER — 44 bytes**

```
+0x00 u32  Version          // unread
+0x04 char Name[16]
+0x14 char HierarchyName[16]
+0x24 u32  NumFrames
+0x28 u16  FrameRate        // u16!
+0x2A u16  Flavor           // 0 = TimeCoded, 1 = AdaptiveDelta
```

Only channel types 0/1/2/6 are kept for compressed animations (Euler types 3/4/5 discarded).

**0x282 flavor 0 — TimeCoded channel:**

```
+0x00 u32 NumTimeCodes
+0x04 u16 Pivot
+0x06 u8  VectorLen
+0x07 u8  ChannelType
+0x08 packets: NumTimeCodes × (1 + VectorLen) u32 words:
      { u32 TimeCode;  f32 Value[VectorLen] }
```

- TimeCode = frame number, low 31 bits (`& 0x7FFFFFFF`).
- **Bit 31 of the SEGMENT-END key's timecode = "binary movement"**: if the next key has bit 31 set, hold
  the current key (no interpolation); otherwise lerp/slerp with `t = (frame − tc0)/(tc1 − tc0)`.
- At/after the last key → hold last. Quat order x,y,z,w. No padding anywhere (⚠️ the u16-timecode layout
  in the old Unity doc is wrong).

**0x282 flavor 1 — AdaptiveDelta channel:**

```
+0x00 u32 NumFrames
+0x04 u16 Pivot
+0x06 u8  VectorLen  (V)
+0x07 u8  ChannelType
+0x08 f32 Scale
+0x0C data:
      f32 Initial[V]                        // frame-0 value per component
      blocks: ceil((NumFrames-1)/16) rows × V blocks of 9 bytes each
      block = { u8 TableIndex; u8 Nibbles[8] }   // 16 4-bit signed deltas
```

Block addressing: component c, group g at `data + V*4 + (g*V + c)*9`; group g covers frames
16g+1 … 16g+16; frame f uses nibble `(f−1)&15` of group `(f−1)>>4`. Nibble order **low nibble first**.

Decode:
```
val[c] = Initial[c]
for f = 1 .. target:
    block = &Data[V*4 + (((f-1)>>4)*V + c)*9]
    k = (f-1) & 15
    b = block[1 + (k>>1)];  n = (k&1) ? (b>>4)&0xF : b&0xF
    if (n & 8) n |= ~0xF                    // sign-extend
    val[c] += n * FilterTable[block[0]] * Scale
```

**FilterTable (256 floats):** entries 0–15 = `10^(i−8)` (1e-8 … 1e7, static data at 0xBC5DA4);
entries 16–255 = `1.0 − sin( (i−16) * (90/240) degrees )` for i=16..255. (Matches OpenSAGE's codec.)
Fractional frames: decode value(n) and value(n+1), lerp/slerp by frac.

**0x283 compressed bit channel:**

```
+0x00 u32 NumTimeCodes
+0x04 u16 Pivot
+0x06 u8  ChannelType     // 0 = visibility
+0x07 u8  DefaultVal      // parsed, never actually consulted by the eval
+0x08 u32 TimeCodes[NumTimeCodes]   // bit 31 = bit VALUE, low 31 bits = frame
```

Eval: find last entry with frame ≤ current; value = bit 31 of that entry.

### 10.3 Morph Animation (0x2C0) — standard W3D HMorphAnim

```
0x2C0
  0x2C1  header, 48 bytes
  0x2C2  × ChannelCount, each containing (positionally, IDs unchecked):
      0x2C3  PoseName        // NUL-terminated: names ANOTHER HAnim (0x200/0x280) by name
      0x2C4  KeyData         // (size/8) records of { u32 MorphFrame; u32 PoseFrame }
  0x2C5  PivotChannelData    // NumPivots × u32 = channel index driving each pivot
```

**0x2C1 header:**

```
+0x00 u32  Version         // writer emits 0
+0x04 char Name[16]
+0x14 char HierarchyName[16]
+0x24 u32  FrameCount
+0x28 f32  FrameRate
+0x2C u32  ChannelCount
```

Semantics: a morph channel *time-remaps* a referenced pose animation. For playing frame F, binary-search
the key list on MorphFrame; output = lerp/slerp between pose-anim samples at PoseFrame[i] and
PoseFrame[i+1] with `t = (F − MorphFrame[i]) / (MorphFrame[i+1] − MorphFrame[i])`.
All actual transform data lives in the referenced pose animations.

### 10.4 Channel types

| Type | Meaning | Evaluated? |
|---|---|---|
| 0 | X translation | yes |
| 1 | Y translation | yes |
| 2 | Z translation | yes |
| 3 | X rotation (Euler) | parsed, **never evaluated** — ignore |
| 4 | Y rotation (Euler) | parsed, never evaluated |
| 5 | Z rotation (Euler) | parsed, never evaluated |
| 6 | Quaternion (VectorLen=4, x,y,z,w) | yes |
| bit type 0 | Visibility | yes |

---

## 11. LOD Model (0x400) — the real distance-switched LOD

```
0x401 header, 24 bytes: { u32 Version; char Name[16]; u16 NumLODs (u32 read, masked) }
0x402 × NumLODs, 40 bytes: { char RenderObjName[32]; f32 LODMin; f32 LODMax }
```

LODMin/LODMax are the switching distances. (Loader vtables 0x9A4970/0x9A4A80; parse fn 0x9A4CA0, asm only.)

## 12. HLOD (0x700)

Structure: one 0x701, then exactly LodCount × 0x702 (LOD arrays), then any number of 0x705/0x706.

**0x701 header — 40 bytes:** `{ u32 Version; u32 LodCount; char ModelName[16] @+8; char HierarchyName[16] @+24 }`
(**ModelName first.**)

**0x702 LOD array** contains:
- **0x703** — 8 bytes: `{ u32 ModelCount; f32 MaxScreenSize }` (default FLT_MAX = no limit; **it is a float, not flags**)
- **0x704** × ModelCount — 36 bytes: `{ u32 BoneIndex; char Name[32] }`

**0x705 aggregate array** — same 0x703/0x704 grammar (MaxScreenSize unused). Members are instantiated
by name and attached to bones. **0x706 proxy array** — same grammar (only ModelCount used).

## 13. Aggregate (0x600)

- **0x601 header** — 20 bytes: `{ u32 Version; char Name[16] }`
- **0x602 info** — 36 bytes `{ char BaseModelName[32]; u32 SubobjectCount }` + SubobjectCount ×
  **64 bytes** `{ char SubobjectName[32]; char BoneName[32] }`
- **0x603 texture replacer info** — effectively skipped
- **0x604 class info** — 20 bytes: `{ u32 OriginalClassID; u32 Flags; u32 reserved[3] }`

## 14. Emitter (0x500)

Version in header gates parsing: ≥ 0x10001 → 0x504+0x505 expected (client writes 0x20000).
Keyframe extras dispatch on **0x509/0x50A/0x50B/0x50C/0x50D** (not 0x510+).

| ID | Name | Layout |
|---|---|---|
| 0x501 | HEADER | 20 bytes: `{ u32 Version; char Name[16] }` |
| 0x502 | USER_DATA | 12-byte header `{ u32 Type; u32 StringLen; u32 unused }` + StringLen bytes |
| 0x503 | INFO | 332 bytes: `char TextureFilename[260]; f32 StartSize, EndSize, Lifetime, EmissionRate, MaxEmissions, VelocityRandom, PositionRandom, FadeTime, Gravity, Elasticity; vec3 Velocity, Acceleration; u8[4] StartColor(RGBA), EndColor` |
| 0x504 | INFOV2 | 124 bytes: `u32 BurstSize; Volume CreationVolume(32); Volume VelRandom(32); f32 OutwardVel, VelInherit; u8 Shader[16]; u32 RenderMode; u32 FrameMode; u32 rsv[6]` where Volume = `{u32 ClassID; f32 v1,v2,v3; u32 rsv[4]}` |
| 0x505 | PROPS | 40-byte header `{ u32 NumColorKf, NumOpacityKf, NumSizeKf; u8 ColorRandom[4]; f32 OpacityRandom, SizeRandom; u32 rsv[4] }` then NumColorKf × `{f32 Time; u8 RGBA}` + NumOpacityKf × `{f32 Time; f32 Value}` + NumSizeKf × `{f32 Time; f32 Value}` — record 0 of each list = the start value |
| 0x509 | LINE_PROPERTIES | 64 bytes: `u32 Flags; u32 SubdivisionLevel; f32 NoiseAmplitude, MergeAbortFactor, TextureTileFactor, UPerSec, VPerSec; u32 rsv[9]` |
| 0x50A | ROTATION_KEYFRAMES | 16-byte header `{ u32 KeyframeCount; f32 Random; f32 OrientationRandom; u32 rsv }` + **(KeyframeCount+1)** × `{f32 Time; f32 Rotation}` (record 0: Time ignored, value = initial rotational velocity) |
| 0x50B | FRAME_KEYFRAMES | 16-byte header `{ u32 KeyframeCount; f32 Random; u32 rsv[2] }` + (KeyframeCount+1) × `{f32 Time; f32 Frame}` |
| 0x50C | BLUR_TIME_KEYFRAMES | 12-byte header `{ u32 KeyframeCount; f32 Random; u32 rsv }` + (KeyframeCount+1) × `{f32 Time; f32 BlurTime}` |
| 0x50D | (extra info) | 40 bytes raw → def+0x1E0; consumption untraced |

## 15. ShdMesh (0xB00) — shader-mesh format

Class name `ShdMeshClass` (debug string proof). Grammar (`FUN_00a0a380`, c:1430714):

```
0xB01  NAME           NUL-terminated string (whole chunk)
0xB02  HEADER         80 bytes:
       +0x00 u32 Flags            (bit 0x1000 tested)
       +0x04 u32 packed           ((v>>4 & 0xFF)*2 → engine param)
       +0x10 u32 NumSubMeshes
       +0x28 vec3 BoxMin
       +0x34 vec3 BoxMax
       +0x40 f32[4] BoundingSphere (center xyz + radius)
0xB03  user text      read into 260-byte buffer, DISCARDED
0xB20  SUBMESH        one per submesh:
   0xB21 SUBMESH_HEADER  56 bytes: { u32 NumTriangles; u32 NumVertices; 8 unused;
                                     10 dwords bounds (box+sphere, exact split unconfirmed) }
   0xB40 SHADER          nested: { u32 ClassID (1..5) } + class-specific payload chunk
   0xB43 VERTICES        NumVertices × vec3
   0xB44 VERTEX_NORMALS  NumVertices × vec3
   0xB45 TRIANGLES       NumTriangles × 3 × u16   (u16 indices HERE, unlike mesh 0x20)
   0xB46 VERTEX_SHADE_INDICES  NumVertices × u32
   0xB47 UV0 / 0xB48 UV1 NumVertices × {f32 u, f32 v}
   0xB49/0xB4A/0xB4B     tangent basis S / T / SxT, NumVertices × vec3
```

---

## 16. Corrections vs earlier docs

| Old claim (doc) | Reality | Proof |
|---|---|---|
| 0x800 = EnB shader container (this file v1) | Loader `FUN_00488500` reads **splines.wsd** (FX spline DB); 0x800/0x808/0x810/0x811 are its internal chunks, never reachable from W3D loading | c:594072, 594618; no prototype loader for 0x800 |
| 0x15 = vertex skinning container (v1) | 0x15 = legacy **MATERIALS3**; skinning is chunk 0x0E | c:1387354 |
| 0x23–0x26 = vertex color channels (v1) | Standard **PRELIT** material containers | c:1388415; W3dview 107123 |
| 0x2C0 EnB-specific "Compressed Animation 2" (v1, delta doc) | Standard **morph animation**; 0x2C4 = (MorphFrame, PoseFrame) u32 pairs | c:1360740, 1360091 |
| 0xB01 = EnB LOD model (v1) | Loader chunk type is **0xB00 = ShdMeshClass**; real LOD model is **0x400** | vtable 0xA08C60; debug string c:1430443 |
| Chunks 0x54/0x55/0x58/0x59 (EXTENSIONS doc) | Network-message class factory allocation sizes — **not W3D chunks** | c:368089+ |
| Triangles: u16 indices + 12 unknown bytes (v1, Unity doc) | Standard 32-byte W3dTriStruct, u32 indices | c:1308778; W3dview 89210 |
| Pivot: quat at +0x20 W-first, no eulers (v1) | Standard 60-byte pivot; eulers at +0x20 ignored; quat x,y,z,w at +0x2C | c:1361501; W3dview 41292 |
| 0x201: HierarchyName at +4, AnimName[12] at +0x14 (v1) | Name at +4, HierarchyName at +0x14 (both 16 bytes) | writer roundtrip c:1360988; W3dview 105994 |
| HLOD 0x703: u32 count + u32 Flags (v1) | u32 count + **f32 MaxScreenSize** | c:1363125 (FLT_MAX default); W3dview 43369 |
| 0x282 TimeCoded: u16 timecode array + separate float data (Unity doc) | Interleaved packets `{u32 TimeCode; f32[VectorLen]}`, bit 31 = no-interp | c:1394593; W3dview 39700 |
| Stage-1 mapper mask 0xFF000000 (Unity doc assumption) | **0x0000FF00** | c:1310961; W3dview 66584 |
| Emitter rotation/frame keys at 0x510/0x511/0x512 (v1) | **0x50A/0x50B/0x50C** (+0x50D extra) | c:1296429 |
| 0x05/0x0D/0x21 "EnB-specific" (v1) | Legacy standard W3D chunks (also parsed by W3dview) | W3dview 107347–107484 |

## 17. C# viewer implementation checklist

1. Generic chunk walker (8-byte headers, size-bounded recursion, skip-unknown).
2. Mesh: header → vertices/normals/UVs(flip V)/triangles(u32 idx) → materials from **both** paths:
   modern (0x28/0x29/0x2A/0x30/0x38) and legacy MATERIALS3 (0x15 + 0x21 per-face binding).
3. Prelit: honor header prelit bits or ignore 0x23–0x26 safely.
4. Shader → blend-mode classification table in §4.
5. Hierarchy: 60-byte pivots, T·R local transforms, version<0x30000 root synthesis.
6. Animation: 0x200 channels (types 0,1,2,6 only), 0x280 TimeCoded + AdaptiveDelta (FilterTable in §10.2),
   0x2C0 morph = time-remap of referenced pose anims.
7. HLOD to bind meshes to bones; Aggregate for composed models; 0x400 LODMODEL for distance switching.
8. Textures: `.tga` names verbatim; texture animation comes from mapper args (§6), not TEXTURE_INFO.
9. Emitters optional; layouts in §14 when needed.

### Still unextracted (lower priority)
- HMODEL (0x300), COLLECTION (0x420), BOX (0x740), NULL (0x750), DAZZLE (0x900) payloads
  (loader fns catalogued in §2 — likely standard W3D layouts).
- EnB loaders 0x741/0x742 (parse fns 0x90DFE0 / 0x912F50).
- ShdMesh 0xB40 shader class IDs 1–5 payloads; 0xB21 bounds dword order.
- Emitter 0x50D consumption; MATERIAL3 0x1B map-info semantics.

## 18. Verified against the shipped game files (2026-10-11)

All 6,368 `.w3d` files of the game's Art folder were walked chunk by chunk and cross-checked with
the client loader. Results that refine or correct earlier sections:

| Topic | Verified fact |
|---|---|
| Chunk flag | The 0x80000000 "has sub-chunks" bit is unreliable: ~800 meshes and many 0x100/0x200/0x300/0x500/0x600/0x700 containers omit it. Recurse by chunk id. |
| Triangle winding | In file order `cross(v1-v0, v2-v0)` points along the stored normal for ~95% of triangles (CCW in W3D's right-handed Z-up space); `PlaneDist == dot(normal, v0)`. |
| §5 prevalence | Only 103 meshes (mesh version 3.0) use MATERIALS3; the modern 0x28/0x29/0x2A/0x30/0x38 path covers the other ~56,700. 32 meshes use the W3D 1.x header 0x01 (+0x09/0x14), which the client's mesh loader does not accept. |
| §3.3 id arrays | 0x39 vertex material ids are one id or **NumVertices** ids (never per face); 0x3A shader ids and 0x49 texture ids are one id or NumTriangles ids; 5,207 texture id arrays contain -1 (= untextured face). 16% of meshes have 2–4 passes. |
| §6 mappers | Stage mapper values 0x13/0x14 occur (0x14 in ~5,500 vertex materials); later WW3D names them Grid-WS classic environment / Grid-WS environment. |
| §10 animation | Channel values are **deltas** applied on top of the pivot base pose: `pivot = parent * base * Translate(t) * Rotate(q)`. First-frame translations are ~0 for 6,732 channels whose pivot base translation is not (18 equal the base); first-frame rotations are identity for 2,052 channels with a non-identity base (61 equal it). All 24 compressed animations are time-coded. One uncompressed channel (SG01-IDL.W3D) declares more frames than its chunk holds; many have trailing pad bytes. |
| HMODEL (0x300) | 121 files. 0x301 header 40 bytes `{u32 Version; char Name[16]; char HierarchyName[16]; u16 NumConnections; u16 pad}`; 0x302 (also 0x303/0x304/0x306) nodes 18 bytes `{char RenderObjName[16]; u16 PivotIdx}`; 0x305 aux data 184 bytes. Render object = `<Name>.<RenderObjName>`. |
| Pre-3.0 files | 10 hierarchies are version 0x20000 (HEDRA_EX, POD01, ZOOM, ...). Their HMODEL node and animation (version < 0x30000) pivot indices are 0-based over the file pivots, i.e. they need +1 after the implicit root pivot is inserted. |
| 0x741 / 0x742 | Renegade SPHERE / RING render objects (70 files): sub-chunk 0x1 header (164 / 168 bytes: version, attributes, char Name[32], center, extent, ...), 0x2-0x5 keyframe channel containers holding 0x03150809 micro-chunk blobs. |
| HLOD | 15,265 LOD-array entries are `NULL` placeholders and 33 reference meshes of other models; 0x705 aggregate arrays (50 files) and 0x706 proxy arrays (6 files) only reference objects outside the file. |
| §14 emitter | 0x502 = 12-byte header `{u32 Type; u32 StringLen; u32 reserved}` + StringLen bytes (sizes 12 / 13 seen). 0x50A / 0x50B headers are 16 bytes and 0x50C 12 bytes, followed by **Count + 1** records (Count records for the 5 version-0x10000 emitters); the "reserved" words are not always zero (often 3). 0x504 is 124 bytes. |
| Dazzle | Type names in the files: INTERIOR01, AFTERBURN, YELLOWSTAR, SUN, BLUESTAR. |
| Shader struct | `PostDetailAlphaFunc` holds out-of-range values (28, 193) in 6 shaders; the client ignores the post-detail fields. |
| Shader stage ops | `FUN_0093b510` maps PriGradient to stage-0 COLOROP: 0 = SELECTARG1 (texture only, no lighting), 1 = MODULATE, 2 = ADD, 3/4 = BUMPENVMAP(LUMINANCE), 5 = MODULATE2X. DetailColorFunc to stage-1 COLOROP: 1 = SELECTARG1 (detail), 2 = MODULATE, 3 = ADDSMOOTH, 4 = ADD, 5 = SUBTRACT(detail - current), 6 = SUBTRACT(current - detail), 7 = BLENDTEXTUREALPHA, 8 = BLENDCURRENTALPHA, 9 = ADDSIGNED, 10 = ADDSIGNED2X, 11 = MODULATE2X (used for lightmaps), 12 = MODULATEALPHA_ADDCOLOR (current + current.a * detail; ADD on devices without it). DetailAlphaFunc 1/2/3 = SELECTARG1/MODULATE/ADDSMOOTH. Ops the device lacks fall back to MODULATE / ADD. |
| Material passes | Every pass is drawn, in order, over the same triangles (depth test LEQUAL). Interiors use pass 1 for lightmapped overlays blended by the texture alpha (e.g. Src = ONE_MINUS_SRC_ALPHA, Dst = SRC_ALPHA in nbar01), so a viewer that draws only pass 0 shows flat gradients instead of the floor / sign artwork. |
| Vertex material lighting | Lit color = saturate(Emissive + Ambient * sceneAmbient + Diffuse * lights). After load (`FUN_009475E0`) the client bakes each pass's DCG into the color array multiplied by a material color and points a color source at it (`FUN_0092f030/60/90` = ambient / emissive / diffuse source). For materials with black Diffuse and Ambient and a non-black Emissive it bakes DCG x Emissive, makes it the diffuse source and turns **lighting off** (`+0x60 = 0`): the surface is texture x DCG x Emissive (baked interior lighting, e.g. jsd01a). A white Emissive next to a non-black Diffuse / Ambient saturates the lit sum instead. |
| Texture files | 1 of the 4,039 DDS files is DXT3 (`tadaOcake.dds`); its alpha blocks differ from DXT5. The AI-upscaled `.png` copies next to the originals lost or darkened their color under transparent pixels (32 nearly black there, e.g. `g_lng1.png`, ~90 more visibly darker); the viewer restores it from the original `.dds` / `.tga`. |
| Environment mappers | Mapper Apply functions (vtable slot 5, e.g. 0x934fb0 / 0x9348b0) set D3DTSS_TEXCOORDINDEX and a texture matrix: Environment (1), Grid env (15), WS env (13), Grid WS env (20) use CAMERASPACEREFLECTIONVECTOR (0x30000); Cheap env (2), Grid classic (14), WS classic (12), Grid WS classic (19) use CAMERASPACENORMAL (0x10000). Matrix u = 0.5 x + 0.5, v = 0.5 y + 0.5. WS variants first rotate the vector back to world space with the inverse view and pick two W3D axes by the `Axis` arg (X: y,z; Y: x,z; Z (default): x,y). Grid variants scale by 0.5 / width and add the cell: u = (0.5 a + 0.5 + col) / width, v = (0.5 b + 0.5 + row) / width. |
| Grid mapper (7) | Matrix [1 0 col/width; 0 1 row/width] (`0x933740`): no scale, the authored UVs already span one cell; col = frame & (width-1), row = frame >> Log2Width, rows counted from the texture's first row; frame = Offset + elapsed frames (FPS), wrapped by Last / width^2. |
