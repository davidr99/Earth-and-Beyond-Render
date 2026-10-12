Shader "W3D/Particle"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _DetailTex ("Detail Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        [Toggle] _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [Toggle] _ApplyLighting ("Apply Lighting", Float) = 1
        [Toggle] _UseVertexColor ("Use Vertex Color", Float) = 0
        [Toggle] _PremultiplyAlpha ("Premultiply Alpha", Float) = 0
        [HideInInspector] _FlipBackfaceU ("Flip Backface U", Float) = 0
        [HideInInspector] _W3DViewerAmbientColor ("W3D Viewer Ambient", Color) = (0.18,0.18,0.18,1)
        [HideInInspector] _W3DViewerLightDirection ("W3D Viewer Light Direction", Vector) = (0.35,0.85,0.35,0)
        [HideInInspector] _W3DViewerLightColor ("W3D Viewer Light Color", Color) = (1,1,1,1)
        [HideInInspector] _W3DViewerLightIntensity ("W3D Viewer Light Intensity", Float) = 1.1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        [Toggle] _ZWrite ("ZWrite", Float) = 0
        _DetailColorFunc ("Detail Color Func", Float) = 0
        _DetailAlphaFunc ("Detail Alpha Func", Float) = 0
        _PriGradient ("Primary Gradient", Float) = 1
        _DetailUseUV2 ("Detail Uses UV2", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        Cull [_Cull]

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "W3DCombine.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _DetailTex;
            float4 _DetailTex_ST;
            fixed4 _Color;
            float _AlphaClip;
            float _Cutoff;
            float _ApplyLighting;
            float _UseVertexColor;
            float _PremultiplyAlpha;
            float _FlipBackfaceU;
            float _DetailColorFunc;
            float _DetailAlphaFunc;
            float _PriGradient;
            float _DetailUseUV2;
            float4 _W3DViewerAmbientColor;
            float4 _W3DViewerLightDirection;
            float4 _W3DViewerLightColor;
            float _W3DViewerLightIntensity;



            fixed3 ApplyW3DPreviewLighting(fixed3 color, float3 normalWS)
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

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.uv2 = (_DetailUseUV2 > 0.5) ? v.uv2 : v.uv;
                o.normalWS = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i, fixed facing : VFACE) : SV_Target
            {
                float2 baseUv = i.uv;
                float2 detailUv = i.uv2;
                if (_FlipBackfaceU > 0.5 && facing < 0)
                {
                    baseUv.x = 1.0 - baseUv.x;
                    detailUv.x = 1.0 - detailUv.x;
                }

                baseUv = TRANSFORM_TEX(baseUv, _MainTex);
                detailUv = TRANSFORM_TEX(detailUv, _DetailTex);

                fixed4 tex = tex2D(_MainTex, baseUv);
                fixed4 detailTex = tex2D(_DetailTex, detailUv);
                // stage 0: texture combined with the (lit) gradient, as the W3D shader's PriGradient says
                fixed3 gradient = _Color.rgb;
                if (_UseVertexColor > 0.5)
                    gradient *= i.color.rgb;
                if (_ApplyLighting > 0.5)
                    gradient = ApplyW3DPreviewLighting(gradient, facing < 0 ? -i.normalWS : i.normalWS);

                fixed4 col;
                col.rgb = W3DApplyPrimaryGradient(tex.rgb, gradient, (int)floor(_PriGradient + 0.5));
                col.a = tex.a * _Color.a * i.color.a;

                int detailColorFunc = (int)floor(_DetailColorFunc + 0.5);
                int detailAlphaFunc = (int)floor(_DetailAlphaFunc + 0.5);
                if (detailColorFunc != 0 || detailAlphaFunc != 0)
                {
                    col.rgb = W3DApplyDetailColor(col, detailTex, detailColorFunc);
                    col.a = W3DApplyDetailAlpha(col.a, detailTex.a, detailAlphaFunc);
                }

                if (_PremultiplyAlpha > 0.5)
                    col.rgb *= col.a;

                if (_AlphaClip > 0.5)
                    clip(col.a - _Cutoff);

                return col;
            }
            ENDCG
        }
    }
    FallBack "Sprites/Default"
}
