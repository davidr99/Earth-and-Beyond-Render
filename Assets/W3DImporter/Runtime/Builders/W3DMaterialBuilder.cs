using System;
using UnityEngine;
using UnityEngine.Rendering;
using W3DImporter.Structs.Mesh;

namespace W3DImporter.Builders
{
    public static class W3DMaterialBuilder
    {
        private const bool EnableMaterialDiagnostics = false;
        private const uint TextureAttributeAlphaBitmap = 0x0020u;
        private const uint TextureHintMask = 0xFF00u;
        private const uint TextureHintEmissive = 0x0100u;

        private static Shader _litShader;
        private static Shader _transparentShader;
        private static Shader _additiveShader;
        private static Shader _particleShader;

        private static bool MeshHasTintedDcg(W3DMesh w3dMesh)
        {
            if (w3dMesh == null || w3dMesh.MaterialPasses.Count == 0)
                return false;

            var dcg = w3dMesh.MaterialPasses[0].Dcg;
            if (dcg == null || dcg.Count == 0)
                return false;

            for (int i = 0; i < dcg.Count; i++)
            {
                if (dcg[i].r >= 16 || dcg[i].g >= 16 || dcg[i].b >= 16)
                    return true;
            }

            return false;
        }

        private static Shader LitShader
        {
            get
            {
                if (_litShader != null) return _litShader;

                string[] shaderNames = {
                    "W3D/Opaque",
                    "HDRP/Unlit",
                    "Unlit/Texture",
                    "Sprites/Default"
                };

                foreach (var name in shaderNames)
                {
                    _litShader = Shader.Find(name);
                    if (_litShader != null)
                    {
                        Debug.Log($"[W3D] Using shader: {name}");
                        return _litShader;
                    }
                }

                Debug.LogError("[W3D] No valid shader found! Materials will be pink.");
                return null;
            }
        }

        private static Shader TransparentShader
        {
            get
            {
                if (_transparentShader != null) return _transparentShader;

                string[] shaderNames = {
                    "W3D/Transparent",
                    "HDRP/Unlit",
                    "Sprites/Default",
                    "Unlit/Transparent",
                    "Unlit/Texture"
                };

                foreach (var name in shaderNames)
                {
                    _transparentShader = Shader.Find(name);
                    if (_transparentShader != null)
                        return _transparentShader;
                }

                return LitShader;
            }
        }

        private static Shader AdditiveShader
        {
            get
            {
                if (_additiveShader != null) return _additiveShader;

                string[] shaderNames = {
                    "W3D/Additive",
                    "W3D/Transparent",
                    "HDRP/Unlit",
                    "Sprites/Default",
                    "Unlit/Transparent",
                    "Unlit/Texture"
                };

                foreach (var name in shaderNames)
                {
                    _additiveShader = Shader.Find(name);
                    if (_additiveShader != null)
                    {
                        Debug.Log($"[W3D] Using additive shader: {name}");
                        return _additiveShader;
                    }
                }

                _additiveShader = TransparentShader;
                return _additiveShader;
            }
        }

        private static Shader ParticleShader
        {
            get
            {
                if (_particleShader != null) return _particleShader;

                string[] shaderNames = {
                    "W3D/Particle",
                    "W3D/Transparent",
                    "HDRP/Unlit",
                    "Sprites/Default",
                    "Unlit/Transparent",
                    "Unlit/Texture"
                };

                foreach (var name in shaderNames)
                {
                    _particleShader = Shader.Find(name);
                    if (_particleShader != null)
                    {
                        Debug.Log($"[W3D] Using particle/screen shader: {name}");
                        return _particleShader;
                    }
                }

                _particleShader = TransparentShader;
                return _particleShader;
            }
        }

        public static Material[] Build(W3DMesh w3dMesh, W3DTextureResolver textureResolver)
        {
            if (w3dMesh.EffectPass != null || w3dMesh.Surface != null)
                return BuildFromEnbUserText(w3dMesh, textureResolver);

            if (w3dMesh.VertMaterials.Count > 0)
            {
                var materials = BuildFromVertexMaterials(w3dMesh, textureResolver);
                if (w3dMesh.PassIndex > 0)
                {
                    for (int i = 0; i < materials.Length; i++)
                        DrawAfterPreviousPasses(materials[i], w3dMesh.PassIndex);
                }
                return materials;
            }

            return new[] { CreateDefaultMaterial() };
        }

        // An additional material pass covers the same triangles as the passes before it and
        // must be drawn after them (the client draws the passes in order, depth test LEQUAL).
        // HDRP renders queues 2000-2500 (opaque) and 2900-3100 (transparent).
        private static void DrawAfterPreviousPasses(Material mat, int passIndex)
        {
            if (mat == null)
                return;

            // The CG additive / particle shaders transform vertices differently from W3D/Opaque,
            // so an overlay drawn with them z-fights with the pass below. W3D/Transparent uses
            // the same transform as W3D/Opaque and takes any blend pair.
            if (TransparentShader != null && (mat.shader == AdditiveShader || mat.shader == ParticleShader))
                MoveToShader(mat, TransparentShader);

            int queue = mat.renderQueue >= 0 ? mat.renderQueue : mat.shader.renderQueue;
            int last = queue <= 2500 ? 2500 : 3100;
            mat.renderQueue = Mathf.Min(queue + passIndex, last);
        }

        // True when Build returns one material per face binding, i.e. the mesh has to be
        // split into matching submeshes.
        public static bool UsesFaceMaterialSplit(W3DMesh w3dMesh)
        {
            return w3dMesh.EffectPass == null &&
                   w3dMesh.Surface == null &&
                   w3dMesh.VertMaterials.Count > 0 &&
                   W3DTextureAnimationResolver.TryResolveFaceMaterialBindings(w3dMesh, 0, out _, out _);
        }

        private static Material[] BuildFromVertexMaterials(W3DMesh w3dMesh, W3DTextureResolver textureResolver)
        {
            if (W3DTextureAnimationResolver.TryResolveFaceMaterialBindings(w3dMesh, 0, out _, out var usedBindings))
            {
                var splitMaterials = new Material[usedBindings.Count];
                bool useVertexColorForSplit = MeshHasTintedDcg(w3dMesh);
                for (int i = 0; i < usedBindings.Count; i++)
                {
                    splitMaterials[i] = BuildVertexMaterial(w3dMesh, i, textureResolver, useVertexColorForSplit, usedBindings[i]);
                    splitMaterials[i].name = $"{w3dMesh.Name}.faceMat{i}";
                }

                if (W3DLog.Verbose) Debug.Log($"[W3D] Built {splitMaterials.Length} split materials for '{w3dMesh.Name}' from per-face face-material bindings");
                return splitMaterials;
            }

            // Every triangle uses the same vertex material / shader / textures: one material
            // for the mesh's single submesh.
            return new[] { BuildVertexMaterial(w3dMesh, 0, textureResolver, MeshHasTintedDcg(w3dMesh)) };
        }

        private static Material BuildVertexMaterial(
            W3DMesh w3dMesh,
            int materialIndex,
            W3DTextureResolver textureResolver,
            bool useVertexColor,
            W3DFaceMaterialBinding faceBindingOverride = null)
        {
            // materialIndex is the face-binding index of a split mesh (0 otherwise); all
            // lookups below resolve the pass ids through that binding.
            var vm = faceBindingOverride != null &&
                     faceBindingOverride.VertexMaterialIndex >= 0 &&
                     faceBindingOverride.VertexMaterialIndex < w3dMesh.VertMaterials.Count
                ? w3dMesh.VertMaterials[faceBindingOverride.VertexMaterialIndex]
                : W3DTextureAnimationResolver.ResolveVertexMaterial(w3dMesh, materialIndex) ?? w3dMesh.VertMaterials[0];

            var mat = new Material(LitShader);
            mat.name = w3dMesh.Name + "." + vm.Name;
            SetOpaque(mat);

            int materialLookupIndex = materialIndex;
            int primaryTextureIndex = GetPrimaryTextureIndex(w3dMesh, materialLookupIndex);
            ApplyTexture(mat, w3dMesh, materialLookupIndex, textureResolver, primaryTextureIndex);

            if (vm.Info != null)
            {
                ApplyVertexMaterialInfo(mat, vm.Info);

                if (vm.Info.Emissive.r > 0.01f || vm.Info.Emissive.g > 0.01f || vm.Info.Emissive.b > 0.01f)
                {
                    if (mat.HasProperty("_EmissiveColor"))
                        mat.SetColor("_EmissiveColor", vm.Info.Emissive);
                    if (mat.HasProperty("_EmissionColor"))
                        mat.SetColor("_EmissionColor", vm.Info.Emissive);
                }
                if (vm.Info.Emissive.r > 0 || vm.Info.Emissive.g > 0 || vm.Info.Emissive.b > 0)
                    mat.EnableKeyword("_EMISSION");
            }

            var texInfo = primaryTextureIndex >= 0 && primaryTextureIndex < w3dMesh.Textures.Count
                ? w3dMesh.Textures[primaryTextureIndex].TextureInfo
                : 0u;
            if (EnableMaterialDiagnostics)
                Debug.Log($"[W3D MatDiag] '{mat.name}': vertMats={w3dMesh.VertMaterials.Count} shaders={w3dMesh.Shaders.Count} texInfo=0x{texInfo:X8} (hasAlphaBitmap={(texInfo & TextureAttributeAlphaBitmap) != 0} emissiveHint={(texInfo & TextureHintMask) == TextureHintEmissive}) effectPass={(w3dMesh.EffectPass != null ? w3dMesh.EffectPass.EffectPassType.ToString() : "null")}");

            string explicitTextureName = primaryTextureIndex >= 0 && primaryTextureIndex < w3dMesh.Textures.Count
                ? w3dMesh.Textures[primaryTextureIndex].File
                : null;

            // The client derives blend state solely from the W3D shader struct; when one
            // exists it is authoritative (an opaque combo intentionally stays lit/opaque).
            // Texture-name and TEXTURE_INFO heuristics are a fallback for meshes with no
            // shader data at all.
            var resolvedShader = ResolveShaderForMaterial(w3dMesh, materialLookupIndex, faceBindingOverride);
            if (resolvedShader == null && w3dMesh.Shaders.Count > 0)
            {
                int shaderIdx = Mathf.Min(materialLookupIndex, w3dMesh.Shaders.Count - 1);
                resolvedShader = w3dMesh.Shaders[shaderIdx];
            }

            if (resolvedShader != null)
            {
                ApplyBlendMode(mat, resolvedShader);
            }
            else
            {
                bool blendApplied = ApplyTextureDrivenBlendMode(mat, w3dMesh, materialLookupIndex, explicitTextureName);
                if (EnableMaterialDiagnostics)
                    Debug.Log($"[W3D MatDiag] '{mat.name}': no Shaders chunk parsed, textureDriven={blendApplied}, shaderAfter='{mat.shader?.name}'");

                if (!blendApplied && vm.Info != null && GetMaterialAlpha(vm.Info) < 0.999f)
                    SetTransparent(mat);
            }

            bool vertexColorIrrelevant = ApplyEmissiveDrivenSurface(mat, vm.Info);
            ApplyMeshFlags(mat, w3dMesh);
            int? secondaryTextureIndex = faceBindingOverride != null
                ? (faceBindingOverride.SecondaryTextureIndex >= 0 ? (int?)faceBindingOverride.SecondaryTextureIndex : null)
                : null;
            ApplySecondaryTextureStage(mat, w3dMesh, materialLookupIndex, textureResolver, resolvedShader, secondaryTextureIndex);
            SetPrimaryGradient(mat, resolvedShader);
            ApplyTexGen(mat, vm);
            // vertex colors (DCG) feed the lighting, see ApplyEmissiveDrivenSurface
            SetUseVertexColor(mat, useVertexColor && !vertexColorIrrelevant);
            return mat;
        }

        // Environment mappers: the shader generates the stage's texture coordinates from the
        // normal / reflection vector (W3DTextureAnimationResolver.GetStageTexGen). Only the HLSL
        // W3D shaders do that, so CG-shader materials move to W3D/Transparent (any blend pair).
        private static void ApplyTexGen(Material mat, W3DVertexMaterial vm)
        {
            int baseKind = W3DTextureAnimationResolver.GetStageTexGen(vm, 0, out int baseAxis, out Vector4 baseGrid);
            int detailKind = W3DTextureAnimationResolver.GetStageTexGen(vm, 1, out int detailAxis, out Vector4 detailGrid);
            if (baseKind == 0 && detailKind == 0)
                return;

            if (TransparentShader != null && (mat.shader == AdditiveShader || mat.shader == ParticleShader))
                MoveToShader(mat, TransparentShader);

            if (mat.HasProperty("_BaseTexGen"))
            {
                mat.SetFloat("_BaseTexGen", baseKind);
                mat.SetFloat("_BaseTexGenAxis", baseAxis);
                mat.SetVector("_BaseTexGenGrid", baseGrid);
                mat.SetFloat("_DetailTexGen", detailKind);
                mat.SetFloat("_DetailTexGenAxis", detailAxis);
                mat.SetVector("_DetailTexGenGrid", detailGrid);
            }
        }

        private static Material[] BuildFromEnbUserText(W3DMesh w3dMesh, W3DTextureResolver textureResolver)
        {
            var effect = w3dMesh.EffectPass;
            var surface = w3dMesh.Surface;

            bool useVertexColor = MeshHasTintedDcg(w3dMesh);
            Shader shader = effect != null && effect.EffectPassType == 2 ? AdditiveShader : LitShader;

            var mat = new Material(shader);
            mat.name = string.IsNullOrEmpty(effect?.Shader)
                ? w3dMesh.Name + ".Effect"
                : w3dMesh.Name + "." + effect.Shader;
            SetOpaque(mat);

            var baseTextureName = effect?.Texture;
            if (string.IsNullOrWhiteSpace(baseTextureName))
                baseTextureName = surface?.ColorMap;

            ApplyTextureByName(mat, baseTextureName, textureResolver);

            if ((baseTextureName == null || mat.mainTexture == null) && w3dMesh.Textures.Count > 0)
                ApplyTexture(mat, w3dMesh, 0, textureResolver);

            if (!string.IsNullOrWhiteSpace(surface?.DetailMap))
                ApplyDetailTexture(mat, surface.DetailMap, surface.XDetailScale, surface.YDetailScale, textureResolver);

            bool blendApplied = ApplyEffectBlendMode(mat, effect);
            if (!blendApplied)
                ApplyTextureDrivenBlendMode(mat, w3dMesh, 0, baseTextureName);

            ApplyMeshFlags(mat, w3dMesh);
            SetUseVertexColor(mat, useVertexColor);
            return new[] { mat };
        }

        private static void ApplyTexture(Material mat, W3DMesh w3dMesh, int matIndex, W3DTextureResolver resolver, int? textureIndexOverride = null)
        {
            int texIdx = textureIndexOverride ?? GetPrimaryTextureIndex(w3dMesh, matIndex);
            if (texIdx < 0 || texIdx >= w3dMesh.Textures.Count)
                return;

            var tex = resolver.Resolve(w3dMesh.Textures[texIdx].File);
            if (tex == null)
                return;

            mat.mainTexture = tex;
            SetMaterialTexture(mat, tex);
            if (W3DLog.Verbose) Debug.Log($"[W3D] Applied texture '{w3dMesh.Textures[texIdx].File}' to material '{mat.name}'");
        }

        private static W3DShader ResolveShaderForMaterial(W3DMesh w3dMesh, int materialIndex, W3DFaceMaterialBinding faceBindingOverride)
        {
            if (faceBindingOverride != null &&
                faceBindingOverride.ShaderIndex >= 0 &&
                faceBindingOverride.ShaderIndex < w3dMesh.Shaders.Count)
            {
                return w3dMesh.Shaders[faceBindingOverride.ShaderIndex];
            }

            return W3DTextureAnimationResolver.ResolveShader(w3dMesh, materialIndex);
        }

        private static void ApplySecondaryTextureStage(
            Material mat,
            W3DMesh w3dMesh,
            int matIndex,
            W3DTextureResolver resolver,
            W3DShader shaderOverride = null,
            int? secondaryTextureIndexOverride = null)
        {
            if (mat == null)
                return;

            var shader = shaderOverride ?? W3DTextureAnimationResolver.ResolveShader(w3dMesh, matIndex);
            if (!UsesDetailTexture(shader))
            {
                SetPostDetailCombine(mat, 0, 0, false);
                return;
            }

            string textureName = null;
            if (secondaryTextureIndexOverride.HasValue)
            {
                int secondaryTextureIndex = secondaryTextureIndexOverride.Value;
                if (secondaryTextureIndex < 0 || secondaryTextureIndex >= w3dMesh.Textures.Count)
                {
                    SetPostDetailCombine(mat, 0, 0, false);
                    return;
                }

                textureName = w3dMesh.Textures[secondaryTextureIndex].File;
            }
            else
            {
                var binding = W3DTextureAnimationResolver.ResolveStageTextureBinding(w3dMesh, matIndex, 1);
                if (binding == null)
                {
                    SetPostDetailCombine(mat, 0, 0, false);
                    return;
                }

                textureName = binding.TextureName;
            }

            var tex = resolver.Resolve(textureName);
            if (tex == null)
            {
                SetPostDetailCombine(mat, 0, 0, false);
                return;
            }

            SetSecondaryMaterialTexture(mat, tex);
            SetPostDetailCombine(mat, shader.DetailColorFunc, shader.DetailAlphaFunc, UseSecondaryStageUvSet(w3dMesh, matIndex));
            if (W3DLog.Verbose) Debug.Log($"[W3D] Applied secondary texture '{textureName}' to material '{mat.name}' with detailColor={shader.DetailColorFunc} detailAlpha={shader.DetailAlphaFunc}");
        }

        private static void ApplyVertexMaterialInfo(Material mat, W3DVertexMaterialInfo info)
        {
            if (mat == null || info == null)
                return;

            // Textured: force white tint so the texture isn't multiplied down (E&B
            // frequently leaves diffuse near-black while the texture carries albedo).
            // Untextured: respect the vertex material diffuse, otherwise solid-color
            // meshes like WALL_BLACK01 lose their intended color and render white.
            var baseColor = mat.mainTexture != null ? Color.white : info.Diffuse;
            baseColor.a = GetMaterialAlpha(info);
            SetMaterialColor(mat, baseColor);
        }

        // DX8 fixed-function gradient = Emissive + Ambient·sceneAmbient + Diffuse·light,
        // saturated to 1. When Diffuse and Ambient are both black the surface is
        // lighting-INDEPENDENT: the client renders texture × Emissive (screens, glow strips,
        // beams). With vertex colors the client goes further (mesh post-load FUN_009475E0):
        // it bakes DCG × Emissive into the vertex colors, makes them the diffuse source and
        // turns lighting OFF, so the surface is texture × DCG × Emissive (baked interior
        // lighting such as jsd01a's). A white Emissive next to a non-black Diffuse / Ambient
        // saturates the lit sum instead: full texture brightness, vertex colors irrelevant.
        // Mirror both: unlit, tinted by the emissive color.
        // Returns true when vertex colors make no difference to the result.
        private static bool ApplyEmissiveDrivenSurface(Material mat, W3DVertexMaterialInfo info)
        {
            if (mat == null || info == null)
                return false;

            bool saturated = info.Emissive.r >= 0.99f && info.Emissive.g >= 0.99f && info.Emissive.b >= 0.99f;
            bool emissiveOnly = IsBlackRgb(info.Diffuse) && IsBlackRgb(info.Ambient) && HasVisibleRgb(info.Emissive);
            if (!saturated && !emissiveOnly)
                return false;

            SetLightingEnabled(mat, false);

            var currentColor = GetMaterialColor(mat);
            var tint = emissiveOnly ? info.Emissive : Color.white;
            tint.a = currentColor.a;
            SetMaterialColor(mat, tint);
            return !emissiveOnly;
        }

        // Switches the shader, keeping textures, colors, render queue and cull mode (the float
        // properties shared by the W3D shaders keep their values).
        private static void MoveToShader(Material mat, Shader shader)
        {
            var mainTexture = mat.mainTexture;
            var detailTexture = GetSecondaryMaterialTexture(mat);
            var detailScale = GetSecondaryMaterialTextureScale(mat);
            var color = GetMaterialColor(mat);
            var emissive = GetMaterialEmissive(mat);
            int queue = mat.renderQueue;
            float cull = mat.HasProperty("_Cull") ? mat.GetFloat("_Cull")
                : mat.HasProperty("_CullMode") ? mat.GetFloat("_CullMode") : (float)CullMode.Back;

            mat.shader = shader;
            RestoreMaterialState(mat, mainTexture, detailTexture, detailScale, color, emissive);
            if (mat.HasProperty("_Cull"))
                mat.SetFloat("_Cull", cull);
            if (mat.HasProperty("_CullMode"))
                mat.SetFloat("_CullMode", cull);
            mat.renderQueue = queue;
        }

        // How stage 0 combines the texture with the gradient (W3D shader PriGradient).
        private static void SetPrimaryGradient(Material mat, W3DShader shader)
        {
            if (mat.HasProperty("_PriGradient"))
                mat.SetFloat("_PriGradient", shader != null ? shader.PriGradient : 1f);
        }

        private static bool HasVisibleRgb(Color color)
        {
            return color.r > 0.01f || color.g > 0.01f || color.b > 0.01f;
        }

        private static bool IsBlackRgb(Color color)
        {
            return color.r <= 0.01f && color.g <= 0.01f && color.b <= 0.01f;
        }

        // Vertex-material Translucency is read but UNUSED by the client (W3D_FORMAT.md §6);
        // only Opacity contributes to alpha.
        private static float GetMaterialAlpha(W3DVertexMaterialInfo info)
        {
            if (info == null)
                return 1f;

            if (info.Opacity > 0f && info.Opacity <= 1f)
                return info.Opacity;

            return 1f;
        }

        private static void ApplyTextureByName(Material mat, string textureName, W3DTextureResolver resolver)
        {
            if (string.IsNullOrWhiteSpace(textureName))
                return;

            var tex = resolver.Resolve(textureName);
            if (tex == null)
                return;

            mat.mainTexture = tex;
            SetMaterialTexture(mat, tex);
        }

        private static void ApplyDetailTexture(Material mat, string textureName, float? xScale, float? yScale, W3DTextureResolver resolver)
        {
            if (string.IsNullOrWhiteSpace(textureName))
                return;

            var tex = resolver.Resolve(textureName);
            if (tex == null)
                return;

            if (mat.HasProperty("_DetailAlbedoMap"))
                mat.SetTexture("_DetailAlbedoMap", tex);
            if (mat.HasProperty("_DetailMap"))
                mat.SetTexture("_DetailMap", tex);

            var scale = new Vector2(xScale ?? 1f, yScale ?? 1f);
            if (mat.HasProperty("_DetailAlbedoMapScale"))
                mat.SetTextureScale("_DetailAlbedoMap", scale);
            if (mat.HasProperty("_DetailMap"))
                mat.SetTextureScale("_DetailMap", scale);
        }

        // W3D on-disk shader enum (0-based). SrcBlend and DestBlend use different enums.
        // SrcBlend: 0=ZERO, 1=ONE, 2=SRC_ALPHA, 3=ONE_MINUS_SRC_ALPHA
        // DestBlend: 0=ZERO, 1=ONE, 2=SRC_COLOR, 3=ONE_MINUS_SRC_COLOR, 4=SRC_ALPHA, 5=ONE_MINUS_SRC_ALPHA
        // The D3D values (1=ZERO, 2=ONE, ...) seen in W3dview.exe are its *internal* API constants,
        // not the disk format.
        private const byte SRC_ZERO = 0;
        private const byte SRC_ONE = 1;
        private const byte SRC_SRC_ALPHA = 2;
        private const byte SRC_ONE_MINUS_SRC_ALPHA = 3;

        private const byte DST_ZERO = 0;
        private const byte DST_ONE = 1;
        private const byte DST_SRC_COLOR = 2;
        private const byte DST_ONE_MINUS_SRC_COLOR = 3;
        private const byte DST_SRC_ALPHA = 4;
        private const byte DST_ONE_MINUS_SRC_ALPHA = 5;

        // Reached only if ApplyTextureDrivenBlendMode didn't classify via TextureInfo.
        // Classify from the W3D shader blend states. Enum values are 0-based per the
        // on-disk W3D format — see SRC_* / DST_* constants above.
        private static bool ApplyBlendMode(Material mat, W3DShader shader)
        {
            if (shader == null)
                return false;

            byte src = shader.SrcBlend;
            byte dst = shader.DestBlend;

            if (W3DLog.Verbose) Debug.Log($"[W3D Blend] '{mat.name}' src={src} dst={dst} alphaTest={shader.AlphaTest} depthMask={shader.DepthMask}");

            // Additive: preserve whether the W3D pass is ONE/ONE or SRC_ALPHA/ONE.
            if (dst == DST_ONE && (src == SRC_ONE || src == SRC_SRC_ALPHA))
            {
                SetAdditive(mat, src == SRC_ONE ? BlendMode.One : BlendMode.SrcAlpha);
                ApplyDepthState(mat, shader);
                return true;
            }

            // Screen: Src=ONE + Dst=ONE_MINUS_SRC_COLOR
            if (src == SRC_ONE && dst == DST_ONE_MINUS_SRC_COLOR)
            {
                SetScreen(mat);
                ApplyDepthState(mat, shader);
                return true;
            }

            // Pre-multiplied alpha (glass, decals): Src=ONE + Dst=ONE_MINUS_SRC_ALPHA
            if (src == SRC_ONE && dst == DST_ONE_MINUS_SRC_ALPHA)
            {
                SetTransparent(mat, BlendMode.One, BlendMode.OneMinusSrcAlpha, true);
                ApplyDepthState(mat, shader);
                return true;
            }

            // Standard alpha: Src=SRC_ALPHA + Dst=ONE_MINUS_SRC_ALPHA
            if (src == SRC_SRC_ALPHA && dst == DST_ONE_MINUS_SRC_ALPHA)
            {
                SetTransparent(mat, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, false);
                ApplyDepthState(mat, shader);
                return true;
            }

            // Preserve remaining color-destination blend families exactly instead of
            // collapsing them into a generic multiplicative approximation.
            if (dst == DST_SRC_COLOR)
            {
                SetParticleBlend(mat, MapSourceBlendMode(src), BlendMode.SrcColor);
                ApplyDepthState(mat, shader);
                return true;
            }

            if (dst == DST_ONE_MINUS_SRC_COLOR)
            {
                SetParticleBlend(mat, MapSourceBlendMode(src), BlendMode.OneMinusSrcColor);
                ApplyDepthState(mat, shader);
                return true;
            }

            // Any other blend pair is reproduced as is (overlay passes use e.g.
            // ONE_MINUS_SRC_ALPHA / SRC_ALPHA to let the pass below show through).
            if (src != SRC_ONE || dst != DST_ZERO)
            {
                SetParticleBlend(mat, MapSourceBlendMode(src), MapDestBlendMode(dst));
                ApplyDepthState(mat, shader);
                return true;
            }

            // AlphaTest on an otherwise opaque pass
            if (shader.AlphaTest == 1)
            {
                SetAlphaClip(mat);
                ApplyDepthState(mat, shader);
                return true;
            }

            // Opaque (Src=ONE Dst=ZERO) and unrecognized combos: leave on LitShader.
            ApplyDepthState(mat, shader);
            return false;
        }

        // W3D DepthMask: 0 = disable ZWrite, 1 = enable.
        // W3D DepthCompare: 1=NEVER..8=ALWAYS (D3DCMP_*). We only toggle ZWrite here —
        // custom DepthCompare would require per-shader variants, which the existing
        // W3D/Standard / W3D/Additive / W3D/Particle set doesn't expose.
        private static void ApplyDepthState(Material mat, W3DShader shader)
        {
            if (shader == null)
                return;

            if (mat.HasProperty("_ZWrite"))
                mat.SetFloat("_ZWrite", shader.DepthMask != 0 ? 1f : 0f);
            if (mat.HasProperty("_TransparentZWrite"))
                mat.SetFloat("_TransparentZWrite", shader.DepthMask != 0 ? 1f : 0f);
        }

        private static void SetMultiplicative(Material mat)
        {
            var mainTexture = mat.mainTexture;
            var detailTexture = GetSecondaryMaterialTexture(mat);
            var detailScale = GetSecondaryMaterialTextureScale(mat);
            var color = GetMaterialColor(mat);
            var emissive = GetMaterialEmissive(mat);

            var targetShader = ParticleShader != null ? ParticleShader : TransparentShader;
            if (targetShader != null && mat.shader != targetShader)
                mat.shader = targetShader;

            RestoreMaterialState(mat, mainTexture, detailTexture, detailScale, color, emissive);

            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)BlendMode.DstColor);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
            if (mat.HasProperty("_ZWrite"))
                mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_AlphaClip"))
                mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_AlphaCutoffEnable"))
                mat.SetFloat("_AlphaCutoffEnable", 0f);
            SetPremultiplyAlpha(mat, false);
            SetLightingEnabled(mat, true);
            SetDefaultCullMode(mat);

            mat.renderQueue = 3000;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.DisableKeyword("_ALPHATEST_ON");
        }

        private static BlendMode MapSourceBlendMode(byte src)
        {
            switch (src)
            {
                case SRC_ZERO:
                    return BlendMode.Zero;
                case SRC_ONE:
                    return BlendMode.One;
                case SRC_SRC_ALPHA:
                    return BlendMode.SrcAlpha;
                case SRC_ONE_MINUS_SRC_ALPHA:
                    return BlendMode.OneMinusSrcAlpha;
                default:
                    return BlendMode.One;
            }
        }

        private static BlendMode MapDestBlendMode(byte dst)
        {
            switch (dst)
            {
                case DST_ZERO:
                    return BlendMode.Zero;
                case DST_ONE:
                    return BlendMode.One;
                case DST_SRC_COLOR:
                    return BlendMode.SrcColor;
                case DST_ONE_MINUS_SRC_COLOR:
                    return BlendMode.OneMinusSrcColor;
                case DST_SRC_ALPHA:
                    return BlendMode.SrcAlpha;
                case DST_ONE_MINUS_SRC_ALPHA:
                    return BlendMode.OneMinusSrcAlpha;
                default:
                    return BlendMode.Zero;
            }
        }

        private static void SetParticleBlend(Material mat, BlendMode srcBlend, BlendMode dstBlend)
        {
            var mainTexture = mat.mainTexture;
            var detailTexture = GetSecondaryMaterialTexture(mat);
            var detailScale = GetSecondaryMaterialTextureScale(mat);
            var color = GetMaterialColor(mat);
            var emissive = GetMaterialEmissive(mat);

            var targetShader = ParticleShader != null ? ParticleShader : TransparentShader;
            if (targetShader != null && mat.shader != targetShader)
                mat.shader = targetShader;

            RestoreMaterialState(mat, mainTexture, detailTexture, detailScale, color, emissive);

            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)srcBlend);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)dstBlend);
            if (mat.HasProperty("_AlphaSrcBlend"))
                mat.SetFloat("_AlphaSrcBlend", (float)srcBlend);
            if (mat.HasProperty("_AlphaDstBlend"))
                mat.SetFloat("_AlphaDstBlend", (float)dstBlend);
            if (mat.HasProperty("_ZWrite"))
                mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_AlphaClip"))
                mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_AlphaCutoffEnable"))
                mat.SetFloat("_AlphaCutoffEnable", 0f);
            SetPremultiplyAlpha(mat, false);
            SetLightingEnabled(mat, true);
            SetDefaultCullMode(mat);

            mat.renderQueue = 3000;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.DisableKeyword("_ALPHATEST_ON");
        }

        private static bool ApplyEffectBlendMode(Material mat, W3DEffectPassDescription effect)
        {
            if (effect == null)
                return false;

            switch (effect.EffectPassType)
            {
                case 1:
                    SetTransparent(mat);
                    return true;
                case 2:
                    SetAdditive(mat);
                    return true;
                case 3:
                    SetTransparent(mat);
                    return true;
                default:
                    return false;
            }
        }

        private static bool ApplyTextureDrivenBlendMode(Material mat, W3DMesh w3dMesh, int matIndex, string explicitTextureName = null)
        {
            string textureName = explicitTextureName;
            W3DTexture textureInfo = null;
            var shader = W3DTextureAnimationResolver.ResolveShader(w3dMesh, matIndex);

            if (!string.IsNullOrWhiteSpace(textureName))
                textureName = NormalizeTextureName(textureName);

            if (string.IsNullOrWhiteSpace(textureName))
            {
                int texIdx = GetPrimaryTextureIndex(w3dMesh, matIndex);
                if (texIdx >= 0 && texIdx < w3dMesh.Textures.Count)
                {
                    textureInfo = w3dMesh.Textures[texIdx];
                    textureName = NormalizeTextureName(textureInfo.File);
                }
            }
            else
            {
                textureInfo = FindTextureByName(w3dMesh, textureName);
            }

            // Texture-name hint only. Mesh names like SCREEN_GLOW## can be non-additive
            // multi-stage materials, so forcing additive from the mesh name blows them out.
            bool usesDetailTexture = UsesDetailTexture(W3DTextureAnimationResolver.ResolveShader(w3dMesh, matIndex));
            if (usesDetailTexture)
                return false;

            if (LooksLikeAdditiveTexture(textureName))
            {
                SetAdditive(mat);
                return true;
            }

            if (textureInfo == null)
                return false;

            bool hasAlphaBitmap = (textureInfo.TextureInfo & TextureAttributeAlphaBitmap) != 0;
            bool emissiveHint = (textureInfo.TextureInfo & TextureHintMask) == TextureHintEmissive;

            if (emissiveHint)
            {
                SetAdditive(mat);
                return true;
            }

            if (shader == null && hasAlphaBitmap)
            {
                SetTransparent(mat, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, false);
                return true;
            }

            return false;
        }

        private static bool UsesDetailTexture(W3DShader shader)
        {
            return shader != null && (shader.DetailColorFunc != 0 || shader.DetailAlphaFunc != 0);
        }

        private static bool UseSecondaryStageUvSet(W3DMesh w3dMesh, int matIndex)
        {
            var pass = W3DTextureAnimationResolver.ResolveMaterialPass(w3dMesh, matIndex, out _);
            if (pass == null || pass.TxStages.Count <= 1)
                return false;

            var stage = pass.TxStages[1];
            return stage != null && stage.TxCoords.Count > 0 && stage.TxCoords[0] != null && stage.TxCoords[0].Count > 0;
        }

        private static int GetPrimaryTextureIndex(W3DMesh w3dMesh, int matIndex)
        {
            var binding = W3DTextureAnimationResolver.ResolvePrimaryTextureBinding(w3dMesh, matIndex);
            if (binding != null && binding.TextureIndex >= 0 && binding.TextureIndex < w3dMesh.Textures.Count)
                return binding.TextureIndex;

            // When the base pass has texture stages, no binding means the faces are untextured
            // (texture id -1); only meshes without stage data fall back to the texture list.
            var pass = W3DTextureAnimationResolver.ResolveMaterialPass(w3dMesh, matIndex, out _);
            if (pass != null && pass.TxStages.Count > 0)
                return -1;

            int texIdx = -1;

            if (texIdx < 0 && matIndex < w3dMesh.Textures.Count)
                texIdx = matIndex;

            if (texIdx < 0 && w3dMesh.Textures.Count > 0)
                texIdx = 0;

            return texIdx;
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

        private static W3DTexture FindTextureByName(W3DMesh w3dMesh, string textureName)
        {
            for (int i = 0; i < w3dMesh.Textures.Count; i++)
            {
                var candidate = w3dMesh.Textures[i];
                if (NormalizeTextureName(candidate.File) == textureName)
                    return candidate;
            }

            return null;
        }

        private static string NormalizeTextureName(string textureName)
        {
            if (string.IsNullOrWhiteSpace(textureName))
                return string.Empty;

            return System.IO.Path.GetFileNameWithoutExtension(textureName).Trim().ToUpperInvariant();
        }

        private static bool LooksLikeAdditiveTexture(string textureName)
        {
            if (string.IsNullOrWhiteSpace(textureName))
                return false;

            return textureName.StartsWith("FLARE", StringComparison.OrdinalIgnoreCase)
                || textureName.StartsWith("GLOW", StringComparison.OrdinalIgnoreCase)
                || textureName.Contains("LENS", StringComparison.OrdinalIgnoreCase);
        }

        private static void SetTransparent(
            Material mat,
            BlendMode srcBlend = BlendMode.SrcAlpha,
            BlendMode dstBlend = BlendMode.OneMinusSrcAlpha,
            bool premultiplyAlpha = false,
            bool applyLighting = true)
        {
            var mainTexture = mat.mainTexture;
            var detailTexture = GetSecondaryMaterialTexture(mat);
            var detailScale = GetSecondaryMaterialTextureScale(mat);
            var color = GetMaterialColor(mat);
            var emissive = GetMaterialEmissive(mat);

            if (TransparentShader != null && mat.shader != TransparentShader)
                mat.shader = TransparentShader;

            RestoreMaterialState(mat, mainTexture, detailTexture, detailScale, color, emissive);

            if (mat.HasProperty("_SurfaceType"))
                mat.SetFloat("_SurfaceType", 1f);
            if (mat.HasProperty("_BlendMode"))
                mat.SetFloat("_BlendMode", 0f);
            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))
                mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_ZWrite"))
                mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_TransparentZWrite"))
                mat.SetFloat("_TransparentZWrite", 0f);
            if (mat.HasProperty("_AlphaCutoffEnable"))
                mat.SetFloat("_AlphaCutoffEnable", 0f);
            if (mat.HasProperty("_AlphaClip"))
                mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_Mode"))
                mat.SetFloat("_Mode", 3f);
            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)srcBlend);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)dstBlend);
            if (mat.HasProperty("_AlphaSrcBlend"))
                mat.SetFloat("_AlphaSrcBlend", (float)srcBlend);
            if (mat.HasProperty("_AlphaDstBlend"))
                mat.SetFloat("_AlphaDstBlend", (float)dstBlend);
            SetPremultiplyAlpha(mat, premultiplyAlpha);
            SetLightingEnabled(mat, applyLighting);
            SetDefaultCullMode(mat);

            mat.renderQueue = 3000;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHATEST_ON");
        }

        private static void SetAdditive(Material mat, BlendMode srcBlend = BlendMode.SrcAlpha)
        {
            var mainTexture = mat.mainTexture;
            var detailTexture = GetSecondaryMaterialTexture(mat);
            var detailScale = GetSecondaryMaterialTextureScale(mat);
            var color = GetMaterialColor(mat);
            var emissive = GetMaterialEmissive(mat);

            if (AdditiveShader != null && mat.shader != AdditiveShader)
                mat.shader = AdditiveShader;

            RestoreMaterialState(mat, mainTexture, detailTexture, detailScale, color, emissive);
            if (mat.HasProperty("_SurfaceType"))
                mat.SetFloat("_SurfaceType", 1f);
            if (mat.HasProperty("_BlendMode"))
                mat.SetFloat("_BlendMode", 1f);
            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)srcBlend);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)BlendMode.One);
            if (mat.HasProperty("_AlphaSrcBlend"))
                mat.SetFloat("_AlphaSrcBlend", (float)srcBlend);
            if (mat.HasProperty("_AlphaDstBlend"))
                mat.SetFloat("_AlphaDstBlend", (float)BlendMode.One);
            if (mat.HasProperty("_ZWrite"))
                mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_TransparentZWrite"))
                mat.SetFloat("_TransparentZWrite", 0f);
            if (mat.HasProperty("_AlphaClip"))
                mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_AlphaCutoffEnable"))
                mat.SetFloat("_AlphaCutoffEnable", 0f);
            SetPremultiplyAlpha(mat, false);
            SetLightingEnabled(mat, true);
            SetDefaultCullMode(mat);
            mat.renderQueue = 3100;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
        }

        private static void SetScreen(Material mat)
        {
            var mainTexture = mat.mainTexture;
            var detailTexture = GetSecondaryMaterialTexture(mat);
            var detailScale = GetSecondaryMaterialTextureScale(mat);
            var color = GetMaterialColor(mat);
            var emissive = GetMaterialEmissive(mat);

            var screenShader = ParticleShader != null ? ParticleShader : TransparentShader;
            if (screenShader != null && mat.shader != screenShader)
                mat.shader = screenShader;

            RestoreMaterialState(mat, mainTexture, detailTexture, detailScale, color, emissive);
            if (mat.HasProperty("_SurfaceType"))
                mat.SetFloat("_SurfaceType", 1f);
            if (mat.HasProperty("_BlendMode"))
                mat.SetFloat("_BlendMode", 1f);
            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))
                mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcColor);
            if (mat.HasProperty("_AlphaSrcBlend"))
                mat.SetFloat("_AlphaSrcBlend", (float)BlendMode.One);
            if (mat.HasProperty("_AlphaDstBlend"))
                mat.SetFloat("_AlphaDstBlend", (float)BlendMode.OneMinusSrcColor);
            if (mat.HasProperty("_ZWrite"))
                mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_TransparentZWrite"))
                mat.SetFloat("_TransparentZWrite", 0f);
            if (mat.HasProperty("_AlphaClip"))
                mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_AlphaCutoffEnable"))
                mat.SetFloat("_AlphaCutoffEnable", 0f);
            SetPremultiplyAlpha(mat, false);
            SetLightingEnabled(mat, true);
            SetDefaultCullMode(mat);

            mat.renderQueue = 3050;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
        }

        private static void SetAlphaClip(Material mat)
        {
            if (mat.shader != LitShader && LitShader != null)
                mat.shader = LitShader;

            var mainTexture = mat.mainTexture;
            var detailTexture = GetSecondaryMaterialTexture(mat);
            var detailScale = GetSecondaryMaterialTextureScale(mat);
            var color = GetMaterialColor(mat);
            var emissive = GetMaterialEmissive(mat);
            RestoreMaterialState(mat, mainTexture, detailTexture, detailScale, color, emissive);

            if (mat.HasProperty("_SurfaceType"))
                mat.SetFloat("_SurfaceType", 0f);
            if (mat.HasProperty("_BlendMode"))
                mat.SetFloat("_BlendMode", 0f);
            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 0f);
            if (mat.HasProperty("_AlphaCutoffEnable"))
                mat.SetFloat("_AlphaCutoffEnable", 1f);
            if (mat.HasProperty("_AlphaCutoff"))
                mat.SetFloat("_AlphaCutoff", 0.5f);
            if (mat.HasProperty("_AlphaClip"))
                mat.SetFloat("_AlphaClip", 1f);
            if (mat.HasProperty("_Cutoff"))
                mat.SetFloat("_Cutoff", 0.5f);
            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
            if (mat.HasProperty("_AlphaSrcBlend"))
                mat.SetFloat("_AlphaSrcBlend", (float)BlendMode.One);
            if (mat.HasProperty("_AlphaDstBlend"))
                mat.SetFloat("_AlphaDstBlend", (float)BlendMode.Zero);
            if (mat.HasProperty("_ZWrite"))
                mat.SetFloat("_ZWrite", 1f);
            SetPremultiplyAlpha(mat, false);
            SetLightingEnabled(mat, true);
            SetDefaultCullMode(mat);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 2450;
            mat.SetOverrideTag("RenderType", "TransparentCutout");
        }

        private static void RestoreMaterialState(
            Material mat,
            Texture mainTexture,
            Texture detailTexture,
            Vector2 detailScale,
            Color color,
            Color emissive)
        {
            if (mainTexture != null)
            {
                mat.mainTexture = mainTexture;
                SetMaterialTexture(mat, mainTexture);
            }

            if (detailTexture != null)
            {
                SetSecondaryMaterialTexture(mat, detailTexture);
                SetSecondaryMaterialTextureScale(mat, detailScale);
            }

            SetMaterialColor(mat, color);
            SetMaterialEmissive(mat, emissive);
        }

        private static void SetOpaque(Material mat)
        {
            if (mat == null)
                return;

            var mainTexture = mat.mainTexture;
            var detailTexture = GetSecondaryMaterialTexture(mat);
            var detailScale = GetSecondaryMaterialTextureScale(mat);
            var color = GetMaterialColor(mat);
            var emissive = GetMaterialEmissive(mat);

            if (LitShader != null && mat.shader != LitShader)
                mat.shader = LitShader;

            RestoreMaterialState(mat, mainTexture, detailTexture, detailScale, color, emissive);

            if (mat.HasProperty("_SurfaceType"))
                mat.SetFloat("_SurfaceType", 0f);
            if (mat.HasProperty("_BlendMode"))
                mat.SetFloat("_BlendMode", 0f);
            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 0f);
            if (mat.HasProperty("_AlphaCutoffEnable"))
                mat.SetFloat("_AlphaCutoffEnable", 0f);
            if (mat.HasProperty("_AlphaClip"))
                mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
            if (mat.HasProperty("_AlphaSrcBlend"))
                mat.SetFloat("_AlphaSrcBlend", (float)BlendMode.One);
            if (mat.HasProperty("_AlphaDstBlend"))
                mat.SetFloat("_AlphaDstBlend", (float)BlendMode.Zero);
            if (mat.HasProperty("_ZWrite"))
                mat.SetFloat("_ZWrite", 1f);
            if (mat.HasProperty("_TransparentZWrite"))
                mat.SetFloat("_TransparentZWrite", 0f);
            SetPremultiplyAlpha(mat, false);
            SetLightingEnabled(mat, true);
            SetDefaultCullMode(mat);

            mat.renderQueue = -1;
            mat.SetOverrideTag("RenderType", "");
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
        }

        private static void SetMaterialTexture(Material mat, Texture tex)
        {
            if (mat.HasProperty("_UnlitColorMap"))
                mat.SetTexture("_UnlitColorMap", tex);
            if (mat.HasProperty("_BaseColorMap"))
                mat.SetTexture("_BaseColorMap", tex);
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", tex);
        }

        private static void SetSecondaryMaterialTexture(Material mat, Texture tex)
        {
            if (mat.HasProperty("_DetailTex"))
                mat.SetTexture("_DetailTex", tex);
            if (mat.HasProperty("_DetailMap"))
                mat.SetTexture("_DetailMap", tex);
            if (mat.HasProperty("_DetailAlbedoMap"))
                mat.SetTexture("_DetailAlbedoMap", tex);
        }

        private static void SetSecondaryMaterialTextureScale(Material mat, Vector2 scale)
        {
            if (mat.HasProperty("_DetailTex"))
                mat.SetTextureScale("_DetailTex", scale);
            if (mat.HasProperty("_DetailMap"))
                mat.SetTextureScale("_DetailMap", scale);
            if (mat.HasProperty("_DetailAlbedoMap"))
                mat.SetTextureScale("_DetailAlbedoMap", scale);
        }

        private static void SetPostDetailCombine(Material mat, int colorFunc, int alphaFunc, bool useUv2)
        {
            if (mat.HasProperty("_DetailColorFunc"))
                mat.SetFloat("_DetailColorFunc", colorFunc);
            if (mat.HasProperty("_DetailAlphaFunc"))
                mat.SetFloat("_DetailAlphaFunc", alphaFunc);
            if (mat.HasProperty("_DetailUseUV2"))
                mat.SetFloat("_DetailUseUV2", useUv2 ? 1f : 0f);
        }

        private static void SetMaterialColor(Material mat, Color color)
        {
            if (mat.HasProperty("_UnlitColor"))
                mat.SetColor("_UnlitColor", color);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
        }

        private static void SetLightingEnabled(Material mat, bool enabled)
        {
            if (mat.HasProperty("_ApplyLighting"))
                mat.SetFloat("_ApplyLighting", enabled ? 1f : 0f);
        }

        private static void SetUseVertexColor(Material mat, bool enabled)
        {
            if (mat.HasProperty("_UseVertexColor"))
                mat.SetFloat("_UseVertexColor", enabled ? 1f : 0f);
        }

        private static void SetPremultiplyAlpha(Material mat, bool enabled)
        {
            if (mat.HasProperty("_PremultiplyAlpha"))
                mat.SetFloat("_PremultiplyAlpha", enabled ? 1f : 0f);
        }

        private static void SetDefaultCullMode(Material mat)
        {
            if (mat.HasProperty("_Cull"))
                mat.SetFloat("_Cull", (float)CullMode.Back);
            if (mat.HasProperty("_CullMode"))
                mat.SetFloat("_CullMode", (float)CullMode.Back);
            if (mat.HasProperty("_TransparentCullMode"))
                mat.SetFloat("_TransparentCullMode", (float)CullMode.Back);
            if (mat.HasProperty("_OpaqueCullMode"))
                mat.SetFloat("_OpaqueCullMode", (float)CullMode.Back);
            if (mat.HasProperty("_DoubleSidedEnable"))
                mat.SetFloat("_DoubleSidedEnable", 0f);
            if (mat.HasProperty("_FlipBackfaceU"))
                mat.SetFloat("_FlipBackfaceU", 0f);
        }

        private static void SetMaterialEmissive(Material mat, Color color)
        {
            if (mat.HasProperty("_EmissiveColor"))
                mat.SetColor("_EmissiveColor", color);
            if (mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor", color);
        }

        private static Color GetMaterialColor(Material mat)
        {
            if (mat.HasProperty("_UnlitColor"))
                return mat.GetColor("_UnlitColor");
            if (mat.HasProperty("_BaseColor"))
                return mat.GetColor("_BaseColor");
            if (mat.HasProperty("_Color"))
                return mat.GetColor("_Color");
            return Color.white;
        }

        private static Color GetMaterialEmissive(Material mat)
        {
            if (mat.HasProperty("_EmissiveColor"))
                return mat.GetColor("_EmissiveColor");
            if (mat.HasProperty("_EmissionColor"))
                return mat.GetColor("_EmissionColor");
            return Color.black;
        }

        private static Texture GetSecondaryMaterialTexture(Material mat)
        {
            if (mat.HasProperty("_DetailTex"))
                return mat.GetTexture("_DetailTex");
            if (mat.HasProperty("_DetailMap"))
                return mat.GetTexture("_DetailMap");
            if (mat.HasProperty("_DetailAlbedoMap"))
                return mat.GetTexture("_DetailAlbedoMap");
            return null;
        }

        private static Vector2 GetSecondaryMaterialTextureScale(Material mat)
        {
            if (mat.HasProperty("_DetailTex"))
                return mat.GetTextureScale("_DetailTex");
            if (mat.HasProperty("_DetailMap"))
                return mat.GetTextureScale("_DetailMap");
            if (mat.HasProperty("_DetailAlbedoMap"))
                return mat.GetTextureScale("_DetailAlbedoMap");
            return Vector2.one;
        }

        private static void ApplyMeshFlags(Material mat, W3DMesh w3dMesh)
        {
            SetDefaultCullMode(mat);

            if (!w3dMesh.EffectiveTwoSided)
                return;

            if (mat.HasProperty("_Cull"))
                mat.SetFloat("_Cull", (float)CullMode.Off);
            if (mat.HasProperty("_CullMode"))
                mat.SetFloat("_CullMode", (float)CullMode.Off);
            if (mat.HasProperty("_TransparentCullMode"))
                mat.SetFloat("_TransparentCullMode", (float)CullMode.Off);
            if (mat.HasProperty("_OpaqueCullMode"))
                mat.SetFloat("_OpaqueCullMode", (float)CullMode.Off);
            if (mat.HasProperty("_DoubleSidedEnable"))
                mat.SetFloat("_DoubleSidedEnable", 1f);
            if (mat.HasProperty("_FlipBackfaceU"))
                mat.SetFloat("_FlipBackfaceU", 1f);
        }

        private static Material CreateDefaultMaterial()
        {
            var mat = new Material(LitShader);
            mat.name = "W3D_Default";
            SetOpaque(mat);
            SetMaterialColor(mat, Color.gray);
            return mat;
        }
    }
}
