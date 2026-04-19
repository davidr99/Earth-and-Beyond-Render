using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using W3DImporter.Structs.Mesh;

namespace W3DImporter.Builders
{
    public static class W3DMeshBuilder
    {
        public static GameObject Build(W3DMesh w3dMesh, W3DTextureResolver textureResolver)
        {
            var go = new GameObject(w3dMesh.Name);

            if (ShouldDebugAnimation(w3dMesh))
                LogAnimationDiagnostics(w3dMesh);

            if (w3dMesh.EffectiveHidden || IsNonRenderableMarker(w3dMesh.Name))
                go.SetActive(false);

            var mesh = new UnityEngine.Mesh();
            mesh.name = w3dMesh.Name;

            if (w3dMesh.Verts.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            var vertices = new Vector3[w3dMesh.Verts.Count];
            for (int i = 0; i < w3dMesh.Verts.Count; i++)
                vertices[i] = W3DCoordinateConverter.ConvertPosition(w3dMesh.Verts[i]);
            mesh.vertices = vertices;

            if (w3dMesh.Normals.Count == w3dMesh.Verts.Count)
            {
                var normals = new Vector3[w3dMesh.Normals.Count];
                for (int i = 0; i < w3dMesh.Normals.Count; i++)
                    normals[i] = W3DCoordinateConverter.ConvertNormal(w3dMesh.Normals[i]);
                mesh.normals = normals;
            }

            ApplyTriangles(mesh, w3dMesh);

            SetUVs(mesh, w3dMesh);
            SetVertexColors(mesh, w3dMesh);

            if (mesh.normals == null || mesh.normals.Length == 0)
                mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            var materials = W3DMaterialBuilder.Build(w3dMesh, textureResolver);
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = w3dMesh.CastsShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = w3dMesh.CastsShadow;
            AttachResolvedTextureAnimations(go, w3dMesh, textureResolver, materials);
            AttachUvScroller(go, w3dMesh);
            AttachBillboard(go, w3dMesh);

            Debug.Log($"[W3D] Mesh '{w3dMesh.Name}': verts={mesh.vertexCount}, tris={mesh.triangles.Length/3}, " +
                      $"uvs={mesh.uv?.Length ?? 0}, materials={materials.Length}, " +
                      $"textures={w3dMesh.Textures.Count}, passes={w3dMesh.MaterialPasses.Count}, " +
                      $"camAligned={w3dMesh.IsCameraAligned}, camOriented={w3dMesh.IsCameraOriented}");
            for (int i = 0; i < materials.Length; i++)
            {
                var tex = materials[i]?.mainTexture;
                Debug.Log($"  mat[{i}] shader='{materials[i]?.shader?.name}' texture='{tex?.name ?? "NULL"}' " +
                         $"size={tex?.width ?? 0}x{tex?.height ?? 0}");
            }

            return go;
        }

        private static void ApplyTriangles(UnityEngine.Mesh mesh, W3DMesh w3dMesh)
        {
            if (W3DTextureAnimationResolver.TryResolveFaceMaterialBindings(w3dMesh, 0, out var faceBindingIndices, out var usedBindings))
            {
                var submeshTriangles = new List<int>[usedBindings.Count];
                for (int i = 0; i < usedBindings.Count; i++)
                    submeshTriangles[i] = new List<int>();

                for (int i = 0; i < w3dMesh.Triangles.Count; i++)
                {
                    int submeshIndex = (i < faceBindingIndices.Count) ? faceBindingIndices[i] : 0;
                    if (submeshIndex < 0 || submeshIndex >= submeshTriangles.Length)
                        submeshIndex = 0;

                    var tri = w3dMesh.Triangles[i];
                    var indices = submeshTriangles[submeshIndex];
                    indices.Add((int)tri.VertIds[0]);
                    indices.Add((int)tri.VertIds[1]);
                    indices.Add((int)tri.VertIds[2]);
                }

                mesh.subMeshCount = usedBindings.Count;
                for (int i = 0; i < submeshTriangles.Length; i++)
                    mesh.SetTriangles(submeshTriangles[i], i, false);

                Debug.Log($"[W3D] Mesh '{w3dMesh.Name}': split into {usedBindings.Count} submeshes from per-face face-material bindings");
                return;
            }

            var triangles = new int[w3dMesh.Triangles.Count * 3];
            for (int i = 0; i < w3dMesh.Triangles.Count; i++)
            {
                triangles[i * 3 + 0] = (int)w3dMesh.Triangles[i].VertIds[0];
                triangles[i * 3 + 1] = (int)w3dMesh.Triangles[i].VertIds[1];
                triangles[i * 3 + 2] = (int)w3dMesh.Triangles[i].VertIds[2];
            }
            mesh.triangles = triangles;
        }

        // E&B uses "D_LIGHT##" mesh-name markers for dynamic light placement at runtime.
        // The mesh itself is a proxy/placeholder geometry and shouldn't render in the scene.
        private static bool IsNonRenderableMarker(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.StartsWith("D_LIGHT", System.StringComparison.OrdinalIgnoreCase);
        }

        private static void AttachResolvedTextureAnimations(GameObject go, W3DMesh w3dMesh, W3DTextureResolver textureResolver, Material[] materials)
        {
            if (materials == null || materials.Length == 0)
                return;

            var textureSlots = new List<W3DAnimatedTexture.Slot>();
            var transformSlots = new List<W3DTextureTransformAnimator.Slot>();

            for (int matIndex = 0; matIndex < materials.Length; matIndex++)
            {
                var material = materials[matIndex];
                var resolvedAnimations = W3DTextureAnimationResolver.ResolveTextureAnimations(w3dMesh, matIndex);
                if (resolvedAnimations == null || resolvedAnimations.Count == 0)
                    continue;

                for (int animIndex = 0; animIndex < resolvedAnimations.Count; animIndex++)
                {
                    var resolved = resolvedAnimations[animIndex];
                    if (resolved == null)
                        continue;

                    if (ShouldDebugAnimation(w3dMesh))
                        Debug.Log($"[W3D AnimResolve] {DescribeAnimation(w3dMesh, resolved)}");

                    if (!resolved.IsSupported)
                    {
                        Debug.LogWarning($"[W3D AnimResolve] '{w3dMesh.Name}' mat={matIndex}: mapper='{resolved.MapperName}' unsupported ({resolved.UnsupportedReason})");
                        continue;
                    }

                    string texturePropertyName = ResolveTexturePropertyName(material, resolved.StageIndex);
                    if (string.IsNullOrWhiteSpace(texturePropertyName))
                    {
                        Debug.LogWarning($"[W3D AnimResolve] '{w3dMesh.Name}' mat={matIndex} stage={resolved.StageIndex}: no material texture property available");
                        continue;
                    }

                    if (resolved.StageIndex == 1 && !IsSecondaryStageEnabled(material))
                        continue;

                    if (material == null || !material.HasProperty(texturePropertyName) || material.GetTexture(texturePropertyName) == null)
                    {
                        Debug.LogWarning($"[W3D AnimResolve] '{w3dMesh.Name}' mat={matIndex} stage={resolved.StageIndex}: texture property '{texturePropertyName}' is not bound");
                        continue;
                    }

                    bool isMainTextureProperty = IsMainTextureProperty(texturePropertyName);
                    if (resolved.Kind == W3DTextureAnimationKind.FrameSequence)
                    {
                        var frames = textureResolver.ResolveAnimation(resolved.TextureName, (uint)Mathf.Max(1, resolved.TextureFrameCount));
                        if (frames == null || frames.Length <= 1)
                        {
                            Debug.LogWarning($"[W3D AnimTex] '{w3dMesh.Name}' mat={matIndex}: texture='{resolved.TextureName}' resolved no usable frame sequence from frameCount={resolved.TextureFrameCount}");
                            continue;
                        }

                        textureSlots.Add(new W3DAnimatedTexture.Slot
                        {
                            MaterialIndex = matIndex,
                            TexturePropertyName = texturePropertyName,
                            IsMainTextureProperty = isMainTextureProperty,
                            Frames = frames,
                            AnimType = resolved.TextureAnimType,
                            FrameRate = resolved.TextureFrameRate
                        });
                        continue;
                    }

                    if (resolved.UsesUvTransform)
                    {
                        transformSlots.Add(new W3DTextureTransformAnimator.Slot
                        {
                            MaterialIndex = matIndex,
                            TexturePropertyName = texturePropertyName,
                            Label = resolved.TextureName,
                            Kind = resolved.Kind,
                            GridWidth = resolved.GridWidth,
                            FrameCount = resolved.FrameCount,
                            FramesPerSecond = resolved.FramesPerSecond,
                            FrameOffset = resolved.FrameOffset,
                            UScale = resolved.UScale,
                            VScale = resolved.VScale,
                            UPerSec = resolved.UPerSec,
                            VPerSec = resolved.VPerSec,
                            UAmplitude = resolved.UAmplitude,
                            UFrequency = resolved.UFrequency,
                            UPhase = resolved.UPhase,
                            VAmplitude = resolved.VAmplitude,
                            VFrequency = resolved.VFrequency,
                            VPhase = resolved.VPhase,
                            StepU = resolved.StepU,
                            StepV = resolved.StepV,
                            StepsPerSecond = resolved.StepsPerSecond,
                            PeriodSeconds = resolved.PeriodSeconds
                        });
                    }
                }
            }

            if (textureSlots.Count > 0)
            {
                var animator = go.AddComponent<W3DAnimatedTexture>();
                animator.Slots = textureSlots.ToArray();
            }

            if (transformSlots.Count > 0)
            {
                var animator = go.AddComponent<W3DTextureTransformAnimator>();
                animator.Slots = transformSlots.ToArray();
            }
        }

        private static string DescribeAnimation(W3DMesh w3dMesh, W3DResolvedTextureAnimation animation)
        {
            string args = string.IsNullOrWhiteSpace(animation.Args)
                ? ""
                : $" args='{SanitizeForLog(animation.Args)}'";

            return $"'{w3dMesh.Name}' mat={animation.MaterialIndex} stage={animation.StageIndex} tex='{animation.TextureName}' " +
                   $"kind={animation.Kind} source={animation.Source} mapper='{animation.MapperName}' " +
                   $"fps={animation.FramesPerSecond} frameCount={animation.FrameCount} frameOffset={animation.FrameOffset}{args}";
        }

        private static string ResolveTexturePropertyName(Material material, int stageIndex)
        {
            if (material == null)
                return string.Empty;

            if (stageIndex == 1 && material.HasProperty("_DetailTex"))
                return "_DetailTex";
            if (material.HasProperty("_UnlitColorMap"))
                return "_UnlitColorMap";
            if (material.HasProperty("_BaseColorMap"))
                return "_BaseColorMap";
            if (material.HasProperty("_BaseMap"))
                return "_BaseMap";
            if (material.HasProperty("_MainTex"))
                return "_MainTex";

            return string.Empty;
        }

        private static bool IsMainTextureProperty(string texturePropertyName)
        {
            return string.Equals(texturePropertyName, "_UnlitColorMap", System.StringComparison.Ordinal)
                || string.Equals(texturePropertyName, "_BaseColorMap", System.StringComparison.Ordinal)
                || string.Equals(texturePropertyName, "_BaseMap", System.StringComparison.Ordinal)
                || string.Equals(texturePropertyName, "_MainTex", System.StringComparison.Ordinal);
        }

        private static bool IsSecondaryStageEnabled(Material material)
        {
            if (material == null)
                return false;

            float detailColorFunc = material.HasProperty("_DetailColorFunc") ? material.GetFloat("_DetailColorFunc") : 0f;
            float detailAlphaFunc = material.HasProperty("_DetailAlphaFunc") ? material.GetFloat("_DetailAlphaFunc") : 0f;
            return detailColorFunc > 0.5f || detailAlphaFunc > 0.5f;
        }

        private static void AttachUvScroller(GameObject go, W3DMesh w3dMesh)
        {
            var effect = w3dMesh.EffectPass;
            if (effect?.CycleTime == null || effect.CycleTime <= 0f)
                return;

            if (go.GetComponent<W3DTextureTransformAnimator>() != null)
                return;

            var scroller = go.AddComponent<W3DUVScroller>();
            scroller.CycleTime = effect.CycleTime.Value;
            scroller.StartV = effect.StartVCoord ?? 0f;
            scroller.EndV = effect.EndVCoord ?? 1f;
        }

        private static void AttachAnimatedTextures(GameObject go, W3DMesh w3dMesh, W3DTextureResolver textureResolver, Material[] materials)
        {
            if (materials == null || materials.Length == 0 || w3dMesh.Textures.Count == 0)
                return;

            var slots = new List<W3DAnimatedTexture.Slot>();
            for (int matIndex = 0; matIndex < materials.Length; matIndex++)
            {
                var textureInfo = GetAnimatedTextureInfo(w3dMesh, matIndex);
                if (textureInfo == null || !textureInfo.IsAnimated)
                    continue;

                var frames = textureResolver.ResolveAnimation(textureInfo.File, textureInfo.FrameCount);
                if (frames == null || frames.Length <= 1)
                {
                    Debug.LogWarning($"[W3D AnimTex] '{w3dMesh.Name}': texture='{textureInfo.File}' declared animated but resolved frameCount={(frames == null ? 0 : frames.Length)} from sourceFrameCount={textureInfo.FrameCount}");
                    continue;
                }

                slots.Add(new W3DAnimatedTexture.Slot
                {
                    MaterialIndex = matIndex,
                    Frames = frames,
                    AnimType = textureInfo.AnimType,
                    FrameRate = textureInfo.FrameRate
                });

                Debug.Log($"[W3D] Animated texture '{textureInfo.File}' on '{w3dMesh.Name}': frames={frames.Length} animType={textureInfo.AnimType} fps={textureInfo.FrameRate}");
            }

            if (slots.Count == 0)
            {
                Debug.Log($"[W3D AnimTex] '{w3dMesh.Name}': no animated texture slots attached");
                LogAnimationDiagnostics(w3dMesh);
                return;
            }

            var animator = go.AddComponent<W3DAnimatedTexture>();
            animator.Slots = slots.ToArray();
            Debug.Log($"[W3D AnimTex] '{w3dMesh.Name}': attached animated-texture component with {slots.Count} slot(s)");
        }

        private static void AttachAtlasAnimators(GameObject go, W3DMesh w3dMesh, Material[] materials)
        {
            if (materials == null || materials.Length == 0)
                return;

            var slots = new List<W3DAtlasAnimator.Slot>();
            for (int matIndex = 0; matIndex < materials.Length; matIndex++)
            {
                var config = GetGridMapperConfig(w3dMesh, matIndex);
                if (config == null)
                {
                    Debug.Log($"[W3D Atlas] '{w3dMesh.Name}': mat={matIndex} has no atlas config");
                    continue;
                }

                slots.Add(new W3DAtlasAnimator.Slot
                {
                    MaterialIndex = matIndex,
                    GridWidth = config.GridWidth,
                    FrameCount = config.FrameCount,
                    FramesPerSecond = config.FramesPerSecond,
                    FrameOffset = config.FrameOffset
                });

                Debug.Log($"[W3D] Atlas mapper on '{w3dMesh.Name}' mat={matIndex}: grid={config.GridWidth} fps={config.FramesPerSecond} frames={config.FrameCount}");
            }

            if (slots.Count == 0)
            {
                Debug.Log($"[W3D Atlas] '{w3dMesh.Name}': no atlas animator slots attached");
                LogAnimationDiagnostics(w3dMesh);
                return;
            }

            var animator = go.AddComponent<W3DAtlasAnimator>();
            animator.Slots = slots.ToArray();
            Debug.Log($"[W3D Atlas] '{w3dMesh.Name}': attached atlas animator with {slots.Count} slot(s)");
        }

        private static W3DTexture GetAnimatedTextureInfo(W3DMesh w3dMesh, int matIndex)
        {
            string explicitTextureName = w3dMesh.EffectPass?.Texture;
            if (string.IsNullOrWhiteSpace(explicitTextureName))
                explicitTextureName = w3dMesh.Surface?.ColorMap;

            if (!string.IsNullOrWhiteSpace(explicitTextureName))
            {
                string normalized = NormalizeTextureName(explicitTextureName);
                for (int i = 0; i < w3dMesh.Textures.Count; i++)
                {
                    var texture = w3dMesh.Textures[i];
                    if (NormalizeTextureName(texture.File) == normalized)
                        return texture;
                }
            }

            int texIdx = -1;
            int preferredStage = GetPreferredTextureStage(w3dMesh, matIndex);
            if (w3dMesh.MaterialPasses.Count > 0)
            {
                var pass = w3dMesh.MaterialPasses[0];
                if (preferredStage >= 0 && preferredStage < pass.TxStages.Count &&
                    pass.TxStages[preferredStage].TxIds.Count > 0)
                {
                    var txIds = pass.TxStages[preferredStage].TxIds[0];
                    if (txIds.Count > 0)
                        texIdx = txIds[0];
                }
            }

            if (texIdx < 0 && matIndex < w3dMesh.Textures.Count)
                texIdx = matIndex;

            if (texIdx < 0 || texIdx >= w3dMesh.Textures.Count)
                return null;

            return w3dMesh.Textures[texIdx];
        }

        private static string NormalizeTextureName(string textureName)
        {
            if (string.IsNullOrWhiteSpace(textureName))
                return string.Empty;

            return System.IO.Path.GetFileNameWithoutExtension(textureName).Trim().ToUpperInvariant();
        }

        private sealed class GridMapperConfig
        {
            public int GridWidth;
            public int FrameCount;
            public float FramesPerSecond;
            public int FrameOffset;
        }

        private static GridMapperConfig GetGridMapperConfig(W3DMesh w3dMesh, int matIndex)
        {
            var shaderConfig = GetShaderMaterialGridMapperConfig(w3dMesh, matIndex);
            if (shaderConfig != null)
                return shaderConfig;

            var vm = GetVertexMaterialForMaterial(w3dMesh, matIndex);
            if (vm?.Info == null)
            {
                Debug.Log($"[W3D Atlas] '{w3dMesh.Name}': mat={matIndex} has no vertex material info");
                return null;
            }

            int preferredStage = GetPreferredTextureStage(w3dMesh, matIndex);
            string args = preferredStage == 1 ? vm.VmArgs1 : vm.VmArgs0;
            if (string.IsNullOrWhiteSpace(args))
            {
                Debug.Log($"[W3D Atlas] '{w3dMesh.Name}': mat={matIndex} vm args empty for preferredStage={preferredStage}");
                return null;
            }

            int mapping = preferredStage == 1
                ? (int)(vm.Info.Attributes & 0x0000FF00u)
                : (int)(vm.Info.Attributes & 0x00FF0000u);

            bool isGrid = preferredStage == 1 ? mapping == 0x00000700 : mapping == 0x00070000;
            if (!isGrid)
            {
                Debug.Log($"[W3D Atlas] '{w3dMesh.Name}': mat={matIndex} mapping=0x{mapping:X8} is not grid");
                return null;
            }

            int log2Width = GetIniInt(args, "Log2Width", 1);
            int gridWidth = Mathf.Max(1, 1 << log2Width);
            int maxFrames = gridWidth * gridWidth;
            int last = GetIniInt(args, "Last", 0);
            float fps = GetIniFloat(args, "FPS", 1f);
            int frameCount = last <= 0 ? maxFrames : Mathf.Clamp(last, 1, maxFrames);

            return new GridMapperConfig
            {
                GridWidth = gridWidth,
                FrameCount = frameCount,
                FramesPerSecond = fps,
                FrameOffset = 0
            };
        }

        private static GridMapperConfig GetShaderMaterialGridMapperConfig(W3DMesh w3dMesh, int matIndex)
        {
            var shaderMaterial = GetShaderMaterialForMaterial(w3dMesh, matIndex);
            if (shaderMaterial?.Properties == null || shaderMaterial.Properties.Count == 0)
                return null;

            for (int i = 0; i < shaderMaterial.Properties.Count; i++)
            {
                var property = shaderMaterial.Properties[i];
                if (!string.Equals(property.Name, "TextureAnimation_FPS_NumPerRow_LastFrame_FrameOffset_0", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!property.TryGetVector4(out Vector4 value))
                    continue;

                int numPerRow = Mathf.Max(1, Mathf.RoundToInt(value.y));
                int lastFrame = Mathf.Max(0, Mathf.RoundToInt(value.z));
                int frameOffset = Mathf.Max(0, Mathf.RoundToInt(value.w));
                float fps = value.x;
                Debug.Log($"[W3D Atlas] '{w3dMesh.Name}': shader material property '{property.Name}' vec4={value} on mat={matIndex}");

                return new GridMapperConfig
                {
                    GridWidth = numPerRow,
                    FrameCount = Mathf.Max(1, lastFrame + 1),
                    FramesPerSecond = Mathf.Abs(fps) > 0.001f ? fps : 1f,
                    FrameOffset = frameOffset
                };
            }

            return null;
        }

        private static int GetPreferredTextureStage(W3DMesh w3dMesh, int matIndex)
        {
            var binding = W3DTextureAnimationResolver.ResolvePrimaryTextureBinding(w3dMesh, matIndex);
            if (binding != null && binding.StageIndex >= 0)
                return binding.StageIndex;

            return 0;
        }

        private static W3DVertexMaterial GetVertexMaterialForMaterial(W3DMesh w3dMesh, int matIndex)
        {
            var resolved = W3DTextureAnimationResolver.ResolveVertexMaterial(w3dMesh, matIndex);
            if (resolved != null)
                return resolved;

            if (matIndex >= 0 && matIndex < w3dMesh.VertMaterials.Count)
                return w3dMesh.VertMaterials[matIndex];

            return null;
        }

        private static W3DShaderMaterial GetShaderMaterialForMaterial(W3DMesh w3dMesh, int matIndex)
        {
            var resolved = W3DTextureAnimationResolver.ResolveShaderMaterial(w3dMesh, matIndex);
            if (resolved != null)
                return resolved;

            if (matIndex >= 0 && matIndex < w3dMesh.ShaderMaterials.Count)
                return w3dMesh.ShaderMaterials[matIndex];

            return null;
        }

        private static int GetIniInt(string text, string key, int fallback)
        {
            if (TryGetIniValue(text, key, out string value) &&
                int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private static float GetIniFloat(string text, string key, float fallback)
        {
            if (TryGetIniValue(text, key, out string value) &&
                float.TryParse(value, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out float parsed))
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
                if (!string.Equals(candidateKey, key, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                value = line.Substring(equals + 1).Trim();
                return true;
            }

            return false;
        }

        private static void LogAnimationDiagnostics(W3DMesh w3dMesh)
        {
            Debug.Log($"[W3D AnimDiag] '{w3dMesh.Name}': textures={w3dMesh.Textures.Count} vertMats={w3dMesh.VertMaterials.Count} shaderMats={w3dMesh.ShaderMaterials.Count} passes={w3dMesh.MaterialPasses.Count}");

            for (int i = 0; i < w3dMesh.Textures.Count; i++)
            {
                var tex = w3dMesh.Textures[i];
                Debug.Log($"[W3D AnimDiag] '{w3dMesh.Name}': tex[{i}] file='{tex.File}' texInfo=0x{tex.TextureInfo:X8} animType={tex.AnimType} frameCount={tex.FrameCount} frameRate={tex.FrameRate}");
            }

            for (int i = 0; i < w3dMesh.VertMaterials.Count; i++)
            {
                var vm = w3dMesh.VertMaterials[i];
                uint attrs = vm.Info != null ? vm.Info.Attributes : 0u;
                Debug.Log($"[W3D AnimDiag] '{w3dMesh.Name}': vertMat[{i}] name='{vm.Name}' attrs=0x{attrs:X8} vmArgs0='{SanitizeForLog(vm.VmArgs0)}' vmArgs1='{SanitizeForLog(vm.VmArgs1)}'");
            }

            for (int i = 0; i < w3dMesh.ShaderMaterials.Count; i++)
            {
                var shaderMat = w3dMesh.ShaderMaterials[i];
                Debug.Log($"[W3D AnimDiag] '{w3dMesh.Name}': shaderMat[{i}] type='{shaderMat.TypeName}' technique={shaderMat.Technique} props={shaderMat.Properties.Count}");
                for (int p = 0; p < shaderMat.Properties.Count; p++)
                {
                    var prop = shaderMat.Properties[p];
                    string valueString;
                    if (prop.Type == W3DShaderMaterialPropertyType.String)
                        valueString = prop.StringValue;
                    else if (prop.Type == W3DShaderMaterialPropertyType.Float)
                        valueString = prop.FloatValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    else if (prop.Type == W3DShaderMaterialPropertyType.Int)
                        valueString = prop.IntValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    else if (prop.Type == W3DShaderMaterialPropertyType.Bool)
                        valueString = prop.BoolValue ? "true" : "false";
                    else
                        valueString = prop.VectorValue.ToString();

                    Debug.Log($"[W3D AnimDiag] '{w3dMesh.Name}': shaderMat[{i}].prop[{p}] type={prop.Type} name='{prop.Name}' value='{valueString}'");
                }
            }

            for (int i = 0; i < w3dMesh.MaterialPasses.Count; i++)
            {
                var pass = w3dMesh.MaterialPasses[i];
                Debug.Log($"[W3D AnimDiag] '{w3dMesh.Name}': pass[{i}] vmIds={JoinInts(pass.VertexMaterialIds)} shaderIds={JoinInts(pass.ShaderIds)} shaderMatIds={JoinInts(pass.ShaderMaterialIds)} txStages={pass.TxStages.Count} passTxCoords={pass.TxCoords.Count}");
                for (int s = 0; s < pass.TxStages.Count; s++)
                {
                    var stage = pass.TxStages[s];
                    string txIds = stage.TxIds.Count > 0 ? JoinInts(stage.TxIds[0]) : "";
                    int txCoordCount = stage.TxCoords.Count > 0 ? stage.TxCoords[0].Count : 0;
                    Debug.Log($"[W3D AnimDiag] '{w3dMesh.Name}': pass[{i}].stage[{s}] txIds={txIds} txCoordCount={txCoordCount}");
                }
            }

            for (int i = 0; i < Mathf.Max(1, w3dMesh.VertMaterials.Count); i++)
            {
                var resolved = W3DTextureAnimationResolver.ResolvePrimaryTextureAnimation(w3dMesh, i);
                if (resolved != null)
                    Debug.Log($"[W3D AnimDiag] {DescribeAnimation(w3dMesh, resolved)} supported={resolved.IsSupported}");
            }
        }

        private static string JoinInts(List<int> values)
        {
            if (values == null || values.Count == 0)
                return "";

            return string.Join(",", values);
        }

        private static string SanitizeForLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            return text.Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static bool ShouldDebugAnimation(W3DMesh w3dMesh)
        {
            if (w3dMesh == null || string.IsNullOrWhiteSpace(w3dMesh.Name))
                return false;

            return w3dMesh.Name.IndexOf("GLOW", System.StringComparison.OrdinalIgnoreCase) >= 0
                || w3dMesh.Name.IndexOf("FLARE", System.StringComparison.OrdinalIgnoreCase) >= 0
                || w3dMesh.Name.IndexOf("SCREEN", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AttachBillboard(GameObject go, W3DMesh w3dMesh)
        {
            if (w3dMesh.IsCameraOriented)
            {
                var billboard = go.AddComponent<W3DBillboard>();
                billboard.Mode = W3DBillboard.BillboardMode.CameraOriented;
                return;
            }

            if (w3dMesh.IsCameraAligned)
            {
                var billboard = go.AddComponent<W3DBillboard>();
                billboard.Mode = W3DBillboard.BillboardMode.CameraAligned;
            }
        }

        private static void SetUVs(UnityEngine.Mesh mesh, W3DMesh w3dMesh)
        {
            if (w3dMesh.DirectUVs.Count > 0)
            {
                var directUVs = new Vector2[mesh.vertexCount];
                for (int i = 0; i < mesh.vertexCount; i++)
                {
                    if (i < w3dMesh.DirectUVs.Count)
                    {
                        var uv = w3dMesh.DirectUVs[i];
                        directUVs[i] = new Vector2(uv.x, 1f - uv.y);
                    }
                    else
                    {
                        directUVs[i] = Vector2.zero;
                    }
                }

                mesh.uv = directUVs;
                return;
            }

            if (w3dMesh.MaterialPasses.Count == 0)
            {
                Debug.LogWarning($"[W3D UV] {w3dMesh.Name}: no material passes");
                return;
            }

            var pass = W3DTextureAnimationResolver.ResolveMaterialPass(w3dMesh, 0, out _);
            if (pass == null)
            {
                Debug.LogWarning($"[W3D UV] {w3dMesh.Name}: no resolved material pass");
                return;
            }
            List<Vector2> rawUVs = null;
            int preferredStage = GetPreferredTextureStage(w3dMesh, 0);

            if (preferredStage >= 0 &&
                preferredStage < pass.TxStages.Count &&
                pass.TxStages[preferredStage].TxCoords.Count > 0)
            {
                rawUVs = pass.TxStages[preferredStage].TxCoords[0];
                Debug.Log($"[W3D UV] {w3dMesh.Name}: from TxStage[{preferredStage}], {rawUVs.Count} UVs, {mesh.vertexCount} verts");
            }
            else if (pass.TxStages.Count > 0 && pass.TxStages[0].TxCoords.Count > 0)
            {
                rawUVs = pass.TxStages[0].TxCoords[0];
                Debug.Log($"[W3D UV] {w3dMesh.Name}: from TxStage[0], {rawUVs.Count} UVs, {mesh.vertexCount} verts");
            }
            else if (pass.TxCoords.Count > 0)
            {
                rawUVs = pass.TxCoords;
                Debug.Log($"[W3D UV] {w3dMesh.Name}: from pass TxCoords, {rawUVs.Count} UVs, {mesh.vertexCount} verts");
            }
            else
            {
                Debug.LogWarning($"[W3D UV] {w3dMesh.Name}: no UV coords found! " +
                                 $"stages={pass.TxStages.Count}, passCoords={pass.TxCoords.Count}");
                return;
            }

            var finalUVs = new Vector2[mesh.vertexCount];
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                if (i < rawUVs.Count)
                    finalUVs[i] = new Vector2(rawUVs[i].x, 1f - rawUVs[i].y);
                else
                    finalUVs[i] = Vector2.zero;
            }
            mesh.uv = finalUVs;

            if (rawUVs.Count != mesh.vertexCount)
                Debug.LogWarning($"[W3D UV] {w3dMesh.Name}: UV count ({rawUVs.Count}) != vertex count ({mesh.vertexCount}), padded");

            if (rawUVs.Count > 0)
                Debug.Log($"[W3D UV] {w3dMesh.Name}: sample UV[0]={rawUVs[0]}, UV[1]={(rawUVs.Count > 1 ? rawUVs[1].ToString() : "N/A")}");

            if (pass.TxStages.Count > 1 && pass.TxStages[1].TxCoords.Count > 0)
            {
                var txCoords2 = pass.TxStages[1].TxCoords[0];
                if (txCoords2 != null && txCoords2.Count > 0)
                {
                    var uv2 = new Vector2[mesh.vertexCount];
                    for (int i = 0; i < mesh.vertexCount; i++)
                    {
                        if (i < txCoords2.Count)
                        {
                            var uv = txCoords2[i];
                            uv2[i] = new Vector2(uv.x, 1f - uv.y);
                        }
                        else
                        {
                            uv2[i] = mesh.uv != null && i < mesh.uv.Length ? mesh.uv[i] : Vector2.zero;
                        }
                    }

                    mesh.uv2 = uv2;
                }
            }
        }

        private static void SetVertexColors(UnityEngine.Mesh mesh, W3DMesh w3dMesh)
        {
            if (w3dMesh.DirectVertexColors.Count > 0)
            {
                mesh.colors32 = w3dMesh.DirectVertexColors.ToArray();
                return;
            }

            if (w3dMesh.MaterialPasses.Count == 0) return;
            var pass = W3DTextureAnimationResolver.ResolveMaterialPass(w3dMesh, 0, out _);
            if (pass == null)
                return;

            if (pass.Dcg.Count > 0)
            {
                mesh.colors32 = pass.Dcg.ToArray();
            }
        }
    }
}
