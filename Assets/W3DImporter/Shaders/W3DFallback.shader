Shader "W3D/Opaque"
{
    // HDRP-compatible opaque W3D base shader. Transparent variants live in
    // W3D/Transparent so fixed-function W3D meshes can opt into HDRP's opaque
    // pipeline by default and only use the transparent path when their W3D
    // blend state actually requires it.
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
        _PriGradient ("Primary Gradient", Float) = 1
        [HideInInspector] _BaseTexGen ("Stage 0 Texgen", Float) = 0
        [HideInInspector] _BaseTexGenAxis ("Stage 0 Texgen Axis", Float) = 2
        [HideInInspector] _BaseTexGenGrid ("Stage 0 Texgen Grid", Vector) = (1,0,0,1)
        [HideInInspector] _DetailTexGen ("Stage 1 Texgen", Float) = 0
        [HideInInspector] _DetailTexGenAxis ("Stage 1 Texgen Axis", Float) = 2
        [HideInInspector] _DetailTexGenGrid ("Stage 1 Texgen Grid", Vector) = (1,0,0,1)
        _DetailUseUV2 ("Detail Uses UV2", Float) = 1
        [Toggle] _ApplyLighting ("Apply Lighting", Float) = 1
        [Toggle] _UseVertexColor ("Use Vertex Color", Float) = 0
        [Toggle] _PremultiplyAlpha ("Premultiply Alpha", Float) = 0
        [HideInInspector] _FlipBackfaceU ("Flip Backface U", Float) = 0
        [HideInInspector] _W3DViewerAmbientColor ("W3D Viewer Ambient", Color) = (0.18,0.18,0.18,1)
        [HideInInspector] _W3DViewerLightDirection ("W3D Viewer Light Direction", Vector) = (0.35,0.85,0.35,0)
        [HideInInspector] _W3DViewerLightColor ("W3D Viewer Light Color", Color) = (1,1,1,1)
        [HideInInspector] _W3DViewerLightIntensity ("W3D Viewer Light Intensity", Float) = 1.1

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
                float4 _EmissiveColor;
                float _AlphaClip;
                float _Cutoff;
                float _DebugUV;
                float _DetailColorFunc;
                float _DetailAlphaFunc;
                float _PriGradient;
                float _BaseTexGen;
                float _BaseTexGenAxis;
                float4 _BaseTexGenGrid;
                float _DetailTexGen;
                float _DetailTexGenAxis;
                float4 _DetailTexGenGrid;
                float _DetailUseUV2;
                float _ApplyLighting;
                float _UseVertexColor;
                float _PremultiplyAlpha;
                float _FlipBackfaceU;
                float4 _W3DViewerAmbientColor;
                float4 _W3DViewerLightDirection;
                float4 _W3DViewerLightColor;
                float _W3DViewerLightIntensity;
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



            float3 ApplyW3DPreviewLighting(float3 color, float3 normalWS)
            {
                float3 normal = normalWS;
                if (dot(normal, normal) <= 0.0001)
                    normal = float3(0.0, 1.0, 0.0);
                normal = normalize(normal);

                float3 lightDir = _W3DViewerLightDirection.xyz;
                if (dot(lightDir, lightDir) <= 0.0001)
                    lightDir = float3(0.3, 0.8, 0.2);
                lightDir = normalize(lightDir);

                float3 ambient = _W3DViewerAmbientColor.rgb;
                float intensity = max(_W3DViewerLightIntensity, 0.0);
                float3 lightColor = _W3DViewerLightColor.rgb;
                float diffuse = max(abs(dot(normal, lightDir)), 0.35) * intensity;
                return color * saturate(ambient + lightColor * diffuse);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                // environment mappers generate their stage's coordinates (W3DTexGen)
                float3 normalWS   = normalize(output.normalWS);
                float3 reflectWS  = reflect(-GetWorldSpaceNormalizeViewDir(positionWS), normalWS);
                float3x3 worldToView = (float3x3)GetWorldToViewMatrix();
                float3 normalVS   = normalize(mul(worldToView, normalWS));
                float3 reflectVS  = mul(worldToView, reflectWS);
                output.uv         = W3DTexGen(_BaseTexGen, _BaseTexGenAxis, _BaseTexGenGrid, input.uv,
                                              normalVS, reflectVS, normalWS, reflectWS);
                output.uv2        = W3DTexGen(_DetailTexGen, _DetailTexGenAxis, _DetailTexGenGrid,
                                              (_DetailUseUV2 > 0.5) ? input.uv2 : input.uv,
                                              normalVS, reflectVS, normalWS, reflectWS);
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

                // stage 0: texture combined with the (lit) gradient, as the W3D shader's PriGradient says
                float3 gradient = _BaseColor.rgb;
                if (_UseVertexColor > 0.5)
                    gradient *= input.color.rgb;
                if (_ApplyLighting > 0.5)
                    gradient = ApplyW3DPreviewLighting(gradient, isFrontFace ? input.normalWS : -input.normalWS);

                float4 col;
                col.rgb = W3DApplyPrimaryGradient(tex.rgb, gradient, (int)floor(_PriGradient + 0.5));
                col.a = tex.a * _BaseColor.a;

                int detailColorFunc = (int)floor(_DetailColorFunc + 0.5);
                int detailAlphaFunc = (int)floor(_DetailAlphaFunc + 0.5);
                if (detailColorFunc != 0 || detailAlphaFunc != 0)
                {
                    col.rgb = W3DApplyDetailColor(col, detailTex, detailColorFunc);
                    col.a = W3DApplyDetailAlpha(col.a, detailTex.a, detailAlphaFunc);
                }

                if (_AlphaClip > 0.5)
                    clip(col.a - _Cutoff);

                // E&B vertex materials frequently carry emissive=(255,255,255,0)
                // on plain textured meshes (e.g. FLOOR_02 in jbar01_h.w3d). Adding
                // (1,1,1) unconditionally blows every surface out to white, so the
                // original opaque W3D path deliberately did not sample emissive here.
                // Leave the _EmissiveColor property exposed for inspector use but
                // do not fold it into the fragment color by default.

                if (_PremultiplyAlpha > 0.5)
                    col.rgb *= col.a;

                return col;
            }
            ENDHLSL
        }
    }
}
