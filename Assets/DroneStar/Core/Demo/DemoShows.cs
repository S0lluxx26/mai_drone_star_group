using System;
using System.Collections.Generic;
using System.Numerics;

namespace DroneStar.Core
{
    /// <summary>Built-in shows: the flagship demo and starting templates.</summary>
    public static class DemoShows
    {
        public static readonly LedColor Gold = LedColor.FromHex("#FFC21A");
        public static readonly LedColor Amber = LedColor.FromHex("#FF8A00");
        public static readonly LedColor Coral = LedColor.FromHex("#FF5A36");
        public static readonly LedColor Crimson = LedColor.FromHex("#FF1744");
        public static readonly LedColor Magenta = LedColor.FromHex("#FF2E88");
        public static readonly LedColor Pink = LedColor.FromHex("#FF6FB5");
        public static readonly LedColor Violet = LedColor.FromHex("#8A3FFC");
        public static readonly LedColor Blue = LedColor.FromHex("#1E6BFF");
        public static readonly LedColor Cyan = LedColor.FromHex("#16E0FF");
        public static readonly LedColor Teal = LedColor.FromHex("#00D1A0");
        public static readonly LedColor Lime = LedColor.FromHex("#9BFF2E");
        public static readonly LedColor WarmWhite = LedColor.FromHex("#FFE7B0");

        /// <summary>The swatches offered by the colour pickers, in display order.</summary>
        public static readonly LedColor[] Palette =
        {
            Gold, Amber, Coral, Crimson, Magenta, Pink, Violet, Blue, Cyan, Teal, Lime, WarmWhite,
        };

        public sealed class Template
        {
            public string Name;
            public string Description;
            public Func<ShowDocument> Create;
        }

        public static readonly IReadOnlyList<Template> Templates = new[]
        {
            new Template { Name = "A Night of Stars", Description = "The 360-drone flagship demo: twelve scenes, from a rainbow curtain to a starburst finale.", Create = StarGroupNight },
            new Template { Name = "Heart & Rings", Description = "A short 200-drone wedding piece.", Create = HeartAndRings },
            new Template { Name = "Blank Show", Description = "One formation to start from.", Create = Blank },
        };

        const float ShowAltitude = 62f;

        public static ShowDocument StarGroupNight()
        {
            var doc = new ShowDocument
            {
                Title = "A Night of Stars",
                Author = "Mai Drone Star Group",
                DroneCount = 360,
            };
            doc.Cues.Add(MakeCue("Curtain Rise", FormationKind.Grid, 70f,
                light: Light(LightEffect.Rainbow, Gold, Magenta),
                motion: new MotionSpec { Kind = MotionKind.Wave, Amount = 1.6f, FrequencyHz = 0.18f }, hold: 7f));
            doc.Cues.Add(MakeCue("Hello, Mai", FormationKind.Text, 64f, text: "MAI", layers: 2,
                light: Light(LightEffect.Gradient, Gold, Magenta, angle: 90f), hold: 8f));
            doc.Cues.Add(MakeCue("Five-Point Star", FormationKind.Star, 66f,
                light: Light(LightEffect.Chase, Gold, Amber),
                motion: new MotionSpec { Kind = MotionKind.Roll, DegreesPerSecond = 10f }, hold: 10f));
            doc.Cues.Add(MakeCue("Blue Planet", FormationKind.Sphere, 40f,
                light: Light(LightEffect.Radial, Cyan, Blue),
                motion: new MotionSpec { Kind = MotionKind.Turntable, DegreesPerSecond = 15f }, hold: 10f));
            doc.Cues.Add(MakeCue("Spiral Galaxy", FormationKind.Galaxy, 90f, points: 4, pitch: -28f,
                light: Light(LightEffect.Twinkle, Violet, Pink),
                motion: new MotionSpec { Kind = MotionKind.Roll, DegreesPerSecond = 8f }, hold: 10f));
            doc.Cues.Add(MakeCue("Heart of the Group", FormationKind.Heart, 56f,
                light: Light(LightEffect.Pulse, Crimson, Pink),
                motion: new MotionSpec { Kind = MotionKind.Breathe, Amount = 0.12f, FrequencyHz = 0.2f }, hold: 9f));
            doc.Cues.Add(MakeCue("Double Helix", FormationKind.Helix, 68f, turns: 2.5f,
                light: Light(LightEffect.Rainbow, Gold, Magenta, speed: 1.5f),
                motion: new MotionSpec { Kind = MotionKind.Turntable, DegreesPerSecond = 20f }, hold: 10f));
            doc.Cues.Add(MakeCue("Blossom", FormationKind.Flower, 68f, points: 6,
                light: Light(LightEffect.Radial, Pink, Amber),
                motion: new MotionSpec { Kind = MotionKind.Roll, DegreesPerSecond = 12f }, hold: 9f));
            doc.Cues.Add(MakeCue("Butterfly", FormationKind.Butterfly, 64f,
                light: Light(LightEffect.Gradient, Teal, Violet, angle: 0f),
                motion: new MotionSpec { Kind = MotionKind.Wave, Amount = 1.6f, FrequencyHz = 0.18f }, hold: 9f));
            doc.Cues.Add(MakeCue("Star Group", FormationKind.Text, 76f, text: "STAR", layers: 2,
                light: Light(LightEffect.Chase, Gold, Violet, speed: 1.2f), hold: 8f));
            doc.Cues.Add(MakeCue("Crown of Light", FormationKind.Ring, 64f,
                light: Light(LightEffect.Rainbow, Gold, Magenta),
                motion: new MotionSpec { Kind = MotionKind.Turntable, DegreesPerSecond = 10f }, hold: 9f));
            doc.Cues.Add(MakeCue("Starburst Finale", FormationKind.Star, 72f, points: 8, layers: 2,
                light: Light(LightEffect.Twinkle, Gold, WarmWhite, speed: 1.6f), hold: 9f));
            ShowSanitizer.Sanitize(doc);
            return doc;
        }

        public static ShowDocument HeartAndRings()
        {
            var doc = new ShowDocument { Title = "Heart & Rings", DroneCount = 200 };
            doc.Cues.Add(MakeCue("Two Rings", FormationKind.Ring, 48f, style: FillStyle.Outline, layers: 2,
                light: Light(LightEffect.Chase, Gold, WarmWhite),
                motion: new MotionSpec { Kind = MotionKind.Roll, DegreesPerSecond = 12f }, hold: 8f));
            doc.Cues.Add(MakeCue("Heart", FormationKind.Heart, 44f,
                light: Light(LightEffect.Pulse, Crimson, Pink),
                motion: new MotionSpec { Kind = MotionKind.Breathe, Amount = 0.1f, FrequencyHz = 0.2f }, hold: 10f));
            doc.Cues.Add(MakeCue("Forever", FormationKind.Text, 60f, text: "LOVE",
                light: Light(LightEffect.Gradient, Pink, Gold, angle: 0f), hold: 8f));
            ShowSanitizer.Sanitize(doc);
            return doc;
        }

        public static ShowDocument Blank()
        {
            var doc = new ShowDocument { Title = "Untitled Show", DroneCount = 150 };
            doc.Cues.Add(MakeCue("Opening Sphere", FormationKind.Sphere, 34f,
                light: Light(LightEffect.Radial, Cyan, Violet), hold: 8f));
            ShowSanitizer.Sanitize(doc);
            return doc;
        }

        /// <summary>A new cue with sensible defaults for the given shape, used by the editor's "Add cue".</summary>
        public static Cue NewCue(FormationKind kind, int index)
        {
            LedColor a = Palette[(index * 3) % Palette.Length];
            LedColor b = Palette[(index * 3 + 5) % Palette.Length];
            float size = kind == FormationKind.Sphere ? 36f : kind == FormationKind.Text ? 64f : 56f;
            return MakeCue(kind + " " + (index + 1), kind, size, light: Light(LightEffect.Gradient, a, b), hold: 8f);
        }

        static Cue MakeCue(string name, FormationKind kind, float size, LightSpec light, MotionSpec motion = null, float hold = 8f,
            string text = "MAI", int points = 5, float turns = 2f, float pitch = 0f, int layers = 1, FillStyle style = FillStyle.Filled)
        {
            return new Cue
            {
                Name = name,
                HoldSeconds = hold,
                AutoTransition = true,
                Formation = new FormationSpec
                {
                    Kind = kind,
                    Style = style,
                    Center = new Vector3(0f, ShowAltitude, 0f),
                    Size = size,
                    Text = text,
                    Points = points,
                    Turns = turns,
                    PitchDegrees = pitch,
                    Layers = layers,
                },
                Light = light,
                Motion = motion ?? new MotionSpec(),
            };
        }

        static LightSpec Light(LightEffect effect, LedColor a, LedColor b, float speed = 1f, float angle = 90f)
        {
            return new LightSpec { Effect = effect, ColorA = a, ColorB = b, Speed = speed, AngleDegrees = angle };
        }
    }
}
