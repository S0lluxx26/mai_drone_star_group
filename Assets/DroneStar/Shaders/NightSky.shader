Shader "DroneStar/NightSky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.012, 0.018, 0.07, 1)
        _HorizonColor ("Horizon", Color) = (0.11, 0.06, 0.2, 1)
        _GlowColor ("Afterglow", Color) = (0.55, 0.16, 0.22, 1)
        _GlowDirection ("Afterglow direction", Vector) = (-0.6, 0, 1, 0)
        _MoonDirection ("Moon direction", Vector) = (0.45, 0.32, 0.83, 0)
        _MoonColor ("Moon", Color) = (1.0, 0.93, 0.8, 1)
        _StarDensity ("Star density", Range(0.9, 0.999)) = 0.9965
        _StarBrightness ("Star brightness", Float) = 2.2
        _MilkyWay ("Milky Way strength", Float) = 0.22
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "NightCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor;
                float4 _HorizonColor;
                float4 _GlowColor;
                float4 _GlowDirection;
                float4 _MoonDirection;
                float4 _MoonColor;
                float _StarDensity;
                float _StarBrightness;
                float _MilkyWay;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.direction = v.positionOS.xyz;
                return o;
            }

            float3 Stars(float3 dir)
            {
                float3 p = dir * 320.0;
                float3 cell = floor(p);
                float h = DS_Hash13(cell);
                if (h < _StarDensity) return 0;
                float3 local = frac(p) - 0.5;
                float d = length(local);
                float size = lerp(0.06, 0.18, DS_Hash13(cell + 7.1));
                float star = saturate(1.0 - d / size);
                star *= star;
                float twinkle = 0.75 + 0.25 * sin(_Time.y * lerp(1.5, 4.0, DS_Hash13(cell + 3.3)) + h * 40.0);
                float tint = DS_Hash13(cell + 11.7);
                float3 color = lerp(float3(0.75, 0.82, 1.0), float3(1.0, 0.85, 0.65), tint);
                float horizonFade = saturate(dir.y * 5.0);
                return color * star * twinkle * _StarBrightness * horizonFade;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 dir = normalize(i.direction);
                float3 col = DS_SkyGradient(dir, _ZenithColor.rgb, _HorizonColor.rgb, _GlowColor.rgb, _GlowDirection.xyz);

                // Milky Way: a soft noisy band along a tilted great circle.
                float3 bandNormal = normalize(float3(0.35, 0.55, -0.76));
                float bandDistance = dot(dir, bandNormal);
                float band = exp(-bandDistance * bandDistance * 22.0);
                float clouds = DS_Fbm(dir * 6.0);
                col += float3(0.32, 0.26, 0.5) * band * smoothstep(0.35, 0.85, clouds) * _MilkyWay * saturate(dir.y * 3.0);

                col += Stars(dir) * (1.0 + band * 1.5);

                // Moon disc with a wide soft halo.
                float3 moonDir = normalize(_MoonDirection.xyz);
                float m = dot(dir, moonDir);
                float disc = smoothstep(0.99975, 0.99985, m);
                float halo = pow(saturate(m), 380.0) * 0.35 + pow(saturate(m), 30.0) * 0.06;
                float craters = 0.82 + 0.18 * DS_Fbm(dir * 900.0);
                col = lerp(col, _MoonColor.rgb * 2.2 * craters, disc);
                col += _MoonColor.rgb * halo;
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
