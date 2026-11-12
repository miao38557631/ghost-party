Shader "GhostParty/DecorationTransparent"
{
    Properties
    {
        _MainTex ("PNG", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [Header(Wall Cutaway)]
        _GhostCutawayEnabled ("Wall Cutaway Enabled", Float) = 0
        _GhostWallTop ("Visible Wall Top (world Y)", Float) = 10000
    }
    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        Cull Off

        // Opaque architectural cutaway. Each real wall section has one horizontal top; rear walls stay intact.
        Pass
        {
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            half _Cutoff;
            float _GhostCutawayEnabled;
            float _GhostWallTop;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.worldPos = mul(unity_ObjectToWorld, input.vertex).xyz;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, input.uv) * _Color;
                clip(color.a - _Cutoff);

                if (_GhostCutawayEnabled > 0.5)
                {
                    clip(_GhostWallTop - input.worldPos.y);
                }
                return color;
            }
            ENDCG
        }
    }
    Fallback Off
}
