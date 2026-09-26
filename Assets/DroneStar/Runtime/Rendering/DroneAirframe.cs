using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneStar.App
{
    /// <summary>
    /// The light-show quadcopter, after the Draw_in_3D Blender model: a rounded shell over a carbon belly pan,
    /// battery with strap, GPS puck on a mast, antennas, landing skids, four carbon arms with clamps, motors,
    /// twin-ring prop guards, twisted two-blade propellers and the LED bulb hanging underneath.
    /// The origin is the centre of the LED bulb, so each drone's glow sprite sits exactly on it.
    /// Vertex data read by DroneStar/DroneBody: colour.rgb = albedo (linear), colour.a = gloss;
    /// uv0 = (part, spin direction, hub x, hub z) where part is <see cref="PartFrame"/>, <see cref="PartBulb"/>
    /// or <see cref="PartBlade"/>.
    /// </summary>
    public static class DroneAirframe
    {
        public const float PartFrame = 0f;
        public const float PartBulb = 1f;
        public const float PartBlade = 2f;

        /// <summary>Overall span across the prop guards, metres.</summary>
        public const float Span = 0.634f;

        /// <summary>
        /// Depth of the landing skids below the bulb centre (the model origin). A drone standing on its pad is
        /// raised by this much so it rests on its skids instead of sinking into the deck.
        /// </summary>
        public const float GroundClearance = 0.055f;

        // Materials (linear albedo, gloss).
        static readonly Color Shell = new Color(0.62f, 0.64f, 0.68f, 0.62f);
        static readonly Color Carbon = new Color(0.022f, 0.023f, 0.027f, 0.5f);
        static readonly Color Motor = new Color(0.42f, 0.44f, 0.5f, 0.85f);
        static readonly Color Battery = new Color(0.05f, 0.055f, 0.065f, 0.3f);
        static readonly Color Strap = new Color(0.3f, 0.32f, 0.36f, 0.7f);
        static readonly Color Blade = new Color(0.035f, 0.035f, 0.04f, 0.45f);
        static readonly Color Bulb = new Color(0.9f, 0.9f, 0.9f, 0.9f);
        static readonly Color LowBody = new Color(0.2f, 0.21f, 0.24f, 0.3f);

        /// <summary>
        /// The model is authored in the Blender file's coordinates (z up, arms at z = 0) and converted here:
        /// Unity x = x, Unity y = z + <see cref="Lift"/>, Unity z = y. Lift puts the bulb centre at the origin.
        /// </summary>
        const float Lift = 0.036f;

        static Vector3 B(float x, float y, float z) => new Vector3(x, z + Lift, y);

        /// <summary>About 3,000 triangles: for drones close to the camera.</summary>
        public static Mesh BuildDetailed()
        {
            var m = new AirframeMesh();

            // Body: canopy over a belly pan, battery with a strap, GPS mast, antennas and skids.
            m.Paint(Shell);
            m.Ellipsoid(B(0f, 0f, 0.012f), new Vector3(0.105f, 0.034f, 0.078f), 18, 6, 0f, Mathf.PI / 2f);
            m.Paint(Carbon);
            m.Ellipsoid(B(0f, 0f, 0.012f), new Vector3(0.105f, 0.03f, 0.078f), 18, 4, -Mathf.PI / 2f, 0f);
            m.Paint(Battery);
            m.Box(B(0f, 0.004f, 0.05f), new Vector3(0.12f, 0.03f, 0.066f));
            m.Paint(Strap);
            m.Box(B(0f, 0.004f, 0.066f), new Vector3(0.122f, 0.003f, 0.018f));
            m.Paint(Carbon);
            m.Tube(B(0f, -0.056f, 0.03f), B(0f, -0.056f, 0.095f), 0.003f, 6);
            m.Paint(Shell);
            m.Cylinder(B(0f, -0.056f, 0.095f), 0.02f, 0.008f, 14);
            m.Paint(Carbon);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                m.Tube(B(sx * 0.03f, 0.07f, 0.03f), B(sx * 0.036f, 0.078f, 0.08f), 0.002f, 4);
                m.Tube(B(sx * 0.05f, -0.06f, -0.01f), B(sx * 0.075f, -0.06f, -0.085f), 0.0045f, 6);
                m.Tube(B(sx * 0.05f, 0.06f, -0.01f), B(sx * 0.075f, 0.06f, -0.085f), 0.0045f, 6);
                m.Tube(B(sx * 0.075f, -0.085f, -0.085f), B(sx * 0.075f, 0.085f, -0.085f), 0.005f, 6);
            }

            // Arms, motors, guards and propellers. Spin direction alternates by quadrant.
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    float mx = sx * 0.19f, my = sy * 0.19f;
                    m.Paint(Carbon);
                    m.Tube(B(sx * 0.07f, sy * 0.045f, 0.004f), B(mx, my, 0.004f), 0.011f, 8);
                    m.Box(B(sx * 0.075f, sy * 0.05f, 0.004f), new Vector3(0.022f, 0.018f, 0.022f));
                    m.Ring(B(mx, my, 0.042f), 0.127f, 0.0042f, 24);
                    m.Ring(B(mx, my, 0.022f), 0.127f, 0.003f, 24);
                    float diagonal = Mathf.Atan2(sy, sx);
                    foreach (float a in new[] { diagonal + Mathf.PI * 0.75f, diagonal - Mathf.PI * 0.75f })
                    {
                        float cx = Mathf.Cos(a), cy = Mathf.Sin(a);
                        m.Tube(B(mx + cx * 0.02f, my + cy * 0.02f, 0.01f), B(mx + cx * 0.127f, my + cy * 0.127f, 0.042f), 0.003f, 4);
                    }
                    m.Paint(Motor);
                    m.Cylinder(B(mx, my, -0.012f), 0.021f, 0.03f, 14);
                    m.Cylinder(B(mx, my, 0.018f), 0.011f, 0.01f, 10, 0.007f);

                    Vector3 hub = B(mx, my, 0.034f);
                    float spin = sx * sy;
                    m.Paint(Blade);
                    m.SetPart(PartBlade, spin, hub);
                    m.Cylinder(hub - new Vector3(0f, 0.004f, 0f), 0.009f, 0.008f, 8);
                    for (int blade = 0; blade < 2; blade++)
                    {
                        float a = 0.6f * spin + blade * Mathf.PI;
                        m.Propeller(hub, a, spin);
                    }
                    m.SetPart(PartFrame);
                }
            }

            // The LED bulb: a short collar under the belly and a dome below it.
            m.Paint(Bulb);
            m.SetPart(PartBulb);
            m.Cylinder(new Vector3(0f, 0.004f, 0f), 0.034f, 0.014f, 16);
            m.Ellipsoid(new Vector3(0f, 0.004f, 0f), new Vector3(0.034f, 0.032f, 0.034f), 16, 5, -Mathf.PI / 2f, 0f);
            return m.ToMesh("Drone (detailed)");
        }

        /// <summary>About 160 triangles: a body, four arms, blurred rotor discs and the bulb.</summary>
        public static Mesh BuildLow()
        {
            var m = new AirframeMesh();
            m.Paint(LowBody);
            m.Box(B(0f, 0f, 0.02f), new Vector3(0.2f, 0.07f, 0.15f));
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    m.Paint(LowBody);
                    m.Tube(B(sx * 0.06f, sy * 0.04f, 0.01f), B(sx * 0.19f, sy * 0.19f, 0.01f), 0.012f, 4);
                    m.Paint(Blade);
                    m.Disc(B(sx * 0.19f, sy * 0.19f, 0.04f), 0.127f, 10);
                }
            }
            m.Paint(Bulb);
            m.SetPart(PartBulb);
            m.Ellipsoid(new Vector3(0f, 0.004f, 0f), new Vector3(0.034f, 0.032f, 0.034f), 6, 2, -Mathf.PI / 2f, 0f);
            return m.ToMesh("Drone (distant)");
        }
    }

    /// <summary>Mesh builder for the airframe. Faces are oriented by an outward hint, so winding never matters.</summary>
    sealed class AirframeMesh
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<Vector4> uvs = new List<Vector4>();
        readonly List<int> triangles = new List<int>();
        Color paint = Color.white;
        Vector4 part;

        public void Paint(Color c) => paint = c;

        public void SetPart(float kind, float spin = 0f, Vector3 hub = default) => part = new Vector4(kind, spin, hub.x, hub.z);

        /// <summary>Adds a convex polygon. <paramref name="smooth"/> gives per-vertex normals (else flat).</summary>
        public void Face(IList<Vector3> points, Vector3 outward, IList<Vector3> smooth = null)
        {
            Vector3 n = Vector3.zero;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 a = points[i], b = points[(i + 1) % points.Count];
                n.x += (a.y - b.y) * (a.z + b.z);
                n.y += (a.z - b.z) * (a.x + b.x);
                n.z += (a.x - b.x) * (a.y + b.y);
            }
            if (n.sqrMagnitude < 1e-20f) return;
            n.Normalize();
            // Newell's normal points along cross(b - a, c - a), which is the side Unity renders as the front
            // (clockwise winding as seen from there); reverse the order when that side faces inward.
            bool reverse = Vector3.Dot(n, outward) < 0f;
            if (reverse) n = -n;
            int start = vertices.Count;
            for (int k = 0; k < points.Count; k++)
            {
                int i = reverse ? points.Count - 1 - k : k;
                vertices.Add(points[i]);
                normals.Add(smooth != null ? smooth[i] : n);
                colors.Add(paint);
                uvs.Add(part);
            }
            for (int k = 1; k + 1 < points.Count; k++)
            {
                triangles.Add(start);
                triangles.Add(start + k);
                triangles.Add(start + k + 1);
            }
        }

        public void Box(Vector3 c, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            for (int axis = 0; axis < 3; axis++)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 n = Vector3.zero;
                    n[axis] = s;
                    Vector3 u = Vector3.zero, v = Vector3.zero;
                    u[(axis + 1) % 3] = h[(axis + 1) % 3];
                    v[(axis + 2) % 3] = h[(axis + 2) % 3];
                    Vector3 f = c + Vector3.Scale(n, h);
                    Face(new[] { f - u - v, f + u - v, f + u + v, f - u + v }, n);
                }
            }
        }

        /// <summary>Upright cylinder or frustum standing on <paramref name="c"/>, closed at both ends.</summary>
        public void Cylinder(Vector3 c, float r, float h, int sides, float r2 = -1f)
        {
            if (r2 < 0f) r2 = r;
            var lo = new Vector3[sides];
            var hi = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i * 2f * Mathf.PI / sides;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                lo[i] = c + d * r;
                hi[i] = c + d * r2 + Vector3.up * h;
            }
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                Vector3 mid = (lo[i] + lo[j]) * 0.5f - c;
                mid.y = 0f;
                Face(new[] { lo[i], lo[j], hi[j], hi[i] }, mid);
            }
            Face(hi, Vector3.up);
            Face(lo, Vector3.down);
        }

        /// <summary>Open tube between two points (arms, struts, skids).</summary>
        public void Tube(Vector3 a, Vector3 b, float r, int sides)
        {
            Vector3 axis = (b - a).normalized;
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(axis, u);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * 2f * Mathf.PI / sides, a1 = (i + 1) * 2f * Mathf.PI / sides;
                Vector3 d0 = u * Mathf.Cos(a0) + v * Mathf.Sin(a0), d1 = u * Mathf.Cos(a1) + v * Mathf.Sin(a1);
                Face(new[] { a + d0 * r, a + d1 * r, b + d1 * r, b + d0 * r }, d0 + d1, sides >= 6 ? new[] { d0, d1, d1, d0 } : null);
            }
        }

        /// <summary>Horizontal torus-like guard ring made of short tubes.</summary>
        public void Ring(Vector3 c, float radius, float r, int segments)
        {
            Vector3 prev = c + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i * 2f * Mathf.PI / segments;
                Vector3 p = c + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Tube(prev, p, r, 4);
                prev = p;
            }
        }

        /// <summary>Double-sided flat disc (distant rotor blur).</summary>
        public void Disc(Vector3 c, float radius, int sides)
        {
            var pts = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i * 2f * Mathf.PI / sides;
                pts[i] = c + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            }
            Face(pts, Vector3.up);
            Face(pts, Vector3.down);
        }

        /// <summary>Ellipsoid band between latitudes lat0 and lat1 (radians, y up), smooth shaded.</summary>
        public void Ellipsoid(Vector3 c, Vector3 radii, int segments, int rings, float lat0, float lat1)
        {
            Vector3 At(float lon, float lat) => c + new Vector3(Mathf.Cos(lon) * Mathf.Cos(lat) * radii.x, Mathf.Sin(lat) * radii.y, Mathf.Sin(lon) * Mathf.Cos(lat) * radii.z);
            Vector3 Normal(Vector3 p)
            {
                Vector3 d = p - c;
                return new Vector3(d.x / (radii.x * radii.x), d.y / (radii.y * radii.y), d.z / (radii.z * radii.z)).normalized;
            }
            for (int j = 0; j < rings; j++)
            {
                float b0 = lat0 + (lat1 - lat0) * j / rings, b1 = lat0 + (lat1 - lat0) * (j + 1) / rings;
                for (int i = 0; i < segments; i++)
                {
                    float a0 = i * 2f * Mathf.PI / segments, a1 = (i + 1) * 2f * Mathf.PI / segments;
                    Vector3 p0 = At(a0, b0), p1 = At(a1, b0), p2 = At(a1, b1), p3 = At(a0, b1);
                    Vector3[] quad = (p0 - p1).sqrMagnitude < 1e-12f ? new[] { p1, p2, p3 }
                        : (p2 - p3).sqrMagnitude < 1e-12f ? new[] { p0, p1, p2 } : new[] { p0, p1, p2, p3 };
                    var ns = new Vector3[quad.Length];
                    Vector3 outward = Vector3.zero;
                    for (int k = 0; k < quad.Length; k++)
                    {
                        ns[k] = Normal(quad[k]);
                        outward += ns[k];
                    }
                    Face(quad, outward, ns);
                }
            }
        }

        /// <summary>One twisted, tapered blade from the hub outward along angle <paramref name="a"/>.</summary>
        public void Propeller(Vector3 hub, float a, float spin)
        {
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 side = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * spin;
            const int stations = 8;
            var lead = new Vector3[stations];
            var trail = new Vector3[stations];
            for (int k = 0; k < stations; k++)
            {
                float t = k / (float)(stations - 1);
                float radius = 0.012f + 0.098f * t;
                float chord = 0.017f + 0.011f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, t * 1.3f)) - 0.012f * t * t;
                float twist = (24f - 16f * t) * Mathf.Deg2Rad;
                Vector3 centre = hub + d * radius;
                Vector3 offset = side * (chord * 0.5f * Mathf.Cos(twist)) + Vector3.up * (chord * 0.5f * Mathf.Sin(twist));
                lead[k] = centre + offset;
                trail[k] = centre - offset;
            }
            for (int k = 0; k + 1 < stations; k++)
            {
                Vector3[] quad = { lead[k], lead[k + 1], trail[k + 1], trail[k] };
                Vector3 up = Vector3.Cross(lead[k + 1] - lead[k], trail[k] - lead[k]);
                if (up.y < 0f) up = -up;
                Face(quad, up);
                Face(quad, -up);
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
