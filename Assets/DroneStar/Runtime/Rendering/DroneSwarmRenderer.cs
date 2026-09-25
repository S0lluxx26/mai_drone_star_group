using UnityEngine;
using UnityEngine.Rendering;

namespace DroneStar.App
{
    /// <summary>
    /// Draws the swarm: LED glow sprites, their reflections on the lake, fading light trails and the
    /// (instanced) airframes that become visible when the camera flies close.
    /// </summary>
    public sealed class DroneSwarmRenderer : MonoBehaviour
    {
        const int TrailLength = 14;
        const float TrailInterval = 1f / 18f;
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
        Mesh bodyMesh;
        Matrix4x4[] bodyMatrices = new Matrix4x4[0];

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
            bodyMesh = BuildAirframe();
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
            DrawBodies(positions, n);
        }

        void Resize(int n)
        {
            count = n;
            glow.Resize(n);
            trailHistory = new Vector3[n * TrailLength];
            trailVertices = new Vector3[n * TrailLength];
            trailColors = new Color[n * TrailLength];
            bodyMatrices = new Matrix4x4[n];
            lastTrailTime = float.NaN;

            var indices = new int[n * (TrailLength - 1) * 2];
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j + 1 < TrailLength; j++)
                {
                    indices[k++] = i * TrailLength + j;
                    indices[k++] = i * TrailLength + j + 1;
                }
            }
            trailMesh.Clear();
            trailMesh.indexFormat = n * TrailLength > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
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
                    for (int j = 0; j < TrailLength; j++) trailHistory[i * TrailLength + j] = positions[i];
                }
                trailHead = 0;
                lastTrailTime = showTime;
            }
            else if (showTime - lastTrailTime >= TrailInterval)
            {
                trailHead = (trailHead + 1) % TrailLength;
                for (int i = 0; i < n; i++) trailHistory[i * TrailLength + trailHead] = positions[i];
                lastTrailTime = showTime;
            }

            for (int i = 0; i < n; i++)
            {
                int baseIndex = i * TrailLength;
                Color c = colors[i];
                for (int j = 0; j < TrailLength; j++)
                {
                    // j = 0 is the live position; older samples fade out quadratically.
                    Vector3 p = j == 0 ? positions[i] : trailHistory[baseIndex + (trailHead - j + 1 + TrailLength) % TrailLength];
                    float fade = 1f - (float)j / (TrailLength - 1);
                    trailVertices[baseIndex + j] = p;
                    trailColors[baseIndex + j] = new Color(c.r, c.g, c.b, fade * fade * 0.6f);
                }
            }
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            trailMesh.SetVertices(trailVertices, 0, n * TrailLength, flags);
            trailMesh.SetColors(trailColors, 0, n * TrailLength, flags);
        }

        /// <summary>Airframes are ~0.4 m: beyond this distance they are below a pixel, so skip them.</summary>
        const float BodyDrawDistance = 160f;

        public void DrawBodies(Vector3[] positions, int n)
        {
            if (bodyMaterial == null || bodyMesh == null || n == 0 || positions == null || positions.Length < n) return;
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 eye = cam.transform.position;
                float nearest = float.MaxValue;
                for (int i = 0; i < n; i += 7) nearest = Mathf.Min(nearest, (positions[i] - eye).sqrMagnitude);
                if (nearest > BodyDrawDistance * BodyDrawDistance) return;
            }
            for (int i = 0; i < n; i++) bodyMatrices[i] = Matrix4x4.Translate(positions[i]);
            var rp = new RenderParams(bodyMaterial)
            {
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = gameObject.layer,
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 20000f),
            };
            for (int start = 0; start < n; start += BodyBatch)
            {
                Graphics.RenderMeshInstanced(rp, bodyMesh, 0, bodyMatrices, Mathf.Min(BodyBatch, n - start), start);
            }
        }

        /// <summary>A 0.4 m quadcopter: hub, four arms, motor pods and rotor discs.</summary>
        static Mesh BuildAirframe()
        {
            var kit = new MeshKit();
            kit.Box(new Vector3(0f, 0.02f, 0f), new Vector3(0.14f, 0.05f, 0.14f), bottom: true);
            for (int a = 0; a < 4; a++)
            {
                float angle = 45f + 90f * a;
                Quaternion q = Quaternion.Euler(0f, angle, 0f);
                Vector3 dir = q * Vector3.forward;
                var arm = new MeshKit();
                arm.Box(new Vector3(0f, 0.02f, 0.1f), new Vector3(0.025f, 0.018f, 0.2f), bottom: true);
                for (int v = 0; v < arm.Vertices.Count; v++)
                {
                    arm.Vertices[v] = q * arm.Vertices[v];
                    arm.Normals[v] = q * arm.Normals[v];
                }
                Append(kit, arm);
                Vector3 motor = dir * 0.2f;
                kit.Cylinder(motor + Vector3.up * 0.01f, 0.018f, 0.03f, 8);
                kit.Cylinder(motor + Vector3.up * 0.042f, 0.075f, 0.004f, 14);
            }
            return kit.ToMesh("Airframe");
        }

        static void Append(MeshKit into, MeshKit from)
        {
            int offset = into.Vertices.Count;
            into.Vertices.AddRange(from.Vertices);
            into.Normals.AddRange(from.Normals);
            into.Uv0.AddRange(from.Uv0);
            into.Uv1.AddRange(from.Uv1);
            into.Colors.AddRange(from.Colors);
            foreach (int t in from.Triangles) into.Triangles.Add(t + offset);
        }

        void OnDestroy()
        {
            if (glow != null) Destroy(glow.Mesh);
            if (trailMesh != null) Destroy(trailMesh);
            if (bodyMesh != null) Destroy(bodyMesh);
        }
    }
}
