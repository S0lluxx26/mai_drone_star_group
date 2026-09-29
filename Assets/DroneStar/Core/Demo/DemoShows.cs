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
            new Template { Name = "A Night of Stars", Description = "The 8,192-drone flagship: sixteen scenes with full-colour 3D models (robot, whale, Eiffel Tower, starship...) and a starburst finale. The Show tab's fleet presets fly it with fewer drones.", Create = StarGroupNight },
            new Template { Name = "Rise of the Lạc Bird", Description = "Demo 2: 8,192 drones over a Đông Sơn festival stage. A golden Lạc bird beats its wings above the bronze drum, with fountains, mist and lasers.", Create = LacBirdFestival },
            new Template { Name = "Classic Night", Description = "The original 360-drone show: light enough for any device.", Create = ClassicNight },
            new Template { Name = "Heart & Rings", Description = "A short 200-drone wedding piece.", Create = HeartAndRings },
            new Template { Name = "Blank Show", Description = "One formation to start from.", Create = Blank },
        };

        const float ShowAltitude = 62f;

        /// <summary>Formation centre height for the flagship's 2048-drone design, metres.</summary>
        const float GrandAltitude = 100f;

        /// <summary>Drones in the flagship show: about the size of today's record-setting shows.</summary>
        public const int FlagshipDrones = 8192;

        /// <summary>
        /// The flagship: 8,192 drones, sixteen scenes mixing the studio's parametric shapes with full-colour 3D
        /// models from the shape library (ported from the Draw_in_3D drone show). Safety spacing fixes how many
        /// drones a shape of a given size can hold, so more drones means bigger shapes drawn in finer detail.
        /// The show is designed at 2048 drones and grown with <see cref="ShowScaler"/>, exactly as the editor's
        /// fleet presets do, so every preset is the same show at another scale.
        /// </summary>
        public static ShowDocument StarGroupNight()
        {
            ShowDocument doc = StarGroupNightDesign();
            ShowScaler.ResizeForDroneCount(doc, FlagshipDrones);
            return doc;
        }

        /// <summary>
        /// The flagship's design at 2048 drones. A 1.2 m minimum separation and 12 m/s top speed are typical of
        /// large professional shows.
        /// </summary>
        static ShowDocument StarGroupNightDesign()
        {
            var doc = new ShowDocument
            {
                Title = "A Night of Stars",
                Author = "Mai Drone Star Group",
                DroneCount = 2048,
            };
            doc.Limits.MinSeparation = 1.2f;
            doc.Limits.MaxSpeed = 12f;
            doc.Limits.MaxAcceleration = 5f;
            doc.Limits.MaxAltitude = 220f;
            doc.Limits.GeofenceRadius = 260f;
            doc.Pad.Spacing = 2f;
            float a = GrandAltitude;
            doc.Cues.Add(MakeCue("Curtain Rise", FormationKind.Grid, 134f, altitude: a,
                light: Light(LightEffect.Rainbow, Gold, Magenta),
                motion: new MotionSpec { Kind = MotionKind.Wave, Amount = 3f, FrequencyHz = 0.16f }, hold: 7f));
            doc.Cues.Add(MakeCue("Hello, Mai", FormationKind.Text, 122f, text: "MAI", layers: 2, altitude: a,
                light: Light(LightEffect.Gradient, Gold, Magenta, angle: 90f), hold: 8f));
            doc.Cues.Add(ModelCue("Robot Friend", "Robot", 103f, a,
                new MotionSpec { Kind = MotionKind.Turntable, DegreesPerSecond = 8f }, hold: 9f));
            doc.Cues.Add(ModelCue("Fish", "Fish", 160f, a,
                new MotionSpec { Kind = MotionKind.Wave, Amount = 2f, FrequencyHz = 0.2f }, hold: 8f));
            doc.Cues.Add(MakeCue("Blue Planet", FormationKind.Sphere, 77f, altitude: a,
                light: Light(LightEffect.Radial, Cyan, Blue),
                motion: new MotionSpec { Kind = MotionKind.Turntable, DegreesPerSecond = 12f }, hold: 9f));
            doc.Cues.Add(ModelCue("Butterfly", "Butterfly", 144f, a,
                new MotionSpec { Kind = MotionKind.Wave, Amount = 2f, FrequencyHz = 0.2f }, hold: 8f));
            doc.Cues.Add(ModelCue("Hot-Air Balloons", "Hot air balloon", 113f, a,
                new MotionSpec { Kind = MotionKind.Rise, Amount = 12f }, hold: 9f));
            doc.Cues.Add(MakeCue("Spiral Galaxy", FormationKind.Galaxy, 172f, points: 4, pitch: -28f, altitude: a,
                light: Light(LightEffect.Twinkle, Violet, Pink),
                motion: new MotionSpec { Kind = MotionKind.Roll, DegreesPerSecond = 5f }, hold: 9f));
            doc.Cues.Add(ModelCue("Eiffel Tower", "Eiffel Tower", 105f, a,
                new MotionSpec { Kind = MotionKind.Turntable, DegreesPerSecond = 10f }, hold: 9f));
            doc.Cues.Add(MakeCue("Heart of the Group", FormationKind.Heart, 107f, altitude: a,
                light: Light(LightEffect.Pulse, Crimson, Pink),
                motion: new MotionSpec { Kind = MotionKind.Breathe, Amount = 0.1f, FrequencyHz = 0.2f }, hold: 8f));
            doc.Cues.Add(ModelCue("Big Ship", "Big ship", 88f, a, null, hold: 8f, speed: 0.6f));
            doc.Cues.Add(ModelCue("Whale", "Whale", 146f, a,
                new MotionSpec { Kind = MotionKind.Wave, Amount = 2.5f, FrequencyHz = 0.18f }, hold: 9f));
            doc.Cues.Add(ModelCue("Birthday Cake", "Birthday cake", 61f, a,
                new MotionSpec { Kind = MotionKind.Turntable, DegreesPerSecond = 12f }, hold: 9f, speed: 1.2f));
            doc.Cues.Add(ModelCue("Starship Launch", "Starship launch", 135f, a,
                new MotionSpec { Kind = MotionKind.Rise, Amount = 25f }, hold: 10f));
            doc.Cues.Add(ModelCue("Happy Day", "Happy day", 128f, a, null, hold: 8f, speed: 1.5f));
            doc.Cues.Add(MakeCue("Starburst Finale", FormationKind.Star, 142f, points: 8, layers: 2, altitude: a,
                light: Light(LightEffect.Twinkle, Gold, WarmWhite, speed: 1.6f), hold: 9f));
            ShowSanitizer.Sanitize(doc);
            return doc;
        }

        /// <summary>
        /// Demo 2, "Rise of the Lạc Bird": a Đông Sơn festival. The bronze drum's face, its fourteen-ray sun, the
        /// river, a lotus, and the Lạc bird of the drums beating its wings above a festival stage with fountains and
        /// lasers. Designed at 2048 drones and grown to the flagship's fleet like the first demo.
        /// </summary>
        public static ShowDocument LacBirdFestival()
        {
            ShowDocument doc = LacBirdFestivalDesign();
            ShowScaler.ResizeForDroneCount(doc, FlagshipDrones);
            return doc;
        }

        static ShowDocument LacBirdFestivalDesign()
        {
            var doc = new ShowDocument
            {
                Title = "Rise of the Lạc Bird",
                Author = "Mai Drone Star Group",
                DroneCount = 2048,
                Venue = ShowVenue.Festival,
            };
            doc.Limits.MinSeparation = 1.2f;
            doc.Limits.MaxSpeed = 12f;
            doc.Limits.MaxAcceleration = 5f;
            doc.Limits.MaxAltitude = 220f;
            doc.Limits.GeofenceRadius = 260f;
            doc.Pad.Spacing = 2f;
            float a = GrandAltitude;
            doc.Cues.Add(ModelCue("Drum of Đông Sơn", "Bronze drum", 150f, a,
                new MotionSpec { Kind = MotionKind.Roll, DegreesPerSecond = 6f }, hold: 10f));
            doc.Cues.Add(MakeCue("Sun of the Drum", FormationKind.Star, 150f, points: 14, layers: 2, altitude: a,
                light: Light(LightEffect.Radial, WarmWhite, Amber),
                motion: new MotionSpec { Kind = MotionKind.Breathe, Amount = 0.08f, FrequencyHz = 0.2f }, hold: 8f));
            doc.Cues.Add(MakeCue("River of Waves", FormationKind.Wave, 170f, altitude: a,
                light: Light(LightEffect.Gradient, Cyan, Blue),
                motion: new MotionSpec { Kind = MotionKind.Wave, Amount = 2.5f, FrequencyHz = 0.16f }, hold: 8f));
            doc.Cues.Add(MakeCue("Lotus in Bloom", FormationKind.Flower, 140f, points: 8, altitude: a,
                light: Light(LightEffect.Gradient, Pink, WarmWhite),
                motion: new MotionSpec { Kind = MotionKind.Breathe, Amount = 0.1f, FrequencyHz = 0.18f }, hold: 8f));
            // Wing tips sit tens of metres from the shoulder, so a bird this size beats its wings slowly (within the
            // speed and acceleration limits); bigger fleets slow it further.
            doc.Cues.Add(ModelCue("Rise of the Lạc Bird", "Lac bird", 180f, a,
                new MotionSpec { Kind = MotionKind.Flap, Amount = 10f, FrequencyHz = 0.065f }, hold: 16f));
            doc.Cues.Add(MakeCue("Heart of the Festival", FormationKind.Heart, 110f, altitude: a,
                light: Light(LightEffect.Pulse, Crimson, Gold),
                motion: new MotionSpec { Kind = MotionKind.Breathe, Amount = 0.08f, FrequencyHz = 0.2f }, hold: 8f));
            doc.Cues.Add(ModelCue("The Lạc Bird Soars", "Lac bird", 180f, a,
                new MotionSpec { Kind = MotionKind.Rise, Amount = 25f }, hold: 12f, speed: 1.6f));
            ShowSanitizer.Sanitize(doc);
            return doc;
        }

        /// <summary>The original 360-drone show (every shape parametric, light enough for any device).</summary>
        public static ShowDocument ClassicNight()
        {
            var doc = new ShowDocument
            {
                Title = "Classic Night",
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

        /// <summary>
        /// The shows whose large-fleet transitions ship pre-solved in DemoAssignments.bytes: both demos at every
        /// fleet preset that needs the auction solver, resized exactly as the editor's fleet presets do.
        /// </summary>
        public static IEnumerable<ShowDocument> PresolvedShows()
        {
            foreach (Func<ShowDocument> demo in new Func<ShowDocument>[] { StarGroupNight, LacBirdFestival })
            {
                foreach (int size in ShowBounds.FleetSizes)
                {
                    if (size <= ShowCompiler.ExactAssignmentLimit) continue;
                    ShowDocument doc = demo();
                    if (doc.DroneCount != size) ShowScaler.ResizeForDroneCount(doc, size);
                    yield return doc;
                }
            }
        }

        /// <summary>The name shown in the editor for a formation kind ("Custom" is presented as "Sketch").</summary>
        public static string KindLabel(FormationKind kind) => kind == FormationKind.Custom ? "Sketch" : kind.ToString();

        /// <summary>
        /// A new cue with sensible defaults for the given shape, used by the editor's "Add cue". With a
        /// <paramref name="context"/> show, the size follows its fleet (the square root of the drone count,
        /// relative to a 360-drone show at 1.5 m separation) and the altitude matches its existing cues.
        /// </summary>
        public static Cue NewCue(FormationKind kind, int index, ShowDocument context = null, string model = "Robot")
        {
            LedColor a = Palette[(index * 3) % Palette.Length];
            LedColor b = Palette[(index * 3 + 5) % Palette.Length];
            float size = kind == FormationKind.Sphere ? 36f : kind == FormationKind.Text ? 64f : kind == FormationKind.Custom ? 80f
                : kind == FormationKind.Model ? 60f : 56f;
            float altitude = ShowAltitude;
            if (context != null)
            {
                float fleet = MathF.Sqrt(Math.Max(context.DroneCount, 1) / 360f) * (context.Limits.MinSeparation / 1.5f);
                size *= Math.Max(0.4f, fleet);
                float sum = 0f;
                int count = 0;
                foreach (Cue c in context.Cues)
                {
                    if (c?.Formation == null) continue;
                    sum += c.Formation.Center.Y;
                    count++;
                }
                altitude = count > 0 ? sum / count : Math.Max(ShowAltitude, size * 0.6f + 20f);
            }
            LightSpec light = kind == FormationKind.Model ? Light(LightEffect.Artwork, a, b) : Light(LightEffect.Gradient, a, b);
            string name = kind == FormationKind.Model ? model : KindLabel(kind) + " " + (index + 1);
            Cue cue = MakeCue(name, kind, size, light: light, hold: 8f, layers: kind == FormationKind.Custom ? 2 : 1, altitude: altitude);
            if (kind == FormationKind.Custom) cue.Formation.CustomPoints = CustomShapes.Smiley();
            if (kind == FormationKind.Model)
            {
                cue.Formation.Model = model;
                // Models differ a lot in how many drones they hold at a given size: size this one for the fleet.
                if (context != null && ShapeLibrary.TryGet(model, out _))
                {
                    cue.Formation.Size = size * 0.5f;
                    cue.Formation.Size = FormationGenerator.SizeToLight(cue.Formation, context.DroneCount, context.Limits.FormationSpacing);
                    float clear = cue.Formation.Size * 0.6f + context.Pad.HoverAltitude;
                    if (cue.Formation.Center.Y < clear) cue.Formation.Center = new Vector3(cue.Formation.Center.X, clear, cue.Formation.Center.Z);
                }
            }
            return cue;
        }

        static Cue ModelCue(string name, string model, float size, float altitude, MotionSpec motion, float hold = 8f, float speed = 1f)
        {
            Cue cue = MakeCue(name, FormationKind.Model, size, Light(LightEffect.Artwork, Gold, Magenta, speed: speed), motion, hold, altitude: altitude);
            cue.Formation.Model = model;
            return cue;
        }

        static Cue MakeCue(string name, FormationKind kind, float size, LightSpec light, MotionSpec motion = null, float hold = 8f,
            string text = "MAI", int points = 5, float turns = 2f, float pitch = 0f, int layers = 1, FillStyle style = FillStyle.Filled,
            float altitude = ShowAltitude)
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
                    Center = new Vector3(0f, altitude, 0f),
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
