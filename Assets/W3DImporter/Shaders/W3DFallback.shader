Shader "W3D/Standard"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _DetailTex ("Detail Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _EmissionColor ("Emission", Color) = (0,0,0,0)
        [Toggle] _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Toggle] _ZWrite ("ZWrite", Float) = 1
        [Toggle] _DebugUV ("Debug UVs", Float) = 0
        _DetailColorFunc ("Detail Color Func", Float) = 0
        _DetailAlphaFunc ("Detail Alpha Func", Float) = 0
        _DetailUseUV2 ("Detail Uses UV2", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        Cull [_Cull]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float4 vertex : SV_POSITION;
                float3 normal : TEXCOORD2;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _DetailTex;
            float4 _DetailTex_ST;
            fixed4 _Color;
            fixed4 _EmissionColor;
            float _AlphaClip;
            float _Cutoff;
            float _DebugUV;
            float _DetailColorFunc;
            float _DetailAlphaFunc;
            float _DetailUseUV2;

            fixed3 ApplyDetailColor(fixed4 localCol, fixed4 detailCol, int func)
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

            fixed ApplyDetailAlpha(fixed localAlpha, fixed detailAlpha, int func)
            {
                switch (func)
                {
                    case 1: return detailAlpha;
                    case 2: return saturate(localAlpha * detailAlpha);
                    case 3: return saturate(localAlpha + detailAlpha - (localAlpha * detailAlpha));
                    default: return localAlpha;
                }
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                float2 detailUv = (_DetailUseUV2 > 0.5) ? v.uv2 : v.uv;
                o.uv2 = TRANSFORM_TEX(detailUv, _DetailTex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Debug mode: show UVs as red/green
                if (_DebugUV > 0.5)
                    return fixed4(i.uv.x, i.uv.y, 0, 1);

                fixed4 tex = tex2D(_MainTex, i.uv);
                fixed4 detailTex = tex2D(_DetailTex, i.uv2);

                // Simple lighting
                float light = dot(normalize(i.normal), float3(0.3, 0.8, 0.2)) * 0.4 + 0.6;

                fixed4 col = tex * _Color * i.color;
                int detailColorFunc = (int)floor(_DetailColorFunc + 0.5);
                int detailAlphaFunc = (int)floor(_DetailAlphaFunc + 0.5);
                if (detailColorFunc != 0 || detailAlphaFunc != 0)
                {
                    col.rgb = ApplyDetailColor(col, detailTex, detailColorFunc);
                    col.a = ApplyDetailAlpha(col.a, detailTex.a, detailAlphaFunc);
                }
                if (_AlphaClip > 0.5)
                    clip(col.a - _Cutoff);
                col.rgb *= light;
                //col.rgb += _EmissionColor.rgb;
                return col;
            }
            ENDCG
        }
    }
    FallBack "Unlit/Texture"
}
