using DroneStar.Core;
using UnityEngine;

namespace DroneStar.App
{
    /// <summary>Bridges the engine-free core types to Unity types.</summary>
    public static class ShowConvert
    {
        public static Vector3 ToUnity(this System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);

        public static System.Numerics.Vector3 ToNumerics(this Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);

        /// <summary>LED colour for rendering: converted to the project's working colour space.</summary>
        public static Color ToRender(this LedColor c)
        {
            var color = new Color(c.R, c.G, c.B, 1f);
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
        }

        /// <summary>LED colour for UI swatches (sRGB, as the designer picked it).</summary>
        public static Color ToDisplay(this LedColor c) => new Color(c.R, c.G, c.B, 1f);

        public static LedColor ToLed(this Color c) => new LedColor(c.r, c.g, c.b);
    }
}
