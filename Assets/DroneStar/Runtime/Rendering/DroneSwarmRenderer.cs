using UnityEngine;
using UnityEngine.Rendering;

namespace DroneStar.App
{
    /// <summary>
    /// Draws the swarm: LED glow sprites, their reflections on the lake, fading light trails and the instanced
    /// quadcopters (<see cref="DroneAirframe"/>): a detailed model with spinning props and a lit LED bulb for the
    /// drones nearest the camera and a light one further out, each leaning into its direction of flight.
    /// </summary>
    public sealed class DroneSwarmRenderer : MonoBehaviour
    {
        /// <summary>Seconds of flight a light trail shows.</summary>
        const float TrailSeconds = 13f / 18f;

        // Samples per trail: 14 up to 2,048 drones, fewer for bigger fleets (seen from further away, where a
        // coarser trail looks the same) so the per-frame trail upload stays about the same size.
        int trailLength = 14;
        float trailInterval = TrailSeconds / 13f;
        const int BodyBatch = 250;

        [SerializeField] Material glowMaterial;
        [SerializeField] Material reflectionMaterial;
        [SerializeField] Material trailMaterial;
        [SerializeField] Material bodyMaterial;

        GlowSprites glow;
        MeshRenderer glowRenderer;
        MeshRenderer reflectionRenderer;
        Mesh trailMesh;
        MeshRenderer trailRenderer;

        Vector3[] trailHistory = new Vector3[0];
        int trailHead;
        float lastTrailTime = float.NaN;
        Vector3[] trailVertices = new Vector3[0];
        Color[] trailColors = new Color[0];
        int count;

        public bool TrailsEnabled { get; set; } = true;
        public bool ReflectionsEnabled { get; set; } = true;
        public int DroneCount => count;

        public void Configure(Material glowMat, Material reflectionMat, Material trailMat, Material bodyMat)
        {
            glowMaterial = glowMat;
            reflectionMaterial = reflectionMat;
            trailMaterial = trailMat;
            bodyMaterial = bodyMat;
        }

        void Awake()
        {
            glow = new GlowSprites("Drone LEDs");
            glowRenderer = CreateChild("LED Glow", glow.Mesh, glowMaterial);
            reflectionRenderer = CreateChild("LED Reflections", glow.Mesh, reflectionMaterial);
            trailMesh = new Mesh { name = "Light Trails" };
            trailMesh.MarkDynamic();
            trailRenderer = CreateChild("Light Trails", trailMesh, trailMaterial);
            bodyProps = new MaterialPropertyBlock();
            detailedMesh = DroneAirframe.BuildDetailed();
            distantMesh = DroneAirframe.BuildLow();
        }

        MeshRenderer CreateChild(string childName, Mesh mesh, Material material)
        {
            var go = new GameObject(childName);
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

        /// <summary>Uploads one frame. Positions are world metres; colours are already in render space.</summary>
        public void Render(Vector3[] positions, Color[] colors, int n, float showTime, bool resetTrails)
        {
            if (glow == null) return;
            if (n != count) Resize(n);

            glow.SetPositionsAndColors(positions, colors, n);
            glow.Upload(includeUvs: false);
            reflectionRenderer.enabled = ReflectionsEnabled;

            UpdateTrails(positions, colors, n, showTime, resetTrails);
            UpdateMotion(positions, colors, n, showTime, resetTrails);
            DrawBodies(positions, n);
        }

        void Resize(int n)
        {
            count = n;
            glow.Resize(n);
            trailLength = n <= 2048 ? 14 : n <= 4096 ? 10 : 7;
            trailInterval = TrailSeconds / (trailLength - 1);
            trailHistory = new Vector3[n * trailLength];
            trailVertices = new Vector3[n * trailLength];
            trailColors = new Color[n * trailLength];
            lastTrailTime = float.NaN;

            var indices = new int[n * (trailLength - 1) * 2];
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j + 1 < trailLength; j++)
                {
                    indices[k++] = i * trailLength + j;
                    indices[k++] = i * trailLength + j + 1;
                }
            }
            trailMesh.Clear();
            trailMesh.indexFormat = n * trailLength > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            trailMesh.SetVertices(trailVertices);
            trailMesh.SetColors(trailColors);
            trailMesh.SetIndices(indices, MeshTopology.Lines, 0, false);
            trailMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 20000f);
        }

        void UpdateTrails(Vector3[] positions, Color[] colors, int n, float showTime, bool reset)
        {
            trailRenderer.enabled = TrailsEnabled;
            if (!TrailsEnabled) return;

            bool jumped = float.IsNaN(lastTrailTime) || showTime < lastTrailTime - 1e-4f || showTime - lastTrailTime > 1f;
            if (reset || jumped)
            {
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < trailLength; j++) trailHistory[i * trailLength + j] = positions[i];
                }
                trailHead = 0;
                lastTrailTime = showTime;
            }
            else if (showTime - lastTrailTime >= trailInterval)
            {
                trailHead = (trailHead + 1) % trailLength;
                for (int i = 0; i < n; i++) trailHistory[i * trailLength + trailHead] = positions[i];
                lastTrailTime = showTime;
            }

            for (int i = 0; i < n; i++)
            {
                int baseIndex = i * trailLength;
                Color c = colors[i];
                for (int j = 0; j < trailLength; j++)
                {
                    // j = 0 is the live position; older samples fade out quadratically.
                    Vector3 p = j == 0 ? positions[i] : trailHistory[baseIndex + (trailHead - j + 1 + trailLength) % trailLength];
                    float fade = 1f - (float)j / (trailLength - 1);
                    trailVertices[baseIndex + j] = p;
                    trailColors[baseIndex + j] = new Color(c.r, c.g, c.b, fade * fade * 0.6f);
                }
            }
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            trailMesh.SetVertices(trailVertices, 0, n * trailLength, flags);
            trailMesh.SetColors(trailColors, 0, n * trailLength, flags);
        }

        // ------------------------------------------------------------------ airframes

        /// <summary>Detailed airframes (spinning props, lit bulb) go to the nearest drones inside this range.</summary>
        const float NearDistance = 48f;
        /// <summary>Beyond this a 0.63 m drone is about two pixels and hidden by its own glow, so it is skipped.</summary>
        const float FarDistance = 190f;
        /// <summary>Propeller turn rate in radians per second of show time (reads as spinning, not strobing).</summary>
        const float PropSpin = 38f;
        /// <summary>Largest forward lean in radians (about 21 degrees at full speed).</summary>
        const float MaxLean = 0.37f;

        static readonly int LedColorId = Shader.PropertyToID("_LedColor");
        static readonly int PropAngleId = Shader.PropertyToID("_PropAngle");
        static readonly int SwarmGlowId = Shader.PropertyToID("_SwarmGlow");

        Mesh detailedMesh;
        Mesh distantMesh;
        MaterialPropertyBlock bodyProps;
        readonly InstanceBatches nearBatches = new InstanceBatches();
        readonly InstanceBatches farBatches = new InstanceBatches();
        Color[] ledColors = new Color[0];
        Vector3[] velocity = new Vector3[0];
        Vector3[] previous = new Vector3[0];
        float previousTime = float.NaN;
        Vector3[] pads = new Vector3[0];
        float[] distance2 = new float[0];
        float[] selectScratch = new float[0];
        float propAngle;
        Color swarmGlow;

        /// <summary>How many drones may use the detailed airframe at once (lower on phones and the web).</summary>
        public int DetailedCapacity { get; set; } = 256;

        /// <summary>Drones drawn with the detailed and the distant airframe in the last frame (for tests and stats).</summary>
        public int DetailedDrawn { get; private set; }
        public int DistantDrawn { get; private set; }

        /// <summary>Each drone's launch pad, so propellers only turn once it has lifted off.</summary>
        public void SetPads(Vector3[] padPositions) => pads = padPositions ?? new Vector3[0];

        void UpdateMotion(Vector3[] positions, Color[] colors, int n, float showTime, bool reset)
        {
            if (velocity.Length != n)
            {
                velocity = new Vector3[n];
                previous = new Vector3[n];
                distance2 = new float[n];
                selectScratch = new float[n];
                reset = true;
            }
            ledColors = colors;
            float dt = showTime - previousTime;
            if (reset || float.IsNaN(previousTime) || dt < 0f || dt > 0.5f)
            {
                System.Array.Copy(positions, previous, n);
                System.Array.Clear(velocity, 0, n);
                previousTime = showTime;
            }
            else if (dt > 1e-4f)
            {
                // Smoothed so the lean eases in and out like a real airframe.
                float k = 1f - Mathf.Exp(-dt * 6f);
                for (int i = 0; i < n; i++)
                {
                    velocity[i] = Vector3.Lerp(velocity[i], (positions[i] - previous[i]) / dt, k);
                    previous[i] = positions[i];
                }
                previousTime = showTime;
            }
            propAngle = Mathf.Repeat(showTime * PropSpin, 2f * Mathf.PI);

            // The lit swarm bathes nearby airframes in its average colour.
            float r = 0f, g = 0f, b = 0f;
            for (int i = 0; i < n; i++)
            {
                Color c = colors[i];
                r += c.r;
                g += c.g;
                b += c.b;
            }
            float scale = n > 0 ? 0.05f / n : 0f;
            swarmGlow = new Color(r * scale, g * scale, b * scale, 0f);
        }

        /// <summary>
        /// Issues the instanced airframe draws for this frame. Called every frame (also while paused, when the
        /// camera may still move), so the level of detail follows the camera.
        /// </summary>
        public void DrawBodies(Vector3[] positions, int n)
        {
            DetailedDrawn = DistantDrawn = 0;
            if (bodyMaterial == null || detailedMesh == null || n == 0 || positions == null || positions.Length < n) return;
            if (distance2.Length != n) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 eye = cam.transform.position;

            const float near2 = NearDistance * NearDistance, far2 = FarDistance * FarDistance;
            int inside = 0, visible = 0;
            for (int i = 0; i < n; i++)
            {
                float d = (positions[i] - eye).sqrMagnitude;
                distance2[i] = d;
                if (d < near2) inside++;
                if (d < far2) visible++;
            }
            if (visible == 0) return;
            // With more drones in range than detailed slots, the nearest ones get them.
            float limit = near2;
            int capacity = Mathf.Max(0, DetailedCapacity);
            if (inside > capacity) limit = capacity == 0 ? -1f : KthSmallest(distance2, n, capacity - 1, selectScratch);

            nearBatches.Begin();
            farBatches.Begin();
            bool padsKnown = pads.Length == n;
            for (int i = 0; i < n; i++)
            {
                float d = distance2[i];
                if (d >= far2) continue;
                Vector3 p = positions[i];
                Vector3 v = velocity[i];
                Quaternion lean = Quaternion.identity;
                float speed = Mathf.Sqrt(v.x * v.x + v.z * v.z);
                if (speed > 0.05f)
                {
                    // Tip toward the direction of flight, as a quadcopter must to accelerate and hold speed.
                    float angle = Mathf.Min(MaxLean, speed * 0.032f);
                    float t = Mathf.Tan(angle) / speed;
                    lean = Quaternion.FromToRotation(Vector3.up, new Vector3(v.x * t, 1f, v.z * t));
                }
                // On the pad (and for the first metre of the climb) the airframe stands on its skids.
                float height = padsKnown ? p.y - pads[i].y : p.y;
                float stand = DroneAirframe.GroundClearance * Mathf.Clamp01(1f - height);
                Matrix4x4 m = Matrix4x4.TRS(new Vector3(p.x, p.y + stand, p.z), lean, Vector3.one);
                bool flying = padsKnown ? (p - pads[i]).sqrMagnitude > 0.02f : p.y > 0.3f;
                Color c = ledColors.Length > i ? ledColors[i] : Color.black;
                var led = new Vector4(c.r, c.g, c.b, flying ? 1f : 0f);
                if (d <= limit && d < near2 && nearBatches.Count < capacity) nearBatches.Add(m, led);
                else farBatches.Add(m, led);
            }
            DetailedDrawn = nearBatches.Count;
            DistantDrawn = farBatches.Count;
            nearBatches.Draw(detailedMesh, bodyMaterial, bodyProps, propAngle, swarmGlow, gameObject.layer);
            farBatches.Draw(distantMesh, bodyMaterial, bodyProps, propAngle, swarmGlow, gameObject.layer);
        }

        /// <summary>The k-th smallest of the first n values (0-based), by quickselect on a scratch copy.</summary>
        internal static float KthSmallest(float[] values, int n, int k, float[] scratch)
        {
            System.Array.Copy(values, scratch, n);
            int lo = 0, hi = n - 1;
            while (lo < hi)
            {
                float pivot = scratch[(lo + hi) >> 1];
                int i = lo, j = hi;
                while (i <= j)
                {
                    while (scratch[i] < pivot) i++;
                    while (scratch[j] > pivot) j--;
                    if (i <= j)
                    {
                        float tmp = scratch[i];
                        scratch[i] = scratch[j];
                        scratch[j] = tmp;
                        i++;
                        j--;
                    }
                }
                if (k <= j) hi = j;
                else if (k >= i) lo = i;
                else break;
            }
            return scratch[k];
        }

        /// <summary>Instance matrices and LED colours in fixed-size batches for Graphics.DrawMeshInstanced.</summary>
        sealed class InstanceBatches
        {
            readonly System.Collections.Generic.List<Matrix4x4[]> matrices = new System.Collections.Generic.List<Matrix4x4[]>();
            readonly System.Collections.Generic.List<Vector4[]> colors = new System.Collections.Generic.List<Vector4[]>();

            public int Count { get; private set; }

            public void Begin() => Count = 0;

            public void Add(Matrix4x4 m, Vector4 led)
            {
                int batch = Count / BodyBatch, slot = Count % BodyBatch;
                if (batch == matrices.Count)
                {
                    matrices.Add(new Matrix4x4[BodyBatch]);
                    colors.Add(new Vector4[BodyBatch]);
                }
                matrices[batch][slot] = m;
                colors[batch][slot] = led;
                Count++;
            }

            public void Draw(Mesh mesh, Material material, MaterialPropertyBlock props, float angle, Color glow, int layer)
            {
                for (int start = 0, batch = 0; start < Count; start += BodyBatch, batch++)
                {
                    // Draw calls copy the property block, so one block serves every batch.
                    props.Clear();
                    props.SetVectorArray(LedColorId, colors[batch]);
                    props.SetFloat(PropAngleId, angle);
                    props.SetColor(SwarmGlowId, glow);
                    Graphics.DrawMeshInstanced(mesh, 0, material, matrices[batch], Mathf.Min(BodyBatch, Count - start), props,
                        ShadowCastingMode.Off, false, layer, null, LightProbeUsage.Off);
                }
            }
        }

        void OnDestroy()
        {
            if (glow != null) Destroy(glow.Mesh);
            if (trailMesh != null) Destroy(trailMesh);
            if (detailedMesh != null) Destroy(detailedMesh);
            if (distantMesh != null) Destroy(distantMesh);
        }
    }
}
