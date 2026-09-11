// Adapted from Modular Avatar Resonite. Copyright (c) 2025 bd_. MIT license; see COPYING.md.
#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using p = nadena.dev.ndmf.proto;

namespace BEPFairyTech.ResoConverter
{
    internal class GenericShaderTranslator : IShaderTranslator
    {
        protected readonly TextureAssetImporter textureImporter;
        protected readonly float toonShadowStrength;
        protected readonly List<UnityEngine.Object> _tempObjects = new List<UnityEngine.Object>();
        private readonly Action<string>? warning;
        private readonly HashSet<string> emittedWarnings = new HashSet<string>();

        public GenericShaderTranslator(TextureAssetImporter textureImporter, Action<string>? warning = null, float toonShadowStrength = 0.5f)
        {
            this.textureImporter = textureImporter;
            this.warning = warning;
            this.toonShadowStrength = float.IsNaN(toonShadowStrength) || float.IsInfinity(toonShadowStrength) ? 0.5f : Mathf.Clamp01(toonShadowStrength);
        }

        public void Dispose()
        {
            foreach (var obj in _tempObjects) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            _tempObjects.Clear();
        }

        protected void Warn(Material mat, string message)
        {
            var text = "マテリアル「" + mat.name + "」: " + message;
            if (!emittedWarnings.Add(text)) return;
            if (warning != null) warning(text); else Debug.LogWarning("[ResoConverter] " + text);
        }

        protected virtual bool GetOrBakeMainTexture(Material mat, out Texture? texture,
            out Texture? reference, out Vector2 scale, out Vector2 offset)
        {
            var property = mat.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            texture = reference = mat.GetTextureSafe(property);
            scale = mat.GetTextureScaleSafe(property);
            offset = mat.GetTextureOffsetSafe(property);
            if (texture != null && IsMobileMultiply(mat.shader))
            {
                var baker = NewBakeMaterial(mat);
                if (baker != null) texture = Bake(baker, texture, 5);
            }
            return texture != null;
        }

        protected virtual bool GetOrBakeEmissionTexture(Material mat, out Texture? texture,
            out Texture? reference, out Vector2 scale, out Vector2 offset)
        {
            texture = reference = mat.GetTextureSafe("_EmissionMap");
            scale = mat.GetTextureScaleSafe("_EmissionMap");
            offset = mat.GetTextureOffsetSafe("_EmissionMap");
            return texture != null;
        }

        protected virtual bool GetMatcapTexture(Material mat, out Texture? texture, out Texture? reference)
        {
            texture = reference = mat.GetTextureSafe("_MatCapTex");
            return texture != null;
        }

        public virtual bool GetNormalMapTexture(Material mat, out Texture? texture,
            out Texture? reference, out Vector2 scale, out Vector2 offset)
        {
            texture = reference = mat.GetTextureSafe("_BumpMap");
            scale = mat.GetTextureScaleSafe("_BumpMap");
            offset = mat.GetTextureOffsetSafe("_BumpMap");
            var strength = mat.GetFloatSafe("_BumpScale", 1f) ?? 1f;
            if (texture != null && !Mathf.Approximately(strength, 1f))
            {
                var bake = NewBakeMaterial(mat);
                if (bake != null)
                {
                    bake.SetTexture("_MainTex", texture);
                    bake.SetFloat("_NormalScale", strength);
                    var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
                    bake.SetFloat("_NormalPacked", importer != null && importer.textureType == TextureImporterType.NormalMap ? 1 : 0);
                    texture = Bake(bake, texture, 2, true);
                }
            }
            return texture != null;
        }

        public virtual bool TryTranslateMaterial(Material material, out p.Material? protoMat)
        {
            protoMat = new p.Material();
            if (material == null) return false;
            if (material.shader == null || material.shader.name == "Hidden/InternalErrorShader")
                Warn(material, "元のシェーダーがありません。読み取れる基本色とテクスチャのみ変換します。");

            if (GetOrBakeMainTexture(material, out var texture, out var reference, out var scale, out var offset))
            {
                if (textureImporter(texture, reference, out var id, out _)) protoMat.MainTexture = id;
                protoMat.MainTextureScaleOffset = ScaleOffset(scale, offset);
            }
            if (GetOrBakeEmissionTexture(material, out texture, out reference, out scale, out offset))
            {
                if (textureImporter(texture, reference, out var id, out _)) protoMat.EmissionMap = id;
                protoMat.EmissionMapScaleOffset = ScaleOffset(scale, offset);
            }
            if (GetMatcapTexture(material, out texture, out reference) && textureImporter(texture, reference, out var matcapId, out _))
                protoMat.MatcapTexture = matcapId;
            if (GetNormalMapTexture(material, out texture, out reference, out scale, out offset))
            {
                if (textureImporter(texture, reference, out var id, out var translated))
                {
                    protoMat.NormalMap = id;
                    if (translated != null) translated.IsNormalMap = true;
                }
                protoMat.NormalMapScaleOffset = ScaleOffset(scale, offset);
            }

            protoMat.MainColor = MaterialColor(material.GetColorSafe(material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color", Color.white));
            protoMat.EmissionColor = material.GetColorSafe("_EmissionColor", Color.black).ToRPC();
            protoMat.MatcapColor = MaterialColor(material.GetColorSafe("_MatCapColor", Color.white));
            protoMat.AlphaClip = Mathf.Clamp01(material.GetFloatSafe("_Cutoff", 0.5f) ?? 0.5f);
            protoMat.Smoothness = Mathf.Clamp01(material.GetFloatSafe("_Smoothness", material.GetFloatSafe("_Glossiness", 0.5f)) ?? 0.5f);
            protoMat.Metallic = Mathf.Clamp01(material.GetFloatSafe("_Metallic", 0f) ?? 0f);
            protoMat.Reflectivity = 0.5f;
            var metallicMap = material.GetTextureSafe("_MetallicGlossMap");
            if (metallicMap != null && textureImporter(metallicMap, metallicMap, out var metallicId, out _))
                protoMat.SmoothnessMetallicReflectionMap = metallicId;

            protoMat.BlendMode = DetermineBlend(material);
            protoMat.CullMode = DetermineCull(material);
            protoMat.ZWrite = (material.GetFloatSafe("_ZWrite", protoMat.BlendMode == p.BlendMode.Opaque || protoMat.BlendMode == p.BlendMode.Cutout ? 1 : 0) ?? 0) > 0.5f;
            protoMat.UnityRenderQueue = material.renderQueue;
            var shaderName = material.shader != null ? material.shader.name : "";
            protoMat.Category = shaderName.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0 ? p.MaterialCategory.Unlit : p.MaterialCategory.Pbr;
            if (IsMobileMultiply(material.shader))
            {
                protoMat.Category = p.MaterialCategory.Unlit;
                protoMat.BlendMode = p.BlendMode.Multiply;
                protoMat.CullMode = p.CullMode.None;
                protoMat.ZWrite = false;
                Warn(material, "Mobile Multiply を非ライティングの乗算材質に変換しました。テクスチャの透明度は白との補間にベイクします。頂点カラーを使う場合は合成結果に差があります。");
            }
            else if (!(this is LiltoonShaderSupport) && !(this is VrchatMobileToonShaderSupport) && shaderName != "Standard" && !shaderName.StartsWith("Unlit/", StringComparison.Ordinal) && !shaderName.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal))
                Warn(material, "このシェーダーは基本プロパティから近似変換します。独自の描画効果は移植されません。");
            return true;
        }

        internal static bool IsMobileMultiply(Shader? shader) => shader != null && shader.name == "VRChat/Mobile/Particles/Multiply";

        internal static p.BlendMode DetermineBlend(Material mat)
        {
            var name = (mat.shader != null ? mat.shader.name : "") + " " + mat.GetTag("VRCFallback", false) + " " + mat.GetTag("RenderType", false);
            if (name.IndexOf("Cutout", StringComparison.OrdinalIgnoreCase) >= 0 || mat.IsKeywordEnabled("_ALPHATEST_ON") || (mat.GetFloatSafe("_AlphaClip", 0f) ?? 0f) > 0.5f)
                return p.BlendMode.Cutout;
            if (mat.HasProperty("_Mode"))
            {
                switch (Mathf.RoundToInt(mat.GetFloat("_Mode")))
                {
                    case 1: return p.BlendMode.Cutout;
                    case 2: return p.BlendMode.Fade;
                    case 3: return p.BlendMode.Transparent;
                }
            }
            if ((mat.GetFloatSafe("_TransparentMode", 0) ?? 0) == 1) return p.BlendMode.Cutout;
            if ((mat.GetFloatSafe("_DstBlend", 0) ?? 0) == 1 && (mat.GetFloatSafe("_SrcBlend", 1) ?? 1) == 5) return p.BlendMode.Additive;
            if (name.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) >= 0 || (mat.GetFloatSafe("_Surface", 0) ?? 0) > 0 || (mat.GetFloatSafe("_TransparentMode", 0) ?? 0) > 1)
                return p.BlendMode.Alpha;
            if (name.IndexOf("Fade", StringComparison.OrdinalIgnoreCase) >= 0) return p.BlendMode.Fade;
            return p.BlendMode.Opaque;
        }

        internal static p.CullMode DetermineCull(Material mat)
        {
            var cull = mat.GetFloatSafe("_Cull");
            if (cull.HasValue) return Mathf.RoundToInt(cull.Value) == 0 ? p.CullMode.None : Mathf.RoundToInt(cull.Value) == 1 ? p.CullMode.Front : p.CullMode.Back;
            return mat.GetTag("VRCFallback", false).Contains("DoubleSided") ? p.CullMode.None : p.CullMode.Back;
        }

        protected static p.Color MaterialColor(Color color)
        {
            var result = color.ToRPC();
            result.Profile = p.ColorProfile.SRgb;
            return result;
        }

        protected static p.ScaleOffset ScaleOffset(Vector2 scale, Vector2 offset) =>
            new p.ScaleOffset { Scale = scale.ToRPC(), Offset = offset.ToRPC() };

        protected Material? NewBakeMaterial(Material source)
        {
            var shader = Shader.Find("Hidden/BEPFairyTech/ResoConverter/Bake");
            if (shader == null || !shader.isSupported)
            {
                Warn(source, "ベイク用シェーダーを利用できません。テクスチャ合成を省略します。");
                return null;
            }
            var result = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _tempObjects.Add(result);
            return result;
        }

        protected static void CopyTexture(Material source, Material target, string from, string to, Texture? fallback = null)
        {
            target.SetTexture(to, source.GetTextureSafe(from) ?? fallback ?? Texture2D.whiteTexture);
            target.SetTextureScale(to, source.GetTextureScaleSafe(from));
            target.SetTextureOffset(to, source.GetTextureOffsetSafe(from));
        }

        protected Texture2D Bake(Material material, Texture? reference, int pass = 0, bool linear = false, int width = 0, int height = 0)
        {
            width = Mathf.Clamp(width > 0 ? width : reference != null ? reference.width : 4, 1, SystemInfo.maxTextureSize);
            height = Mathf.Clamp(height > 0 ? height : reference != null ? reference.height : 4, 1, SystemInfo.maxTextureSize);
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var previousWrite = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = !linear && QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(reference, rt, material, pass);
                RenderTexture.active = rt;
                var output = new Texture2D(width, height, TextureFormat.RGBA32, false, linear)
                {
                    name = (reference != null ? reference.name : material.name) + "_ResoBaked",
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = reference != null ? reference.wrapMode : TextureWrapMode.Repeat
                };
                _tempObjects.Add(output);
                output.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                output.Apply();
                return output;
            }
            finally
            {
                GL.sRGBWrite = previousWrite;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
