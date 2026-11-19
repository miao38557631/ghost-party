Shader "GhostParty/WallOcclusionTransparent"
{
    Properties
    {
        _MainTex ("Wall Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 0.3)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off

        // Capture the scene once, before any faded wall draws. Every overlapping
        // section then composites against the same image instead of stacking alpha.
        GrabPass { "_GhostWallBackdrop" }

        Pass
        {
            // All faded wall sections share one screen-space coverage mask.
            // The first section draws at 30%; later sections cannot darken the same pixel.
            Stencil
            {
                Ref 128
                ReadMask 128
                WriteMask 128
                Comp NotEqual
                Pass Replace
            }

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
                float4 grabPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            sampler2D _GhostWallBackdrop;
            float4 _MainTex_ST;
            fixed4 _Color;
            half _Cutoff;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.grabPos = ComputeGrabScreenPos(output.vertex);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 wall = tex2D(_MainTex, input.uv) * _Color;
                clip(wall.a - _Cutoff * _Color.a);
                fixed3 backdrop = tex2Dproj(_GhostWallBackdrop, UNITY_PROJ_COORD(input.grabPos)).rgb;
                fixed3 result = lerp(backdrop, wall.rgb, wall.a);

                // The upper room wall has dark empty space behind it, while lower
                // walls have a pink floor. Lift only dark backdrops toward the
                // wall's own color so the same fade does not turn gray overhead.
                fixed luminance = dot(backdrop, fixed3(0.2126, 0.7152, 0.0722));
                fixed darkBackdrop = saturate((0.45 - luminance) / 0.35);
                fixed3 pinkFloor = wall.rgb * fixed3(0.72, 0.62, 0.68);
                result = lerp(result, max(result, pinkFloor), darkBackdrop * 0.8);
                return fixed4(result, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
