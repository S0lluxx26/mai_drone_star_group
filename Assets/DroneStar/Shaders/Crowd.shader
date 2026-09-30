Shader "DroneStar/Crowd"
{
    // The audience: one camera-facing quad per person, drawn as a silhouette (head, shoulders, body, arms) from a
    // distance field, dark against the lawn with a rim lit by the show. _Mode 1 draws the second quad of each
    // person instead: the phone held up to film the show (and the odd camera flash).
    //   POSITION   feet (world)
    //   UV0        (corner x −1..1, corner y 0..1, height m, random)
    //   UV1        (phone threshold, cheer threshold, 0, 0)
    //   COLOR      clothing tint
    // Crowd behaviour follows the show through globals set every frame: _PhoneShare (share of people filming),
    // _Cheer (arms up as a scene appears), _Energy and _ShowTint (the colour of the light the show casts).
    Properties
    {
        [Enum(Body, 0, Phone, 1)] _Mode ("Quad", Float) = 0
        _Intensity ("Phone HDR intensity", Float) = 2
        _MinPixels ("Phone minimum size (px)", Float) = 1.2
        _BodyColor ("Silhouette colour", Color) = (0.006, 0.008, 0.012, 1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 0
        [Toggle] _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Crowd"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull Off
            // Smooth silhouette edges under MSAA; phones output alpha 1, so full coverage.
            AlphaToMask On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Mode;
                float _Intensity;
                float _MinPixels;
                float4 _BodyColor;
                float _SrcBlend;
                float _DstBlend;
                float _ZWrite;
            CBUFFER_END

            // Set every frame by AudienceCrowd.
            float _ShowTime;
            float _Energy;
            float _Cheer;
            float _PhoneShare;
            float4 _ShowTint;

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
                float2 local : TEXCOORD0;     // metres in the person's frame (body) or the sprite corner (phone)
                float4 pose : TEXCOORD1;      // phone up, both arms up, height, phone hand side
                float3 color : TEXCOORD2;     // clothing (body) or light (phone)
                float front : TEXCOORD3;      // 1 when seen from the show's side (lit faces)
            };

            static const float HalfWidth = 0.46;
            static const float Headroom = 0.62;

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                float3 feet = TransformObjectToWorld(v.positionOS);
                float height = v.uv0.z, rnd = v.uv0.w;
                float phone = v.uv1.x < _PhoneShare ? 1.0 : 0.0;
                float cheer = v.uv1.y < _Cheer * 0.65 ? 1.0 : 0.0;
                float side = rnd > 0.5 ? 1.0 : -1.0;

                // Stand upright and turn to face the camera (cylindrical billboard).
                float3 toCam = _WorldSpaceCameraPos - feet;
                toCam.y = 0.0;
                float len = length(toCam);
                float3 fwd = len > 1e-3 ? toCam / len : float3(0.0, 0.0, -1.0);
                float3 right = float3(fwd.z, 0.0, -fwd.x);
                float sway = sin(_ShowTime * (1.1 + rnd) + rnd * 20.0) * 0.035 * (0.3 + _Energy);
                o.pose = float4(phone, cheer, height, side);
                // People face the lake (+z); seen from the water side, the show lights their fronts.
                o.front = saturate(fwd.z);

                if (_Mode < 0.5)
                {
                    float top = height + Headroom;
                    float3 world = feet + right * (v.uv0.x * HalfWidth + sway) + float3(0.0, v.uv0.y * top, 0.0);
                    o.positionCS = TransformWorldToHClip(world);
                    o.local = float2(v.uv0.x * HalfWidth, v.uv0.y * top);
                    o.color = v.color.rgb;
                    return o;
                }

                // The phone, in the raised hand (the same hand the silhouette raises).
                float2 corner = v.uv0.xy * float2(1.0, 2.0) - float2(0.0, 1.0);
                float3 hand = feet + right * (side * 0.22 + sway) + float3(0.0, height + 0.36, 0.0);
                float3 viewPos = TransformWorldToView(hand);
                float dist = max(-viewPos.z, 0.05);
                float pixelWorld = 2.0 * dist / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y);
                float size = 0.13;
                float halfSize = max(size * 0.5, _MinPixels * 0.5 * pixelWorld);
                float glow = 0.75 + 0.25 * sin(_ShowTime * 0.7 + rnd * 30.0);
                // Now and then someone takes a photo with the flash on, more often as a scene appears.
                float slot = floor(_ShowTime * 6.0);
                float h = frac(sin(slot * 12.9898 + rnd * 781.233 + v.uv1.x * 131.7) * 43758.5453);
                float flash = h > 1.0 - (0.0005 + 0.004 * _Cheer) ? 1.0 : 0.0;
                float brightness = phone * max(glow, flash * 6.0) * saturate(size * 0.5 / halfSize);
                viewPos.xy += corner * halfSize * phone;
                o.positionCS = TransformWViewToHClip(viewPos);
                o.local = corner;
                o.color = lerp(float3(0.62, 0.78, 1.0), float3(1.0, 0.86, 0.66), v.uv1.y) * brightness;
                if (flash > 0.5) o.color = float3(1.0, 1.0, 1.0) * brightness;
                return o;
            }

            float Box(float2 p, float2 b)
            {
                float2 q = abs(p) - b;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
            }

            float Capsule(float2 p, float2 a, float2 b, float r)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h) - r;
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_Mode > 0.5)
                {
                    float r2 = dot(i.local, i.local);
                    if (r2 >= 1.0) discard;
                    float soft = exp(-r2 * 3.0) * (1.0 - r2);
                    return half4(i.color * soft * _Intensity, 1.0);
                }

                float2 p = i.local;
                float height = i.pose.z;
                float shoulder = height - 0.27;
                float d = length(p - float2(0.0, height - 0.115)) - 0.105;                                   // head
                d = min(d, Box(p - float2(0.0, height - 0.215), float2(0.05, 0.06)));                        // neck
                d = min(d, Box(p - float2(0.0, 0.5 * (shoulder + 0.82)), float2(0.12, 0.5 * (shoulder - 0.82))) - 0.07); // body
                d = min(d, Box(p - float2(0.0, 0.42), float2(0.14, 0.42)));                                 // legs
                if (i.pose.y > 0.5)
                {
                    d = min(d, Capsule(p, float2(-0.16, shoulder - 0.03), float2(-0.3, height + 0.44), 0.05));
                    d = min(d, Capsule(p, float2(0.16, shoulder - 0.03), float2(0.3, height + 0.44), 0.05));
                }
                else if (i.pose.x > 0.5)
                {
                    d = min(d, Capsule(p, float2(i.pose.w * 0.16, shoulder - 0.03), float2(i.pose.w * 0.22, height + 0.33), 0.05));
                }
                // Edge coverage for alpha-to-coverage (MSAA); without MSAA the clip keeps the shape.
                float coverage = saturate(0.5 - d / max(fwidth(d), 1e-4));
                clip(coverage - 0.02);
                // Backlit by the show: nearly black, with a faint edge of its colour (and a warmer face when seen
                // from the lake).
                float rim = saturate(1.0 + d / 0.03);
                float3 col = _BodyColor.rgb + _ShowTint.rgb * (0.006 + 0.09 * rim) + (i.color * 0.04 + _ShowTint.rgb * 0.05) * i.front;
                return half4(col, coverage);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
