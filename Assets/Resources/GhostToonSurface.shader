Shader "GhostParty/Toon/Surface"
{
    Properties
    {
        _MainTex ("Painted Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [Header(Painted Volume Mask)]
        _ShadeMask ("Grayscale Shade Mask", 2D) = "white" {}
        _ShadeMaskStrength ("Shadow Strength", Range(0, 1)) = 0
        _ShadeThreshold ("Shadow Boundary", Range(0, 1)) = 0.58
        _ShadeMinBrightness ("Shadow Brightness", Range(0, 1)) = 0.72
        [Enum(Sable,0,Cult,1,Hybrid,2)] _ToonLook ("Look", Float) = 0
        [Header(Ink Outline)]
        _OutlinePixels ("Outer Width (screen pixels)", Range(0, 4)) = 1.75
        _OutlineColor ("Outer Color", Color) = (0.12, 0.08, 0.16, 1)
        [HideInInspector] _GhostSourceShader ("Original Shader Kind", Float) = -1
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
        [HideInInspector] _GhostCutawayEnabled ("Wall Cutaway Enabled", Float) = 0
        [HideInInspector] _GhostWallTop ("Visible Wall Top", Float) = 10000
    }

    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        Cull Off

        Pass
        {
            Tags { "LightMode" = "Always" }
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Assets/Resources/GhostToonLighting.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _OutlineColor;
            half _Cutoff;
            half _OutlinePixels;
            float _GhostCutawayEnabled;
            float _GhostWallTop;

            v2f vert(appdata_full v)
            {
                v2f output;
                output.pos = UnityObjectToClipPos(v.vertex);
                output.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                output.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                if (_OutlinePixels <= 0.01h) return fixed4(0, 0, 0, 0);
                if (_GhostCutawayEnabled > 0.5)
                    clip(_GhostWallTop - input.worldPos.y);
                half center = tex2D(_MainTex, input.uv).a * _Color.a;
                float2 pixel = float2(length(float2(ddx(input.uv.x), ddy(input.uv.x))),
                                      length(float2(ddx(input.uv.y), ddy(input.uv.y)))) * _OutlinePixels;
                half neighbor = GhostToonOutlineCoverage(_MainTex, input.uv, pixel) * _Color.a;
                half opacity = smoothstep(0.005h, 0.12h, neighbor) *
                               (1.0h - smoothstep(_Cutoff * 0.6h, _Cutoff, center)) * _OutlineColor.a;
                return fixed4(_OutlineColor.rgb, opacity);
            }
            ENDCG
        }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            ZWrite On
            AlphaToMask On

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            #include "Assets/Resources/GhostToonLighting.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                half3 worldNormal : TEXCOORD2;
                SHADOW_COORDS(3)
            };

            sampler2D _MainTex;
            sampler2D _ShadeMask;
            float4 _MainTex_ST;
            fixed4 _Color;
            half _Cutoff;
            half _ShadeMaskStrength;
            half _ShadeThreshold;
            half _ShadeMinBrightness;
            float _GhostCutawayEnabled;
            float _GhostWallTop;

            v2f vert(appdata_full v)
            {
                v2f output;
                output.pos = UnityObjectToClipPos(v.vertex);
                output.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                output.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(v.normal);
                TRANSFER_SHADOW(output);
                return output;
            }

            fixed4 frag(v2f input, fixed face : VFACE) : SV_Target
            {
                fixed4 paint = tex2D(_MainTex, input.uv) * _Color;
                if (_GhostCutawayEnabled > 0.5)
                    clip(_GhostWallTop - input.worldPos.y);
                clip(paint.a - _Cutoff);

                half3 normal = normalize(input.worldNormal) * (face >= 0 ? 1 : -1);
                half3 lightDirection = normalize(UnityWorldSpaceLightDir(input.worldPos));
                half ndotl = saturate(dot(normal, lightDirection));
                half shadow = SHADOW_ATTENUATION(input);
                paint.rgb = GhostToonShade(paint.rgb, ndotl, shadow, _LightColor0.rgb);
                if (_ShadeMaskStrength > 0.001h)
                {
                    half mask = tex2D(_ShadeMask, input.uv).r;
                    half edge = max(fwidth(mask), 0.002h);
                    half lit = smoothstep(_ShadeThreshold - edge, _ShadeThreshold + edge, mask);
                    paint.rgb *= lerp(1.0h, lerp(_ShadeMinBrightness, 1.0h, lit), _ShadeMaskStrength);
                }
                return paint;
            }
            ENDCG
        }

        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"

            struct v2f
            {
                V2F_SHADOW_CASTER;
                float2 uv : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            half _Cutoff;
            float _GhostCutawayEnabled;
            float _GhostWallTop;

            v2f vert(appdata_full v)
            {
                v2f output;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(output);
                output.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                output.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return output;
            }

            float4 frag(v2f input) : SV_Target
            {
                clip(tex2D(_MainTex, input.uv).a * _Color.a - _Cutoff);
                if (_GhostCutawayEnabled > 0.5)
                    clip(_GhostWallTop - input.worldPos.y);
                SHADOW_CASTER_FRAGMENT(input)
            }
            ENDCG
        }
    }
    Fallback Off
}
