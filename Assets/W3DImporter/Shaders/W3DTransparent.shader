Shader "W3D/Transparent"
{
    // HDRP-compatible transparent W3D shader. This mirrors the opaque path but
    // keeps transparent shader tags so HDRP sorts and renders alpha-blended W3D
    // materials through the correct pipeline stage.
    Properties
    {
        [MainTexture] _BaseColorMap ("Color Map", 2D) = "white" {}
        _DetailTex ("Detail Texture", 2D) = "white" {}
        [MainColor]   _BaseColor   ("Color", Color)  = (1,1,1,1)
        _EmissiveColor ("Emission", Color) = (0,0,0,0)
        [Toggle] _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [Toggle] _DebugUV ("Debug UVs", Float) = 0
        _DetailColorFunc ("Detail Color Func", Float) = 0
        _DetailAlphaFunc ("Detail Alpha Func", Float) = 0
        _DetailUseUV2 ("Detail Uses UV2", Float) = 1
        [Toggle] _ApplyLighting ("Apply Lighting", Float) = 1
        [Toggle] _UseVertexColor ("Use Vertex Color", Float) = 0
        [Toggle] _PremultiplyAlpha ("Premultiply Alpha", Float) = 0
        [HideInInspector] _FlipBackfaceU ("Flip Backface U", Float) = 0

        [HideInInspector] _SrcBlend ("Src Blend", Float) = 1.0
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 0.0
        [HideInInspector] _ZWrite   ("ZWrite", Float)    = 1.0
        [HideInInspector] _CullMode ("Cull", Float)      = 2.0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            Cull   [_CullMode]
            ZWrite [_ZWrite]
            Blend  [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_BaseColorMap);
            SAMPLER(sampler_BaseColorMap);
            TEXTURE2D(_DetailTex);
            SAMPLER(sampler_DetailTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColorMap_ST;
                float4 _DetailTex_ST;
                float4 _BaseColor;
                float4 _EmissiveColor;
                float _AlphaClip;
                float _Cutoff;
                float _DebugUV;
                float _DetailColorFunc;
                float _DetailAlphaFunc;
                float _DetailUseUV2;
                float _ApplyLighting;
                float _UseVertexColor;
                float _PremultiplyAlpha;
                float _FlipBackfaceU;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD2;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
                float4 color      : COLOR;
            };

            float3 ApplyDetailColor(float4 localCol, float4 detailCol, int func)
            {
                switch (func)
                {
                    case 1: return detailCol.rgb;
                    case 2: return localCol.rgb * detailCol.rgb;
                    case 3: return saturate(localCol.rgb + detailCol.rgb - (localCol.rgb * detailCol.rgb));
                    case 4: return saturate(localCol.rgb + detailCol.rgb);
                    case 5: return saturate(localCol.rgb - detailCol.rgb);
                    case 6: return saturate(detailCol.rgb - localCol.rgb);
                    case 7: return lerp(localCol.rgb, detailCol.rgb, detailCol.a);
                    case 8: return lerp(localCol.rgb, detailCol.rgb, localCol.a);
                    default: return localCol.rgb;
                }
            }

            float ApplyDetailAlpha(float localAlpha, float detailAlpha, int func)
            {
                switch (func)
                {
                    case 1: return detailAlpha;
                    case 2: return saturate(localAlpha * detailAlpha);
                    case 3: return saturate(localAlpha + detailAlpha - (localAlpha * detailAlpha));
                    default: return localAlpha;
                }
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.uv         = input.uv;
                output.uv2        = (_DetailUseUV2 > 0.5) ? input.uv2 : input.uv;
                output.color      = input.color;
                return output;
            }

            float4 Frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                float2 baseUv = input.uv;
                float2 detailUv = input.uv2;
                if (_FlipBackfaceU > 0.5 && !isFrontFace)
                {
                    baseUv.x = 1.0 - baseUv.x;
                    detailUv.x = 1.0 - detailUv.x;
                }

                baseUv = baseUv * _BaseColorMap_ST.xy + _BaseColorMap_ST.zw;
                detailUv = detailUv * _DetailTex_ST.xy + _DetailTex_ST.zw;

                if (_DebugUV > 0.5)
                    return float4(baseUv.x, baseUv.y, 0, 1);

                float4 tex = SAMPLE_TEXTURE2D(_BaseColorMap, sampler_BaseColorMap, baseUv);
                float4 detailTex = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, detailUv);

                float4 col = tex * _BaseColor;
                if (_UseVertexColor > 0.5)
                    col.rgb *= input.color.rgb;

                int detailColorFunc = (int)floor(_DetailColorFunc + 0.5);
                int detailAlphaFunc = (int)floor(_DetailAlphaFunc + 0.5);
                if (detailColorFunc != 0 || detailAlphaFunc != 0)
                {
                    col.rgb = ApplyDetailColor(col, detailTex, detailColorFunc);
                    col.a = ApplyDetailAlpha(col.a, detailTex.a, detailAlphaFunc);
                }

                if (_AlphaClip > 0.5)
                    clip(col.a - _Cutoff);

                if (_ApplyLighting > 0.5)
                {
                    float light = dot(normalize(input.normalWS), float3(0.3, 0.8, 0.2)) * 0.4 + 0.6;
                    col.rgb *= light;
                }

                if (_PremultiplyAlpha > 0.5)
                    col.rgb *= col.a;

                return col;
            }
            ENDHLSL
        }
    }
}
