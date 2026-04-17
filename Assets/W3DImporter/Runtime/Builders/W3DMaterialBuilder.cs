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
                    "W3D/Standard",
                    "Universal Render Pipeline/Lit",
                    "Universal Render Pipeline/Simple Lit",
                    "Standard",
                    "Legacy Shaders/Diffuse",
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
                    "Legacy Shaders/Transparent/Diffuse",
                    "Particles/Standard Unlit",
                    "Sprites/Default",
                    "W3D/Particle",
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
                _additiveShader = Shader.Find("W3D/Additive")
                    ?? Shader.Find("W3D/Particle")
                    ?? Shader.Find("Particles/Standard Unlit")
                    ?? TransparentShader;
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
                mat.color = Color.white;

                if (vm.Info != null)
                {
                    if (vm.Info.Emissive.r > 0.01f || vm.Info.Emissive.g > 0.01f || vm.Info.Emissive.b > 0.01f)
                        mat.SetColor("_EmissionColor", vm.Info.Emissive);
                    if (vm.Info.Emissive.r > 0 || vm.Info.Emissive.g > 0 || vm.Info.Emissive.b > 0)
                        mat.EnableKeyword("_EMISSION");
                }

                ApplyTexture(mat, w3dMesh, i, textureResolver);

                bool blendApplied = false;
                if (w3dMesh.Shaders.Count > 0)
                {
                    int shaderIdx = Mathf.Min(i, w3dMesh.Shaders.Count - 1);
                    blendApplied = ApplyBlendMode(mat, w3dMesh.Shaders[shaderIdx]);
                }

                if (!blendApplied)
                    ApplyTextureDrivenBlendMode(mat, w3dMesh, i);

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
            mat.color = Color.white;

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
            mat.SetTexture("_BaseMap", tex);
            mat.SetTexture("_MainTex", tex);
            Debug.Log($"[W3D] Applied texture '{w3dMesh.Textures[texIdx].File}' to material '{mat.name}'");
        }

        private static void ApplyTextureByName(Material mat, string textureName, W3DTextureResolver resolver)
        {
            if (string.IsNullOrWhiteSpace(textureName))
                return;

            var tex = resolver.Resolve(textureName);
            if (tex == null)
                return;

            mat.mainTexture = tex;
            mat.SetTexture("_BaseMap", tex);
            mat.SetTexture("_MainTex", tex);
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

        private static bool ApplyBlendMode(Material mat, W3DShader shader)
        {
            if (shader == null)
                return false;

            if (shader.DestBlend == 1 && shader.SrcBlend == 1)
            {
                SetAdditive(mat);
                return true;
            }

            if (shader.DestBlend == 3 || shader.SrcBlend == 2)
            {
                SetTransparent(mat);
                return true;
            }

            if (shader.AlphaTest == 1)
            {
                SetAlphaClip(mat);
                return true;
            }

            return false;
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

            if (LooksLikeAdditiveTexture(textureName))
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
            int texIdx = -1;

            if (w3dMesh.MaterialPasses.Count > 0)
            {
                var pass = w3dMesh.MaterialPasses[0];
                if (pass.TxStages.Count > 0 && pass.TxStages[0].TxIds.Count > 0)
                {
                    var txIds = pass.TxStages[0].TxIds[0];
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
            var color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;

            if (TransparentShader != null && mat.shader != TransparentShader)
                mat.shader = TransparentShader;

            RestoreMaterialState(mat, mainTexture, color);

            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))
                mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_ZWrite"))
                mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_AlphaClip"))
                mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_Mode"))
                mat.SetFloat("_Mode", 3f);
            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);

            mat.renderQueue = 3000;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON");
        }

        private static void SetAdditive(Material mat)
        {
            var mainTexture = mat.mainTexture;
            var color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;

            if (AdditiveShader != null && mat.shader != AdditiveShader)
                mat.shader = AdditiveShader;

            RestoreMaterialState(mat, mainTexture, color);
            mat.renderQueue = 3100;
            mat.SetOverrideTag("RenderType", "Transparent");
        }

        private static void SetAlphaClip(Material mat)
        {
            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 0f);
            if (mat.HasProperty("_AlphaClip"))
                mat.SetFloat("_AlphaClip", 1f);
            if (mat.HasProperty("_Cutoff"))
                mat.SetFloat("_Cutoff", 0.5f);
            mat.EnableKeyword("_ALPHATEST_ON");
        }

        private static void RestoreMaterialState(Material mat, Texture mainTexture, Color color)
        {
            if (mainTexture != null)
            {
                mat.mainTexture = mainTexture;
                if (mat.HasProperty("_MainTex"))
                    mat.SetTexture("_MainTex", mainTexture);
                if (mat.HasProperty("_BaseMap"))
                    mat.SetTexture("_BaseMap", mainTexture);
            }

            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
        }

        private static void ApplyMeshFlags(Material mat, W3DMesh w3dMesh)
        {
            if (!w3dMesh.EffectiveTwoSided)
                return;

            if (mat.HasProperty("_Cull"))
                mat.SetFloat("_Cull", (float)CullMode.Off);
            if (mat.HasProperty("_CullMode"))
                mat.SetFloat("_CullMode", (float)CullMode.Off);
            if (mat.HasProperty("_DoubleSidedEnable"))
                mat.SetFloat("_DoubleSidedEnable", 1f);
        }

        private static Material CreateDefaultMaterial()
        {
            var mat = new Material(LitShader);
            mat.name = "W3D_Default";
            mat.color = Color.gray;
            return mat;
        }
    }
}
