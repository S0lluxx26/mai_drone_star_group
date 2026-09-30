Shader "DroneStar/Fountain"
{
    // Festival fountains, mist and steam, animated entirely on the GPU. Each quad is one droplet (or one puff):
    //   POSITION   the jet's nozzle (world)
    //   UV0        (corner x, corner y, phase 0..1, jet index)
    //   UV1        (jet direction xyz, random 0..1)
    //   COLOR.a    colour group / 4 (ring, arch, front row, mist; steam vents alternate the ring and arch colours)
    // _JetHeights[jet] (metres, set every frame from the choreography) gives each droplet its parabola; droplets
    // are lit from the underwater lights below, so they glow at the base and fade toward the top. Steam
    // (_Mist 2) rises from vents in the water in slow billowing columns, as much as _SteamLevel allows.
    Properties
    {
        _Size ("Droplet size (m)", Float) = 0.45
        _MinPixels ("Minimum size (px)", Float) = 1.6
        _Intensity ("HDR intensity", Float) = 1.6
        _Mist ("Droplets (0), mist puffs (1) or steam (2)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Fountain"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Size;
                float _MinPixels;
                float _Intensity;
                float _Mist;
            CBUFFER_END

            // Set every frame by FestivalVenue (arrays are not material properties).
            float _JetHeights[128];
            float4 _GroupColors[4];
            float _ShowTime;
            float _SteamLevel;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                float3 color : TEXCOORD1;
            };

            static const float Gravity = 9.81;

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                float3 nozzle = TransformObjectToWorld(v.positionOS);
                int group = (int)round(v.color.a * 4.0);
                float3 tint = _GroupColors[min(group, 3)].rgb;
                float phase = v.uv0.z, rnd = v.uv1.w;
                float3 center;
                float size, brightness;
                if (_Mist > 1.5)
                {
                    // Steam: each puff rises and swells from its vent, drifting downwind, then thins away.
                    // Low, soft clouds (a few metres high) rather than columns: each puff starts somewhere around its
                    // vent and spreads as it drifts.
                    float age = frac(_ShowTime * (0.07 + 0.03 * rnd) + phase);
                    float3 wind = float3(0.9, 0.0, 0.35);
                    float3 around = float3((rnd - 0.5) * 7.0, 0.0, (frac(rnd * 7.31) - 0.5) * 4.0);
                    float3 wobble = float3(sin(age * 5.0 + rnd * 20.0), 0.0, cos(age * 4.0 + rnd * 13.0)) * age * 3.0;
                    center = nozzle + around + float3(0.0, 0.6 + age * (5.0 + 5.0 * rnd), 0.0) + wind * age * 7.0 + wobble;
                    size = _Size * (0.45 + 1.6 * age);
                    brightness = saturate(_SteamLevel) * pow(sin(3.14159 * age), 1.6) * (0.5 + 0.5 * rnd);
                }
                else if (_Mist > 0.5)
                {
                    // Slow drifting puffs hugging the water.
                    float t = _ShowTime * (0.05 + 0.04 * rnd) + phase * 6.2832;
                    center = nozzle + float3(sin(t) * 4.0, 0.8 + 0.6 * sin(t * 1.7 + rnd * 5.0), cos(t * 0.8) * 3.0);
                    size = _Size * (0.7 + 0.6 * rnd);
                    brightness = 0.55 + 0.45 * sin(t * 2.3 + rnd * 9.0);
                }
                else
                {
                    float height = max(_JetHeights[(int)v.uv0.w], 0.0);
                    float3 dir = normalize(v.uv1.xyz);
                    float vy = sqrt(2.0 * Gravity * max(height, 0.05));
                    float speed = vy / max(dir.y, 0.25);
                    float life = 2.0 * vy / Gravity;
                    // A fixed cadence per droplet (not tied to the height), so height changes never make them jump.
                    float age = frac(_ShowTime * 0.3 + phase) * life;
                    float3 spray = float3(rnd - 0.5, 0.0, frac(rnd * 7.31) - 0.5) * age * 0.9;
                    center = nozzle + dir * speed * age + float3(0.0, -0.5 * Gravity * age * age, 0.0) + spray;
                    float lift = saturate((center.y - nozzle.y) / max(height, 1.0));
                    size = _Size * (0.7 + 0.8 * age / max(life, 0.1));
                    // Lit from the nozzle: bright at the foot, dimmer at the crown, gone when the jet is off.
                    brightness = (1.0 - 0.65 * lift) * saturate(height * 0.5) * (1.0 - smoothstep(0.8, 1.0, age / max(life, 0.01)));
                }
                float3 viewPos = TransformWorldToView(center);
                float dist = max(-viewPos.z, 0.05);
                float pixelWorld = 2.0 * dist / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y);
                float halfSize = max(size * 0.5, _MinPixels * 0.5 * pixelWorld);
                brightness *= saturate(size * 0.5 / halfSize);
                // Idle jets and absent steam collapse to nothing, so they cost no fill.
                viewPos.xy += v.uv0.xy * halfSize * (brightness > 1e-4 ? 1.0 : 0.0);
                o.positionCS = TransformWViewToHClip(viewPos);
                o.corner = v.uv0.xy;
                // Steam is mostly white, touched by the stage colours; water takes on its lights' colour.
                o.color = lerp(float3(0.55, 0.62, 0.7), tint, _Mist > 1.5 ? 0.35 : 0.7) * brightness;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r2 = dot(i.corner, i.corner);
                if (r2 >= 1.0) discard;
                float soft = exp(-r2 * 3.0) * (1.0 - r2);
                return half4(i.color * soft * _Intensity, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
