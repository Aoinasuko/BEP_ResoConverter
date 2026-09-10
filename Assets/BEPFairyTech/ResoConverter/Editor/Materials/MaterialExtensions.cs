// Adapted from Modular Avatar Resonite. Copyright (c) 2025 bd_. MIT license; see COPYING.md.
#nullable enable
using UnityEngine;

namespace BEPFairyTech.ResoConverter
{
    internal static class MaterialExtensions
    {
        public static Texture? GetTextureSafe(this Material mat, string name) =>
            mat.HasProperty(name) ? mat.GetTexture(name) : null;
        public static Vector2 GetTextureScaleSafe(this Material mat, string name) =>
            mat.HasProperty(name) ? mat.GetTextureScale(name) : Vector2.one;
        public static Vector2 GetTextureOffsetSafe(this Material mat, string name) =>
            mat.HasProperty(name) ? mat.GetTextureOffset(name) : Vector2.zero;
        public static Color GetColorSafe(this Material mat, string name, Color fallback) =>
            mat.HasProperty(name) ? mat.GetColor(name) : fallback;
        public static float? GetFloatSafe(this Material mat, string name, float? fallback = null) =>
            mat.HasProperty(name) ? mat.GetFloat(name) : fallback;
        public static Vector4 GetVectorSafe(this Material mat, string name, Vector4 fallback) =>
            mat.HasProperty(name) ? mat.GetVector(name) : fallback;
    }
}
