using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using W3DImporter.Structs.Mesh;

namespace W3DImporter.Builders
{
    public enum W3DTextureAnimationKind
    {
        None = 0,
        FrameSequence,
        GridAtlas,
        LinearOffset,
        SineLinearOffset,
        StepLinearOffset,
        ZigZagLinearOffset,
        Rotate,
        Random,
        Screen,
        Unsupported
    }

    public sealed class W3DTextureBinding
    {
        public int MaterialIndex;
        public int PassIndex = -1;
        public int StageIndex = -1;
        public int TextureIndex = -1;
        public string ExplicitTextureName = "";
        public W3DTexture Texture;

        public string TextureName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ExplicitTextureName))
                    return ExplicitTextureName;

                return Texture?.File ?? string.Empty;
            }
        }
    }

    public sealed class W3DFaceMaterialBinding
    {
        public int VertexMaterialIndex = -1;
        public int PrimaryTextureIndex = -1;
        public int SecondaryTextureIndex = -1;
        public int ShaderIndex = -1;
        // first triangle using this binding; per-face / per-vertex ids of any stage resolve through it
        public int RepresentativeFace = -1;
    }

    public sealed class W3DResolvedTextureAnimation
    {
        public int MaterialIndex;
        public W3DTextureBinding Binding;
        public W3DTextureAnimationKind Kind;
        public string Source = "";
        public string MapperName = "";
        public string Args = "";
        public bool IsSupported = true;
        public string UnsupportedReason = "";

        public ushort TextureAnimType;
        public float TextureFrameRate;
        public int TextureFrameCount;

        public int GridWidth = 1;
        public int FrameCount = 1;
        public float FramesPerSecond;
        public int FrameOffset;

        public float UScale = 1f;
        public float VScale = 1f;
        public float UPerSec;
        public float VPerSec;

        public float UAmplitude;
        public float UFrequency = 1f;
        public float UPhase;
        public float VAmplitude;
        public float VFrequency = 1f;
        public float VPhase;

        public float StepU;
        public float StepV;
        public float StepsPerSecond;

        public float PeriodSeconds;
        public float RotationSpeedHz;
        public float UCenter;
        public float VCenter;

        public int StageIndex => Binding?.StageIndex ?? -1;
        public int TextureIndex => Binding?.TextureIndex ?? -1;
        public string TextureName => Binding?.TextureName ?? string.Empty;
        public bool UsesUvTransform => IsSupported && Kind != W3DTextureAnimationKind.None && Kind != W3DTextureAnimationKind.FrameSequence;
    }

    public static class W3DTextureAnimationResolver
    {
        private const uint Stage0Mask = 0x00FF0000u;
        private const uint Stage1Mask = 0x0000FF00u;

        private const int Stage0Screen = 0x00030000;
        private const int Stage0LinearOffset = 0x00040000;
        private const int Stage0Scale = 0x00060000;
        private const int Stage0Grid = 0x00070000;
        private const int Stage0Rotate = 0x00080000;
        private const int Stage0SineLinearOffset = 0x00090000;
        private const int Stage0StepLinearOffset = 0x000A0000;
        private const int Stage0ZigZagLinearOffset = 0x000B0000;
        private const int Stage0EnvironmentMin = 0x00010000;
        private const int Stage0EnvironmentMax = 0x00120000;
        private const int Stage0Random = 0x00100000;
        private const int Stage0Edge = 0x00110000;
        private const int Stage0BumpEnv = 0x00120000;

        private const int Stage1Screen = 0x00000300;
        private const int Stage1LinearOffset = 0x00000400;
        private const int Stage1Scale = 0x00000600;
        private const int Stage1Grid = 0x00000700;
        private const int Stage1Rotate = 0x00000800;
        private const int Stage1SineLinearOffset = 0x00000900;
        private const int Stage1StepLinearOffset = 0x00000A00;
        private const int Stage1ZigZagLinearOffset = 0x00000B00;
        private const int Stage1EnvironmentMin = 0x00000100;
        private const int Stage1EnvironmentMax = 0x00001200;
        private const int Stage1Random = 0x00001000;
        private const int Stage1Edge = 0x00001100;
        private const int Stage1BumpEnv = 0x00001200;

        private static readonly Regex ShaderTextureAnimationRegex = new Regex(
            @"^TextureAnimation_FPS_NumPerRow_LastFrame_FrameOffset_(\d+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static W3DTextureBinding ResolvePrimaryTextureBinding(W3DMesh w3dMesh, int materialIndex)
        {
            var explicitBinding = ResolveExplicitTextureBinding(w3dMesh, materialIndex);
            if (explicitBinding != null)
                return explicitBinding;

            var firstBinding = ResolveFirstTextureBinding(w3dMesh, materialIndex);
            var shader = ResolveShader(w3dMesh, materialIndex);
            if (firstBinding != null && shader != null && (shader.DetailColorFunc != 0 || shader.DetailAlphaFunc != 0))
                return firstBinding;

            var animations = ResolveTextureAnimations(w3dMesh, materialIndex);
            var promotedBinding = ResolvePromotedAnimatedBinding(firstBinding, animations);
            if (promotedBinding != null)
                return promotedBinding;

            if (firstBinding != null)
                return firstBinding;

            for (int i = 0; i < animations.Count; i++)
            {
                if (animations[i]?.Binding != null)
                    return animations[i].Binding;
            }

            return null;
        }

        public static W3DResolvedTextureAnimation ResolvePrimaryTextureAnimation(W3DMesh w3dMesh, int materialIndex)
        {
            var primaryBinding = ResolvePrimaryTextureBinding(w3dMesh, materialIndex);
            var animations = ResolveTextureAnimations(w3dMesh, materialIndex);
            if (animations.Count == 0)
                return null;

            W3DResolvedTextureAnimation firstMatchingAnimation = null;
            for (int i = 0; i < animations.Count; i++)
            {
                var animation = animations[i];
                if (animation == null)
                    continue;

                if (primaryBinding != null && !BindingsMatch(primaryBinding, animation.Binding))
                    continue;

                if (firstMatchingAnimation == null)
                    firstMatchingAnimation = animation;

                if (animation.IsSupported)
                    return animation;
            }

            return firstMatchingAnimation;
        }

        public static List<W3DResolvedTextureAnimation> ResolveTextureAnimations(W3DMesh w3dMesh, int materialIndex)
        {
            var results = new List<W3DResolvedTextureAnimation>();
            if (w3dMesh == null)
                return results;

            var claimedStages = new HashSet<int>();
            ResolveShaderAtlasAnimations(w3dMesh, materialIndex, results, claimedStages);
            ResolveVertexMapperAnimations(w3dMesh, materialIndex, results, claimedStages);

            if (results.Count == 0)
                ResolveDirectTextureAnimation(w3dMesh, materialIndex, results);

            return results;
        }

        // Materials are built from the base pass. Additional passes (environment / glow overlays)
        // are drawn on top of it by the client and are not mapped to separate Unity materials.
        public static W3DMaterialPass ResolveMaterialPass(W3DMesh w3dMesh, int materialIndex, out int passIndex)
        {
            passIndex = -1;
            if (w3dMesh == null || w3dMesh.MaterialPasses.Count == 0)
                return null;

            passIndex = 0;
            return w3dMesh.MaterialPasses[0];
        }

        public static W3DVertexMaterial ResolveVertexMaterial(W3DMesh w3dMesh, int materialIndex)
        {
            var pass = ResolveMaterialPass(w3dMesh, materialIndex, out _);
            if (pass != null && pass.VertexMaterialIds.Count > 0)
            {
                int vmIndex = FaceVertexMaterialId(w3dMesh, pass, RepresentativeFace(w3dMesh, materialIndex));
                if (vmIndex >= 0 && vmIndex < w3dMesh.VertMaterials.Count)
                    return w3dMesh.VertMaterials[vmIndex];
            }

            if (materialIndex >= 0 && materialIndex < w3dMesh.VertMaterials.Count)
                return w3dMesh.VertMaterials[materialIndex];

            return null;
        }

        public static W3DShaderMaterial ResolveShaderMaterial(W3DMesh w3dMesh, int materialIndex)
        {
            var pass = ResolveMaterialPass(w3dMesh, materialIndex, out _);
            if (pass != null && pass.ShaderMaterialIds.Count > 0)
            {
                int shaderMaterialIndex = PassId(pass.ShaderMaterialIds, RepresentativeFace(w3dMesh, materialIndex));
                if (shaderMaterialIndex >= 0 && shaderMaterialIndex < w3dMesh.ShaderMaterials.Count)
                    return w3dMesh.ShaderMaterials[shaderMaterialIndex];
            }

            if (materialIndex >= 0 && materialIndex < w3dMesh.ShaderMaterials.Count)
                return w3dMesh.ShaderMaterials[materialIndex];

            return null;
        }

        public static W3DShader ResolveShader(W3DMesh w3dMesh, int materialIndex)
        {
            var pass = ResolveMaterialPass(w3dMesh, materialIndex, out _);
            if (pass != null && pass.ShaderIds.Count > 0)
            {
                int shaderIndex = PassId(pass.ShaderIds, RepresentativeFace(w3dMesh, materialIndex));
                if (shaderIndex >= 0 && shaderIndex < w3dMesh.Shaders.Count)
                    return w3dMesh.Shaders[shaderIndex];
            }

            if (materialIndex >= 0 && materialIndex < w3dMesh.Shaders.Count)
                return w3dMesh.Shaders[materialIndex];

            return null;
        }

        // ------------------------------------------------------------------
        // Material pass id arrays hold either one id for the whole pass or one id per
        // element (client FUN_009bbec0 / FUN_009bbf80 / FUN_009bc810):
        //   VERTEX_MATERIAL_IDS (0x39) - one per VERTEX
        //   SHADER_IDS (0x3A), TEXTURE_IDS (0x49) - one per TRIANGLE, texture id -1 = none
        // ------------------------------------------------------------------

        private sealed class FaceBindingCache
        {
            public readonly List<int> FaceIndices = new List<int>();
            public readonly List<W3DFaceMaterialBinding> Bindings = new List<W3DFaceMaterialBinding>();
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<W3DMesh, FaceBindingCache> FaceBindingCaches =
            new System.Runtime.CompilerServices.ConditionalWeakTable<W3DMesh, FaceBindingCache>();

        private static int PassId(List<int> ids, int face)
        {
            if (ids == null || ids.Count == 0)
                return -1;
            if (ids.Count == 1 || face < 0 || face >= ids.Count)
                return ids[0];
            return ids[face];
        }

        private static int FaceVertexMaterialId(W3DMesh w3dMesh, W3DMaterialPass pass, int face)
        {
            var ids = pass?.VertexMaterialIds;
            if (ids == null || ids.Count == 0)
                return -1;
            if (ids.Count == 1)
                return ids[0];

            var tri = face >= 0 && face < w3dMesh.Triangles.Count ? w3dMesh.Triangles[face] : null;
            if (ids.Count == w3dMesh.Verts.Count && tri != null && tri.VertIds[0] < ids.Count)
                return ids[(int)tri.VertIds[0]];
            if (ids.Count == w3dMesh.Triangles.Count)
                return PassId(ids, face);
            return ids[0];
        }

        private static int FaceTextureId(W3DMesh w3dMesh, W3DMaterialPass pass, int stageIndex, int face)
        {
            if (pass == null || stageIndex < 0 || stageIndex >= pass.TxStages.Count)
                return -1;

            var stage = pass.TxStages[stageIndex];
            if (stage?.TxIds == null || stage.TxIds.Count == 0)
                return -1;

            int textureIndex = PassId(stage.TxIds[0], face);
            return textureIndex >= 0 && textureIndex < w3dMesh.Textures.Count ? textureIndex : -1;
        }

        private static FaceBindingCache GetFaceBindings(W3DMesh w3dMesh)
        {
            if (w3dMesh?.Triangles == null || w3dMesh.Triangles.Count == 0 || w3dMesh.MaterialPasses.Count == 0)
                return null;

            if (FaceBindingCaches.TryGetValue(w3dMesh, out var cached))
                return cached;

            var pass = w3dMesh.MaterialPasses[0];
            var cache = new FaceBindingCache();
            var bindingIndexByKey = new Dictionary<(int, int, int, int), int>();

            for (int face = 0; face < w3dMesh.Triangles.Count; face++)
            {
                int vertexMaterialIndex = FaceVertexMaterialId(w3dMesh, pass, face);
                int shaderIndex = PassId(pass.ShaderIds, face);
                int primaryTextureIndex = FaceTextureId(w3dMesh, pass, 0, face);
                int secondaryTextureIndex = FaceTextureId(w3dMesh, pass, 1, face);
                var key = (vertexMaterialIndex, primaryTextureIndex, secondaryTextureIndex, shaderIndex);

                if (!bindingIndexByKey.TryGetValue(key, out int bindingIndex))
                {
                    bindingIndex = cache.Bindings.Count;
                    bindingIndexByKey[key] = bindingIndex;
                    cache.Bindings.Add(new W3DFaceMaterialBinding
                    {
                        VertexMaterialIndex = vertexMaterialIndex,
                        PrimaryTextureIndex = primaryTextureIndex,
                        SecondaryTextureIndex = secondaryTextureIndex,
                        ShaderIndex = shaderIndex,
                        RepresentativeFace = face
                    });
                }

                cache.FaceIndices.Add(bindingIndex);
            }

            FaceBindingCaches.Add(w3dMesh, cache);
            return cache;
        }

        // Material index -> triangle whose ids describe that material. With a per-face split the
        // material index is the binding index; otherwise every face shares the same ids.
        private static int RepresentativeFace(W3DMesh w3dMesh, int materialIndex)
        {
            var cache = GetFaceBindings(w3dMesh);
            if (cache == null || cache.Bindings.Count == 0)
                return 0;

            if (cache.Bindings.Count > 1 && materialIndex >= 0 && materialIndex < cache.Bindings.Count)
                return cache.Bindings[materialIndex].RepresentativeFace;

            return cache.Bindings[0].RepresentativeFace;
        }

        public static int FindTextureIndexByName(W3DMesh w3dMesh, string textureName)
        {
            if (w3dMesh == null || string.IsNullOrWhiteSpace(textureName))
                return -1;

            string normalized = NormalizeTextureName(textureName);
            for (int i = 0; i < w3dMesh.Textures.Count; i++)
            {
                if (NormalizeTextureName(w3dMesh.Textures[i].File) == normalized)
                    return i;
            }

            return -1;
        }

        public static string NormalizeTextureName(string textureName)
        {
            if (string.IsNullOrWhiteSpace(textureName))
                return string.Empty;

            return System.IO.Path.GetFileNameWithoutExtension(textureName).Trim().ToUpperInvariant();
        }

        private static void ResolveShaderAtlasAnimations(
            W3DMesh w3dMesh,
            int materialIndex,
            List<W3DResolvedTextureAnimation> results,
            HashSet<int> claimedStages)
        {
            var shaderMaterial = ResolveShaderMaterial(w3dMesh, materialIndex);
            if (shaderMaterial?.Properties == null || shaderMaterial.Properties.Count == 0)
                return;

            for (int i = 0; i < shaderMaterial.Properties.Count; i++)
            {
                var property = shaderMaterial.Properties[i];
                if (property == null || string.IsNullOrWhiteSpace(property.Name))
                    continue;

                var match = ShaderTextureAnimationRegex.Match(property.Name);
                if (!match.Success)
                    continue;

                if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int stageIndex))
                    continue;

                if (!property.TryGetVector4(out var value))
                    continue;

                var binding = ResolveStageTextureBinding(w3dMesh, materialIndex, stageIndex);
                if (binding == null)
                    continue;

                claimedStages.Add(stageIndex);
                results.Add(new W3DResolvedTextureAnimation
                {
                    MaterialIndex = materialIndex,
                    Binding = binding,
                    Kind = W3DTextureAnimationKind.GridAtlas,
                    Source = "ShaderMaterial",
                    MapperName = property.Name,
                    GridWidth = Math.Max(1, UnityEngine.Mathf.RoundToInt(value.y)),
                    FrameCount = Math.Max(1, UnityEngine.Mathf.RoundToInt(value.z) + 1),
                    FramesPerSecond = value.x,
                    FrameOffset = Math.Max(0, UnityEngine.Mathf.RoundToInt(value.w))
                });
            }
        }

        private static void ResolveVertexMapperAnimations(
            W3DMesh w3dMesh,
            int materialIndex,
            List<W3DResolvedTextureAnimation> results,
            HashSet<int> claimedStages)
        {
            var vm = ResolveVertexMaterial(w3dMesh, materialIndex);
            if (vm?.Info == null)
                return;

            for (int stageIndex = 0; stageIndex < 2; stageIndex++)
            {
                if (claimedStages.Contains(stageIndex))
                    continue;

                int rawMapping = GetStageMapping(vm.Info.Attributes, stageIndex);
                if (rawMapping == 0)
                    continue;

                var binding = ResolveStageTextureBinding(w3dMesh, materialIndex, stageIndex);
                if (binding == null)
                    continue;

                string args = stageIndex == 0 ? vm.VmArgs0 : vm.VmArgs1;
                var animation = CreateMapperAnimation(materialIndex, binding, stageIndex, rawMapping, args);
                if (animation == null)
                    continue;

                claimedStages.Add(stageIndex);
                results.Add(animation);
            }
        }

        private static void ResolveDirectTextureAnimation(
            W3DMesh w3dMesh,
            int materialIndex,
            List<W3DResolvedTextureAnimation> results)
        {
            var binding = ResolveFirstTextureBinding(w3dMesh, materialIndex);
            if (binding?.Texture == null || !binding.Texture.IsAnimated)
                return;

            results.Add(new W3DResolvedTextureAnimation
            {
                MaterialIndex = materialIndex,
                Binding = binding,
                Kind = W3DTextureAnimationKind.FrameSequence,
                Source = "TextureInfo",
                MapperName = "TEXTURE_INFO",
                TextureAnimType = binding.Texture.AnimType,
                TextureFrameCount = Math.Max(1, (int)binding.Texture.FrameCount),
                TextureFrameRate = binding.Texture.FrameRate
            });
        }

        // Texture coordinate generation of the environment mapper families, done by the shader
        // (W3DCombine.hlsl W3DTexGen; the mapper Apply functions were decoded from client.exe:
        // "classic" / cheap mappers use the camera-space normal, the others the reflection vector,
        // WS mappers rotate it into world space and project along Axis, grid mappers squeeze the
        // result into one cell of a (1 << Log2Width)^2 atlas). Returns the W3DTexGen kind, 0 = mesh uvs.
        public static int GetStageTexGen(W3DVertexMaterial vm, int stageIndex, out int axis, out UnityEngine.Vector4 grid)
        {
            axis = 2;
            grid = new UnityEngine.Vector4(1f, 0f, 0f, 1f);
            if (vm?.Info == null)
                return 0;

            int mapper = (int)((stageIndex == 0 ? vm.Info.Attributes >> 16 : vm.Info.Attributes >> 8) & 0xFF);
            string args = stageIndex == 0 ? vm.VmArgs0 : vm.VmArgs1;
            int kind;
            bool gridded = false;
            switch (mapper)
            {
                case 1: kind = 1; break;                  // environment
                case 2: kind = 2; break;                  // cheap environment
                case 12: kind = 3; break;                 // WS classic environment
                case 13: kind = 4; break;                 // WS environment
                case 14: kind = 2; gridded = true; break; // grid classic environment
                case 15: kind = 1; gridded = true; break; // grid environment
                case 19: kind = 3; gridded = true; break; // grid WS classic environment
                case 20: kind = 4; gridded = true; break; // grid WS environment
                default: return 0;
            }

            if (kind >= 3 && TryGetIniValue(args, "Axis", out string axisName) && axisName.Length > 0)
            {
                char c = char.ToUpperInvariant(axisName[0]);
                axis = c == 'X' ? 0 : c == 'Y' ? 1 : 2;
            }

            if (gridded)
            {
                int width = 1 << Math.Max(0, Math.Min(GetIniInt(args, "Log2Width", 1), 8));
                int cells = width * width;
                int last = GetIniInt(args, "Last", 0);
                grid = new UnityEngine.Vector4(
                    width,
                    GetIniFloat(args, "FPS", 1f),
                    Math.Max(0, GetIniInt(args, "Offset", 0)),
                    last > 0 ? Math.Min(last, cells) : cells);
            }

            return kind;
        }

        private static W3DResolvedTextureAnimation CreateMapperAnimation(
            int materialIndex,
            W3DTextureBinding binding,
            int stageIndex,
            int rawMapping,
            string args)
        {
            // environment mappers are handled by the shader (GetStageTexGen)
            int mapper = stageIndex == 0 ? rawMapping >> 16 : rawMapping >> 8;
            if (mapper == 1 || mapper == 2 || (mapper >= 12 && mapper <= 15) || mapper == 19 || mapper == 20)
                return null;

            string mapperName = GetMapperName(stageIndex, rawMapping);
            if (string.IsNullOrEmpty(mapperName))
                return null;

            var animation = new W3DResolvedTextureAnimation
            {
                MaterialIndex = materialIndex,
                Binding = binding,
                Source = "VertexMapper",
                MapperName = mapperName,
                Args = args ?? string.Empty
            };

            switch (mapperName)
            {
                case "GRID":
                {
                    int log2Width = GetIniInt(args, "Log2Width", 1);
                    int gridWidth = Math.Max(1, 1 << log2Width);
                    int maxFrames = gridWidth * gridWidth;
                    int last = GetIniInt(args, "Last", 0);

                    animation.Kind = W3DTextureAnimationKind.GridAtlas;
                    animation.GridWidth = gridWidth;
                    animation.FrameCount = last <= 0 ? maxFrames : UnityEngine.Mathf.Clamp(last, 1, maxFrames);
                    animation.FramesPerSecond = GetIniFloat(args, "FPS", 1f);
                    return animation;
                }

                case "LINEAR_OFFSET":
                    animation.Kind = W3DTextureAnimationKind.LinearOffset;
                    animation.UScale = GetIniFloat(args, "UScale", 1f);
                    animation.VScale = GetIniFloat(args, "VScale", 1f);
                    animation.UPerSec = GetIniFloat(args, "UPerSec", 0f);
                    animation.VPerSec = GetIniFloat(args, "VPerSec", 0f);
                    return animation;

                case "SCALE":
                    return null;

                case "SINE_LINEAR_OFFSET":
                    animation.Kind = W3DTextureAnimationKind.SineLinearOffset;
                    animation.UAmplitude = GetIniFloat(args, "UAmp", 1f);
                    animation.UFrequency = GetIniFloat(args, "UFreq", 1f);
                    animation.UPhase = GetIniFloat(args, "UPhase", 0f);
                    animation.VAmplitude = GetIniFloat(args, "VAmp", 1f);
                    animation.VFrequency = GetIniFloat(args, "VFreq", 1f);
                    animation.VPhase = GetIniFloat(args, "VPhase", 0f);
                    return animation;

                case "STEP_LINEAR_OFFSET":
                    animation.Kind = W3DTextureAnimationKind.StepLinearOffset;
                    animation.StepU = GetIniFloat(args, "UStep", 0f);
                    animation.StepV = GetIniFloat(args, "VStep", 0f);
                    animation.StepsPerSecond = GetIniFloat(args, "SPS", 0f);
                    return animation;

                case "ZIGZAG_LINEAR_OFFSET":
                    animation.Kind = W3DTextureAnimationKind.ZigZagLinearOffset;
                    animation.UPerSec = GetIniFloat(args, "UPerSec", 0f);
                    animation.VPerSec = GetIniFloat(args, "VPerSec", 0f);
                    animation.PeriodSeconds = Math.Abs(GetIniFloat(args, "Period", 0f));
                    return animation;

                case "ROTATE":
                    animation.Kind = W3DTextureAnimationKind.Rotate;
                    animation.IsSupported = false;
                    animation.UnsupportedReason = "Rotate mapper requires full UV rotation, which the current Unity material path does not expose.";
                    animation.UScale = GetIniFloat(args, "UScale", 1f);
                    animation.VScale = GetIniFloat(args, "VScale", 1f);
                    animation.RotationSpeedHz = GetIniFloat(args, "Speed", 0.1f);
                    animation.UCenter = GetIniFloat(args, "UCenter", 0f);
                    animation.VCenter = GetIniFloat(args, "VCenter", 0f);
                    return animation;

                case "RANDOM":
                    animation.Kind = W3DTextureAnimationKind.Random;
                    animation.IsSupported = false;
                    animation.UnsupportedReason = "Random mapper includes rotation in the original WW3D runtime and is not implemented in the Unity material path.";
                    animation.FramesPerSecond = GetIniFloat(args, "FPS", 0f);
                    animation.UPerSec = GetIniFloat(args, "UPerSec", 0f);
                    animation.VPerSec = GetIniFloat(args, "VPerSec", 0f);
                    return animation;

                case "SCREEN":
                    animation.Kind = W3DTextureAnimationKind.Screen;
                    animation.IsSupported = false;
                    animation.UnsupportedReason = "Screen mapper depends on camera-space projection and needs shader support that is not yet implemented.";
                    animation.UScale = GetIniFloat(args, "UScale", 1f);
                    animation.VScale = GetIniFloat(args, "VScale", 1f);
                    animation.UPerSec = GetIniFloat(args, "UPerSec", 0f);
                    animation.VPerSec = GetIniFloat(args, "VPerSec", 0f);
                    return animation;

                default:
                    animation.Kind = W3DTextureAnimationKind.Unsupported;
                    animation.IsSupported = false;
                    animation.UnsupportedReason = "Mapper is not part of the Unity texture-animation implementation.";
                    return animation;
            }
        }

        private static W3DTextureBinding ResolveExplicitTextureBinding(W3DMesh w3dMesh, int materialIndex)
        {
            string textureName = w3dMesh?.EffectPass?.Texture;
            if (string.IsNullOrWhiteSpace(textureName))
                textureName = w3dMesh?.Surface?.ColorMap;

            if (string.IsNullOrWhiteSpace(textureName))
                return null;

            int textureIndex = FindTextureIndexByName(w3dMesh, textureName);
            return new W3DTextureBinding
            {
                MaterialIndex = materialIndex,
                TextureIndex = textureIndex,
                Texture = textureIndex >= 0 && textureIndex < w3dMesh.Textures.Count ? w3dMesh.Textures[textureIndex] : null,
                ExplicitTextureName = textureName
            };
        }

        private static W3DTextureBinding ResolveFirstTextureBinding(W3DMesh w3dMesh, int materialIndex)
        {
            var pass = ResolveMaterialPass(w3dMesh, materialIndex, out int passIndex);
            if (pass != null)
            {
                for (int stageIndex = 0; stageIndex < pass.TxStages.Count; stageIndex++)
                {
                    var binding = ResolveStageTextureBinding(w3dMesh, materialIndex, stageIndex);
                    if (binding != null)
                        return binding;
                }

                // The pass defines its textures; texture id -1 (or no TEXTURE_IDS) means the
                // client draws these faces untextured, so don't borrow another texture.
                if (pass.TxStages.Count > 0)
                    return null;
            }

            if (materialIndex >= 0 && materialIndex < w3dMesh.Textures.Count)
            {
                return new W3DTextureBinding
                {
                    MaterialIndex = materialIndex,
                    PassIndex = passIndex,
                    TextureIndex = materialIndex,
                    Texture = w3dMesh.Textures[materialIndex]
                };
            }

            if (w3dMesh.Textures.Count > 0)
            {
                return new W3DTextureBinding
                {
                    MaterialIndex = materialIndex,
                    PassIndex = passIndex,
                    TextureIndex = 0,
                    Texture = w3dMesh.Textures[0]
                };
            }

            return null;
        }

        public static W3DTextureBinding ResolveStageTextureBinding(W3DMesh w3dMesh, int materialIndex, int stageIndex)
        {
            if (stageIndex < 0)
                return null;

            var pass = ResolveMaterialPass(w3dMesh, materialIndex, out int passIndex);
            if (pass == null || stageIndex >= pass.TxStages.Count)
                return null;

            int textureIndex = FaceTextureId(w3dMesh, pass, stageIndex, RepresentativeFace(w3dMesh, materialIndex));
            if (textureIndex < 0)
                return null;

            return new W3DTextureBinding
            {
                MaterialIndex = materialIndex,
                PassIndex = passIndex,
                StageIndex = stageIndex,
                TextureIndex = textureIndex,
                Texture = w3dMesh.Textures[textureIndex]
            };
        }

        // Splits the mesh by (vertex material, stage 0 texture, stage 1 texture, shader) of the
        // base pass. Returns false when every triangle shares one binding (single material).
        public static bool TryResolveFaceMaterialBindings(
            W3DMesh w3dMesh,
            int materialIndex,
            out List<int> faceBindingIndices,
            out List<W3DFaceMaterialBinding> usedBindings)
        {
            faceBindingIndices = null;
            usedBindings = null;

            var cache = GetFaceBindings(w3dMesh);
            if (cache == null || cache.Bindings.Count <= 1)
                return false;

            faceBindingIndices = cache.FaceIndices;
            usedBindings = cache.Bindings;
            return true;
        }

        private static W3DTextureBinding ResolvePromotedAnimatedBinding(
            W3DTextureBinding defaultBinding,
            List<W3DResolvedTextureAnimation> animations)
        {
            if (defaultBinding == null || animations == null || animations.Count == 0)
                return null;

            for (int i = 0; i < animations.Count; i++)
            {
                var animation = animations[i];
                if (animation == null || !animation.IsSupported)
                    continue;

                if (BindingsMatch(defaultBinding, animation.Binding))
                    return null;
            }

            // Preserve the common WW3D rule that stage 0 is the visible base texture.
            // Only promote an animated stage when stage 0 is clearly a generic glow-style
            // placeholder and the animated stage carries the actual atlas/screen content.
            if (!LooksLikePlaceholderOverlayTexture(defaultBinding.TextureName))
                return null;

            for (int i = 0; i < animations.Count; i++)
            {
                var animation = animations[i];
                if (animation == null || !animation.IsSupported || animation.Binding == null)
                    continue;

                if (!CanPromoteAnimatedBinding(defaultBinding, animation))
                    continue;

                return animation.Binding;
            }

            return null;
        }

        private static bool CanPromoteAnimatedBinding(W3DTextureBinding defaultBinding, W3DResolvedTextureAnimation animation)
        {
            if (defaultBinding == null || animation?.Binding == null)
                return false;

            if (BindingsMatch(defaultBinding, animation.Binding))
                return false;

            if (animation.StageIndex <= defaultBinding.StageIndex)
                return false;

            if (LooksLikePlaceholderOverlayTexture(animation.TextureName))
                return false;

            switch (animation.Kind)
            {
                case W3DTextureAnimationKind.GridAtlas:
                case W3DTextureAnimationKind.FrameSequence:
                    return true;
                default:
                    return false;
            }
        }

        private static bool LooksLikePlaceholderOverlayTexture(string textureName)
        {
            string normalized = NormalizeTextureName(textureName);
            if (normalized.Length == 0)
                return false;

            return normalized == "GLOW" ||
                   normalized.StartsWith("GLOW", StringComparison.Ordinal) ||
                   normalized.StartsWith("FLARE", StringComparison.Ordinal) ||
                   normalized.StartsWith("LENS", StringComparison.Ordinal);
        }

        private static bool BindingsMatch(W3DTextureBinding left, W3DTextureBinding right)
        {
            if (left == null || right == null)
                return false;

            if (left.StageIndex >= 0 && right.StageIndex >= 0 && left.StageIndex != right.StageIndex)
                return false;

            if (left.TextureIndex >= 0 && right.TextureIndex >= 0)
                return left.TextureIndex == right.TextureIndex;

            string leftName = NormalizeTextureName(left.TextureName);
            string rightName = NormalizeTextureName(right.TextureName);
            if (leftName.Length == 0 || rightName.Length == 0)
                return false;

            return leftName == rightName;
        }

        private static int GetStageMapping(uint attributes, int stageIndex)
        {
            return stageIndex == 0
                ? (int)(attributes & Stage0Mask)
                : (int)(attributes & Stage1Mask);
        }

        private static string GetMapperName(int stageIndex, int rawMapping)
        {
            if (stageIndex == 0)
            {
                switch (rawMapping)
                {
                    case Stage0Screen:
                        return "SCREEN";
                    case Stage0LinearOffset:
                        return "LINEAR_OFFSET";
                    case Stage0Scale:
                        return "SCALE";
                    case Stage0Grid:
                        return "GRID";
                    case Stage0Rotate:
                        return "ROTATE";
                    case Stage0SineLinearOffset:
                        return "SINE_LINEAR_OFFSET";
                    case Stage0StepLinearOffset:
                        return "STEP_LINEAR_OFFSET";
                    case Stage0ZigZagLinearOffset:
                        return "ZIGZAG_LINEAR_OFFSET";
                    case Stage0Random:
                        return "RANDOM";
                    case Stage0Edge:
                        return "EDGE";
                    case Stage0BumpEnv:
                        return "BUMPENV";
                }

                if (rawMapping >= Stage0EnvironmentMin && rawMapping <= Stage0EnvironmentMax)
                    return "ENVIRONMENT_FAMILY";
            }
            else
            {
                switch (rawMapping)
                {
                    case Stage1Screen:
                        return "SCREEN";
                    case Stage1LinearOffset:
                        return "LINEAR_OFFSET";
                    case Stage1Scale:
                        return "SCALE";
                    case Stage1Grid:
                        return "GRID";
                    case Stage1Rotate:
                        return "ROTATE";
                    case Stage1SineLinearOffset:
                        return "SINE_LINEAR_OFFSET";
                    case Stage1StepLinearOffset:
                        return "STEP_LINEAR_OFFSET";
                    case Stage1ZigZagLinearOffset:
                        return "ZIGZAG_LINEAR_OFFSET";
                    case Stage1Random:
                        return "RANDOM";
                    case Stage1Edge:
                        return "EDGE";
                    case Stage1BumpEnv:
                        return "BUMPENV";
                }

                if (rawMapping >= Stage1EnvironmentMin && rawMapping <= Stage1EnvironmentMax)
                    return "ENVIRONMENT_FAMILY";
            }

            return string.Empty;
        }

        private static int GetIniInt(string text, string key, int fallback)
        {
            if (TryGetIniValue(text, key, out string value) &&
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private static float GetIniFloat(string text, string key, float fallback)
        {
            if (TryGetIniValue(text, key, out string value) &&
                float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private static bool TryGetIniValue(string text, string key, out string value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var lines = text.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                    continue;

                int equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;

                string candidateKey = line.Substring(0, equals).Trim();
                if (!string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase))
                    continue;

                value = line.Substring(equals + 1).Trim();
                return true;
            }

            return false;
        }
    }
}
