using System;
using System.Collections.Generic;
using System.Numerics;

namespace DroneStar.Core
{
    /// <summary>
    /// The festival stage (<see cref="ShowVenue.Festival"/>): its layout and the timing of the effects each cue asks
    /// for (<see cref="Cue.Effects"/>). The renderer, the safety check and the exports all read it, and effect levels
    /// are pure functions of the compiled show and the time, so the editor preview, seeks, the demo run and the cue
    /// sheet always agree.
    /// </summary>
    public static class FestivalStage
    {
        /// <summary>Centre of the stage platform, on the lake between the audience shore and the launch pad.</summary>
        public static readonly Vector3 Center = new Vector3(0f, 0f, -205f);

        public const float PlatformRadius = 28f;
        public const float DeckHeight = 0.6f;
        public const float DrumBase = 1.6f;

        /// <summary>The boat wings run from here to <see cref="BoatEnd"/> metres either side of the centre.</summary>
        public const float BoatStart = 27f;

        public const float BoatEnd = 108f;

        /// <summary>Seconds per drumbeat; the drum, lasers, front jets and flames keep time with it.</summary>
        public const float Beat = 0.5f;

        /// <summary>A bar of the drum rhythm: four beats, a strong hit on the one and a lighter one on the three.</summary>
        public const float Bar = 4f * Beat;

        /// <summary>Tallest flame a projector throws, metres above its nozzle.</summary>
        public const float FlameReach = 12f;

        /// <summary>
        /// Distance every drone must keep from every flame while a scene's flames are armed, metres: well beyond
        /// the heat plume, as show crews keep drones clear of pyrotechnics.
        /// </summary>
        public const float FlameSafetyDistance = 20f;

        /// <summary>Flames are armed from this long before a hold (the first burst) ...</summary>
        public const float FlameArmLead = 0.5f;

        /// <summary>... until this long after it, when the last burst has died away.</summary>
        public const float FlameArmTail = 1.5f;

        /// <summary>Steam fades in over this long from the start of the cue's flight in ...</summary>
        public const float SteamFadeIn = 2f;

        /// <summary>... and out over this long after its hold.</summary>
        public const float SteamFadeOut = 2.5f;

        /// <summary>Flame projectors per boat wing; the rest of <see cref="FlameNozzles"/> line the platform's front.</summary>
        public const int FlamesPerBoat = 6;

        public const int FrontFlames = 8;

        public static float BoatDeck(float s) => 1.0f + 5.2f * s * s * s;

        public static float BoatHalfWidth(float s) => 6.4f * (1f - 0.82f * s * s);

        /// <summary>Point on a boat wing's centre line at <paramref name="s"/> (0 at the platform, 1 at the prow), side −1 left or +1 right.</summary>
        public static Vector3 BoatAt(float s, int side) => Center + new Vector3(side * (BoatStart + (BoatEnd - BoatStart) * s), 0f, 0f);

        static readonly Vector3[] flameNozzles = BuildFlameNozzles();
        static readonly Vector3 flameMin, flameMax;

        static FestivalStage()
        {
            flameMin = new Vector3(float.MaxValue);
            flameMax = new Vector3(float.MinValue);
            foreach (Vector3 n in flameNozzles)
            {
                flameMin = Vector3.Min(flameMin, n);
                flameMax = Vector3.Max(flameMax, n + new Vector3(0f, FlameReach, 0f));
            }
        }

        /// <summary>
        /// Flame projector nozzles: <see cref="FlamesPerBoat"/> along each boat wing's deck (left, then right, from
        /// the platform outward), then <see cref="FrontFlames"/> across the platform's front edge, left to right.
        /// </summary>
        public static IReadOnlyList<Vector3> FlameNozzles => flameNozzles;

        static Vector3[] BuildFlameNozzles()
        {
            var list = new List<Vector3>();
            for (int side = -1; side <= 1; side += 2)
            {
                for (int k = 0; k < FlamesPerBoat; k++)
                {
                    float s = 0.1f + 0.14f * k;
                    list.Add(BoatAt(s, side) + new Vector3(0f, BoatDeck(s) + 0.4f, 0f));
                }
            }
            // An arc across the audience side (−z) of the platform, in front of the drum.
            for (int k = 0; k < FrontFlames; k++)
            {
                float a = MathF.PI * (1.25f + 0.5f * k / (FrontFlames - 1));
                float r = PlatformRadius - 2f;
                list.Add(Center + new Vector3(MathF.Cos(a) * r, DeckHeight + 0.3f, MathF.Sin(a) * r));
            }
            return list.ToArray();
        }

        /// <summary>Which bank projector <paramref name="jet"/> belongs to (0 left boat, 1 right boat, 2 front) and its place in it.</summary>
        public static int FlameBank(int jet, out int along)
        {
            if (jet < FlamesPerBoat)
            {
                along = jet;
                return 0;
            }
            if (jet < 2 * FlamesPerBoat)
            {
                along = jet - FlamesPerBoat;
                return 1;
            }
            along = jet - 2 * FlamesPerBoat;
            return 2;
        }

        // ------------------------------------------------------------------ flames

        /// <summary>
        /// The cue whose flames are armed at time <paramref name="t"/> (from <see cref="FlameArmLead"/> before its
        /// hold to <see cref="FlameArmTail"/> after it), or −1. Flames exist only at the festival venue.
        /// </summary>
        public static int ArmedFlameCue(CompiledShow show, float t)
        {
            if (show == null || show.Document.Venue != ShowVenue.Festival) return -1;
            IReadOnlyList<CueTiming> timings = show.CueTimings;
            for (int i = timings.Count - 1; i >= 0; i--)
            {
                if (show.Document.Cues[i].Effects.Flames == FlameMode.Off) continue;
                CueTiming c = timings[i];
                if (t >= c.HoldStart - FlameArmLead && t <= c.HoldEnd + FlameArmTail) return i;
            }
            return -1;
        }

        /// <summary>Flame height of projector <paramref name="jet"/> at time t, as a fraction of <see cref="FlameReach"/> (0 = not firing).</summary>
        public static float FlameLevel(CompiledShow show, int jet, float t)
        {
            if (show == null || show.Document.Venue != ShowVenue.Festival || jet < 0 || jet >= flameNozzles.Length) return 0f;
            float level = 0f;
            IReadOnlyList<CueTiming> timings = show.CueTimings;
            for (int i = 0; i < timings.Count; i++)
            {
                FlameMode mode = show.Document.Cues[i].Effects.Flames;
                if (mode == FlameMode.Off) continue;
                CueTiming c = timings[i];
                float local = t - c.HoldStart;
                if (local < -FlameArmLead || local > c.HoldSeconds + FlameArmTail) continue;
                level = Math.Max(level, Burst(mode, jet, local, c.HoldSeconds));
            }
            return level;
        }

        /// <summary>
        /// A cue's flame programme, <paramref name="local"/> seconds after its hold starts. Bursts start only inside
        /// the hold; each flares for a moment, holds, then dies away within half a second.
        /// </summary>
        public static float Burst(FlameMode mode, int jet, float local, float hold)
        {
            if (mode == FlameMode.Off || local < 0f) return 0f;
            int bank = FlameBank(jet, out int along);
            float best = 0f;
            int last = (int)MathF.Floor(local / Bar);
            for (int j = Math.Max(0, last - 1); j <= last; j++)
            {
                float barStart = j * Bar;
                if (mode == FlameMode.Salvo)
                {
                    // A long opening burst as the scene appears, then a volley on every bar, rippling outward.
                    float ripple = 0.03f * along;
                    if (j == 0) best = Math.Max(best, Pulse(local, barStart + ripple, hold, 1.2f, 1f));
                    else best = Math.Max(best, Pulse(local, barStart + ripple, hold, 0.45f, 0.8f));
                }
                else
                {
                    // The drum rhythm: the front row and one boat on the one (boats alternate bar by bar), the
                    // other boat on the three; a boat's projectors fire in a quick ripple from the platform out.
                    int boatOnOne = j % 2;
                    float ripple = 0.04f * along;
                    if (bank == 2 || bank == boatOnOne) best = Math.Max(best, Pulse(local, barStart + ripple, hold, 0.35f, 1f));
                    else best = Math.Max(best, Pulse(local, barStart + 3f * Beat + ripple, hold, 0.35f, 0.85f));
                }
            }
            return best < 0.01f ? 0f : best;
        }

        static float Pulse(float local, float start, float hold, float on, float peak)
        {
            if (start >= hold) return 0f;
            float tau = local - start;
            if (tau < 0f) return 0f;
            if (tau < on) return peak * Math.Min(1f, tau / 0.06f);
            return peak * MathF.Exp(-(tau - on) / 0.12f);
        }

        /// <summary>Distance from <paramref name="p"/> to the nearest flame column (a nozzle up to its full reach).</summary>
        public static float DistanceToFlames(Vector3 p)
        {
            float best = float.PositiveInfinity;
            foreach (Vector3 n in flameNozzles)
            {
                float y = Math.Min(Math.Max(p.Y, n.Y), n.Y + FlameReach);
                float dx = p.X - n.X, dy = p.Y - y, dz = p.Z - n.Z;
                best = Math.Min(best, dx * dx + dy * dy + dz * dz);
            }
            return MathF.Sqrt(best);
        }

        /// <summary>Distance from <paramref name="p"/> to the box around every flame column: a cheap lower bound of <see cref="DistanceToFlames"/>.</summary>
        public static float DistanceToFlameBounds(Vector3 p)
        {
            Vector3 d = Vector3.Max(Vector3.Max(flameMin - p, p - flameMax), Vector3.Zero);
            return d.Length();
        }

        // ------------------------------------------------------------------ steam

        /// <summary>
        /// How much steam rises at time t, 0..1: it fades in from the start of each steaming cue's flight in and
        /// out after its hold.
        /// </summary>
        public static float SteamLevel(CompiledShow show, float t)
        {
            if (show == null || show.Document.Venue != ShowVenue.Festival) return 0f;
            float level = 0f;
            IReadOnlyList<CueTiming> timings = show.CueTimings;
            for (int i = 0; i < timings.Count; i++)
            {
                if (!show.Document.Cues[i].Effects.Steam) continue;
                CueTiming c = timings[i];
                float up = (t - c.TransitStart) / SteamFadeIn;
                float down = 1f - (t - c.HoldEnd) / SteamFadeOut;
                level = Math.Max(level, ShowMath.Clamp(Math.Min(up, down), 0f, 1f));
            }
            return level;
        }

        // ------------------------------------------------------------------ descriptions

        /// <summary>A short human-readable summary, e.g. "lasers sweep · fountains tall · flames salvo · steam".</summary>
        public static string Describe(StageEffects fx)
        {
            if (fx == null) return "";
            var parts = new List<string>(4)
            {
                "lasers " + fx.Lasers.ToString().ToLowerInvariant(),
                "fountains " + fx.Fountains.ToString().ToLowerInvariant(),
            };
            if (fx.Flames != FlameMode.Off) parts.Add("flames " + fx.Flames.ToString().ToLowerInvariant());
            if (fx.Steam) parts.Add("steam");
            return string.Join(" · ", parts);
        }
    }
}
