Shader "DroneStar/FestivalBronze"
{
    // Festival stage surfaces: cast bronze lit by the moon, the stage floods and a camera-side sheen, with the
    // Đông Sơn patterns drawn procedurally as glowing gilded lines.
    //   COLOR.rgb  bronze tint;  COLOR.a  part: 0 plain bronze, 0.25 sun-star medallion, 0.5 drum bands,
    //              0.75 glowing trim, 1 dark hull
    //   UV0.xy     pattern coordinates: medallion = position / radius (−1..1); bands = (turns around, height 0..1)
    Properties
    {
        _Flood ("Stage flood light", Color) = (0.95, 0.62, 0.3, 1)
        _FloodStrength ("Flood strength", Float) = 0.55
        _Gilt ("Gilded lines (HDR)", Color) = (1.0, 0.62, 0.22, 1)
        _GiltIntensity ("Line brightness", Float) = 3.2
        _Trim ("Trim lights (HDR)", Color) = (1.0, 0.78, 0.45, 1)
        _TrimIntensity ("Trim brightness", Float) = 4
        _Pulse ("Drumbeat pulse (0..1)", Float) = 0
        _Accent ("Show colour accent", Color) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Bronze"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Flood;
                float _FloodStrength;
                float4 _Gilt;
                float _GiltIntensity;
                float4 _Trim;
                float _TrimIntensity;
                float _Pulse;
                float4 _Accent;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float fog : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.uv = v.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // Antialiased line: 1 on the line, fading over about a pixel either side.
            float Line(float d, float width)
            {
                float aa = max(fwidth(d), 1e-4);
                return 1.0 - smoothstep(width, width + aa * 1.5, abs(d));
            }

            // The drum face: a fourteen-ray sun, rings, a band of circles-with-dots and a rim of ticks.
            float Medallion(float2 p)
            {
                float r = length(p);
                float a = atan2(p.y, p.x) / (2.0 * PI);
                float glow = 0.0;
                // Sun: solid core and tapering rays.
                float ray = abs(frac(a * 14.0 + 0.5) - 0.5) * 2.0; // 0 on a ray's axis
                float rays = step(r, 0.42) * (1.0 - smoothstep(0.0, 0.2 * (1.0 - r / 0.42) + 0.02, ray));
                glow += max(rays, 1.0 - smoothstep(0.07, 0.09, r));
                // Rings.
                glow += Line(r - 0.48, 0.006) + Line(r - 0.53, 0.006) + Line(r - 0.76, 0.006) + Line(r - 0.81, 0.006) + Line(r - 0.97, 0.01);
                // Circles-with-dots between 0.53 and 0.76.
                float cell = frac(a * 30.0) - 0.5;
                float2 c = float2(cell * 2.0 * PI * r / 30.0, r - 0.645);
                glow += (Line(length(c) - 0.035, 0.005) + (1.0 - smoothstep(0.008, 0.014, length(c)))) * step(0.53, r) * step(r, 0.76);
                // Ticks between 0.81 and 0.97.
                float tick = abs(frac(a * 90.0) - 0.5);
                glow += (1.0 - smoothstep(0.08, 0.14, tick)) * step(0.83, r) * step(r, 0.95);
                return saturate(glow);
            }

            // The drum's side: rings of lines with zigzag and ladder bands between them.
            float Bands(float2 uv)
            {
                float y = uv.y, x = uv.x;
                float glow = 0.0;
                glow += Line(y - 0.06, 0.004) + Line(y - 0.1, 0.004) + Line(y - 0.46, 0.004) + Line(y - 0.5, 0.004);
                glow += Line(y - 0.74, 0.004) + Line(y - 0.78, 0.004) + Line(y - 0.93, 0.004);
                float zig = abs(frac(x * 64.0) - 0.5) * 2.0;
                glow += Line((y - 0.1) / 0.36 - zig, 0.02) * step(0.1, y) * step(y, 0.46);
                float ladder = abs(frac(x * 160.0) - 0.5);
                glow += (1.0 - smoothstep(0.1, 0.16, ladder)) * step(0.78, y) * step(y, 0.93);
                return saturate(glow);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                float part = i.color.a;
                float3 albedo = i.color.rgb;
                if (part > 0.87)
                {
                    // Dark hull below the deck line.
                    float3 dark = albedo * (0.05 + 0.1 * saturate(n.y));
                    return half4(MixFog(dark, i.fog), 1.0);
                }
                if (part > 0.62) return half4(MixFog(_Trim.rgb * _TrimIntensity * (0.85 + 0.3 * _Pulse), i.fog), 1.0);

                Light moon = GetMainLight();
                float3 col = albedo * (0.05 + moon.color * saturate(dot(n, moon.direction)) * 0.6);
                // Floods from the audience side and above; the uplights graze the drum.
                float3 floodDir = normalize(float3(0.0, 0.55, -1.0));
                col += albedo * _Flood.rgb * _FloodStrength * (0.35 + 0.65 * saturate(dot(n, floodDir)));
                // Polished bronze catches the floods toward the viewer.
                float3 h = normalize(floodDir + V);
                col += _Flood.rgb * pow(saturate(dot(n, h)), 36.0) * 0.5;
                // Show colour spill (fountains, lasers).
                col += albedo * _Accent.rgb * 0.25;

                float pattern = 0.0;
                if (part > 0.12 && part < 0.37) pattern = Medallion(i.uv);
                else if (part > 0.37 && part < 0.62) pattern = Bands(i.uv);
                col += _Gilt.rgb * _GiltIntensity * pattern * (0.75 + 0.5 * _Pulse);
                return half4(MixFog(col, i.fog), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
