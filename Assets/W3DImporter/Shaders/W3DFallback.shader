Shader "W3D/Standard"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _EmissionColor ("Emission", Color) = (0,0,0,0)
        [Toggle] _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Toggle] _ZWrite ("ZWrite", Float) = 1
        [Toggle] _DebugUV ("Debug UVs", Float) = 0
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
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 normal : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _EmissionColor;
            float _AlphaClip;
            float _Cutoff;
            float _DebugUV;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Debug mode: show UVs as red/green
                if (_DebugUV > 0.5)
                    return fixed4(i.uv.x, i.uv.y, 0, 1);

                fixed4 tex = tex2D(_MainTex, i.uv);

                // Simple lighting
                float light = dot(normalize(i.normal), float3(0.3, 0.8, 0.2)) * 0.4 + 0.6;

                fixed4 col = tex * _Color;
                if (_AlphaClip > 0.5)
                    clip(col.a - _Cutoff);
                col.rgb *= light;
                col.rgb += _EmissionColor.rgb;
                return col;
            }
            ENDCG
        }
    }
    FallBack "Unlit/Texture"
}
