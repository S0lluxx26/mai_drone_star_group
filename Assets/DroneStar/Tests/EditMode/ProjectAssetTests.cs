using DroneStar.App;
using DroneStar.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DroneStar.Tests
{
    /// <summary>Checks the generated project: shaders compile, materials and scene are wired, URP is active.</summary>
    public class ProjectAssetTests
    {
        [TestCase("DroneStar/DroneGlow")]
        [TestCase("DroneStar/NightSky")]
        [TestCase("DroneStar/Water")]
        [TestCase("DroneStar/CityWindows")]
        [TestCase("DroneStar/AdditiveLines")]
        [TestCase("DroneStar/DroneBody")]
        [TestCase("DroneStar/FestivalBronze")]
        [TestCase("DroneStar/Fountain")]
        [TestCase("DroneStar/LaserBeam")]
        [TestCase("DroneStar/Flame")]
        [TestCase("DroneStar/Crowd")]
        public void ShaderCompilesWithoutErrors(string name)
        {
            Shader shader = Shader.Find(name);
            Assert.That(shader, Is.Not.Null, name);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, name + " has compile errors");
            foreach (ShaderMessage m in ShaderUtil.GetShaderMessages(shader))
            {
                Assert.That(m.severity, Is.Not.EqualTo(UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error), name + ": " + m.message);
            }
        }

        [Test]
        public void UniversalPipelineIsActiveWithHdrAndMsaa()
        {
            var urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            Assert.That(urp, Is.Not.Null);
            Assert.That(urp.supportsHDR, Is.True);
            Assert.That(urp.msaaSampleCount, Is.EqualTo(4));
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                Assert.That(QualitySettings.GetRenderPipelineAssetAt(i), Is.EqualTo(urp), QualitySettings.names[i]);
            }
        }

        [Test]
        public void SceneIsInBuildSettingsAndWired()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(ProjectBuilder.ScenePath), Is.Not.Null);
            Assert.That(EditorBuildSettings.scenes.Length, Is.EqualTo(1));
            Assert.That(EditorBuildSettings.scenes[0].path, Is.EqualTo(ProjectBuilder.ScenePath));
        }

        [Test]
        public void ReflectionMaterialMirrorsAtTheWaterLevel()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/DroneStar/Materials/DroneReflection.mat");
            Assert.That(m, Is.Not.Null);
            Assert.That(m.IsKeywordEnabled("_REFLECTION"), Is.True);
            Assert.That(m.GetFloat("_WaterLevel"), Is.EqualTo(NightEnvironment.WaterLevel));
            var body = AssetDatabase.LoadAssetAtPath<Material>("Assets/DroneStar/Materials/DroneBody.mat");
            Assert.That(body.shader.name, Is.EqualTo("DroneStar/DroneBody"));
            Assert.That(body.enableInstancing, Is.True, "airframes are drawn with DrawMeshInstanced");
            var glow = AssetDatabase.LoadAssetAtPath<Material>("Assets/DroneStar/Materials/DroneGlow.mat");
            Assert.That(glow.GetFloat("_NearShrink"), Is.GreaterThan(0f), "LED glows tighten up close so airframes show");
        }

        [Test]
        public void AirframeMeshesFaceOutwardWithinBudget()
        {
            Mesh detailed = DroneAirframe.BuildDetailed(), low = DroneAirframe.BuildLow();
            try
            {
                Assert.That(detailed.triangles.Length / 3, Is.InRange(2000, 4500), "detailed airframe triangle budget");
                Assert.That(low.triangles.Length / 3, Is.LessThan(250), "distant airframe triangle budget");
                Bounds b = detailed.bounds;
                Assert.That(b.size.x, Is.EqualTo(DroneAirframe.Span).Within(0.02f));
                Assert.That(b.size.z, Is.EqualTo(DroneAirframe.Span).Within(0.02f));
                // The LED bulb sits at the origin, under the body; the skids hang below it.
                var uv = new System.Collections.Generic.List<Vector4>();
                detailed.GetUVs(0, uv);
                Vector3[] v = detailed.vertices;
                Vector3[] n = detailed.normals;
                int bulb = 0, blades = 0;
                for (int i = 0; i < v.Length; i++)
                {
                    if (uv[i].x > 0.5f && uv[i].x < 1.5f)
                    {
                        bulb++;
                        Assert.That(v[i].magnitude, Is.LessThan(0.06f));
                    }
                    if (uv[i].x > 1.5f) blades++;
                    Assert.That(n[i].magnitude, Is.EqualTo(1f).Within(1e-3f));
                }
                Assert.That(bulb, Is.GreaterThan(50));
                Assert.That(blades, Is.GreaterThan(200));
                Assert.That(b.min.y, Is.LessThan(-0.04f), "landing skids reach below the bulb");
                // Winding: every triangle's front face (cross(b - a, c - a) in Unity) agrees with its normals,
                // which Face() derives from the outward hint, so a reversed face cannot slip through.
                int[] tris = detailed.triangles;
                for (int t = 0; t < tris.Length; t += 3)
                {
                    Vector3 a = v[tris[t]], p = v[tris[t + 1]], q = v[tris[t + 2]];
                    Vector3 front = Vector3.Cross(p - a, q - a);
                    if (front.sqrMagnitude < 1e-14f) continue;
                    Vector3 normal = n[tris[t]] + n[tris[t + 1]] + n[tris[t + 2]];
                    Assert.That(Vector3.Dot(front, normal), Is.GreaterThan(0f), "triangle " + t / 3 + " is wound inward");
                }
                // And outward means outward: the top of the canopy faces up, the bottom of the bulb faces down.
                int up = 0, down = 0;
                for (int i = 0; i < v.Length; i++)
                {
                    if (v[i].y > b.max.y - 0.01f && n[i].y > 0.5f) up++;
                    if (uv[i].x > 0.5f && uv[i].x < 1.5f && v[i].y < -0.02f && n[i].y < -0.5f) down++;
                }
                Assert.That(up, Is.GreaterThan(0));
                Assert.That(down, Is.GreaterThan(0));
            }
            finally
            {
                Object.DestroyImmediate(detailed);
                Object.DestroyImmediate(low);
            }
        }

        [Test]
        public void StudioSceneShipsTheModelLibraryAndBakedDemo()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ProjectBuilder.ScenePath);
            var app = Object.FindAnyObjectByType<ShowStudioApp>();
            Assert.That(app, Is.Not.Null);
            var so = new SerializedObject(app);
            Assert.That(so.FindProperty("shapePack").objectReferenceValue, Is.Not.Null, "model shapes");
            Assert.That(so.FindProperty("demoAssignments").objectReferenceValue, Is.Not.Null, "pre-solved demo transitions");
            var festival = so.FindProperty("festival").objectReferenceValue as FestivalVenue;
            Assert.That(festival, Is.Not.Null, "the festival venue for Demo 2");
            Assert.That(festival.gameObject.activeSelf, Is.False, "shown only for shows staged at the festival");
            var audience = so.FindProperty("audience").objectReferenceValue as AudienceCrowd;
            Assert.That(audience, Is.Not.Null, "the audience on the shore");
            Assert.That(audience.gameObject.activeSelf, Is.True, "the audience watches every show"); 
        }

        [Test]
        public void PostProfileHasBloomAndTonemapping()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/DroneStar/Settings/StudioPost.asset");
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.TryGet(out Bloom bloom), Is.True);
            Assert.That(bloom.intensity.value, Is.GreaterThan(0f));
            Assert.That(profile.TryGet(out Tonemapping _), Is.True);
        }

        [Test]
        public void PlayerIsBrandedAndLinear()
        {
            Assert.That(PlayerSettings.productName, Is.EqualTo("Drone Star Studio"));
            Assert.That(PlayerSettings.companyName, Is.EqualTo("Mai Drone Star Group"));
            Assert.That(PlayerSettings.colorSpace, Is.EqualTo(ColorSpace.Linear));
            Assert.That(PlayerSettings.WebGL.template, Is.EqualTo("PROJECT:DroneStar"));
        }
    }
}
