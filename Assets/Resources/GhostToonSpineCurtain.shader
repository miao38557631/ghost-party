Shader "GhostParty/Toon/SpineCurtain"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Spine Atlas", 2D) = "black" {}
        _Cutoff ("Shadow Alpha Cutoff", Range(0, 1)) = 0.1
        [Toggle(_STRAIGHT_ALPHA_INPUT)] _StraightAlphaInput ("Straight Alpha Texture", Float) = 0
        [Enum(Sable,0,Cult,1,Hybrid,2)] _ToonLook ("Look", Float) = 2
        _ToonStrength ("Toon Lighting Strength", Range(0, 1)) = 1
        _SableShadowTint ("Sable Shadow", Color) = (0.78, 0.8, 0.88, 1)
        _SableLightTint ("Sable Light", Color) = (1, 0.98, 0.94, 1)
        _SableThreshold ("Sable Threshold", Range(0, 1)) = 0.46
        _SableSoftness ("Sable Softness", Range(0.001, 0.3)) = 0.025
        _CultDeepTint ("Cult Deep", Color) = (0.69, 0.57, 0.73, 1)
        _CultShadowTint ("Cult Shadow", Color) = (0.78, 0.61, 0.76, 1)
        _CultLightTint ("Cult Light", Color) = (1, 0.95, 0.85, 1)
        _CultDarkThreshold ("Cult Dark Threshold", Range(0, 1)) = 0.18
        _CultLightThreshold ("Cult Light Threshold", Range(0, 1)) = 0.58
        _CultSoftness ("Cult Softness", Range(0.001, 0.3)) = 0.09
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma shader_feature _STRAIGHT_ALPHA_INPUT
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            #include "Assets/Spine/Runtime/spine-unity/Shaders/CGIncludes/Spine-Common.cginc"
            #include "Assets/Resources/GhostToonLighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                float3 worldPos : TEXCOORD1;
                SHADOW_COORDS(2)
            };

            sampler2D _MainTex;
            half _ToonStrength;

            v2f vert(appdata input)
            {
                v2f output;
                output.pos = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = PMAGammaToTargetSpace(input.color);
                output.worldPos = mul(unity_ObjectToWorld, input.vertex).xyz;
                TRANSFER_SHADOW(output);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 paint = tex2D(_MainTex, input.uv);
                #ifdef _STRAIGHT_ALPHA_INPUT
                paint.rgb *= paint.a;
                #endif
                paint *= input.color;
                half3 normal = UnityObjectToWorldNormal(float3(0, 0, -1));
                half3 lightDirection = normalize(UnityWorldSpaceLightDir(input.worldPos));
                half ndotl = abs(dot(normal, lightDirection));
                paint.rgb = lerp(paint.rgb,
                    GhostToonShade(paint.rgb, ndotl, SHADOW_ATTENUATION(input), _LightColor0.rgb),
                    _ToonStrength);
                return paint;
            }
            ENDCG
        }

        UsePass "Spine/Skeleton/Caster"
    }
    Fallback Off
}
