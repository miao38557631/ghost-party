Shader "GhostParty/DecorationTransparent"
{
    Properties
    {
        _MainTex ("PNG", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [Header(Ghost Visibility Cutout)]
        _GhostCutHalfWidth ("Cutout Half Width (fraction of screen height)", Float) = 0
        _GhostCutFeather ("Cutout Edge Feather (fraction of screen height)", Float) = 0.025
        _GhostCutPos ("Cutout Center (viewport 0-1)", Vector) = (0.5, 0.5, 0, 0)
        _GhostCutPlayer ("Ghost World Position", Vector) = (0, 0, 0, 0)
        _GhostCutGroundForward ("Camera Ground Forward", Vector) = (0, 0, 1, 0)
    }
    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        Cull Off

        // Keep the wall opaque and cut a softly feathered vertical opening only in front of the ghost.
        Pass
        {
            ZWrite On
            AlphaToMask On

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
                float4 screen : TEXCOORD2;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            half _Cutoff;
            float _GhostCutHalfWidth;
            float _GhostCutFeather;
            float4 _GhostCutPos;
            float4 _GhostCutPlayer;
            float4 _GhostCutGroundForward;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.worldPos = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.screen = ComputeScreenPos(output.vertex);
                return output;
            }

            half EdgeDither(float2 pixel)
            {
                float2 cell = frac(floor(pixel) * 0.5);
                half x = cell.x < 0.5 ? 0 : 1;
                half y = cell.y < 0.5 ? 0 : 1;
                return (x < 0.5 ? (y < 0.5 ? 0.125 : 0.625)
                                : (y < 0.5 ? 0.875 : 0.375));
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, input.uv) * _Color;
                clip(color.a - _Cutoff);

                if (_GhostCutHalfWidth > 0.0001)
                {
                    float screenX = input.screen.x / input.screen.w;
                    float horizontalDistance = abs(screenX - _GhostCutPos.x) *
                        (_ScreenParams.x / _ScreenParams.y);
                    // Compare on the floor plane: the top of a rear wall must not count as foreground.
                    float groundDepth = dot((input.worldPos - _GhostCutPlayer.xyz).xz,
                        _GhostCutGroundForward.xz);
                    if (groundDepth < -0.05)
                    {
                        float feather = max(_GhostCutFeather, 0.0001);
                        half wallCoverage = smoothstep(_GhostCutHalfWidth - feather,
                            _GhostCutHalfWidth + feather, horizontalDistance);
                        clip(wallCoverage - EdgeDither(input.vertex.xy));
                    }
                }
                return color;
            }
            ENDCG
        }
    }
    Fallback Off
}
