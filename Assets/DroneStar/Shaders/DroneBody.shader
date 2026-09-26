Shader "DroneStar/DroneBody"
{
    // Instanced light-show quadcopter (see DroneAirframe.cs). Lighting is hand-built for the night scene:
    // moonlight, a sky/ground ambient, the glow of the surrounding swarm, a soft key light riding with the
    // camera (reveals close drones, fades out within _KeyRange) and the drone's own LED bulb lighting its
    // belly, skids and props from below. Per instance: _LedColor = (LED colour, props spinning 0/1).
    // Vertex data: colour = (albedo, gloss); uv0 = (part, spin direction, hub x, hub z), part 1 = bulb, 2 = blade.
    Properties
    {
        _AmbientSky ("Ambient from above", Color) = (0.03, 0.045, 0.1, 1)
        _AmbientGround ("Ambient from below", Color) = (0.008, 0.01, 0.02, 1)
        _KeyStrength ("Camera key light", Float) = 1.1
        _KeyRange ("Camera key range (m)", Float) = 40
        _LedGlow ("LED bulb brightness", Float) = 3.2
        _Underglow ("LED underglow on the airframe", Float) = 1.4
        _PropAngle ("Propeller angle (rad)", Float) = 0
        _SwarmGlow ("Light from the surrounding swarm", Color) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Body"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _AmbientSky;
                float4 _AmbientGround;
                float _KeyStrength;
                float _KeyRange;
                float _LedGlow;
                float _Underglow;
                float _PropAngle;
                float4 _SwarmGlow;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(PerDrone)
                UNITY_DEFINE_INSTANCED_PROP(float4, _LedColor)
            UNITY_INSTANCING_BUFFER_END(PerDrone)

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float4 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 albedo : TEXCOORD2; // rgb albedo, a gloss
                float4 led : TEXCOORD3;    // rgb LED colour, a part
                float3 bulbWS : TEXCOORD4;
                float fog : TEXCOORD5;
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                float4 led = UNITY_ACCESS_INSTANCED_PROP(PerDrone, _LedColor);
                float3 p = v.positionOS;
                float3 n = v.normalOS;
                if (v.uv.x > 1.5)
                {
                    // Propeller blades turn about their motor hub while the drone flies.
                    float s, c;
                    sincos(_PropAngle * v.uv.y * led.a, s, c);
                    float2 d = p.xz - v.uv.zw;
                    p.xz = v.uv.zw + float2(c * d.x - s * d.y, s * d.x + c * d.y);
                    n.xz = float2(c * n.x - s * n.z, s * n.x + c * n.z);
                }
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldDir(n); // rigid transforms only, so M works for normals
                o.bulbWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                o.albedo = v.color;
                o.led = float4(led.rgb, v.uv.x);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 albedo = i.albedo.rgb;
                float gloss = i.albedo.a;
                float3 led = i.led.rgb;
                float facing = saturate(dot(n, V));

                if (i.led.a > 0.5 && i.led.a < 1.5)
                {
                    // The bulb: a diffuser lit from inside, brightest at the bottom, with a faint glassy rim.
                    float3 bulb = led * _LedGlow * (0.7 + 0.5 * saturate(-n.y)) + albedo * 0.015;
                    bulb += (1.0 - facing) * (1.0 - facing) * (0.04 + led * 0.6);
                    return half4(MixFog(bulb, i.fog), 1.0);
                }

                float specPower = exp2(3.0 + gloss * 6.0);
                float3 col = albedo * (lerp(_AmbientGround.rgb, _AmbientSky.rgb, n.y * 0.5 + 0.5) + _SwarmGlow.rgb);

                Light moon = GetMainLight();
                float moonDiffuse = saturate(dot(n, moon.direction));
                float3 hm = normalize(moon.direction + V);
                col += moon.color * moonDiffuse * (albedo + gloss * 0.35 * pow(saturate(dot(n, hm)), specPower));

                float3 toKey = _WorldSpaceCameraPos + float3(0.0, 1.2, 0.0) - i.positionWS;
                float keyDistance = length(toKey);
                float3 L = toKey / max(keyDistance, 1e-3);
                float keyFalloff = saturate(1.0 - keyDistance / _KeyRange);
                keyFalloff *= keyFalloff * _KeyStrength;
                float3 hk = normalize(L + V);
                col += keyFalloff * (albedo * saturate(dot(n, L) * 0.75 + 0.25) + gloss * 0.6 * pow(saturate(dot(n, hk)), specPower));

                // The bulb lights what is around and below it; dark carbon still catches a coloured sheen.
                float3 toBulb = i.bulbWS - i.positionWS;
                float bulbDistance = length(toBulb);
                float bulbFacing = saturate(dot(n, toBulb / max(bulbDistance, 1e-4)) * 0.7 + 0.3);
                col += led * _Underglow * bulbFacing * (albedo + 0.06 + gloss * 0.1) / (1.0 + bulbDistance * bulbDistance * 110.0);

                // Rim of sky light so dark airframes still read against the night.
                float rim = pow(1.0 - facing, 4.0);
                col += rim * (_AmbientSky.rgb * 1.6 + _SwarmGlow.rgb * 2.0);

                return half4(MixFog(col, i.fog), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
