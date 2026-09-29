using System.Collections.Generic;
using DroneStar.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneStar.App
{
    /// <summary>
    /// The festival venue (<see cref="ShowVenue.Festival"/>): a floating stage in front of the audience with a
    /// Đông Sơn bronze drum on a sun-star platform and boat-shaped wings ending in bird-head prows, gilded edges
    /// that reflect in the lake, fountains with an arched water screen and mist, and lasers. Fountains, lasers and
    /// the drum's glow are choreographed from the compiled show (its timeline and cue colours) as pure functions of
    /// show time, so pausing, seeking and replays always agree. Built on first use; everything is procedural.
    /// </summary>
    public sealed class FestivalVenue : MonoBehaviour
    {
        /// <summary>Centre of the stage platform: between the audience shore and the launch barge.</summary>
        public static readonly Vector3 StageCenter = new Vector3(0f, 0f, -205f);

        const float PlatformRadius = 28f;
        const float DeckHeight = 0.6f;
        const float DrumBase = 1.6f;
        const float BoatStart = 27f;
        const float BoatEnd = 108f;

        /// <summary>Seconds per drumbeat; the drum's glow, lasers and front jets pulse on it.</summary>
        public const float Beat = 0.5f;

        static readonly int JetHeightsId = Shader.PropertyToID("_JetHeights");
        static readonly int GroupColorsId = Shader.PropertyToID("_GroupColors");
        static readonly int ShowTimeId = Shader.PropertyToID("_ShowTime");
        static readonly int PulseId = Shader.PropertyToID("_Pulse");
        static readonly int AccentId = Shader.PropertyToID("_Accent");

        [SerializeField] Material bronzeMaterial;
        [SerializeField] Material lightMaterial;
        [SerializeField] Material lightReflectionMaterial;
        [SerializeField] Material fountainMaterial;
        [SerializeField] Material mistMaterial;
        [SerializeField] Material laserMaterial;

        bool built;
        MeshRenderer stageRenderer;
        MeshRenderer fountainRenderer;
        MeshRenderer mistRenderer;
        Mesh laserMesh;
        MaterialPropertyBlock stageProps;
        MaterialPropertyBlock waterProps;

        readonly List<Jet> jets = new List<Jet>();
        readonly List<Emitter> emitters = new List<Emitter>();
        float[] jetHeights = new float[128];
        readonly Vector4[] groupColors = new Vector4[4];
        Vector3[] laserVertices;
        Color[] laserColors;

        struct Jet
        {
            public Vector3 Nozzle;
            public Vector3 Direction;
            public int Group; // 0 ring, 1 arch, 2 front row
            public float Along; // position in its group, 0..1
        }

        struct Emitter
        {
            public Vector3 Origin;
            public int Group; // 0 left prow, 1 right prow, 2 behind the drum
            public float Along; // position in its fan, 0..1
        }

        public bool Visible => gameObject.activeSelf;

        public void Configure(Material bronze, Material lights, Material lightReflections, Material fountains, Material mist, Material lasers)
        {
            bronzeMaterial = bronze;
            lightMaterial = lights;
            lightReflectionMaterial = lightReflections;
            fountainMaterial = fountains;
            mistMaterial = mist;
            laserMaterial = lasers;
        }

        public void SetVisible(bool visible)
        {
            if (visible && !built) Build();
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }

        // ------------------------------------------------------------------ choreography

        /// <summary>Animates fountains, lasers and the drum's glow for show time <paramref name="t"/>.</summary>
        public void Tick(CompiledShow show, float t, Camera cam)
        {
            if (!built || show == null || !gameObject.activeSelf) return;
            Moment m = MomentAt(show, t);
            float pulse = DrumPulse(t);

            stageProps.SetFloat(PulseId, pulse * m.Energy);
            stageProps.SetColor(AccentId, m.A.ToRender() * m.Energy * 0.6f);
            stageRenderer.SetPropertyBlock(stageProps);

            for (int k = 0; k < jets.Count; k++) jetHeights[k] = JetHeight(jets[k], m, t, pulse);
            groupColors[0] = WaterTint(m.A);
            groupColors[1] = WaterTint(m.B);
            groupColors[2] = new Color(1f, 0.62f, 0.22f).linear;
            groupColors[3] = new Color(0.36f, 0.52f, 0.95f).linear * (0.55f + 0.45f * m.Energy);
            waterProps.SetFloatArray(JetHeightsId, jetHeights);
            waterProps.SetVectorArray(GroupColorsId, groupColors);
            waterProps.SetFloat(ShowTimeId, t);
            fountainRenderer.SetPropertyBlock(waterProps);
            mistRenderer.SetPropertyBlock(waterProps);

            UpdateLasers(m, t, pulse, cam);
        }

        /// <summary>Where the show is at time t, as the effects see it.</summary>
        struct Moment
        {
            public SegmentKind Kind;
            public float Local;      // seconds into the segment
            public float Progress;   // 0..1 through the segment
            public float Edge;       // 0 near a segment boundary (lasers blank there), 1 otherwise
            public float Energy;     // overall liveliness 0..1
            public LedColor A, B;
            public bool PreShow;
        }

        static Moment MomentAt(CompiledShow show, float t)
        {
            var m = new Moment { A = new LedColor(1f, 0.7f, 0.25f), B = new LedColor(0.3f, 0.45f, 1f) };
            ShowSegment seg = show.SegmentAt(t);
            if (seg == null) return m;
            m.Kind = seg.Kind;
            m.Local = Mathf.Max(0f, t - seg.Start);
            float duration = Mathf.Max(seg.End - seg.Start, 1e-3f);
            m.Progress = Mathf.Clamp01(m.Local / duration);
            m.Edge = Mathf.Clamp01(Mathf.Min(m.Local, seg.End - t) / 0.4f);
            m.PreShow = seg.Kind == SegmentKind.Ground && show.SegmentIndexAt(t) == 0;
            int cue = seg.CueIndex >= 0 ? seg.CueIndex : seg.FromCueIndex;
            if (cue >= 0 && cue < show.Document.Cues.Count)
            {
                LightSpec light = show.Document.Cues[cue].Light;
                m.A = light.ColorA;
                m.B = light.ColorB;
            }
            switch (seg.Kind)
            {
                case SegmentKind.Hold: m.Energy = 1f; break;
                case SegmentKind.Transit: m.Energy = 0.7f; break;
                case SegmentKind.Takeoff: m.Energy = 0.8f; break;
                case SegmentKind.Return: m.Energy = 0.55f * (1f - m.Progress) + 0.2f; break;
                case SegmentKind.Landing: m.Energy = 0.3f * (1f - m.Progress); break;
                default: m.Energy = m.PreShow ? 0.35f : 0f; break;
            }
            return m;
        }

        /// <summary>A bronze-drum rhythm: a strong beat on one and a lighter one on the "and" of three.</summary>
        static float DrumPulse(float t)
        {
            float bar = Beat * 4f;
            return Mathf.Exp(-Mathf.Repeat(t, bar) * 7f) + 0.6f * Mathf.Exp(-Mathf.Repeat(t - Beat * 3f, bar) * 7f);
        }

        static Color WaterTint(LedColor c)
        {
            Color render = c.ToRender();
            return Color.Lerp(render, Color.white * 0.8f, 0.25f);
        }

        static float JetHeight(Jet jet, Moment m, float t, float pulse)
        {
            float wave = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * (0.22f * t - jet.Along * 2f));
            // Every new scene opens with a burst from the ring that settles over a couple of seconds.
            float burst = m.Kind == SegmentKind.Hold ? Mathf.Exp(-m.Local * 1.4f) : 0f;
            switch (jet.Group)
            {
                case 0:
                    if (m.Kind == SegmentKind.Hold) return 7f + 7f * wave + 12f * burst;
                    if (m.Kind == SegmentKind.Transit) return 4f + 6f * wave;
                    if (m.Kind == SegmentKind.Takeoff) return 10f + 5f * wave;
                    return (3f + 3f * wave) * m.Energy;
                case 1:
                {
                    // The arched screen: jet heights trace a semicircle behind the drum during each scene.
                    float x = jet.Along * 2f - 1f;
                    float arch = 26f * Mathf.Sqrt(Mathf.Max(0f, 1f - x * x));
                    if (m.Kind == SegmentKind.Hold)
                    {
                        float rise = Mathf.SmoothStep(0f, 1f, m.Local / 2.5f);
                        return Mathf.Max(0.5f, arch * rise * Mathf.Min(1f, m.Edge * 2f));
                    }
                    if (m.Kind == SegmentKind.Transit) return 3f + 5f * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * (0.35f * t + x)));
                    return 1.5f * m.Energy;
                }
                default:
                {
                    // Front row: a chase on the drumbeat.
                    float chase = Mathf.Exp(-Mathf.Repeat(t * 2f - jet.Along * 3f, 3f) * 4f);
                    return (2.5f + 8f * chase * m.Energy + 3f * pulse * m.Energy) * (m.Energy > 0f ? 1f : 0.4f);
                }
            }
        }

        void UpdateLasers(Moment m, float t, float pulse, Camera cam)
        {
            if (cam == null) return;
            Vector3 eye = cam.transform.position;
            float pixel = 2f * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * 0.5f) / Mathf.Max(1f, cam.pixelHeight) * 1.4f;
            const float length = 950f, near = 0.5f, far = 8f;
            // Deep, saturated beams (blue fans at the prows, green behind the drum), leaning a little toward the
            // scene's colours; too much of a warm cue colour would wash them out to white.
            Color left = Color.Lerp(new Color(0.08f, 0.2f, 1f), m.B.ToRender(), 0.18f);
            Color centre = Color.Lerp(new Color(0.06f, 1f, 0.3f), m.A.ToRender(), 0.15f);
            for (int e = 0; e < emitters.Count; e++)
            {
                Emitter em = emitters[e];
                BeamPose(em, m, t, out Vector3 dir, out float power);
                power *= m.Edge * (0.85f + 0.3f * pulse);
                Vector3 o = em.Origin, end = o + dir * length;
                Vector3 side = Vector3.Cross(dir, eye - o);
                if (side.sqrMagnitude < 1e-8f) side = Vector3.right;
                side.Normalize();
                float w0 = Mathf.Max(near, Vector3.Distance(o, eye) * pixel), w1 = Mathf.Max(far, Vector3.Distance(end, eye) * pixel);
                int k = e * 4;
                laserVertices[k] = o - side * w0;
                laserVertices[k + 1] = o + side * w0;
                laserVertices[k + 2] = end + side * w1;
                laserVertices[k + 3] = end - side * w1;
                Color c = (em.Group == 2 ? centre : left) * power;
                // Wider-than-true ribbons (the pixel floor) are dimmed to match, so distant beams stay thin lines.
                laserColors[k] = laserColors[k + 1] = c * (near / w0);
                laserColors[k + 2] = laserColors[k + 3] = c * (far / w1);
            }
            laserMesh.SetVertices(laserVertices);
            laserMesh.SetColors(laserColors);
        }

        /// <summary>
        /// Beam direction and power for an emitter at a moment of the show. The prow fans open outward, away from
        /// the stage, and the green fan behind the drum leans well back, so the beams frame the formation rather
        /// than cross in front of it.
        /// </summary>
        static void BeamPose(Emitter em, Moment m, float t, out Vector3 dir, out float power)
        {
            float outward = em.Group == 0 ? -1f : em.Group == 1 ? 1f : 0f;
            bool centre = em.Group == 2;
            float tilt, lean;
            switch (m.Kind)
            {
                case SegmentKind.Takeoff:
                    // A tunnel of light that opens as the fleet climbs.
                    tilt = centre ? (em.Along - 0.5f) * (0.15f + 0.7f * m.Progress) : outward * (0.1f + 0.5f * em.Along) * (0.3f + 0.7f * m.Progress);
                    lean = centre ? 0.8f : 0.25f;
                    power = 0.9f;
                    break;
                case SegmentKind.Hold:
                    tilt = centre
                        ? (em.Along - 0.5f) * (1.0f + 0.2f * Mathf.Sin(t * 0.7f))
                        : outward * (0.15f + 0.85f * em.Along + 0.12f * Mathf.Sin(t * 0.9f + em.Along * 3f));
                    lean = centre ? 1.15f + 0.1f * Mathf.Sin(t * 0.5f + em.Along) : 0.35f;
                    power = 1f;
                    break;
                case SegmentKind.Transit:
                    tilt = centre
                        ? (em.Along - 0.5f) * 0.8f + 0.3f * Mathf.Sin(t * 1.3f + em.Along * 2f)
                        : outward * (0.2f + 0.7f * (0.5f + 0.5f * Mathf.Sin(t * 1.1f + em.Along * 4f)));
                    lean = centre ? 1.0f : 0.3f;
                    power = 0.65f;
                    break;
                case SegmentKind.Return:
                case SegmentKind.Landing:
                    tilt = (centre ? em.Along - 0.5f : outward * 0.3f) * (1f - m.Progress);
                    lean = centre ? 0.9f : 0.25f;
                    power = m.Energy;
                    break;
                default:
                    // Before the show: slow searchlights; after it, dark.
                    tilt = (centre ? em.Along - 0.5f : outward * 0.4f) * 0.8f + 0.3f * Mathf.Sin(t * 0.4f + em.Along * 5f);
                    lean = centre ? 0.9f : 0.3f;
                    power = m.PreShow ? 0.35f : 0f;
                    break;
            }
            dir = new Vector3(Mathf.Sin(tilt), Mathf.Cos(tilt), lean).normalized;
        }

        // ------------------------------------------------------------------ construction

        void Build()
        {
            built = true;
            stageProps = new MaterialPropertyBlock();
            waterProps = new MaterialPropertyBlock();
            stageRenderer = Spawn("Festival Stage", BuildStage(), bronzeMaterial);
            List<Vector3> lights = StageLights(out List<float> sizes);
            var colors = new List<Color>(lights.Count);
            var rng = new System.Random(8);
            for (int i = 0; i < lights.Count; i++) colors.Add(Color.Lerp(new Color(1f, 0.6f, 0.2f), new Color(1f, 0.82f, 0.5f), (float)rng.NextDouble()).linear);
            Mesh lightMesh = GlowSprites.FromLights("Stage Lights", lights, colors, sizes).Mesh;
            Spawn("Stage Lights", lightMesh, lightMaterial);
            Spawn("Stage Light Reflections", lightMesh, lightReflectionMaterial);
            fountainRenderer = Spawn("Fountains", BuildFountains(), fountainMaterial);
            mistRenderer = Spawn("Mist", BuildMist(), mistMaterial);
            laserMesh = BuildLasers();
            Spawn("Lasers", laserMesh, laserMaterial);
        }

        MeshRenderer Spawn(string objectName, Mesh mesh, Material material)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.allowOcclusionWhenDynamic = false;
            return r;
        }

        static readonly Color Bronze = new Color(0.46f, 0.28f, 0.11f);
        static readonly Color DarkBronze = new Color(0.22f, 0.13f, 0.06f);

        static Mesh BuildStage()
        {
            var m = new SetPieceMesh();
            Vector3 c = StageCenter;
            float water = NightEnvironment.WaterLevel;

            // The platform: a bronze drum-face disc floating on the lake, with a glowing rim.
            m.Cylinder(c + Vector3.up * (water - 0.6f), PlatformRadius, DeckHeight - water + 0.6f, 72, DarkBronze, SetPieceMesh.Plain, false);
            m.Disc(c + Vector3.up * DeckHeight, PlatformRadius, 96, Bronze, SetPieceMesh.Medallion);
            m.Cylinder(c + Vector3.up * (DeckHeight - 0.18f), PlatformRadius + 0.05f, 0.18f, 96, Color.white, SetPieceMesh.Trim, false);

            // Pedestal and the bronze drum, revolved from the Đông Sơn profile (flared foot, waist, swelling
            // shoulder), its face the same sun and rings as the platform.
            m.Cylinder(c + Vector3.up * DeckHeight, 12.5f, DrumBase - DeckHeight, 64, Bronze, SetPieceMesh.Plain, true);
            m.Cylinder(c + Vector3.up * (DrumBase - 0.14f), 12.55f, 0.14f, 72, Color.white, SetPieceMesh.Trim, false);
            var profile = new[]
            {
                new Vector2(10.6f, 0f), new Vector2(10.9f, 0.5f), new Vector2(10.0f, 1.4f), new Vector2(8.6f, 3.2f),
                new Vector2(8.1f, 5.0f), new Vector2(8.1f, 6.8f), new Vector2(8.6f, 8.2f), new Vector2(10.3f, 10.2f),
                new Vector2(10.8f, 11.8f), new Vector2(10.5f, 13.0f), new Vector2(10.2f, 13.6f),
            };
            m.Revolve(c + Vector3.up * DrumBase, profile, 72, Bronze, SetPieceMesh.Bands);
            m.Disc(c + Vector3.up * (DrumBase + 13.6f), 10.2f, 72, Bronze, SetPieceMesh.Medallion);

            // Boat wings with bird-head prows, left and right.
            for (int side = -1; side <= 1; side += 2) BuildBoat(m, side);
            return m.ToMesh("Festival Stage");
        }

        static float BoatDeck(float s) => 1.0f + 5.2f * s * s * s;

        static float BoatHalfWidth(float s) => 6.4f * (1f - 0.82f * s * s);

        static Vector3 BoatAt(float s, int side) => StageCenter + new Vector3(side * Mathf.Lerp(BoatStart, BoatEnd, s), 0f, 0f);

        static void BuildBoat(SetPieceMesh m, int side)
        {
            const int sections = 40;
            float water = NightEnvironment.WaterLevel - 0.4f;
            for (int i = 0; i < sections; i++)
            {
                float s0 = (float)i / sections, s1 = (float)(i + 1) / sections;
                Vector3 a = BoatAt(s0, side), b = BoatAt(s1, side);
                float y0 = BoatDeck(s0), y1 = BoatDeck(s1), w0 = BoatHalfWidth(s0), w1 = BoatHalfWidth(s1);
                var fl0 = a + new Vector3(0f, y0, -w0); var fr0 = a + new Vector3(0f, y0, w0);
                var fl1 = b + new Vector3(0f, y1, -w1); var fr1 = b + new Vector3(0f, y1, w1);
                var bl0 = new Vector3(fl0.x, water, a.z - w0 * 0.8f); var br0 = new Vector3(fr0.x, water, a.z + w0 * 0.8f);
                var bl1 = new Vector3(fl1.x, water, b.z - w1 * 0.8f); var br1 = new Vector3(fr1.x, water, b.z + w1 * 0.8f);
                m.Quad(fl0, fr0, fr1, fl1, Vector3.up, Bronze, SetPieceMesh.Plain);
                // Hull sides: a gilded band below the gunwale over dark bronze.
                m.Quad(bl0, fl0, fl1, bl1, Vector3.back, DarkBronze, SetPieceMesh.Hull);
                m.Quad(br0, fr0, fr1, br1, Vector3.forward, DarkBronze, SetPieceMesh.Hull);
                m.Strip(fl0, fl1, Vector3.back, 0.35f, SetPieceMesh.Trim);
                m.Strip(fr0, fr1, Vector3.forward, 0.35f, SetPieceMesh.Trim);
            }
            // Transom at the stage end and the bird-head prow at the far end.
            Vector3 root = BoatAt(0f, side);
            float wr = BoatHalfWidth(0f), yr = BoatDeck(0f);
            m.Quad(root + new Vector3(0f, water, -wr * 0.8f), root + new Vector3(0f, yr, -wr), root + new Vector3(0f, yr, wr),
                root + new Vector3(0f, water, wr * 0.8f), Vector3.left * side, DarkBronze, SetPieceMesh.Hull);
            Vector3 tip = BoatAt(1f, side) + Vector3.up * BoatDeck(1f);
            Vector3 neck = tip + new Vector3(side * 3.5f, 6.5f, 0f), head = neck + new Vector3(side * 1.2f, 2.2f, 0f);
            m.Tube(tip, neck, 0.9f, 0.65f, 10, Bronze);
            m.Tube(neck, head, 0.65f, 0.55f, 10, Bronze);
            m.Tube(head, head + new Vector3(side * 3.8f, -0.6f, 0f), 0.42f, 0.05f, 8, Color.white, SetPieceMesh.Trim);
            m.Tube(head, head + new Vector3(-side * 2.2f, 1.6f, 0f), 0.3f, 0.05f, 8, Bronze); // crest
        }

        static List<Vector3> StageLights(out List<float> sizes)
        {
            var lights = new List<Vector3>();
            var scale = new List<float>();
            sizes = scale;
            Vector3 c = StageCenter;
            void Ring(float radius, float y, float spacing, float size)
            {
                int n = Mathf.CeilToInt(2f * Mathf.PI * radius / spacing);
                for (int i = 0; i < n; i++)
                {
                    float a = i * 2f * Mathf.PI / n;
                    lights.Add(c + new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius));
                    scale.Add(size);
                }
            }
            Ring(PlatformRadius + 0.3f, DeckHeight + 0.1f, 2.2f, 0.9f);
            Ring(10.5f, DrumBase + 13.8f, 2f, 1.1f);
            Ring(12.8f, DrumBase + 0.3f, 8f, 2.6f);
            for (int side = -1; side <= 1; side += 2)
            {
                for (float s = 0.02f; s < 1f; s += 0.035f)
                {
                    Vector3 p = BoatAt(s, side) + Vector3.up * (BoatDeck(s) + 0.3f);
                    float w = BoatHalfWidth(s);
                    lights.Add(p + Vector3.back * w);
                    lights.Add(p + Vector3.forward * w);
                    sizes.Add(0.8f);
                    sizes.Add(0.8f);
                }
            }
            return lights;
        }

        Mesh BuildFountains()
        {
            jets.Clear();
            Vector3 c = StageCenter;
            float water = NightEnvironment.WaterLevel + 0.1f;
            const int ring = 28, arch = 30, front = 22;
            for (int i = 0; i < ring; i++)
            {
                float a = i * 2f * Mathf.PI / ring;
                var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                jets.Add(new Jet { Nozzle = c + outward * (PlatformRadius + 2.5f) + Vector3.up * water, Direction = (Vector3.up + outward * 0.12f).normalized, Group = 0, Along = (float)i / ring });
            }
            for (int i = 0; i < arch; i++)
            {
                float f = (float)i / (arch - 1);
                jets.Add(new Jet { Nozzle = c + new Vector3(Mathf.Lerp(-25f, 25f, f), DeckHeight + 0.1f, 18f), Direction = new Vector3(0f, 1f, 0.04f).normalized, Group = 1, Along = f });
            }
            for (int i = 0; i < front; i++)
            {
                float f = (float)i / (front - 1);
                jets.Add(new Jet { Nozzle = c + new Vector3(Mathf.Lerp(-100f, 100f, f), water, -33f), Direction = Vector3.up, Group = 2, Along = f });
            }

            var positions = new List<Vector3>();
            var uv0 = new List<Vector4>();
            var uv1 = new List<Vector4>();
            var colors = new List<Color>();
            var indices = new List<int>();
            var rng = new System.Random(41);
            for (int j = 0; j < jets.Count; j++)
            {
                Jet jet = jets[j];
                int drops = jet.Group == 1 ? 200 : jet.Group == 0 ? 130 : 90;
                for (int d = 0; d < drops; d++)
                {
                    float phase = (d + (float)rng.NextDouble() * 0.6f) / drops;
                    float rnd = (float)rng.NextDouble();
                    AddQuad(positions, uv0, uv1, colors, indices, jet.Nozzle, phase, j, jet.Direction, rnd, jet.Group);
                }
            }
            return QuadMesh("Fountain Droplets", positions, uv0, uv1, colors, indices);
        }

        static Mesh BuildMist()
        {
            var positions = new List<Vector3>();
            var uv0 = new List<Vector4>();
            var uv1 = new List<Vector4>();
            var colors = new List<Color>();
            var indices = new List<int>();
            var rng = new System.Random(77);
            Vector3 c = StageCenter;
            for (int i = 0; i < 90; i++)
            {
                Vector3 p;
                if (i < 50)
                {
                    float a = (float)(rng.NextDouble() * 2 * Mathf.PI), r = PlatformRadius + 2f + (float)rng.NextDouble() * 14f;
                    p = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                }
                else
                {
                    float x = Mathf.Lerp(BoatStart, BoatEnd, (float)rng.NextDouble()) * (rng.Next(2) == 0 ? -1f : 1f);
                    p = c + new Vector3(x, 0f, ((float)rng.NextDouble() - 0.5f) * 26f);
                }
                p.y = NightEnvironment.WaterLevel + 0.5f;
                AddQuad(positions, uv0, uv1, colors, indices, p, (float)rng.NextDouble(), 0, Vector3.up, (float)rng.NextDouble(), 3);
            }
            return QuadMesh("Mist", positions, uv0, uv1, colors, indices);
        }

        static void AddQuad(List<Vector3> positions, List<Vector4> uv0, List<Vector4> uv1, List<Color> colors, List<int> indices,
            Vector3 origin, float phase, int jet, Vector3 dir, float rnd, int group)
        {
            int start = positions.Count;
            for (int k = 0; k < 4; k++)
            {
                float cx = k == 0 || k == 3 ? -1f : 1f, cy = k < 2 ? -1f : 1f;
                positions.Add(origin);
                uv0.Add(new Vector4(cx, cy, phase, jet));
                uv1.Add(new Vector4(dir.x, dir.y, dir.z, rnd));
                colors.Add(new Color(0f, 0f, 0f, group / 4f));
            }
            indices.Add(start);
            indices.Add(start + 1);
            indices.Add(start + 2);
            indices.Add(start);
            indices.Add(start + 2);
            indices.Add(start + 3);
        }

        static Mesh QuadMesh(string meshName, List<Vector3> positions, List<Vector4> uv0, List<Vector4> uv1, List<Color> colors, List<int> indices)
        {
            var mesh = new Mesh { name = meshName };
            mesh.indexFormat = positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0, false);
            // Droplets are displaced in the vertex shader; keep them from being culled.
            mesh.bounds = new Bounds(StageCenter, Vector3.one * 600f);
            return mesh;
        }

        Mesh BuildLasers()
        {
            emitters.Clear();
            Vector3 c = StageCenter;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 prow = BoatAt(1f, side) + Vector3.up * (BoatDeck(1f) + 1.5f);
                for (int k = 0; k < 6; k++) emitters.Add(new Emitter { Origin = prow + Vector3.forward * (k - 2.5f) * 0.8f, Group = side < 0 ? 0 : 1, Along = k / 5f });
            }
            for (int k = 0; k < 10; k++) emitters.Add(new Emitter { Origin = c + new Vector3(Mathf.Lerp(-9f, 9f, k / 9f), DrumBase + 0.8f, 16f), Group = 2, Along = k / 9f });

            int n = emitters.Count;
            laserVertices = new Vector3[n * 4];
            laserColors = new Color[n * 4];
            var uv = new Vector2[n * 4];
            var indices = new int[n * 6];
            for (int e = 0; e < n; e++)
            {
                uv[e * 4] = new Vector2(0f, -1f);
                uv[e * 4 + 1] = new Vector2(0f, 1f);
                uv[e * 4 + 2] = new Vector2(1f, 1f);
                uv[e * 4 + 3] = new Vector2(1f, -1f);
                int k = e * 6, v = e * 4;
                indices[k] = v;
                indices[k + 1] = v + 1;
                indices[k + 2] = v + 2;
                indices[k + 3] = v;
                indices[k + 4] = v + 2;
                indices[k + 5] = v + 3;
            }
            var mesh = new Mesh { name = "Laser Beams" };
            mesh.MarkDynamic();
            mesh.vertices = laserVertices;
            mesh.colors = laserColors;
            mesh.uv = uv;
            mesh.triangles = indices;
            mesh.bounds = new Bounds(StageCenter, Vector3.one * 4000f);
            return mesh;
        }

        void OnDestroy()
        {
            foreach (MeshFilter f in GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh != null) Destroy(f.sharedMesh);
            }
        }
    }

    /// <summary>
    /// Mesh builder for the festival set: faces oriented by an outward hint, with a bronze tint and a pattern part
    /// (see FestivalBronze.shader) per vertex, and pattern coordinates in UV0.
    /// </summary>
    sealed class SetPieceMesh
    {
        public const float Plain = 0f, Medallion = 0.25f, Bands = 0.5f, Trim = 0.75f, Hull = 1f;

        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> triangles = new List<int>();

        void Add(Vector3 p, Vector3 n, Color tint, float part, Vector2 uv)
        {
            vertices.Add(p);
            normals.Add(n);
            colors.Add(new Color(tint.r, tint.g, tint.b, part));
            uvs.Add(uv);
        }

        /// <summary>A quad a–b–c–d, flipped if needed so its front faces <paramref name="outward"/>.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Color tint, float part,
            Vector2 ua = default, Vector2 ub = default, Vector2 uc = default, Vector2 ud = default)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) n = Vector3.Cross(c - a, d - a);
            bool flip = Vector3.Dot(n, outward) < 0f;
            n = (flip ? -n : n).normalized;
            int s = vertices.Count;
            Add(a, n, tint, part, ua);
            Add(b, n, tint, part, ub);
            Add(c, n, tint, part, uc);
            Add(d, n, tint, part, ud);
            if (flip)
            {
                triangles.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
            }
            else
            {
                triangles.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
        }

        /// <summary>A thin glowing strip hanging below the edge a–b.</summary>
        public void Strip(Vector3 a, Vector3 b, Vector3 outward, float height, float part)
        {
            Vector3 down = Vector3.down * height;
            Quad(a + down, a, b, b + down, outward, Color.white, part);
        }

        public void Disc(Vector3 center, float radius, int sides, Color tint, float part)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * 2f * Mathf.PI / sides, a1 = (i + 1) * 2f * Mathf.PI / sides;
                var p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                var p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                // The pattern is laid out as seen from the audience side (−z), which reads upright.
                Vector2 u0 = new Vector2(p0.x, p0.z), u1 = new Vector2(p1.x, p1.z);
                Quad(center, center + p0 * radius, center + p1 * radius, center, Vector3.up, tint, part, Vector2.zero, u0, u1, Vector2.zero);
            }
        }

        public void Cylinder(Vector3 baseCenter, float radius, float height, int sides, Color tint, float part, bool top)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * 2f * Mathf.PI / sides, a1 = (i + 1) * 2f * Mathf.PI / sides;
                var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 b0 = baseCenter + d0 * radius, b1 = baseCenter + d1 * radius;
                Quad(b0, b1, b1 + Vector3.up * height, b0 + Vector3.up * height, d0 + d1, tint, part,
                    new Vector2((float)i / sides, 0f), new Vector2((float)(i + 1) / sides, 0f), new Vector2((float)(i + 1) / sides, 1f), new Vector2((float)i / sides, 1f));
            }
            if (top) Disc(baseCenter + Vector3.up * height, radius, sides, tint, Plain);
        }

        /// <summary>Surface of revolution: profile points are (radius, height) from the base upward.</summary>
        public void Revolve(Vector3 baseCenter, Vector2[] profile, int sides, Color tint, float part)
        {
            float top = profile[profile.Length - 1].y;
            for (int j = 0; j + 1 < profile.Length; j++)
            {
                Vector2 p0 = profile[j], p1 = profile[j + 1];
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * 2f * Mathf.PI / sides, a1 = (i + 1) * 2f * Mathf.PI / sides;
                    var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                    var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    Vector3 v00 = baseCenter + d0 * p0.x + Vector3.up * p0.y, v10 = baseCenter + d1 * p0.x + Vector3.up * p0.y;
                    Vector3 v01 = baseCenter + d0 * p1.x + Vector3.up * p1.y, v11 = baseCenter + d1 * p1.x + Vector3.up * p1.y;
                    Quad(v00, v10, v11, v01, d0 + d1, tint, part,
                        new Vector2((float)i / sides, p0.y / top), new Vector2((float)(i + 1) / sides, p0.y / top),
                        new Vector2((float)(i + 1) / sides, p1.y / top), new Vector2((float)i / sides, p1.y / top));
                }
            }
        }

        /// <summary>Tapered tube from a to b (radius r0 to r1).</summary>
        public void Tube(Vector3 a, Vector3 b, float r0, float r1, int sides, Color tint, float part = Plain)
        {
            Vector3 axis = (b - a).normalized;
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(axis, u);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * 2f * Mathf.PI / sides, a1 = (i + 1) * 2f * Mathf.PI / sides;
                Vector3 d0 = u * Mathf.Cos(a0) + v * Mathf.Sin(a0), d1 = u * Mathf.Cos(a1) + v * Mathf.Sin(a1);
                Quad(a + d0 * r0, a + d1 * r0, b + d1 * r1, b + d0 * r1, d0 + d1, tint, part);
            }
        }

        public Mesh ToMesh(string meshName)
        {
            var mesh = new Mesh { name = meshName };
            mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            return mesh;
        }
    }
}
