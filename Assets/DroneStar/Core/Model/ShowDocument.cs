using System.Collections.Generic;
using System.Numerics;

namespace DroneStar.Core
{
    public enum FormationKind
    {
        Grid,
        Ring,
        Sphere,
        Star,
        Heart,
        Helix,
        Galaxy,
        Wave,
        Cube,
        Text,
        Flower,
        Butterfly,
        Custom,
        /// <summary>A pre-built coloured 3D model from the shape library (robot, whale, Eiffel Tower...).</summary>
        Model,
    }

    public enum FillStyle
    {
        Filled,
        Outline,
    }

    public enum LightEffect
    {
        Solid,
        Gradient,
        Rainbow,
        Chase,
        Twinkle,
        Pulse,
        Radial,
        Fire,
        Off,
        /// <summary>The model's own baked colours (falls back to colour A on other shapes).</summary>
        Artwork,
    }

    public enum MotionKind
    {
        None,
        /// <summary>Rigid rotation about the vertical axis through the formation centre.</summary>
        Turntable,
        /// <summary>Rigid rotation about the formation's facing axis.</summary>
        Roll,
        /// <summary>Scales the formation outward and back; spacing never shrinks.</summary>
        Breathe,
        /// <summary>A travelling ripple along the formation's facing axis.</summary>
        Wave,
        /// <summary>The whole formation drifts upward by Amount metres (balloons, rockets).</summary>
        Rise,
        /// <summary>
        /// Wings beat: models with wings (the Lạc bird) swing each wing rigidly about its shoulder by up to Amount
        /// degrees at FrequencyHz; the body stays still. Shapes without wings hold still.
        /// </summary>
        Flap,
    }

    /// <summary>Hard numeric bounds for every editable field. The sanitizer clamps loaded and edited values to these.</summary>
    public static class ShowBounds
    {
        public const int MinDrones = 1;
        public const int MaxDrones = 8192;

        /// <summary>Fleet sizes offered as presets in the editor.</summary>
        public static readonly int[] FleetSizes = { 256, 512, 1024, 2048, 4096, 8192 };
        public const int MaxCues = 64;
        public const float MinSize = 4f;
        public const float MaxSize = 400f;
        public const int MaxLayers = 4;
        public const int MinPoints = 2;
        public const int MaxPoints = 12;
        public const float MinTurns = 0.5f;
        public const float MaxTurns = 6f;
        public const int MaxTextLength = 24;
        public const float MinTransition = 2f;
        public const float MaxTransition = 120f;
        public const float MaxHold = 120f;
        public const float MaxAngle = 180f;
        public const float MaxCoordinate = 800f;
        public const float MaxSpin = 90f;
        public const float MaxFrequency = 2f;
        public const float MaxEffectSpeed = 5f;
        public const float MaxBrightness = 1f;
        public const float MaxMotionAmount = 60f;

        /// <summary>Largest wingbeat, degrees either side of the resting wing.</summary>
        public const float MaxFlapDegrees = 35f;
    }

    public sealed class FormationSpec
    {
        public FormationKind Kind = FormationKind.Sphere;
        public FillStyle Style = FillStyle.Filled;

        /// <summary>World centre in metres (x right, y up, z away from the audience).</summary>
        public Vector3 Center = new Vector3(0f, 60f, 0f);

        /// <summary>Overall width in metres (height for Helix, diameter for Sphere and Ring).</summary>
        public float Size = 40f;

        public float YawDegrees;
        public float PitchDegrees;

        /// <summary>Depth layers for flat shapes (1 = a single sheet facing the audience).</summary>
        public int Layers = 1;

        public string Text = "MAI";

        /// <summary>Library model name for <see cref="FormationKind.Model"/>.</summary>
        public string Model = "Robot";

        /// <summary>Star points, flower petals or galaxy arms.</summary>
        public int Points = 5;

        /// <summary>Helix turns.</summary>
        public float Turns = 2f;

        /// <summary>Custom shape points in normalised units (the shape's half-size is 1).</summary>
        public List<Vector3> CustomPoints = new List<Vector3>();

        public FormationSpec Clone()
        {
            FormationSpec c = (FormationSpec)MemberwiseClone();
            c.CustomPoints = CustomPoints != null ? new List<Vector3>(CustomPoints) : new List<Vector3>();
            return c;
        }
    }

    public sealed class LightSpec
    {
        public LightEffect Effect = LightEffect.Solid;
        public LedColor ColorA = new LedColor(1f, 0.85f, 0.3f);
        public LedColor ColorB = new LedColor(1f, 0.25f, 0.55f);

        /// <summary>Animation speed multiplier (1 = default tempo).</summary>
        public float Speed = 1f;

        public float Brightness = 1f;

        /// <summary>Direction of the Gradient effect in the formation plane (90 = bottom to top).</summary>
        public float AngleDegrees = 90f;

        public LightSpec Clone() => (LightSpec)MemberwiseClone();
    }

    public sealed class MotionSpec
    {
        public MotionKind Kind = MotionKind.None;

        /// <summary>Rotation rate for Turntable and Roll.</summary>
        public float DegreesPerSecond = 12f;

        /// <summary>Oscillation frequency for Breathe and Wave.</summary>
        public float FrequencyHz = 0.25f;

        /// <summary>Breathe: fractional growth (0.15 = 15 %). Wave: ripple amplitude in metres.</summary>
        public float Amount = 0.15f;

        public MotionSpec Clone() => (MotionSpec)MemberwiseClone();
    }

    /// <summary>What the festival stage's lasers do while a cue flies in and holds.</summary>
    public enum LaserMode
    {
        /// <summary>Follow the show: tunnels at take-off, fans in the holds, searching beams in the flights.</summary>
        Auto,
        Off,
        /// <summary>Still fans: the prows open outward and the centre fan leans back behind the drum.</summary>
        Fans,
        /// <summary>The fans sweep from side to side over each bar of the drum.</summary>
        Sweep,
        /// <summary>The prow fans lean in over the stage, a roof of light.</summary>
        Tunnel,
    }

    /// <summary>What the festival fountains do while a cue flies in and holds.</summary>
    public enum FountainMode
    {
        /// <summary>Follow the show: a burst as each scene appears, the arched water screen in the holds.</summary>
        Auto,
        Off,
        /// <summary>Jets chase each other on the drumbeat.</summary>
        Dance,
        /// <summary>The arched water screen stands tall behind the drum; the rest run low.</summary>
        Arch,
        /// <summary>Every jet at full height.</summary>
        Tall,
    }

    /// <summary>Flame projectors on the festival stage. They fire only during the cue's hold.</summary>
    public enum FlameMode
    {
        Off,
        /// <summary>Bursts on the drum rhythm: the front row and one boat on the one, the other boat on the three.</summary>
        Beat,
        /// <summary>Every projector at once: a long burst as the scene appears, then a volley on every bar.</summary>
        Salvo,
    }

    /// <summary>
    /// The festival stage's effects for one cue, from the start of its flight in to the end of its hold. Effects
    /// are scenery: they never change the flight plan. Flames arm a safety zone that drones must keep clear of
    /// (see <see cref="FestivalStage"/>), which the safety check enforces.
    /// </summary>
    public sealed class StageEffects
    {
        public LaserMode Lasers = LaserMode.Auto;
        public FountainMode Fountains = FountainMode.Auto;
        public FlameMode Flames = FlameMode.Off;

        /// <summary>Steam rises from the water around the stage, lit in the cue's colours.</summary>
        public bool Steam;

        public StageEffects Clone() => (StageEffects)MemberwiseClone();
    }

    public sealed class Cue
    {
        public string Name = "Cue";
        public FormationSpec Formation = new FormationSpec();
        public LightSpec Light = new LightSpec();
        public MotionSpec Motion = new MotionSpec();

        /// <summary>The festival stage's lasers, fountains, flames and steam for this cue (festival venue only).</summary>
        public StageEffects Effects = new StageEffects();

        /// <summary>When true the compiler picks the shortest transition time that respects the speed and acceleration limits.</summary>
        public bool AutoTransition = true;

        public float TransitionSeconds = 8f;
        public float HoldSeconds = 8f;

        public Cue Clone()
        {
            Cue c = (Cue)MemberwiseClone();
            c.Formation = Formation?.Clone();
            c.Light = Light?.Clone();
            c.Motion = Motion?.Clone();
            c.Effects = Effects?.Clone();
            return c;
        }
    }

    public sealed class SafetyLimits
    {
        /// <summary>Minimum centre-to-centre distance between any two drones, metres.</summary>
        public float MinSeparation = 1.5f;

        public float MaxSpeed = 8f;
        public float MaxAcceleration = 4f;
        public float MaxAltitude = 120f;

        /// <summary>Horizontal radius around the launch pad centre that drones must stay inside.</summary>
        public float GeofenceRadius = 150f;

        /// <summary>Battery budget for the whole flight, seconds.</summary>
        public float MaxFlightSeconds = 900f;

        /// <summary>
        /// Formations are laid out with √2 × MinSeparation between neighbours. With optimal (minimum
        /// sum-of-squares) assignment and synchronised straight-line transitions this is the CAPT
        /// condition that keeps every transition at least MinSeparation apart.
        /// </summary>
        public float FormationSpacing => MinSeparation * 1.4143f;

        public SafetyLimits Clone() => (SafetyLimits)MemberwiseClone();
    }

    public sealed class LaunchPadSpec
    {
        public Vector3 Center = Vector3.Zero;
        public float Spacing = 2.5f;
        public float HoverAltitude = 12f;

        /// <summary>Pad grid columns; 0 picks a square-ish grid automatically.</summary>
        public int Columns;

        public LaunchPadSpec Clone() => (LaunchPadSpec)MemberwiseClone();
    }

    /// <summary>
    /// Where a show is staged. The venue is scenery and effects only; it never changes the flight plan (but flames
    /// on the festival stage add a zone the safety check keeps drones out of).
    /// </summary>
    public enum ShowVenue
    {
        /// <summary>The night lake with the launch barge.</summary>
        Lake,
        /// <summary>
        /// The lake with a festival stage in front of the audience: a bronze drum on a sun-star platform, boat-shaped
        /// wings, fountains, lasers, flames and steam, cued per scene by <see cref="Cue.Effects"/>.
        /// </summary>
        Festival,
    }

    /// <summary>The editable show: everything the compiler needs to produce flight paths and light tracks.</summary>
    public sealed class ShowDocument
    {
        public const int CurrentFormatVersion = 1;

        public string Title = "Untitled Show";
        public string Author = "Mai Drone Star Group";
        public int DroneCount = 200;
        public float PreShowSeconds = 3f;
        public float PostShowSeconds = 3f;
        public ShowVenue Venue = ShowVenue.Lake;
        public SafetyLimits Limits = new SafetyLimits();
        public LaunchPadSpec Pad = new LaunchPadSpec();
        public List<Cue> Cues = new List<Cue>();

        public ShowDocument Clone()
        {
            ShowDocument c = (ShowDocument)MemberwiseClone();
            c.Limits = Limits?.Clone();
            c.Pad = Pad?.Clone();
            c.Cues = new List<Cue>(Cues?.Count ?? 0);
            if (Cues != null)
            {
                foreach (Cue cue in Cues)
                {
                    if (cue != null) c.Cues.Add(cue.Clone());
                }
            }
            return c;
        }
    }
}
