using System;
using System.IO;
using DroneStar.Core;

namespace DroneStar.Tests
{
    /// <summary>Loads the model shape pack once for every test in this namespace.</summary>
    [NUnit.Framework.SetUpFixture]
    public class CoreTestSetup
    {
        [NUnit.Framework.OneTimeSetUp]
        public void LoadShapeLibrary() => TestData.EnsureShapeLibrary();
    }

    /// <summary>Locates the model shape pack in the repository (works from the Unity project and dotnet bin folders).</summary>
    public static class TestData
    {
        const string PackPath = "Assets/DroneStar/Data/ShapePack.bytes";

        /// <summary>
        /// A compiler holding the shipped pre-solved flagship transitions, as the app has one. Tests that compile
        /// the 8,192-drone flagship use it: solving its auctions from scratch takes minutes.
        /// </summary>
        public static ShowCompiler BakedCompiler()
        {
            var compiler = new ShowCompiler();
            compiler.ImportCache(File.ReadAllBytes(Find("Assets/DroneStar/Data/DemoAssignments.bytes")));
            return compiler;
        }

        /// <summary>The validator's time step the app uses for a fleet of this size.</summary>
        public static float ValidationStep(int drones) => drones > 1000 ? 0.1f : SafetyValidator.DefaultStep;

        public static void EnsureShapeLibrary()
        {
            if (ShapeLibrary.IsLoaded) return;
            ShapeLibrary.LoadPack(File.ReadAllBytes(Find(PackPath)));
        }

        public static string Find(string relative)
        {
            string dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, relative);
                if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            dir = AppContext.BaseDirectory;
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, relative);
                if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            throw new FileNotFoundException("Could not find " + relative);
        }
    }
}
