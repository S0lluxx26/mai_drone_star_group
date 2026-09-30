Shader "DroneStar/Flame"
{
    // Stage flame projectors, animated entirely on the GPU. Each quad is one flame particle (or a projector's
    // base flash):
    //   POSITION   the projector's nozzle (world)
    //   UV0        (corner x, corner y, phase 0..1, projector index)
    //   UV1        (random, random, kind: 0 particle / 1 base flash, 0)
    // _FlameLevels[projector] (0..1, set every frame from FestivalStage.FlameLevel) sets how high it burns. A
    // particle rises from the nozzle to the top of the flame over its short life, white-hot at the nozzle,
    // orange above and deep red at the tip. With _REFLECTION on, the flames are mirrored onto the lake the way
    // DroneGlow mirrors the drones.
    Properties
    {
        _Size ("Particle size (m)", Float) = 1.1
        _MinPixels ("Minimum size (px)", Float) = 1.5
        _Intensity ("HDR intensity", Float) = 3
        _Reach ("Full flame height (m)", Float) = 12
        [Toggle(_REFLECTION)] _Reflection ("Water reflection", Float) = 0
        _WaterLevel ("Water level", Float) = -1.2
        _ReflectionStretch ("Reflection stretch", Float) = 2.4
        _ReflectionStrength ("Reflection strength", Float) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Flame"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _REFLECTION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Size;
                float _MinPixels;
                float _Intensity;
                float _Reach;
                float _WaterLevel;
                float _ReflectionStretch;
                float _ReflectionStrength;
            CBUFFER_END

            // Set every frame by FestivalVenue (arrays are not material properties).
            float _FlameLevels[32];
            float _ShowTime;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                float3 color : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                float3 nozzle = TransformObjectToWorld(v.positionOS);
                float level = saturate(_FlameLevels[(int)v.uv0.w]);
                float phase = v.uv0.z, rnd = v.uv1.x, rnd2 = v.uv1.y;
                float3 center;
                float size;
                float3 color;
                if (v.uv1.z < 0.5)
                {
                    // A new particle leaves the nozzle every few milliseconds; each lives under half a second.
                    float age = frac(_ShowTime * 2.3 + phase);
                    float height = _Reach * level * (0.72 + 0.28 * rnd);
                    float sway = age * age * (0.8 + 1.6 * rnd);
                    float3 drift = float3(sin(_ShowTime * 7.3 + rnd * 40.0 + age * 6.0), 0.0, cos(_ShowTime * 6.1 + rnd2 * 40.0 + age * 5.0)) * sway * 0.55;
                    center = nozzle + float3(0.0, pow(age, 0.8) * height, 0.0) + drift;
                    size = _Size * (0.6 + 2.6 * age) * (0.45 + 0.55 * level);
                    float3 hot = float3(1.0, 0.9, 0.62), mid = float3(1.0, 0.4, 0.06), tip = float3(0.5, 0.06, 0.01);
                    color = age < 0.3 ? lerp(hot, mid, age / 0.3) : lerp(mid, tip, saturate((age - 0.3) / 0.7));
                    float brightness = level * saturate(age / 0.06) * (1.0 - smoothstep(0.62, 1.0, age)) * (0.7 + 0.3 * rnd2);
                    color *= brightness;
                }
                else
                {
                    // The flash of the burner lighting the deck and the air around the nozzle.
                    center = nozzle + float3(0.0, 1.6, 0.0);
                    size = _Size * 8.0;
                    color = float3(1.0, 0.42, 0.09) * level * 0.35;
                }

                float visible = level > 0.001 ? 1.0 : 0.0;
                #if defined(_REFLECTION)
                    float3 cam = _WorldSpaceCameraPos;
                    float3 mirrored = float3(center.x, 2.0 * _WaterLevel - center.y, center.z);
                    float camHeight = cam.y - _WaterLevel;
                    float depth = _WaterLevel - mirrored.y;
                    float t = camHeight / max(camHeight + depth, 1e-3);
                    float3 onWater = cam + (mirrored - cam) * t;
                    onWater.y = _WaterLevel + 0.03;
                    size *= t;
                    visible *= (camHeight > 0.05 && center.y > _WaterLevel) ? 1.0 : 0.0;
                    center = onWater;
                    color *= _ReflectionStrength;
                #endif

                float3 viewPos = TransformWorldToView(center);
                float dist = max(-viewPos.z, 0.05);
                float pixelWorld = 2.0 * dist / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y);
                float halfSize = max(size * 0.5, _MinPixels * 0.5 * pixelWorld);
                color *= saturate(size * 0.5 / halfSize);
                float2 offset = v.uv0.xy * halfSize;
                #if defined(_REFLECTION)
                    offset.x *= 0.8;
                    offset.y *= _ReflectionStretch;
                #endif
                viewPos.xy += offset * visible;
                o.positionCS = TransformWViewToHClip(viewPos);
                o.corner = v.uv0.xy;
                o.color = color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r2 = dot(i.corner, i.corner);
                if (r2 >= 1.0) discard;
                float soft = exp(-r2 * 2.6) * (1.0 - r2);
                return half4(i.color * soft * _Intensity, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
