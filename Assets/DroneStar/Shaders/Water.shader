Shader "DroneStar/Water"
{
    // Night lake: analytic ripples, a Fresnel mirror of the procedural sky and a moon glint path.
    // Drone reflections are drawn on top by DroneGlow with _REFLECTION enabled.
    Properties
    {
        _DeepColor ("Deep colour", Color) = (0.004, 0.012, 0.03, 1)
        _ZenithColor ("Sky zenith", Color) = (0.012, 0.018, 0.07, 1)
        _HorizonColor ("Sky horizon", Color) = (0.11, 0.06, 0.2, 1)
        _GlowColor ("Afterglow", Color) = (0.55, 0.16, 0.22, 1)
        _GlowDirection ("Afterglow direction", Vector) = (-0.6, 0, 1, 0)
        _MoonDirection ("Moon direction", Vector) = (0.45, 0.32, 0.83, 0)
        _MoonColor ("Moon", Color) = (1.0, 0.93, 0.8, 1)
        _WaveStrength ("Wave strength", Float) = 0.35
        _CityGlow ("City glow on water", Color) = (0.25, 0.12, 0.05, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "NightCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor;
                float4 _ZenithColor;
                float4 _HorizonColor;
                float4 _GlowColor;
                float4 _GlowDirection;
                float4 _MoonDirection;
                float4 _MoonColor;
                float _WaveStrength;
                float4 _CityGlow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float2 WaveGradient(float2 p, float t)
            {
                float2 g = 0;
                float2 d1 = float2(0.8, 0.6), d2 = float2(-0.45, 0.89), d3 = float2(0.2, -0.98), d4 = float2(-0.9, -0.3);
                g += d1 * 0.9 * cos(dot(p, d1) * 0.11 + t * 0.9);
                g += d2 * 0.7 * cos(dot(p, d2) * 0.23 + t * 1.3);
                g += d3 * 0.5 * cos(dot(p, d3) * 0.57 + t * 1.9);
                g += d4 * 0.35 * cos(dot(p, d4) * 1.31 + t * 2.7);
                return g;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 toCam = _WorldSpaceCameraPos - i.positionWS;
                float dist = length(toCam);
                float3 V = toCam / max(dist, 1e-3);
                // Ripples fade with distance so the far lake reads as a calm mirror (and does not alias).
                float strength = _WaveStrength * saturate(1.0 - dist / 1800.0) * 0.12;
                float2 g = WaveGradient(i.positionWS.xz, _Time.y);
                float3 N = normalize(float3(-g.x * strength, 1.0, -g.y * strength));
                float3 R = reflect(-V, N);
                R.y = abs(R.y);

                float3 sky = DS_SkyGradient(R, _ZenithColor.rgb, _HorizonColor.rgb, _GlowColor.rgb, _GlowDirection.xyz);
                float fresnel = 0.03 + 0.97 * pow(1.0 - saturate(dot(N, V)), 5.0);

                float3 moon = normalize(_MoonDirection.xyz);
                float md = saturate(dot(R, moon));
                float glint = pow(md, 1400.0) * 9.0 + pow(md, 90.0) * 0.12;

                // Warm light from the far shore city, strongest on distant water.
                float farWater = saturate((i.positionWS.z - 50.0) / 600.0) * saturate(1.0 - R.y * 4.0);

                float3 col = _DeepColor.rgb * (1.0 - fresnel) + sky * fresnel * 1.1 + _MoonColor.rgb * glint + _CityGlow.rgb * farWater * fresnel;
                col = MixFog(col, i.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
