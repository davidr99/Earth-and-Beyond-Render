Shader "W3D/UnlitVertexColor"
{
    // HDRP-compatible unlit shader that multiplies texture by per-vertex color.
    // Used for E&B glow meshes whose DCG chunk carries the tint (e.g. GLOW25's
    // blue corner vertex) that HDRP/Unlit otherwise drops on the floor.
    Properties
    {
        [MainTexture] _BaseColorMap("Color Map", 2D) = "white" {}
        _DetailTex ("Detail Texture", 2D) = "white" {}
        [MainColor]   _BaseColor   ("Color", Color)  = (1,1,1,1)
        _DetailColorFunc ("Detail Color Func", Float) = 0
        _DetailAlphaFunc ("Detail Alpha Func", Float) = 0
        _DetailUseUV2 ("Detail Uses UV2", Float) = 1

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
                float _DetailColorFunc;
                float _DetailAlphaFunc;
                float _DetailUseUV2;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
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
                output.uv         = TRANSFORM_TEX(input.uv, _BaseColorMap);
                float2 detailUv   = (_DetailUseUV2 > 0.5) ? input.uv2 : input.uv;
                output.uv2        = TRANSFORM_TEX(detailUv, _DetailTex);
                output.color      = input.color;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_BaseColorMap, sampler_BaseColorMap, input.uv);
                float4 detailTex = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, input.uv2);
                float4 col = tex * input.color * _BaseColor;
                int detailColorFunc = (int)floor(_DetailColorFunc + 0.5);
                int detailAlphaFunc = (int)floor(_DetailAlphaFunc + 0.5);
                if (detailColorFunc != 0 || detailAlphaFunc != 0)
                {
                    col.rgb = ApplyDetailColor(col, detailTex, detailColorFunc);
                    col.a = ApplyDetailAlpha(col.a, detailTex.a, detailAlphaFunc);
                }
                return col;
            }
            ENDHLSL
        }
    }

}
