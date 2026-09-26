Shader "DroneStar/CityWindows"
{
    // Night skyline facades.
    //   UV0  facade position in metres (u along the face, v up the wall)
    //   UV1  (building seed, part: 0 wall with windows, 1 roof, 2 plain detail)
    //   UV2  face size in metres (for corner shading and roof-line lights)
    //   COLOR.r facade style (0 punched windows, 0.5 glass curtain wall, 1 ribbon windows)
    //   COLOR.g 1 on the top section of towers with crown lighting
    // Faces are lit by the moon (URP main light) and a sky/ground ambient, glass reflects the night sky,
    // and the window pattern fades to its average colour where it would alias in the distance.
    Properties
    {
        _FacadeColor ("Concrete", Color) = (0.1, 0.11, 0.13, 1)
        _GlassColor ("Glass", Color) = (0.04, 0.055, 0.09, 1)
        _WarmWindow ("Warm windows", Color) = (1.0, 0.64, 0.3, 1)
        _CoolWindow ("Cool windows", Color) = (0.65, 0.8, 1.0, 1)
        _WindowIntensity ("Window intensity", Float) = 1.7
        _LitFraction ("Lit fraction", Range(0, 1)) = 0.3
        _Cell ("Window cell (m)", Vector) = (3.2, 3.6, 0, 0)
        _CrownColor ("Crown lights", Color) = (1.0, 0.86, 0.62, 1)
        _StreetGlow ("Street-level glow", Color) = (0.4, 0.25, 0.1, 1)
        _ZenithColor ("Sky zenith", Color) = (0.008, 0.016, 0.06, 1)
        _HorizonColor ("Sky horizon", Color) = (0.04, 0.08, 0.2, 1)
        _GlowColor ("Horizon glow", Color) = (0.05, 0.1, 0.26, 1)
        _GlowDirection ("Glow direction", Vector) = (-0.6, 0, 1, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "City"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "NightCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FacadeColor;
                float4 _GlassColor;
                float4 _WarmWindow;
                float4 _CoolWindow;
                float _WindowIntensity;
                float _LitFraction;
                float4 _Cell;
                float4 _CrownColor;
                float4 _StreetGlow;
                float4 _ZenithColor;
                float4 _HorizonColor;
                float4 _GlowColor;
                float4 _GlowDirection;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 meta : TEXCOORD1;
                float2 face : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                float3 positionWS : TEXCOORD4;
                float4 style : TEXCOORD5;
                float fogFactor : TEXCOORD6;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.meta = v.uv1;
                o.face = v.uv2;
                o.style = v.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // Returns (window mask, lit amount) for one window-pattern style; cell = position in cells.
            float2 WindowPattern(float2 uv, float styleValue, float seed, out float3 tint)
            {
                float2 cellSize = _Cell.xy;
                float2 lo, hi;
                if (styleValue > 0.75)
                {
                    // Ribbon windows: continuous horizontal bands with thin mullions.
                    cellSize = float2(2.4, _Cell.y);
                    lo = float2(0.04, 0.3);
                    hi = float2(0.96, 0.78);
                }
                else if (styleValue > 0.25)
                {
                    // Glass curtain wall: almost all glass between slim mullions and floor slabs.
                    cellSize = float2(1.6, _Cell.y);
                    lo = float2(0.05, 0.12);
                    hi = float2(0.95, 0.94);
                }
                else
                {
                    // Punched windows in concrete.
                    lo = float2(0.2, 0.26);
                    hi = float2(0.8, 0.8);
                }
                float2 cellPos = uv / cellSize;
                float2 cell = floor(cellPos);
                float2 f = frac(cellPos);
                float2 aa = max(fwidth(cellPos), 1e-4) * 0.75;
                float2 inside = smoothstep(lo - aa, lo + aa, f) * (1.0 - smoothstep(hi - aa, hi + aa, f));
                float mask = inside.x * inside.y;

                // Lit rooms: whole floors switch together on glass towers, single rooms elsewhere.
                float2 roomKey = styleValue > 0.25 ? float2(floor(cell.x / 3.0), cell.y) : cell;
                float h = DS_Hash13(float3(roomKey, seed * 97.0));
                float floorBias = DS_Hash13(float3(0.0, cell.y, seed * 31.0)) * 0.35 - 0.12;
                float lit = step(1.0 - saturate(_LitFraction + floorBias), h);
                float tone = DS_Hash13(float3(roomKey.yx, seed * 13.0 + 5.0));
                tint = lerp(_WarmWindow.rgb, _CoolWindow.rgb, step(0.8, tone)) * lerp(0.5, 1.0, frac(h * 7.3));
                return float2(mask, lit);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 toCam = _WorldSpaceCameraPos - i.positionWS;
                float3 V = normalize(toCam);
                float seed = i.meta.x;
                float part = i.meta.y;
                float styleValue = i.style.r;

                // Per-building material tint so neighbouring blocks read as different buildings.
                float tintSeed = DS_Hash13(float3(seed * 53.0, 1.7, 3.1));
                float3 concrete = _FacadeColor.rgb * lerp(0.7, 1.35, tintSeed) * lerp(float3(1.0, 0.96, 0.9), float3(0.9, 0.95, 1.08), DS_Hash13(float3(seed * 11.0, 2.0, 7.0)));
                float3 baseColor = styleValue > 0.25 && styleValue <= 0.75 ? _GlassColor.rgb : concrete;

                // Lighting: moon key + sky/ground ambient + warm street glow near the base.
                Light moon = GetMainLight();
                float ndl = saturate(dot(N, moon.direction));
                float3 ambient = lerp(float3(0.03, 0.04, 0.075), float3(0.07, 0.09, 0.17), saturate(N.y * 0.5 + 0.5));
                float3 lighting = ambient + moon.color * ndl * 1.25;
                float streetFalloff = exp(-max(i.uv.y, 0.0) / 12.0) * (1.0 - saturate(N.y));
                lighting += _StreetGlow.rgb * streetFalloff * 0.8;

                // Corner shading and roof-line highlight give each block a solid 3D edge.
                float edge = min(i.uv.x, max(i.face.x - i.uv.x, 0.0));
                float corner = lerp(0.62, 1.0, saturate(edge / 3.0));
                float roofLine = part < 0.5 ? smoothstep(1.2, 0.0, abs(i.face.y - i.uv.y)) : 0.0;

                float3 col = baseColor * lighting * (part < 0.5 ? corner : 1.0);
                col += baseColor * roofLine * 0.8;

                // Glass reflects the night sky (Fresnel), on curtain walls and in dark windows.
                float3 R = reflect(-V, N);
                float3 sky = DS_SkyGradient(R, _ZenithColor.rgb, _HorizonColor.rgb, _GlowColor.rgb, _GlowDirection.xyz);
                float fresnel = 0.06 + 0.94 * pow(1.0 - saturate(dot(N, V)), 4.0);

                if (part < 0.5)
                {
                    float3 tint;
                    float2 w = WindowPattern(i.uv, styleValue, seed, tint);
                    float3 glass = _GlassColor.rgb * lighting * 0.6 + sky * fresnel * 1.4;
                    float flicker = 0.88 + 0.12 * sin(_Time.y * (0.2 + frac(seed * 7.0)) + seed * 40.0);
                    float3 windowCol = lerp(glass, tint * _WindowIntensity * flicker, w.y);
                    float3 detailed = lerp(col, windowCol, w.x);

                    // Far away, windows shrink below a pixel and would shimmer. Instead of averaging them into a
                    // glowing wash, merge them into larger groups that each read as one crisp point of light,
                    // so distant towers stay dark masses sprinkled with lights.
                    float2 cellPos = i.uv / _Cell.xy;
                    float pixelsPerCell = 1.0 / max(max(fwidth(cellPos.x), fwidth(cellPos.y)), 1e-4);
                    float level = max(0.0, ceil(log2(3.0 / pixelsPerCell)));
                    if (level > 0.0)
                    {
                        float groupSize = exp2(level);
                        float2 gpos = cellPos / groupSize;
                        float2 gcell = floor(gpos);
                        float2 gf = frac(gpos);
                        float2 gaa = max(fwidth(gpos), 1e-4) * 0.75;
                        float2 gin = smoothstep(0.22 - gaa, 0.22 + gaa, gf) * (1.0 - smoothstep(0.78 - gaa, 0.78 + gaa, gf));
                        float gh = DS_Hash13(float3(gcell, seed * 97.0 + level * 7.0));
                        float glit = step(1.0 - _LitFraction * 0.6, gh);
                        float3 gtint = lerp(_WarmWindow.rgb, _CoolWindow.rgb, step(0.8, frac(gh * 13.7))) * lerp(0.45, 1.0, frac(gh * 7.3));
                        float3 far = col + gin.x * gin.y * glit * gtint * _WindowIntensity * 0.85;
                        float blend = saturate((pixelsPerCell - 1.5) / 1.5);
                        col = lerp(far, detailed, blend);
                    }
                    else
                    {
                        col = detailed;
                    }

                    // Crown lighting on the top floors of some towers.
                    float crown = i.style.g * smoothstep(4.5, 0.5, i.face.y - i.uv.y);
                    col += _CrownColor.rgb * crown * 1.3;
                }
                else if (part < 1.5)
                {
                    // Roofs: darker, with a hint of sky reflection off wet membranes.
                    col = baseColor * lighting * 0.7 + sky * 0.08;
                }
                else
                {
                    // Plain details (spires, crowns, rooftop plant): metal catching a little sky.
                    col = baseColor * lighting * 0.9 + sky * fresnel * 0.5;
                }

                col = MixFog(col, i.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
