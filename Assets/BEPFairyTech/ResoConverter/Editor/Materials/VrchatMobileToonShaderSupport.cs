#nullable enable
using System;
using UnityEngine;
using p = nadena.dev.ndmf.proto;

namespace BEPFairyTech.ResoConverter
{
    // SDK assemblies are optional; shader names and properties are sufficient.
    internal sealed class VrchatMobileToonShaderSupport : GenericShaderTranslator
    {
        public VrchatMobileToonShaderSupport(TextureAssetImporter textureImporter, Action<string>? warning = null,
            float toonShadowStrength = 0.5f) : base(textureImporter, warning, toonShadowStrength) { }

        internal static bool IsSupported(Shader? shader) => shader != null &&
            (shader.name == "VRChat/Mobile/Toon Standard" || shader.name == "VRChat/Mobile/Toon Standard (Outline)");

        public override bool TryTranslateMaterial(Material material, out p.Material? output)
        {
            output = null;
            if (material == null || !IsSupported(material.shader)) return false;
            if (!base.TryTranslateMaterial(material, out output) || output == null) return false;
            output.Category = p.MaterialCategory.Toon;
            output.MainColor = MaterialColor(material.GetColorSafe("_Color", Color.white));
            var culling = Mathf.RoundToInt(material.GetFloatSafe("_Culling", 2) ?? 2);
            output.CullMode = culling == 0 ? p.CullMode.None : culling == 1 ? p.CullMode.Front : p.CullMode.Back;
            var specular = material.IsKeywordEnabled("USE_SPECULAR");
            output.Metallic = specular ? Mathf.Clamp01(material.GetFloatSafe("_MetallicStrength", 0) ?? 0) : 0;
            output.Smoothness = specular ? Mathf.Clamp01(material.GetFloatSafe("_GlossStrength", .5f) ?? .5f) : 0;
            output.Reflectivity = specular ? Mathf.Clamp01(material.GetFloatSafe("_Reflectance", .5f) ?? .5f) : 0;
            output.SmoothnessMetallicReflectionMap = new p.AssetID { Id = 0 };
            var emission = material.GetColorSafe("_EmissionColor", Color.black);
            var strength = material.GetFloatSafe("_EmissionStrength", 1) ?? 1;
            output.EmissionColor = new Color(emission.r * strength, emission.g * strength, emission.b * strength, 1).ToRPC();
            output.MatcapColor = MaterialColor(Color.black);
            output.RimColor = MaterialColor(Color.black);
            output.RimIntensity = 0;
            if (material.IsKeywordEnabled("USE_RIMLIGHT"))
            {
                output.RimColor = MaterialColor(material.GetColorSafe("_RimColor", Color.white));
                output.RimIntensity = Mathf.Max(0, material.GetFloatSafe("_RimIntensity", .5f) ?? .5f);
                output.RimRange = Mathf.Clamp01(material.GetFloatSafe("_RimRange", .3f) ?? .3f);
                output.RimSharpness = Mathf.Max(.0001f, material.GetFloatSafe("_RimSharpness", .1f) ?? .1f);
                output.RimThreshold = 0;
            }
            output.Outline = p.ToonOutlineMode.ToonOutlineNone;
            output.OutlineMask = new p.AssetID { Id = 0 };
            TranslateRamp(material, output);
            Warn(material, "VRChat Mobile Toon Standard の基本色・影ランプ・発光・カリングをトゥーン材質に近似変換しました。元シェーダーの照明応答とは差があります。");
            foreach (var feature in new[] { "USE_MATCAP", "USE_DETAIL_MAPS", "USE_OCCLUSION_MAP", "USE_AUDIOLINK", "USE_HUE_SHIFT", "USE_COLOR_MASK" })
                if (material.IsKeywordEnabled(feature)) Warn(material, feature + " の専用効果は移植されません。");
            if (material.shader.name.EndsWith("(Outline)", StringComparison.Ordinal))
                Warn(material, "Mobile Toon Standard のアウトラインは省略します。");
            if (specular) Warn(material, "スペキュラーは強度のみ近似します。金属・光沢マップのチャンネル指定は省略します。");
            return true;
        }

        public override bool GetNormalMapTexture(Material material, out Texture? texture, out Texture? reference,
            out Vector2 scale, out Vector2 offset)
        {
            if (material.IsKeywordEnabled("USE_NORMAL_MAPS"))
                return base.GetNormalMapTexture(material, out texture, out reference, out scale, out offset);
            texture = reference = null; scale = Vector2.one; offset = Vector2.zero;
            return false;
        }

        private void TranslateRamp(Material material, p.Material output)
        {
            var source = material.GetTextureSafe("_Ramp") ?? Texture2D.whiteTexture;
            var shader = Shader.Find("Hidden/BEPFairyTech/ResoConverter/MobileToonRamp");
            Texture2D ramp;
            if (shader != null && shader.isSupported)
            {
                var baker = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                _tempObjects.Add(baker);
                baker.SetTexture("_MainTex", source);
                baker.SetTextureScale("_MainTex", material.GetTextureScaleSafe("_Ramp"));
                baker.SetTextureOffset("_MainTex", material.GetTextureOffsetSafe("_Ramp"));
                baker.SetColor("_Color", material.GetColorSafe("_Color", Color.white));
                baker.SetFloat("_ShadowAlbedo", material.GetFloatSafe("_ShadowAlbedo", .5f) ?? .5f);
                baker.SetFloat("_ShadowBoost", material.GetFloatSafe("_ShadowBoost", 0) ?? 0);
                baker.SetFloat("_Strength", toonShadowStrength);
                ramp = Bake(baker, source, 0, false, 128, 16);
            }
            else
            {
                // A white ramp avoids adding the backend exemplar's unrelated
                // dark ramp when headless/GPU baking is unavailable.
                ramp = new Texture2D(1, 1, TextureFormat.RGBA32, false, false);
                _tempObjects.Add(ramp); ramp.SetPixel(0, 0, Color.white); ramp.Apply();
                Warn(material, "影ランプのベイク用シェーダーを利用できないため、追加のトゥーン影色を省略します。");
            }
            ramp.name = material.name + "_MobileToonShadowRamp";
            ramp.wrapMode = TextureWrapMode.Clamp;
            if (textureImporter(ramp, null, out var id, out _)) output.ShadowRamp = id;
            output.ShadowRampMask = new p.AssetID { Id = 0 };
        }
    }
}
