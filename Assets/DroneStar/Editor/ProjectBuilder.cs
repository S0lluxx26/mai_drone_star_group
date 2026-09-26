using System;
using System.IO;
using DroneStar.App;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DroneStar.EditorTools
{
    /// <summary>
    /// Generates every asset the studio needs (materials, post-processing profile, UI panel settings and
    /// the scene) from code, configures URP and the player, and builds WebGL / Windows players.
    /// Run from the menu (Drone Star ▸ …) or headless:
    ///   Unity -batchmode -projectPath . -executeMethod DroneStar.EditorTools.ProjectBuilder.RebuildAll -quit
    /// </summary>
    public static class ProjectBuilder
    {
        public const string Root = "Assets/DroneStar";
        public const string ScenePath = Root + "/Scenes/DroneStarStudio.unity";
        const string MaterialDir = Root + "/Materials";
        const string SettingsDir = Root + "/Settings";
        const string PipelinePath = SettingsDir + "/PC_RPAsset.asset";
        const string ProfilePath = SettingsDir + "/StudioPost.asset";
        const string PanelPath = Root + "/UI/StudioPanel.asset";
        const string ThemePath = Root + "/UI/StudioTheme.tss";
        const string StylePath = Root + "/UI/Studio.uss";

        static readonly Vector3 MoonDirection = new Vector3(0.45f, 0.32f, 0.83f).normalized;

        // Night palette (sRGB): a very dark blue sky and black-blue water.
        static readonly Color SkyZenith = new Color(0.008f, 0.016f, 0.06f);
        static readonly Color SkyHorizon = new Color(0.04f, 0.08f, 0.2f);
        static readonly Color SkyGlow = new Color(0.05f, 0.1f, 0.26f);
        static readonly Color WaterDeep = new Color(0f, 0.006f, 0.02f);
        static readonly Color CityOnWater = new Color(0.1f, 0.07f, 0.04f);
        public static readonly Color FogColor = new Color(0.015f, 0.03f, 0.07f);

        static void SkyColors(Material m)
        {
            m.SetColor("_ZenithColor", SkyZenith);
            m.SetColor("_HorizonColor", SkyHorizon);
            m.SetColor("_GlowColor", SkyGlow);
        }

        [MenuItem("Drone Star/Rebuild Project Assets", priority = 1)]
        public static void RebuildAll()
        {
            ConfigurePlayer();
            ConfigurePipeline();
            BuildMaterials(out Materials m);
            VolumeProfile profile = BuildPostProfile();
            PanelSettings panel = BuildPanelSettings();
            BuildScene(m, profile, panel);
            AssetDatabase.SaveAssets();
            Debug.Log("[DroneStar] Project assets rebuilt.");
        }

        // ------------------------------------------------------------------ player & pipeline

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Mai Drone Star Group";
            PlayerSettings.productName = "Drone Star Studio";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.template = "PROJECT:DroneStar";
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.maidronestargroup.dronestarstudio");
        }

        static void ConfigurePipeline()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null) throw new InvalidOperationException("Missing " + PipelinePath);
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 4;
            pipeline.renderScale = 1f;
            pipeline.supportsCameraDepthTexture = false;
            pipeline.supportsCameraOpaqueTexture = false;
            pipeline.shadowDistance = 0f;
            var so = new SerializedObject(pipeline);
            SetBool(so, "m_MainLightShadowsSupported", false);
            SetBool(so, "m_AdditionalLightShadowsSupported", false);
            SetInt(so, "m_AdditionalLightsRenderingMode", 0);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
                QualitySettings.shadows = UnityEngine.ShadowQuality.Disable;
                QualitySettings.vSyncCount = 1;
            }
            QualitySettings.SetQualityLevel(current, false);

            foreach (string unused in new[] { SettingsDir + "/Mobile_RPAsset.asset", SettingsDir + "/Mobile_Renderer.asset" })
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unused) != null) AssetDatabase.DeleteAsset(unused);
            }
        }

        static void SetBool(SerializedObject so, string name, bool value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.boolValue = value;
        }

        static void SetInt(SerializedObject so, string name, int value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.intValue = value;
        }

        // ------------------------------------------------------------------ materials

        sealed class Materials
        {
            public Material Glow, Reflection, Trails, Body, Sky, Water, Land, Hills, Deck, City, Trees, Lamps, Pads;
        }

        static void BuildMaterials(out Materials m)
        {
            Directory.CreateDirectory(MaterialDir);
            Shader glow = RequireShader("DroneStar/DroneGlow");
            Shader lit = RequireShader("Universal Render Pipeline/Lit");
            m = new Materials
            {
                Glow = Mat("DroneGlow", glow, x =>
                {
                    x.SetFloat("_Size", 1.9f);
                    x.SetFloat("_MinPixels", 3.2f);
                    x.SetFloat("_Intensity", 5.5f);
                    x.SetFloat("_CoreSharpness", 24f);
                    x.SetFloat("_HaloFalloff", 4.2f);
                    x.SetFloat("_WhiteCore", 0.55f);
                }),
                Reflection = Mat("DroneReflection", glow, x =>
                {
                    x.SetFloat("_Size", 1.9f);
                    x.SetFloat("_MinPixels", 2.4f);
                    x.SetFloat("_Intensity", 5.5f);
                    x.SetFloat("_Reflection", 1f);
                    x.EnableKeyword("_REFLECTION");
                    x.SetFloat("_WaterLevel", NightEnvironment.WaterLevel);
                    x.SetFloat("_ReflectionStretch", 3.4f);
                    x.SetFloat("_ReflectionStrength", 0.3f);
                }),
                Trails = Mat("LightTrails", RequireShader("DroneStar/AdditiveLines"), x => x.SetFloat("_Intensity", 2.2f)),
                Body = Mat("DroneBody", lit, x => LitColor(x, new Color(0.08f, 0.08f, 0.1f), 0.4f, 0.55f)),
                Sky = Mat("NightSky", RequireShader("DroneStar/NightSky"), x =>
                {
                    x.SetVector("_MoonDirection", MoonDirection);
                    SkyColors(x);
                    x.SetFloat("_MilkyWay", 0.16f);
                }),
                Water = Mat("Lake", RequireShader("DroneStar/Water"), x =>
                {
                    x.SetVector("_MoonDirection", MoonDirection);
                    x.SetFloat("_WaveStrength", 0.24f);
                    SkyColors(x);
                    x.SetColor("_DeepColor", WaterDeep);
                    x.SetColor("_CityGlow", CityOnWater);
                }),
                Land = Mat("Shore", lit, x => LitColor(x, new Color(0.03f, 0.045f, 0.04f), 0f, 0.12f)),
                Hills = Mat("Hills", lit, x => LitColor(x, new Color(0.018f, 0.022f, 0.045f), 0f, 0.05f)),
                Deck = Mat("BargeDeck", lit, x => LitColor(x, new Color(0.07f, 0.07f, 0.085f), 0.5f, 0.35f)),
                City = Mat("CityWindows", RequireShader("DroneStar/CityWindows"), x =>
                {
                    SkyColors(x);
                    x.SetColor("_FacadeColor", new Color(0.1f, 0.11f, 0.13f));
                    x.SetColor("_GlassColor", new Color(0.04f, 0.055f, 0.09f));
                    x.SetColor("_StreetGlow", new Color(0.4f, 0.25f, 0.1f));
                    x.SetFloat("_WindowIntensity", 1.8f);
                    x.SetFloat("_LitFraction", 0.3f);
                }),
                Trees = Mat("Trees", lit, x => LitColor(x, new Color(0.02f, 0.05f, 0.035f), 0f, 0.1f)),
                Lamps = Mat("ShoreLamps", glow, x =>
                {
                    // Lanterns can sit a few metres from the audience camera, so keep the sprite small.
                    x.SetFloat("_Size", 0.45f);
                    x.SetFloat("_MinPixels", 2f);
                    x.SetFloat("_Intensity", 2.6f);
                }),
                Pads = Mat("PadLights", glow, x =>
                {
                    x.SetFloat("_Size", 0.7f);
                    x.SetFloat("_MinPixels", 1.4f);
                    x.SetFloat("_Intensity", 2.4f);
                }),
            };
            m.Body.enableInstancing = true;
            EditorUtility.SetDirty(m.Body);
        }

        static Shader RequireShader(string name)
        {
            Shader s = Shader.Find(name);
            if (s == null) throw new InvalidOperationException("Shader not found: " + name);
            return s;
        }

        static Material Mat(string name, Shader shader, Action<Material> configure)
        {
            string path = MaterialDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }
            configure?.Invoke(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void LitColor(Material m, Color color, float metallic, float smoothness)
        {
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
        }

        // ------------------------------------------------------------------ post-processing & UI

        static VolumeProfile BuildPostProfile()
        {
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath) != null) AssetDatabase.DeleteAsset(ProfilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);

            var bloom = Add<Bloom>(profile);
            bloom.threshold.Override(0.85f);
            bloom.intensity.Override(1.35f);
            bloom.scatter.Override(0.74f);
            bloom.highQualityFiltering.Override(true);

            var tone = Add<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.Neutral);

            var color = Add<ColorAdjustments>(profile);
            color.saturation.Override(14f);
            color.postExposure.Override(0.15f);
            color.contrast.Override(8f);

            var vignette = Add<Vignette>(profile);
            vignette.intensity.Override(0.24f);
            vignette.smoothness.Override(0.45f);

            EditorUtility.SetDirty(profile);
            return profile;
        }

        static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T component = profile.Add<T>(true);
            component.name = typeof(T).Name;
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        static PanelSettings BuildPanelSettings()
        {
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelPath);
            }
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (panel.themeStyleSheet == null) throw new InvalidOperationException("Missing theme " + ThemePath);
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1600, 900);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.sortingOrder = 10;
            EditorUtility.SetDirty(panel);
            return panel;
        }

        // ------------------------------------------------------------------ scene

        static void BuildScene(Materials m, VolumeProfile profile, PanelSettings panel)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraGo.transform.SetPositionAndRotation(new Vector3(0f, 40f, -190f), Quaternion.Euler(9f, 0f, 0f));
            var cam = cameraGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 4200f;
            cam.fieldOfView = 50f;
            cam.allowHDR = true;
            cam.allowMSAA = true;
            var camData = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.renderShadows = false;
            cameraGo.AddComponent<AudioListener>();
            var rig = cameraGo.AddComponent<ShowCameraRig>();

            var moon = new GameObject("Moonlight");
            var light = moon.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.62f, 0.7f, 1f);
            light.intensity = 0.4f;
            light.shadows = LightShadows.None;
            moon.transform.rotation = Quaternion.LookRotation(-MoonDirection);

            var volumeGo = new GameObject("Post Processing");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = profile;

            var envGo = new GameObject("Night Environment");
            var env = envGo.AddComponent<NightEnvironment>();
            env.Configure(m.Sky, m.Water, m.Land, m.Hills, m.Deck, m.City, m.Trees, m.Lamps, m.Pads);
            RenderSettings.skybox = m.Sky;

            var swarmGo = new GameObject("Drone Swarm");
            var swarm = swarmGo.AddComponent<DroneSwarmRenderer>();
            swarm.Configure(m.Glow, m.Reflection, m.Trails, m.Body);

            var uiGo = new GameObject("Studio UI");
            var doc = uiGo.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            var ui = uiGo.AddComponent<StudioUI>();
            var uiSo = new SerializedObject(ui);
            uiSo.FindProperty("styleSheet").objectReferenceValue = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            uiSo.ApplyModifiedPropertiesWithoutUndo();

            var audioGo = new GameObject("Soundtrack");
            audioGo.AddComponent<AudioSource>();
            var score = audioGo.AddComponent<AmbientScore>();

            var demoGo = new GameObject("Demo Director");
            var demo = demoGo.AddComponent<DemoDirector>();

            var bridgeGo = new GameObject("WebBridge");
            var bridge = bridgeGo.AddComponent<WebBridge>();

            var appGo = new GameObject("Drone Star Studio");
            var app = appGo.AddComponent<ShowStudioApp>();
            var appSo = new SerializedObject(app);
            appSo.FindProperty("swarm").objectReferenceValue = swarm;
            appSo.FindProperty("environment").objectReferenceValue = env;
            appSo.FindProperty("cameraRig").objectReferenceValue = rig;
            appSo.FindProperty("ui").objectReferenceValue = ui;
            appSo.FindProperty("demo").objectReferenceValue = demo;
            appSo.FindProperty("score").objectReferenceValue = score;
            appSo.FindProperty("bridge").objectReferenceValue = bridge;
            appSo.ApplyModifiedPropertiesWithoutUndo();

            var captureGo = new GameObject("Capture Runner");
            captureGo.AddComponent<CaptureRunner>();

            // Lighting environment for this scene (also re-applied at runtime by NightEnvironment).
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogDensity = 0.00055f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        // ------------------------------------------------------------------ builds

        [MenuItem("Drone Star/Build WebGL", priority = 20)]
        public static void BuildWebGL() => Build(BuildTarget.WebGL, ArgOr("-output", "Builds/WebGL"));

        [MenuItem("Drone Star/Build Windows", priority = 21)]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, ArgOr("-output", "Builds/Windows/DroneStarStudio.exe"));

        static void Build(BuildTarget target, string output)
        {
            ConfigurePlayer();
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = target,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary s = report.summary;
            Debug.Log(string.Format("[DroneStar] {0} build {1}: {2} errors, {3} warnings, {4:0.0} MB, {5:0} s → {6}",
                target, s.result, s.totalErrors, s.totalWarnings, s.totalSize / (1024.0 * 1024.0), s.totalTime.TotalSeconds, output));
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }

        static string ArgOr(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return fallback;
        }
    }
}
