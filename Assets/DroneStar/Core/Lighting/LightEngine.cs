using System;

namespace DroneStar.Core
{
    /// <summary>Evaluates a cue's light program for one drone at one instant. Pure and deterministic.</summary>
    public static class LightEngine
    {
        /// <summary>LED level while idling on the pads, climbing and landing.</summary>
        public const float StandbyLevel = 0.35f;

        public static LedColor StandbyColor => LedColor.Standby * StandbyLevel;

        /// <param name="t">Seconds since the cue's hold began (negative while flying in).</param>
        public static LedColor Evaluate(LightSpec light, in Slot slot, int drone, float t)
        {
            if (slot.Dark || light == null) return LedColor.Black;
            float speed = light.Speed;
            LedColor a = light.ColorA;
            LedColor b = light.ColorB;
            LedColor c;
            switch (light.Effect)
            {
                case LightEffect.Off:
                    return LedColor.Black;
                case LightEffect.Artwork:
                {
                    LedColor art = slot.Art.MaxComponent > 0f ? slot.Art : a;
                    // A gentle per-drone shimmer keeps the picture alive; Speed 0 holds it perfectly still.
                    float phase = ShowMath.Hash01(drone, 5);
                    float wave = MathF.Sin(ShowMath.TwoPi * (t * 0.35f * speed + phase));
                    float glint = wave > 0f ? MathF.Pow(wave, 12f) : 0f;
                    c = LedColor.Lerp(art * (1f - 0.12f * Math.Min(speed, 1f)), LedColor.White, 0.35f * glint * Math.Min(speed, 1f));
                    break;
                }
                case LightEffect.Solid:
                    c = a;
                    break;
                case LightEffect.Gradient:
                {
                    float angle = light.AngleDegrees * ShowMath.Deg2Rad;
                    float along = slot.Local.X * MathF.Cos(angle) + slot.Local.Y * MathF.Sin(angle);
                    c = LedColor.Lerp(a, b, ShowMath.Clamp01(0.5f + 0.5f * along));
                    break;
                }
                case LightEffect.Rainbow:
                {
                    float hue = slot.Local.X * 0.35f + slot.Local.Y * 0.15f - t * 0.12f * speed;
                    c = LedColor.FromHsv(hue, 0.88f, 1f);
                    break;
                }
                case LightEffect.Chase:
                {
                    // Two comets per lap: a bright head in colour A trailing into a dim bed of colour B.
                    float k = ShowMath.Frac(slot.U * 2f - t * 0.25f * speed);
                    float head = k * k * k * k * k;
                    c = LedColor.Lerp(b * 0.4f, a, head);
                    break;
                }
                case LightEffect.Twinkle:
                {
                    float phase = ShowMath.Hash01(drone, 1);
                    float rate = 0.6f + 0.8f * ShowMath.Hash01(drone, 2);
                    float wave = MathF.Sin(ShowMath.TwoPi * (t * speed * rate * 0.5f + phase));
                    float sparkle = wave > 0f ? MathF.Pow(wave, 16f) : 0f;
                    c = LedColor.Lerp(a * 0.6f, b, sparkle);
                    break;
                }
                case LightEffect.Pulse:
                {
                    float radius = MathF.Sqrt(slot.Local.X * slot.Local.X + slot.Local.Y * slot.Local.Y);
                    float k = 0.5f + 0.5f * MathF.Sin(ShowMath.TwoPi * t * 0.4f * speed - radius * 1.5f);
                    c = LedColor.Lerp(a, b, k) * (0.55f + 0.45f * k);
                    break;
                }
                case LightEffect.Radial:
                {
                    float r = slot.Local.Length();
                    float phase = ShowMath.Frac(r * 0.9f - t * 0.35f * speed);
                    c = LedColor.Lerp(a, b, 0.5f + 0.5f * MathF.Cos(ShowMath.TwoPi * phase));
                    break;
                }
                case LightEffect.Fire:
                {
                    float h = ShowMath.Hash01(drone, 3) * ShowMath.TwoPi;
                    float flicker = 0.5f * MathF.Sin(t * 6f * speed + h) + 0.5f * MathF.Sin(t * 9.7f * speed + slot.Local.X * 4f);
                    float heat = ShowMath.Clamp01(0.62f - 0.42f * slot.Local.Y + 0.28f * flicker);
                    c = LedColor.Lerp(b, a, heat) * (0.75f + 0.25f * heat);
                    break;
                }
                default:
                    c = a;
                    break;
            }
            return (c * light.Brightness).Clamped();
        }
    }
}
