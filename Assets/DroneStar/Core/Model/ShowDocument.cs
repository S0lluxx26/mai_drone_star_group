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
    }

    /// <summary>Hard numeric bounds for every editable field. The sanitizer clamps loaded and edited values to these.</summary>
    public static class ShowBounds
    {
        public const int MinDrones = 1;
        public const int MaxDrones = 1000;
        public const int MaxCues = 64;
        public const float MinSize = 4f;
        public const float MaxSize = 240f;
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
        public const float MaxCoordinate = 500f;
        public const float MaxSpin = 90f;
        public const float MaxFrequency = 2f;
        public const float MaxEffectSpeed = 5f;
        public const float MaxBrightness = 1f;
        public const float MaxMotionAmount = 12f;
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

    public sealed class Cue
    {
        public string Name = "Cue";
        public FormationSpec Formation = new FormationSpec();
        public LightSpec Light = new LightSpec();
        public MotionSpec Motion = new MotionSpec();

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

    /// <summary>The editable show: everything the compiler needs to produce flight paths and light tracks.</summary>
    public sealed class ShowDocument
    {
        public const int CurrentFormatVersion = 1;

        public string Title = "Untitled Show";
        public string Author = "Mai Drone Star Group";
        public int DroneCount = 200;
        public float PreShowSeconds = 3f;
        public float PostShowSeconds = 3f;
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
