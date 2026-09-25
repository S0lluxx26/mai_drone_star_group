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
            Assert.That(body.enableInstancing, Is.True, "airframes are drawn with RenderMeshInstanced");
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
