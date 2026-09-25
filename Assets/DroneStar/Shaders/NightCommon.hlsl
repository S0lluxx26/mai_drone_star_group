#ifndef DRONESTAR_NIGHT_COMMON_INCLUDED
#define DRONESTAR_NIGHT_COMMON_INCLUDED

// Shared sky model for the skybox and the water reflection, so the lake mirrors exactly the sky above it.

float DS_Hash13(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float DS_Noise3(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = DS_Hash13(i);
    float n100 = DS_Hash13(i + float3(1, 0, 0));
    float n010 = DS_Hash13(i + float3(0, 1, 0));
    float n110 = DS_Hash13(i + float3(1, 1, 0));
    float n001 = DS_Hash13(i + float3(0, 0, 1));
    float n101 = DS_Hash13(i + float3(1, 0, 1));
    float n011 = DS_Hash13(i + float3(0, 1, 1));
    float n111 = DS_Hash13(i + float3(1, 1, 1));
    float nx00 = lerp(n000, n100, f.x);
    float nx10 = lerp(n010, n110, f.x);
    float nx01 = lerp(n001, n101, f.x);
    float nx11 = lerp(n011, n111, f.x);
    return lerp(lerp(nx00, nx10, f.y), lerp(nx01, nx11, f.y), f.z);
}

float DS_Fbm(float3 p)
{
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 4; i++)
    {
        v += a * DS_Noise3(p);
        p = p * 2.03 + 17.1;
        a *= 0.5;
    }
    return v;
}

// Gradient-only sky (no stars or moon disc): used for reflections and fog-matched horizons.
float3 DS_SkyGradient(float3 dir, float3 zenith, float3 horizon, float3 glowColor, float3 glowDir)
{
    float h = saturate(dir.y);
    float3 col = lerp(horizon, zenith, pow(h, 0.45));
    // Warm afterglow hugging the horizon, strongest toward glowDir.
    float toward = saturate(dot(normalize(float3(dir.x, 0.0, dir.z) + 1e-5), normalize(float3(glowDir.x, 0.0, glowDir.z))) * 0.5 + 0.5);
    float band = exp(-h * 9.0) * (0.35 + 0.65 * toward * toward);
    col += glowColor * band;
    // Below the horizon fade to a dark ground tone.
    float below = saturate(-dir.y * 6.0);
    col = lerp(col, horizon * 0.35, below);
    return col;
}

#endif
