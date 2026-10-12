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
        [Toggle] _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _DetailColorFunc ("Detail Color Func", Float) = 0
        _DetailAlphaFunc ("Detail Alpha Func", Float) = 0
        _PriGradient ("Primary Gradient", Float) = 1
        _DetailUseUV2 ("Detail Uses UV2", Float) = 1
        [Toggle] _PremultiplyAlpha ("Premultiply Alpha", Float) = 0
        [HideInInspector] _FlipBackfaceU ("Flip Backface U", Float) = 0

        [HideInInspector] _SrcBlend ("Src Blend", Float) = 1.0
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 0.0
        [HideInInspector] _ZWrite   ("ZWrite", Float)    = 1.0
        [HideInInspector] _CullMode ("Cull", Float)      = 2.0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

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
            #include "W3DCombine.hlsl"

            TEXTURE2D(_BaseColorMap);
            SAMPLER(sampler_BaseColorMap);
            TEXTURE2D(_DetailTex);
            SAMPLER(sampler_DetailTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColorMap_ST;
                float4 _DetailTex_ST;
                float4 _BaseColor;
                float _AlphaClip;
                float _Cutoff;
                float _DetailColorFunc;
                float _DetailAlphaFunc;
                float _PriGradient;
                float _DetailUseUV2;
                float _PremultiplyAlpha;
                float _FlipBackfaceU;
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



            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
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

                float4 tex = SAMPLE_TEXTURE2D(_BaseColorMap, sampler_BaseColorMap, baseUv);
                float4 detailTex = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, detailUv);
                float4 col = tex * input.color * _BaseColor;
                int detailColorFunc = (int)floor(_DetailColorFunc + 0.5);
                int detailAlphaFunc = (int)floor(_DetailAlphaFunc + 0.5);
                if (detailColorFunc != 0 || detailAlphaFunc != 0)
                {
                    col.rgb = W3DApplyDetailColor(col, detailTex, detailColorFunc);
                    col.a = W3DApplyDetailAlpha(col.a, detailTex.a, detailAlphaFunc);
                }
                if (_AlphaClip > 0.5)
                    clip(col.a - _Cutoff);
                if (_PremultiplyAlpha > 0.5)
                    col.rgb *= col.a;
                return col;
            }
            ENDHLSL
        }
    }

}
