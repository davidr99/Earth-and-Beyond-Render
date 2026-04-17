using System;
using UnityEngine;
using UnityEngine.Rendering;
using W3DImporter.Structs.Mesh;

namespace W3DImporter.Builders
{
    public static class W3DMaterialBuilder
    {
        private static Shader _litShader;
        private static Shader _transparentShader;
        private static Shader _additiveShader;

        private static Shader LitShader
        {
            get
            {
                if (_litShader != null) return _litShader;

                string[] shaderNames = {
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
                _additiveShader = TransparentShader;
                return _additiveShader;
            }
        }

        public static Material[] Build(W3DMesh w3dMesh, W3DTextureResolver textureResolver)
        {
            if (w3dMesh.EffectPass != null || w3dMesh.Surface != null)
                return BuildFromEnbUserText(w3dMesh, textureResolver);

            if (w3dMesh.VertMaterials.Count > 0)
                return BuildFromVertexMaterials(w3dMesh, textureResolver);

            return new[] { CreateDefaultMaterial() };
        }

        private static Material[] BuildFromVertexMaterials(W3DMesh w3dMesh, W3DTextureResolver textureResolver)
        {
            var materials = new Material[Mathf.Max(1, w3dMesh.VertMaterials.Count)];

            for (int i = 0; i < w3dMesh.VertMaterials.Count; i++)
            {
                var vm = w3dMesh.VertMaterials[i];
                var mat = new Material(LitShader);
                mat.name = w3dMesh.Name + "." + vm.Name;
                SetOpaque(mat);

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

                ApplyTexture(mat, w3dMesh, i, textureResolver);

                var texInfo = w3dMesh.Textures.Count > 0 ? w3dMesh.Textures[0].TextureInfo : 0u;
                Debug.Log($"[W3D MatDiag] '{mat.name}': vertMats={w3dMesh.VertMaterials.Count} shaders={w3dMesh.Shaders.Count} texInfo=0x{texInfo:X8} (blendMode={(texInfo >> 6) & 0x3} hasAlpha={(texInfo & 0x10u) != 0}) effectPass={(w3dMesh.EffectPass != null ? w3dMesh.EffectPass.EffectPassType.ToString() : "null")}");

                // Texture-driven classification first — TextureInfo bits reliably flag
                // glow/flare textures as additive even when the W3DShader blend state
                // says standard alpha. Only fall back to shader-state classification if
                // the texture doesn't provide a verdict.
                bool blendApplied = ApplyTextureDrivenBlendMode(mat, w3dMesh, i);
                Debug.Log($"[W3D MatDiag] '{mat.name}': textureDriven={blendApplied}, shaderAfter='{mat.shader?.name}'");

                if (!blendApplied && w3dMesh.Shaders.Count > 0)
                {
                    int shaderIdx = Mathf.Min(i, w3dMesh.Shaders.Count - 1);
                    blendApplied = ApplyBlendMode(mat, w3dMesh.Shaders[shaderIdx]);
                }

                if (!blendApplied && vm.Info != null && GetMaterialAlpha(vm.Info) < 0.999f)
                {
                    SetTransparent(mat);
                }
                else if (!blendApplied)
                {
                    Debug.Log($"[W3D MatDiag] '{mat.name}': no Shaders chunk parsed — cannot fall back to W3DShader blend-state classification");
                }

                ApplyMeshFlags(mat, w3dMesh);
                materials[i] = mat;
            }

            return materials;
        }

        private static Material[] BuildFromEnbUserText(W3DMesh w3dMesh, W3DTextureResolver textureResolver)
        {
            var effect = w3dMesh.EffectPass;
            var surface = w3dMesh.Surface;

            Shader shader = LitShader;
            if (effect != null && effect.EffectPassType == 2)
                shader = AdditiveShader;

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
            return new[] { mat };
        }

        private static void ApplyTexture(Material mat, W3DMesh w3dMesh, int matIndex, W3DTextureResolver resolver)
        {
            int texIdx = GetPrimaryTextureIndex(w3dMesh, matIndex);
            if (texIdx < 0 || texIdx >= w3dMesh.Textures.Count)
                return;

            var tex = resolver.Resolve(w3dMesh.Textures[texIdx].File);
            if (tex == null)
                return;

            mat.mainTexture = tex;
            SetMaterialTexture(mat, tex);
            Debug.Log($"[W3D] Applied texture '{w3dMesh.Textures[texIdx].File}' to material '{mat.name}'");
        }

        private static void ApplyVertexMaterialInfo(Material mat, W3DVertexMaterialInfo info)
        {
            if (mat == null || info == null)
                return;

            // In E&B assets the diffuse RGB is frequently left at black or near-black
            // while the texture carries the visible albedo. Use material info for
            // opacity, but don't tint the texture down to black.
            var baseColor = Color.white;
            baseColor.a = GetMaterialAlpha(info);
            SetMaterialColor(mat, baseColor);
        }

        private static float GetMaterialAlpha(W3DVertexMaterialInfo info)
        {
            if (info == null)
                return 1f;

            if (info.Opacity > 0f && info.Opacity <= 1f)
                return info.Opacity;

            if (info.Translucency > 0f)
                return 1f - Mathf.Clamp01(info.Translucency);

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

            Debug.Log($"[W3D Blend] '{mat.name}' src={src} dst={dst} alphaTest={shader.AlphaTest} depthMask={shader.DepthMask}");

            // Additive: Dst=ONE with Src=ONE or Src=SRC_ALPHA
            if (dst == DST_ONE && (src == SRC_ONE || src == SRC_SRC_ALPHA))
            {
                SetAdditive(mat);
                ApplyDepthState(mat, shader);
                return true;
            }

            // Pre-multiplied alpha (glass, decals): Src=ONE + Dst=ONE_MINUS_SRC_ALPHA
            if (src == SRC_ONE && dst == DST_ONE_MINUS_SRC_ALPHA)
            {
                SetTransparent(mat);
                ApplyDepthState(mat, shader);
                return true;
            }

            // Standard alpha: Src=SRC_ALPHA + Dst=ONE_MINUS_SRC_ALPHA
            if (src == SRC_SRC_ALPHA && dst == DST_ONE_MINUS_SRC_ALPHA)
            {
                SetTransparent(mat);
                ApplyDepthState(mat, shader);
                return true;
            }

            // Multiplicative: Dst=SRC_COLOR or Dst=ONE_MINUS_SRC_COLOR
            if (dst == DST_SRC_COLOR || dst == DST_ONE_MINUS_SRC_COLOR)
            {
                SetMultiplicative(mat);
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
            var color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;

            if (TransparentShader != null && mat.shader != TransparentShader)
                mat.shader = TransparentShader;

            RestoreMaterialState(mat, mainTexture, color);

            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)BlendMode.DstColor);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
            if (mat.HasProperty("_ZWrite"))
                mat.SetFloat("_ZWrite", 0f);

            mat.renderQueue = 3000;
            mat.SetOverrideTag("RenderType", "Transparent");
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

            // Mesh-name hint: E&B uses FLARE##/GLOW##/LENS## mesh names for light-emitting
            // quads, even when their referenced texture is a generic atlas (JBAR####).
            if (LooksLikeAdditiveTexture(textureName) || LooksLikeAdditiveTexture(w3dMesh.Name))
            {
                SetAdditive(mat);
                return true;
            }

            if (textureInfo == null)
                return false;

            int blendMode = (int)((textureInfo.TextureInfo >> 6) & 0x3u);
            bool hasAlpha = (textureInfo.TextureInfo & 0x10u) != 0;

            if (blendMode >= 2)
            {
                SetAdditive(mat);
                return true;
            }

            if (hasAlpha || blendMode == 1)
            {
                SetTransparent(mat);
                return true;
            }

            return false;
        }

        private static int GetPrimaryTextureIndex(W3DMesh w3dMesh, int matIndex)
        {
            int preferredStage = GetPreferredTextureStage(w3dMesh, matIndex);
            int texIdx = -1;

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

            if (texIdx < 0 && w3dMesh.Textures.Count > 0)
                texIdx = 0;

            return texIdx;
        }

        private static int GetPreferredTextureStage(W3DMesh w3dMesh, int matIndex)
        {
            var vm = GetVertexMaterialForMaterial(w3dMesh, matIndex);
            if (vm?.Info == null)
                return 0;

            int stage0 = (int)(vm.Info.Attributes & 0x00FF0000u);
            int stage1 = (int)(vm.Info.Attributes & 0x0000FF00u);

            if (stage0 == 0x00070000)
                return 0;
            if (stage1 == 0x00000700)
                return 1;

            return 0;
        }

        private static W3DVertexMaterial GetVertexMaterialForMaterial(W3DMesh w3dMesh, int matIndex)
        {
            if (w3dMesh.MaterialPasses.Count > 0)
            {
                var pass = w3dMesh.MaterialPasses[0];
                if (pass.VertexMaterialIds.Count > 0)
                {
                    int vmIndex = pass.VertexMaterialIds[Mathf.Clamp(matIndex, 0, pass.VertexMaterialIds.Count - 1)];
                    if (vmIndex >= 0 && vmIndex < w3dMesh.VertMaterials.Count)
                        return w3dMesh.VertMaterials[vmIndex];
                }
            }

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

        private static void SetTransparent(Material mat)
        {
            var mainTexture = mat.mainTexture;
            var color = GetMaterialColor(mat);

            if (TransparentShader != null && mat.shader != TransparentShader)
                mat.shader = TransparentShader;

            RestoreMaterialState(mat, mainTexture, color);

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
                mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_AlphaSrcBlend"))
                mat.SetFloat("_AlphaSrcBlend", (float)BlendMode.One);
            if (mat.HasProperty("_AlphaDstBlend"))
                mat.SetFloat("_AlphaDstBlend", (float)BlendMode.OneMinusSrcAlpha);

            mat.renderQueue = 3000;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHATEST_ON");
        }

        private static void SetAdditive(Material mat)
        {
            var mainTexture = mat.mainTexture;
            var color = GetMaterialColor(mat);

            if (AdditiveShader != null && mat.shader != AdditiveShader)
                mat.shader = AdditiveShader;

            RestoreMaterialState(mat, mainTexture, color);
            if (mat.HasProperty("_SurfaceType"))
                mat.SetFloat("_SurfaceType", 1f);
            if (mat.HasProperty("_BlendMode"))
                mat.SetFloat("_BlendMode", 1f);
            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)BlendMode.One);
            if (mat.HasProperty("_AlphaSrcBlend"))
                mat.SetFloat("_AlphaSrcBlend", (float)BlendMode.One);
            if (mat.HasProperty("_AlphaDstBlend"))
                mat.SetFloat("_AlphaDstBlend", (float)BlendMode.One);
            if (mat.HasProperty("_ZWrite"))
                mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_TransparentZWrite"))
                mat.SetFloat("_TransparentZWrite", 0f);
            mat.renderQueue = 3100;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
        }

        private static void SetAlphaClip(Material mat)
        {
            if (mat.shader != LitShader && LitShader != null)
                mat.shader = LitShader;

            var mainTexture = mat.mainTexture;
            var color = GetMaterialColor(mat);
            RestoreMaterialState(mat, mainTexture, color);

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
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 2450;
            mat.SetOverrideTag("RenderType", "TransparentCutout");
        }

        private static void RestoreMaterialState(Material mat, Texture mainTexture, Color color)
        {
            if (mainTexture != null)
            {
                mat.mainTexture = mainTexture;
                SetMaterialTexture(mat, mainTexture);
            }

            SetMaterialColor(mat, color);
        }

        private static void SetOpaque(Material mat)
        {
            if (mat == null)
                return;

            var mainTexture = mat.mainTexture;
            var color = GetMaterialColor(mat);

            if (LitShader != null && mat.shader != LitShader)
                mat.shader = LitShader;

            RestoreMaterialState(mat, mainTexture, color);

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

        private static void SetMaterialColor(Material mat, Color color)
        {
            if (mat.HasProperty("_UnlitColor"))
                mat.SetColor("_UnlitColor", color);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
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

        private static void ApplyMeshFlags(Material mat, W3DMesh w3dMesh)
        {
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
