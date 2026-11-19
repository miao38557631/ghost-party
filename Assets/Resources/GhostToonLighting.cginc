#ifndef GHOST_PARTY_TOON_LIGHTING_INCLUDED
#define GHOST_PARTY_TOON_LIGHTING_INCLUDED

half _ToonLook;
half _SableThreshold;
half _SableSoftness;
half _CultDarkThreshold;
half _CultLightThreshold;
half _CultSoftness;
fixed4 _SableShadowTint;
fixed4 _SableLightTint;
fixed4 _CultDeepTint;
fixed4 _CultShadowTint;
fixed4 _CultLightTint;

inline half GhostToonOutlineCoverage(sampler2D paintedTexture, float2 uv, float2 screenPixel)
{
    half coverage = 0;
    float2 inner = screenPixel * 0.5;
    coverage = max(coverage, tex2D(paintedTexture, uv + float2( inner.x, 0)).a);
    coverage = max(coverage, tex2D(paintedTexture, uv + float2(-inner.x, 0)).a);
    coverage = max(coverage, tex2D(paintedTexture, uv + float2(0,  inner.y)).a);
    coverage = max(coverage, tex2D(paintedTexture, uv + float2(0, -inner.y)).a);
    coverage = max(coverage, tex2D(paintedTexture, uv + float2( inner.x,  inner.y)).a);
    coverage = max(coverage, tex2D(paintedTexture, uv + float2(-inner.x,  inner.y)).a);
    coverage = max(coverage, tex2D(paintedTexture, uv + float2( inner.x, -inner.y)).a);
    coverage = max(coverage, tex2D(paintedTexture, uv + float2(-inner.x, -inner.y)).a);
    coverage = max(coverage, tex2D(paintedTexture, uv + float2( screenPixel.x, 0)).a);
    coverage = max(coverage, tex2D(paintedTexture, uv + float2(-screenPixel.x, 0)).a);
    coverage = max(coverage, tex2D(paintedTexture, uv + float2(0,  screenPixel.y)).a);
    coverage = max(coverage, tex2D(paintedTexture, uv + float2(0, -screenPixel.y)).a);
    coverage = max(coverage, tex2D(paintedTexture, uv + float2( screenPixel.x,  screenPixel.y)).a);
    coverage = max(coverage, tex2D(paintedTexture, uv + float2(-screenPixel.x,  screenPixel.y)).a);
    coverage = max(coverage, tex2D(paintedTexture, uv + float2( screenPixel.x, -screenPixel.y)).a);
    coverage = max(coverage, tex2D(paintedTexture, uv + float2(-screenPixel.x, -screenPixel.y)).a);
    return coverage;
}

inline fixed3 GhostToonShade(fixed3 albedo, half ndotl, half shadow, fixed3 lightColor)
{
    // Keep broad indoor surfaces readable when another floor or prop blocks
    // the directional light, while retaining a distinct projected shadow band.
    half illumination = saturate(max(0.2h, ndotl * lerp(0.38h, 1.0h, shadow)));
    half sableEdge = smoothstep(_SableThreshold - _SableSoftness,
                               _SableThreshold + _SableSoftness, illumination);
    fixed3 sable = lerp(_SableShadowTint.rgb, _SableLightTint.rgb, sableEdge);

    half darkEdge = smoothstep(_CultDarkThreshold - _CultSoftness,
                               _CultDarkThreshold + _CultSoftness, illumination);
    half lightEdge = smoothstep(_CultLightThreshold - _CultSoftness,
                                _CultLightThreshold + _CultSoftness, illumination);
    fixed3 cult = lerp(_CultDeepTint.rgb, _CultShadowTint.rgb, darkEdge);
    cult = lerp(cult, _CultLightTint.rgb, lightEdge);

    // Hybrid keeps Sable's hard light boundary and uses Cult's warm palette.
    fixed3 hybrid = lerp(_CultDeepTint.rgb, _CultLightTint.rgb, sableEdge);
    fixed3 tint = lerp(sable, cult, step(0.5h, _ToonLook));
    tint = lerp(tint, hybrid, step(1.5h, _ToonLook));
    // Keep the hand-painted albedo legible even when the key light is dim.
    fixed3 keyColor = lerp(fixed3(1, 1, 1), lightColor, 0.35h * illumination);
    return albedo * tint * keyColor;
}

#endif
