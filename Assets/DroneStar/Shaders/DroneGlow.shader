Shader "DroneStar/DroneGlow"
{
    // Camera-facing glow sprites for drone LEDs and scenery lights. Each sprite is four vertices sharing
    // the light's world position; TEXCOORD0 = (corner.x, corner.y, size scale, intensity).
    // With _REFLECTION on, the sprite is mirrored about the water level and projected onto the water
    // surface, so land in front of the lake still hides it.
    Properties
    {
        _Size ("World size (m)", Float) = 1.8
        _MinPixels ("Minimum size (px)", Float) = 3.5
        _Intensity ("HDR intensity", Float) = 5
        _CoreSharpness ("Core sharpness", Float) = 22
        _HaloFalloff ("Halo falloff", Float) = 4.5
        _WhiteCore ("White-hot core", Range(0, 1)) = 0.55
        _NearShrink ("Shrink closer than (m), 0 = off", Float) = 0
        [Toggle(_REFLECTION)] _Reflection ("Water reflection", Float) = 0
        _WaterLevel ("Water level", Float) = -1.2
        _ReflectionStretch ("Reflection stretch", Float) = 3.2
        _ReflectionStrength ("Reflection strength", Float) = 0.32
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Glow"
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
                float _CoreSharpness;
                float _HaloFalloff;
                float _WhiteCore;
                float _NearShrink;
                float _WaterLevel;
                float _ReflectionStretch;
                float _ReflectionStrength;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float4 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 corner : TEXCOORD0;
                float3 extra : TEXCOORD1; // x = brightness, y = shimmer seed, z = visible
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                float3 center = TransformObjectToWorld(v.positionOS);
                float2 corner = v.uv.xy;
                float size = _Size * v.uv.z;
                float brightness = v.uv.w;
                float visible = 1.0;

                #if defined(_REFLECTION)
                    float3 cam = _WorldSpaceCameraPos;
                    float3 mirrored = float3(center.x, 2.0 * _WaterLevel - center.y, center.z);
                    float camHeight = cam.y - _WaterLevel;
                    float depth = _WaterLevel - mirrored.y;
                    // Point on the water plane along the line of sight to the mirror image.
                    float t = camHeight / max(camHeight + depth, 1e-3);
                    float3 onWater = cam + (mirrored - cam) * t;
                    onWater.y = _WaterLevel + 0.03;
                    size *= t;
                    visible = (camHeight > 0.05 && center.y > _WaterLevel) ? 1.0 : 0.0;
                    center = onWater;
                #endif

                float3 viewPos = TransformWorldToView(center);
                float dist = max(-viewPos.z, 0.05);
                // Up close the glow tightens to the bulb so the drone carrying it stays visible.
                if (_NearShrink > 0.0) size *= clamp(dist / _NearShrink, 0.16, 1.0);
                // URP flips the projection when rendering into an intermediate target on D3D, so _m11 can be negative.
                float pixelWorld = 2.0 * dist / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y);
                float halfSize = max(size * 0.5, _MinPixels * 0.5 * pixelWorld);
                // Sprites held at the pixel floor would otherwise add up to bright blobs far away.
                float coverage = saturate(size * 0.5 / halfSize);
                brightness *= lerp(0.45, 1.0, coverage);

                float2 offset = corner * halfSize;
                #if defined(_REFLECTION)
                    offset.x *= 0.7;
                    offset.y *= _ReflectionStretch;
                    brightness *= _ReflectionStrength;
                #endif
                viewPos.xy += offset * visible;
                o.positionCS = TransformWViewToHClip(viewPos);
                o.color = v.color;
                o.corner = corner;
                o.extra = float3(brightness * visible, center.x * 0.37 + center.z * 0.21, visible);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r2 = dot(i.corner, i.corner);
                if (r2 >= 1.0) discard;
                float core = exp(-r2 * _CoreSharpness);
                float halo = exp(-r2 * _HaloFalloff) * (1.0 - r2);
                float3 col = i.color.rgb * (halo + core * 1.6) + core * _WhiteCore * dot(i.color.rgb, float3(0.3, 0.5, 0.2));
                #if defined(_REFLECTION)
                    float ripple = 0.55 + 0.45 * sin(i.corner.y * 9.0 + _Time.y * 2.3 + i.extra.y);
                    col *= ripple;
                #endif
                return half4(col * _Intensity * i.extra.x, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
