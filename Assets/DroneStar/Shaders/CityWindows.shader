Shader "DroneStar/CityWindows"
{
    // Night skyline facades. UV0 is in metres along the facade (x) and up the wall (y);
    // UV1.x is a per-building seed and UV1.y is 1 on roofs.
    Properties
    {
        _FacadeColor ("Facade", Color) = (0.018, 0.02, 0.035, 1)
        _WarmWindow ("Warm windows", Color) = (1.0, 0.62, 0.26, 1)
        _CoolWindow ("Cool windows", Color) = (0.62, 0.78, 1.0, 1)
        _WindowIntensity ("Window intensity", Float) = 1.6
        _LitFraction ("Lit fraction", Range(0, 1)) = 0.42
        _Cell ("Window cell (m)", Vector) = (3.2, 3.6, 0, 0)
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
            #include "NightCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FacadeColor;
                float4 _WarmWindow;
                float4 _CoolWindow;
                float _WindowIntensity;
                float _LitFraction;
                float4 _Cell;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 meta : TEXCOORD1;
                float fogFactor : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = v.uv;
                o.meta = v.uv1;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 col = _FacadeColor.rgb;
                if (i.meta.y < 0.5)
                {
                    float2 cell = floor(i.uv / _Cell.xy);
                    float2 f = frac(i.uv / _Cell.xy);
                    float inWindow = step(0.18, f.x) * step(f.x, 0.82) * step(0.25, f.y) * step(f.y, 0.8);
                    float h = DS_Hash13(float3(cell, i.meta.x * 97.0));
                    float lit = step(1.0 - _LitFraction, h);
                    float tone = DS_Hash13(float3(cell.yx, i.meta.x * 13.0 + 5.0));
                    float3 window = lerp(_WarmWindow.rgb, _CoolWindow.rgb, step(0.78, tone)) * lerp(0.45, 1.0, frac(h * 7.3));
                    // A handful of windows slowly change, like people moving about.
                    float flicker = 0.85 + 0.15 * sin(_Time.y * (0.3 + tone) + h * 50.0);
                    col += window * inWindow * lit * _WindowIntensity * flicker;
                }
                col = MixFog(col, i.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
