using System.Collections.Generic;
using DroneStar.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneStar.App
{
    /// <summary>
    /// The audience: a crowd on the shore lawn and two grandstands either side of it, facing the lake. Each person is
    /// a silhouette the GPU draws from a distance field (Crowd.shader), small and dark against the lawn with a rim
    /// lit by the show, and some of them film with their phones. The crowd follows the show as a pure function of
    /// show time: phones come up while a scene holds, arms go up as each scene appears, the finale draws the biggest
    /// cheer and the landing gets applause. Present at both venues, built once from a fixed seed (half as many
    /// people on phones).
    /// </summary>
    public sealed class AudienceCrowd : MonoBehaviour
    {
        /// <summary>Grandstands run from this far either side of the centre line ...</summary>
        public const float StandInner = 55f;

        /// <summary>... to this far.</summary>
        public const float StandOuter = 135f;

        /// <summary>Front edge of the grandstands, a few metres back from the water.</summary>
        public const float StandFront = NightEnvironment.ShoreZ - 8f;

        const int StandTiers = 8;
        const float TierDepth = 4f;
        const float TierRise = 0.75f;

        /// <summary>The lawn crowd reaches back this far.</summary>
        public const float CrowdBack = NightEnvironment.ShoreZ - 86f;

        const float SpotX = -7f;
        const float SpotZ = NightEnvironment.ShoreZ - 46f;

        static readonly int ShowTimeId = Shader.PropertyToID("_ShowTime");
        static readonly int EnergyId = Shader.PropertyToID("_Energy");
        static readonly int CheerId = Shader.PropertyToID("_Cheer");
        static readonly int PhoneShareId = Shader.PropertyToID("_PhoneShare");
        static readonly int ShowTintId = Shader.PropertyToID("_ShowTint");

        static readonly Color[] Clothes =
        {
            new Color(0.55f, 0.12f, 0.1f), new Color(0.12f, 0.2f, 0.45f), new Color(0.8f, 0.78f, 0.7f),
            new Color(0.85f, 0.65f, 0.15f), new Color(0.12f, 0.12f, 0.13f), new Color(0.25f, 0.4f, 0.25f),
        };

        [SerializeField] Material bodyMaterial;
        [SerializeField] Material phoneMaterial;
        [SerializeField] Material standMaterial;
        [SerializeField] Material standLightMaterial;

        bool built;
        MeshRenderer crowdRenderer;
        MaterialPropertyBlock props;

        struct Person
        {
            public Vector3 Feet;
            public float Height;
            public float Random;
            public float Phone;
            public float Cheer;
            public Color Clothes;
        }

        /// <summary>People in the crowd (after the device's density).</summary>
        public int People { get; private set; }

        /// <summary>
        /// Where the demo's over-the-crowd shot stands: a gap among the audience on the lawn, at the eye height of
        /// someone on a step, so heads and phones fill the bottom of the frame.
        /// </summary>
        public static Vector3 CameraSpot => new Vector3(SpotX, NightEnvironment.ShoreHeight(SpotX, SpotZ) + 2.3f, SpotZ);

        /// <summary>True on the lawn and grandstands the audience fills (kept clear of trees).</summary>
        public static bool IsAudienceArea(float x, float z) =>
            Mathf.Abs(x) < StandOuter + 45f && z > CrowdBack - 10f && z < NightEnvironment.ShoreZ + 2f;

        public void Configure(Material body, Material phones, Material stands, Material standLights)
        {
            bodyMaterial = body;
            phoneMaterial = phones;
            standMaterial = stands;
            standLightMaterial = standLights;
        }

        void Awake() => Build(Application.isMobilePlatform ? 0.5f : 1f);

        // ------------------------------------------------------------------ behaviour

        /// <summary>
        /// Sets the crowd's behaviour for show time <paramref name="t"/>. <paramref name="stageLight"/> is extra light
        /// the stage throws on the audience (the flames), added to the show's colours.
        /// </summary>
        public void Tick(CompiledShow show, float t, Color stageLight)
        {
            if (!built) return;
            Mood(show, t, out float energy, out float cheer, out float phones, out Color tint);
            props.SetFloat(ShowTimeId, t);
            props.SetFloat(EnergyId, energy);
            props.SetFloat(CheerId, cheer);
            props.SetFloat(PhoneShareId, phones);
            props.SetColor(ShowTintId, tint + stageLight);
            crowdRenderer.SetPropertyBlock(props);
        }

        /// <summary>How lively the audience is at time t, how many cheer and film, and the light the show casts on them.</summary>
        public static void Mood(CompiledShow show, float t, out float energy, out float cheer, out float phones, out Color tint)
        {
            energy = 0f;
            cheer = 0f;
            phones = 0.035f;
            tint = new Color(0.1f, 0.13f, 0.24f); // moonlight before the show
            if (show == null) return;
            ShowSegment seg = show.SegmentAt(t);
            if (seg == null) return;
            int last = show.CueTimings.Count - 1;
            for (int i = 0; i <= last; i++)
            {
                // A cheer as each scene appears: the first and the finale the loudest.
                float since = t - show.CueTimings[i].HoldStart;
                if (since < 0f || since > 15f) continue;
                float peak = i == last ? 1f : i == 0 ? 0.7f : 0.5f;
                cheer = Mathf.Max(cheer, peak * Mathf.Exp(-since / (i == last ? 4f : 2.2f)));
            }
            bool afterLanding = seg.Kind == SegmentKind.Ground && show.SegmentIndexAt(t) > 0;
            float progress = seg.Duration > 1e-3f ? Mathf.Clamp01((t - seg.Start) / seg.Duration) : 1f;
            if (seg.Kind == SegmentKind.Landing) cheer = Mathf.Max(cheer, 0.6f * progress);
            if (afterLanding) cheer = Mathf.Max(cheer, 0.8f * Mathf.Exp(-(t - seg.Start) / 4f));

            switch (seg.Kind)
            {
                case SegmentKind.Hold:
                {
                    energy = 1f;
                    // Phones come up a moment after a scene appears; everyone films the finale.
                    float since = t - seg.Start;
                    phones = (seg.CueIndex == last ? 0.5f : 0.22f) * Mathf.Clamp01(since / 3f) + 0.12f;
                    break;
                }
                case SegmentKind.Transit:
                    energy = 0.7f;
                    phones = 0.14f;
                    break;
                case SegmentKind.Takeoff:
                    energy = 0.6f;
                    phones = 0.18f;
                    break;
                case SegmentKind.Return:
                case SegmentKind.Landing:
                    energy = 0.4f;
                    phones = 0.1f;
                    break;
                default:
                    energy = afterLanding ? 0.5f : 0.15f;
                    break;
            }

            int cue = seg.CueIndex >= 0 ? seg.CueIndex : seg.FromCueIndex;
            if (cue >= 0 && cue < show.Document.Cues.Count && seg.Kind != SegmentKind.Ground)
            {
                LightSpec light = show.Document.Cues[cue].Light;
                Color show1 = light.ColorA.ToRender(), show2 = light.ColorB.ToRender();
                Color glow = Color.Lerp(show1, show2, 0.35f) * (0.25f + 0.35f * light.Brightness * energy);
                tint = Color.Lerp(tint, glow, 0.8f);
            }
        }

        // ------------------------------------------------------------------ construction

        void Build(float density)
        {
            built = true;
            props = new MaterialPropertyBlock();
            var rng = new System.Random(4096);
            var people = new List<Person>();
            PlaceLawn(people, rng, density);
            var kit = new MeshKit();
            var lights = new List<Vector3>();
            PlaceStands(people, rng, density, kit, lights);
            People = people.Count;

            crowdRenderer = Spawn("Audience", BuildCrowdMesh(people), bodyMaterial, phoneMaterial);
            Spawn("Grandstands", kit.ToMesh("Grandstands"), standMaterial);
            var colors = new List<Color>(lights.Count);
            var sizes = new List<float>(lights.Count);
            foreach (Vector3 _ in lights)
            {
                colors.Add(new Color(1f, 0.72f, 0.42f).linear);
                sizes.Add(0.7f);
            }
            Spawn("Grandstand Lights", GlowSprites.FromLights("Grandstand Lights", lights, colors, sizes).Mesh, standLightMaterial);
            Tick(null, 0f, Color.black);
        }

        static bool InClearing(float x, float z)
        {
            // Keep the audience camera (on the shore, swaying a little) and the crowd shot's spot clear.
            float ax = x, az = z - (NightEnvironment.ShoreZ - 14f);
            if (ax * ax + az * az < 5.5f * 5.5f) return true;
            float sx = x - SpotX, sz = z - SpotZ;
            return sx * sx + sz * sz < 4.5f * 4.5f;
        }

        static void PlaceLawn(List<Person> people, System.Random rng, float density)
        {
            float front = NightEnvironment.ShoreZ - 3.5f;
            float standBack = StandFront - StandTiers * TierDepth;
            float z = front;
            while (z > CrowdBack)
            {
                float depth = (front - z) / (front - CrowdBack);
                float spacing = Mathf.Lerp(0.95f, 1.75f, depth);
                // Between the grandstands the lawn is as wide as the gap; behind them it opens out.
                float open = Mathf.SmoothStep(0f, 1f, (standBack - 2f - z) / 14f);
                float half = Mathf.Lerp(StandInner - 2f, StandOuter + 40f, open);
                float occupancy = Mathf.Lerp(0.92f, 0.62f, depth) * density;
                for (float x = -half; x <= half; x += spacing)
                {
                    float jx = x + ((float)rng.NextDouble() - 0.5f) * spacing * 0.7f;
                    float jz = z + ((float)rng.NextDouble() - 0.5f) * spacing * 0.7f;
                    double roll = rng.NextDouble();
                    // Friends and families stand in groups, so the crowd thins in patches.
                    float clump = Mathf.PerlinNoise(jx * 0.08f + 13.1f, jz * 0.08f + 7.7f);
                    if (roll > occupancy * Mathf.Lerp(0.55f, 1.25f, clump) || InClearing(jx, jz)) continue;
                    people.Add(NewPerson(rng, new Vector3(jx, NightEnvironment.ShoreHeight(jx, jz) - 0.03f, jz)));
                }
                z -= spacing * 0.92f;
            }
        }

        static void PlaceStands(List<Person> people, System.Random rng, float density, MeshKit kit, List<Vector3> lights)
        {
            float width = StandOuter - StandInner, depth = StandTiers * TierDepth, back = StandFront - depth;
            for (int side = -1; side <= 1; side += 2)
            {
                float cx = side * 0.5f * (StandInner + StandOuter);
                float ground = NightEnvironment.ShoreHeight(cx, StandFront - 0.5f * depth);
                float floor = ground - 0.6f;
                float highest = ground + 0.8f + (StandTiers - 1) * TierRise;
                for (int k = 0; k < StandTiers; k++)
                {
                    float zf = StandFront - k * TierDepth;
                    float top = ground + 0.8f + k * TierRise;
                    kit.Box(new Vector3(cx, 0.5f * (floor + top), zf - 0.5f * TierDepth), new Vector3(width, top - floor, TierDepth), bottom: false);
                    for (int row = 0; row < 2; row++)
                    {
                        float rz = zf - 1.1f - row * 1.8f;
                        for (float x = StandInner + 0.8f; x < StandOuter - 0.8f; x += 0.72f)
                        {
                            float jx = side * (x + ((float)rng.NextDouble() - 0.5f) * 0.2f);
                            float jz = rz + ((float)rng.NextDouble() - 0.5f) * 0.25f;
                            if (rng.NextDouble() > 0.88 * density) continue;
                            people.Add(NewPerson(rng, new Vector3(jx, top, jz)));
                        }
                    }
                }
                // Back wall with a rail, and the stair walls at each end.
                kit.Box(new Vector3(cx, 0.5f * (floor + highest + 1.2f), back - 0.2f), new Vector3(width, highest + 1.2f - floor, 0.4f));
                foreach (float ex in new[] { StandInner - 0.3f, StandOuter + 0.3f })
                {
                    kit.Box(new Vector3(side * ex, 0.5f * (floor + highest + 1f), StandFront - 0.5f * depth), new Vector3(0.6f, highest + 1f - floor, depth));
                }
                // Warm lights along the front edge and the back rail.
                for (float x = StandInner + 2f; x < StandOuter - 1f; x += 4f)
                {
                    lights.Add(new Vector3(side * x, ground + 0.9f, StandFront + 0.08f));
                    lights.Add(new Vector3(side * x, highest + 1.3f, back - 0.05f));
                }
            }
        }

        static Person NewPerson(System.Random rng, Vector3 feet)
        {
            bool child = rng.NextDouble() < 0.08;
            float height = child ? 1.05f + 0.3f * (float)rng.NextDouble() : 1.52f + 0.33f * (float)rng.NextDouble();
            return new Person
            {
                Feet = feet,
                Height = height,
                Random = (float)rng.NextDouble(),
                Phone = (float)rng.NextDouble(),
                Cheer = (float)rng.NextDouble(),
                Clothes = Clothes[rng.Next(Clothes.Length)] * (0.6f + 0.4f * (float)rng.NextDouble()),
            };
        }

        static Mesh BuildCrowdMesh(List<Person> people)
        {
            int n = people.Count;
            var positions = new Vector3[n * 8];
            var uv0 = new Vector4[n * 8];
            var uv1 = new Vector4[n * 8];
            var colors = new Color[n * 8];
            var bodies = new int[n * 6];
            var phones = new int[n * 6];
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            for (int i = 0; i < n; i++)
            {
                Person p = people[i];
                min = Vector3.Min(min, p.Feet);
                max = Vector3.Max(max, p.Feet);
                for (int q = 0; q < 2; q++)
                {
                    int v = (i * 2 + q) * 4;
                    for (int k = 0; k < 4; k++)
                    {
                        float cx = k == 0 || k == 3 ? -1f : 1f, cy = k < 2 ? 0f : 1f;
                        positions[v + k] = p.Feet;
                        uv0[v + k] = new Vector4(cx, cy, p.Height, p.Random);
                        uv1[v + k] = new Vector4(p.Phone, p.Cheer, q, 0f);
                        colors[v + k] = p.Clothes;
                    }
                    int[] target = q == 0 ? bodies : phones;
                    int t = i * 6;
                    target[t] = v;
                    target[t + 1] = v + 1;
                    target[t + 2] = v + 2;
                    target[t + 3] = v;
                    target[t + 4] = v + 2;
                    target[t + 5] = v + 3;
                }
            }
            var mesh = new Mesh { name = "Audience", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetColors(colors);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(bodies, 0, false);
            mesh.SetTriangles(phones, 1, false);
            // Quads are expanded in the vertex shader, so the bounds cover people standing with their arms up.
            if (n > 0) mesh.bounds = new Bounds((min + max) * 0.5f + Vector3.up * 1.5f, max - min + new Vector3(3f, 6f, 3f));
            return mesh;
        }

        MeshRenderer Spawn(string objectName, Mesh mesh, params Material[] materials)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.allowOcclusionWhenDynamic = false;
            return r;
        }

        void OnDestroy()
        {
            foreach (MeshFilter f in GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh != null) Destroy(f.sharedMesh);
            }
        }
    }
}
