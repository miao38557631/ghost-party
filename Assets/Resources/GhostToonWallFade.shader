Shader "GhostParty/Toon/WallFade"
{
    Properties
    {
        _MainTex ("Wall Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 0.3)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [Enum(Sable,0,Cult,1,Hybrid,2)] _ToonLook ("Look", Float) = 0
        [Header(Ink Outline)]
        _OutlinePixels ("Outer Width (screen pixels)", Range(0, 4)) = 1.75
        _OutlineColor ("Outer Color", Color) = (0.12, 0.08, 0.16, 1)
        [Header(Sable)]
        _SableShadowTint ("Shadow Color", Color) = (0.78, 0.8, 0.88, 1)
        _SableLightTint ("Lit Color", Color) = (1, 0.98, 0.94, 1)
        _SableThreshold ("Light Threshold", Range(0, 1)) = 0.46
        _SableSoftness ("Edge Softness", Range(0.001, 0.3)) = 0.025
        [Header(Cult of the Lamb)]
        _CultDeepTint ("Deep Shadow Color", Color) = (0.69, 0.57, 0.73, 1)
        _CultShadowTint ("Colored Shadow", Color) = (0.78, 0.61, 0.76, 1)
        _CultLightTint ("Lit Color", Color) = (1, 0.95, 0.85, 1)
        _CultDarkThreshold ("Deep Shadow Threshold", Range(0, 1)) = 0.18
        _CultLightThreshold ("Light Threshold", Range(0, 1)) = 0.58
        _CultSoftness ("Edge Softness", Range(0.001, 0.3)) = 0.09
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
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            #include "Assets/Resources/GhostToonLighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 grabPos : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                half3 worldNormal : TEXCOORD3;
                SHADOW_COORDS(4)
            };

            sampler2D _MainTex;
            sampler2D _GhostWallBackdrop;
            float4 _MainTex_ST;
            fixed4 _Color;
            half _Cutoff;
            half _OutlinePixels;
            fixed4 _OutlineColor;

            v2f vert(appdata v)
            {
                v2f output;
                output.pos = UnityObjectToClipPos(v.vertex);
                output.uv = TRANSFORM_TEX(v.uv, _MainTex);
                output.grabPos = ComputeGrabScreenPos(output.pos);
                output.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(v.normal);
                TRANSFER_SHADOW(output);
                return output;
            }

            fixed4 frag(v2f input, fixed face : VFACE) : SV_Target
            {
                fixed4 painted = tex2D(_MainTex, input.uv);
                fixed4 wall = painted * _Color;
                float2 outlinePixel = float2(length(float2(ddx(input.uv.x), ddy(input.uv.x))),
                                             length(float2(ddx(input.uv.y), ddy(input.uv.y)))) * _OutlinePixels;
                if (painted.a < _Cutoff && _OutlinePixels > 0.01h)
                {
                    half neighbor = GhostToonOutlineCoverage(_MainTex, input.uv, outlinePixel);
                    half opacity = smoothstep(0.005h, 0.12h, neighbor) *
                                   (1.0h - smoothstep(_Cutoff * 0.6h, _Cutoff, painted.a));
                    clip(opacity - 0.001h);
                    wall.rgb = _OutlineColor.rgb;
                    wall.a = _Color.a * opacity;
                }
                else
                {
                    clip(painted.a - _Cutoff);
                    half3 normal = normalize(input.worldNormal) * (face >= 0 ? 1 : -1);
                    half3 lightDirection = normalize(UnityWorldSpaceLightDir(input.worldPos));
                    half ndotl = saturate(dot(normal, lightDirection));
                    wall.rgb = GhostToonShade(wall.rgb, ndotl, SHADOW_ATTENUATION(input), _LightColor0.rgb);
                }
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
