Shader "DroneStar/LaserBeam"
{
    // Festival lasers: each beam is a camera-facing ribbon (built on the CPU every frame) with a hot core and a
    // soft halo, fading along its length.   COLOR = HDR beam colour (already scaled for its on-screen width);
    // UV0 = (along the beam 0..1, across −1..1).
    Properties
    {
        _Intensity ("HDR intensity", Float) = 2.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Laser"
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
                float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Clamped: interpolation can overshoot [0, 1] a hair, and pow() of a negative is NaN on D3D.
                float across = saturate(1.0 - abs(i.uv.y));
                float core = pow(across, 6.0) + 0.3 * pow(across, 1.6);
                float along = pow(saturate(1.0 - i.uv.x), 1.3) * smoothstep(0.0, 0.01, i.uv.x + 0.003);
                return half4(i.color.rgb * core * along * _Intensity, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
