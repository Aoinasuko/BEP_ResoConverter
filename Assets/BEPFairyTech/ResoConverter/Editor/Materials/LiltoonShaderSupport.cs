// Adapted from Modular Avatar Resonite and lilToon texture baking.
// Copyright (c) 2020-present lilxyzw; Copyright (c) 2025 bd_. MIT license; see COPYING.md.
#nullable enable
using System;
using UnityEditor;
using UnityEngine;
using p = nadena.dev.ndmf.proto;

namespace BEPFairyTech.ResoConverter
{
    // Detection and property access deliberately use strings: installing lilToon is optional.
    internal sealed class LiltoonShaderSupport : GenericShaderTranslator
    {
        public LiltoonShaderSupport(TextureAssetImporter textureImporter, Action<string>? warning = null, float toonShadowStrength = 0.5f)
            : base(textureImporter, warning, toonShadowStrength) { }

        internal static bool IsLiltoonShader(Shader? shader) => shader != null &&
            (shader.name.IndexOf("lilToon", StringComparison.OrdinalIgnoreCase) >= 0 || shader.name.StartsWith("Hidden/lts", StringComparison.Ordinal));

        public override bool TryTranslateMaterial(Material material, out p.Material? protoMat)
        {
            protoMat = null;
            if (material == null || !IsLiltoonShader(material.shader)) return false;
            if (material.shader.name.Contains("FakeShadow"))
            {
                protoMat = new p.Material { Category = p.MaterialCategory.FakeShadow };
                return true;
            }
            var copy = new Material(material) { hideFlags = HideFlags.HideAndDontSave, name = material.name };
            _tempObjects.Add(copy);
            if (!base.TryTranslateMaterial(copy, out protoMat) || protoMat == null) return false;
            protoMat.Category = p.MaterialCategory.Toon;
            // lilToon also declares a hidden _BaseColor for inspector compatibility; _Color is authoritative.
            protoMat.MainColor = MaterialColor(copy.GetColorSafe("_Color", Color.white));
            protoMat.Metallic = Enabled(copy, "_UseReflection") ? copy.GetFloatSafe("_Metallic", 0) ?? 0 : 0;
            protoMat.Smoothness = Enabled(copy, "_UseReflection") && Enabled(copy, "_ApplyReflection", true)
                ? copy.GetFloatSafe("_Smoothness", 0) ?? 0 : 0;
            protoMat.Reflectivity = copy.GetFloatSafe("_Reflectance", 0.04f) ?? 0.04f;
            // Disabled feature blocks can still contain configured textures and colors in lilToon.
            var emission = copy.GetColorSafe("_EmissionColor", Color.black);
            emission = new Color(emission.r * emission.a, emission.g * emission.a, emission.b * emission.a, 1);
            protoMat.EmissionColor = Enabled(copy, "_UseEmission") ? emission.ToRPC() : Color.black.ToRPC();
            if (!Enabled(copy, "_UseMatCap")) protoMat.MatcapColor = Color.black.ToRPC();
            if (Enabled(copy, "_Invisible"))
            {
                protoMat.MainColor.A = 0;
                protoMat.BlendMode = p.BlendMode.Alpha;
            }
            BakeMetallicMap(copy, protoMat);
            TranslateShadowSettings(copy, protoMat);
            TranslateOutlineSettings(copy, protoMat);
            TranslateRimSettings(copy, protoMat);
            ReportUnsupported(copy);
            return true;
        }

        private static bool Enabled(Material mat, string property, bool fallback = false) =>
            (mat.GetFloatSafe(property, fallback ? 1f : 0f) ?? 0) > 0.5f;

        private static bool HasNonDefaultTexture(Material mat, string property)
        {
            var texture = mat.GetTextureSafe(property);
            if (texture == null || texture == Texture2D.whiteTexture || texture == Texture2D.grayTexture || texture == Texture2D.blackTexture) return false;
            var path = AssetDatabase.GetAssetPath(texture);
            return !path.StartsWith("Resources/unity_builtin", StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith("Library/unity default resources", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasOutline(Material mat)
        {
            var name = mat.shader.name;
            return name.IndexOf("Multi", StringComparison.OrdinalIgnoreCase) >= 0
                ? Enabled(mat, "_UseOutline") : name.IndexOf("Outline", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        protected override bool GetOrBakeMainTexture(Material mat, out Texture? texture,
            out Texture? reference, out Vector2 scale, out Vector2 offset)
        {
            texture = reference = mat.GetTextureSafe("_MainTex");
            scale = mat.GetTextureScaleSafe("_MainTex");
            offset = mat.GetTextureOffsetSafe("_MainTex");
            var hsvg = mat.GetVectorSafe("_MainTexHSVG", new Vector4(0, 1, 1, 1));
            var requiresBake = hsvg != new Vector4(0, 1, 1, 1) || Enabled(mat, "_UseMain2ndTex") || Enabled(mat, "_UseMain3rdTex") || (mat.GetFloatSafe("_MainGradationStrength", 0) ?? 0) != 0;
            if (requiresBake)
            {
                var shader = Shader.Find("Hidden/ltsother_baker");
                if (shader != null && shader.isSupported)
                {
                    var baker = new Material(mat) { shader = shader, hideFlags = HideFlags.HideAndDontSave };
                    _tempObjects.Add(baker);
                    baker.shaderKeywords = Array.Empty<string>();
                    // The lilToon baker works in the main texture's UV space. Keep the outer ST once.
                    baker.SetTexture("_MainTex", texture != null ? texture : Texture2D.whiteTexture);
                    var resolution = texture;
                    foreach (var property in new[] { "_Main2ndTex", "_Main3rdTex", "_Main2ndBlendMask", "_Main3rdBlendMask" })
                    {
                        var candidate = mat.GetTextureSafe(property);
                        if (candidate != null && (resolution == null || candidate.width * (long)candidate.height > resolution.width * (long)resolution.height)) resolution = candidate;
                    }
                    texture = Bake(baker, texture ?? Texture2D.whiteTexture, 0, false,
                        resolution != null ? resolution.width : 4, resolution != null ? resolution.height : 4);
                    // _Color is already included by the baker, including the order of overlay layers.
                    mat.SetColor("_Color", Color.white);
                }
                else Warn(mat, "lilToon のテクスチャベイカーがありません。色調補正と第2・第3レイヤーは省略します。");
            }
            var alphaMode = Mathf.RoundToInt(mat.GetFloatSafe("_AlphaMaskMode", 0) ?? 0);
            var alphaMask = mat.GetTextureSafe("_AlphaMask");
            if (alphaMode > 0 && alphaMask != null)
            {
                var baker = NewBakeMaterial(mat);
                if (baker != null)
                {
                    baker.SetTexture("_MainTex", texture ?? Texture2D.whiteTexture);
                    CopyTexture(mat, baker, "_AlphaMask", "_Mask");
                    baker.SetFloat("_AlphaMode", alphaMode);
                    baker.SetFloat("_AlphaScale", mat.GetFloatSafe("_AlphaMaskScale", 1) ?? 1);
                    baker.SetFloat("_AlphaValue", mat.GetFloatSafe("_AlphaMaskValue", 0) ?? 0);
                    var resolution = texture ?? alphaMask;
                    texture = Bake(baker, texture ?? Texture2D.whiteTexture, 0, false, resolution.width, resolution.height);
                }
            }
            return texture != null;
        }

        protected override bool GetOrBakeEmissionTexture(Material mat, out Texture? texture,
            out Texture? reference, out Vector2 scale, out Vector2 offset)
        {
            texture = reference = null;
            scale = Vector2.one;
            offset = Vector2.zero;
            if (!Enabled(mat, "_UseEmission")) return false;
            reference = mat.GetTextureSafe("_EmissionMap");
            var mask = mat.GetTextureSafe("_EmissionBlendMask");
            var baker = NewBakeMaterial(mat);
            if (baker == null) return base.GetOrBakeEmissionTexture(mat, out texture, out reference, out scale, out offset);
            // lilToon multiplies emission by its alpha and the blend mask. Bake all UV transforms once.
            CopyTexture(mat, baker, "_EmissionMap", "_MainTex");
            CopyTexture(mat, baker, "_EmissionBlendMask", "_Mask");
            baker.SetFloat("_EmissionBlend", mat.GetFloatSafe("_EmissionBlend", 1) ?? 1);
            var resolution = reference ?? mask;
            texture = Bake(baker, reference ?? Texture2D.whiteTexture, 1, false,
                resolution != null ? resolution.width : 4, resolution != null ? resolution.height : 4);
            return true;
        }

        protected override bool GetMatcapTexture(Material mat, out Texture? texture, out Texture? reference)
        {
            texture = reference = null;
            if (!Enabled(mat, "_UseMatCap")) return false;
            if (HasNonDefaultTexture(mat, "_MatCapBlendMask"))
            {
                Warn(mat, "マスク付き MatCap は XiexeToon で同じ合成ができないため省略します。");
                return false;
            }
            reference = mat.GetTextureSafe("_MatCapTex");
            if (reference == null) return false;
            var baker = NewBakeMaterial(mat);
            if (baker == null) { texture = reference; return true; }
            baker.SetColor("_Tint", mat.GetColorSafe("_MatCapColor", Color.white) * (mat.GetFloatSafe("_MatCapBlend", 1) ?? 1));
            texture = Bake(baker, reference, 4);
            mat.SetColor("_MatCapColor", Color.white);
            return true;
        }

        public override bool GetNormalMapTexture(Material mat, out Texture? texture,
            out Texture? reference, out Vector2 scale, out Vector2 offset)
        {
            if (Enabled(mat, "_UseBumpMap")) return base.GetNormalMapTexture(mat, out texture, out reference, out scale, out offset);
            texture = reference = null;
            scale = Vector2.one;
            offset = Vector2.zero;
            return false;
        }

        private void BakeMetallicMap(Material mat, p.Material output)
        {
            output.SmoothnessMetallicReflectionMap = new p.AssetID { Id = 0 };
            if (!Enabled(mat, "_UseReflection")) return;
            var smooth = mat.GetTextureSafe("_SmoothnessTex");
            var metal = mat.GetTextureSafe("_MetallicGlossMap");
            var reflection = mat.GetTextureSafe("_ReflectionColorTex");
            var baker = NewBakeMaterial(mat);
            if (baker == null) return;
            CopyTexture(mat, baker, "_SmoothnessTex", "_Smoothness");
            CopyTexture(mat, baker, "_MetallicGlossMap", "_Metallic");
            CopyTexture(mat, baker, "_ReflectionColorTex", "_Reflection");
            baker.SetColor("_ReflectionColor", mat.GetColorSafe("_ReflectionColor", Color.white));
            var baked = Bake(baker, smooth ?? metal ?? reflection, 3, true);
            if (textureImporter(baked, null, out var id, out _)) output.SmoothnessMetallicReflectionMap = id;
        }

        private void TranslateShadowSettings(Material mat, p.Material output)
        {
            Texture2D ramp;
            var mask = Enabled(mat, "_UseShadow") ? mat.GetTextureSafe("_ShadowStrengthMask") : null;
            var shader = Shader.Find("Hidden/BEPFairyTech/ResoConverter/ShadowRamp");
            if (Enabled(mat, "_UseShadow") && shader != null && shader.isSupported)
            {
                var baker = new Material(mat) { shader = shader, hideFlags = HideFlags.HideAndDontSave };
                _tempObjects.Add(baker);
                baker.SetFloat("_ShadowStrength", (mat.GetFloatSafe("_ShadowStrength", 1) ?? 1) * toonShadowStrength);
                ramp = Bake(baker, null, 0, false, 128, 16);
            }
            else
            {
                if (Enabled(mat, "_UseShadow")) Warn(mat, "トゥーン影のベイク用シェーダーを利用できません。影色を省略します。");
                ramp = new Texture2D(128, 16, TextureFormat.RGBA32, false, false);
                _tempObjects.Add(ramp);
                var pixels = new Color[128 * 16];
                for (var i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
                ramp.SetPixels(pixels);
                ramp.Apply();
            }
            if (mask != null)
            {
                var row = ramp.GetPixels32();
                var pixels = new Color[128 * 128];
                for (var y = 0; y < 128; y++)
                    for (var x = 0; x < 128; x++) pixels[y * 128 + x] = Color.Lerp(Color.white, row[x], y / 127f);
                ramp = new Texture2D(128, 128, TextureFormat.RGBA32, false, false);
                _tempObjects.Add(ramp);
                ramp.SetPixels(pixels);
                ramp.Apply();
            }
            ramp.name = mat.name + "_ShadowRamp";
            ramp.wrapMode = TextureWrapMode.Clamp;
            if (textureImporter(ramp, null, out var rampId, out _)) output.ShadowRamp = rampId;
            // AssetID 0 prevents the backend's exemplar material from adding its own mask.
            output.ShadowRampMask = new p.AssetID { Id = 0 };
            if (mask != null && textureImporter(mask, mask, out var maskId, out _)) output.ShadowRampMask = maskId;
        }

        private void TranslateOutlineSettings(Material mat, p.Material output)
        {
            var enabled = HasOutline(mat);
            output.Outline = enabled ? p.ToonOutlineMode.ToonOutlineLit : p.ToonOutlineMode.ToonOutlineNone;
            output.OutlineMask = new p.AssetID { Id = 0 };
            if (!enabled) return;
            output.OutlineColor = MaterialColor(mat.GetColorSafe("_OutlineColor", Color.black));
            output.OutlineWidth = mat.GetFloatSafe("_OutlineWidth", 0) ?? 0;
            var mask = mat.GetTextureSafe("_OutlineWidthMask");
            if (mask != null && textureImporter(mask, mask, out var id, out _)) output.OutlineMask = id;
        }

        private void TranslateRimSettings(Material mat, p.Material output)
        {
            output.RimColor = MaterialColor(Color.black);
            output.RimIntensity = 0;
            if (!Enabled(mat, "_UseRim")) return;
            var color = mat.GetColorSafe("_RimColor", Color.white);
            output.RimIntensity = Mathf.Max(0, color.a);
            output.RimColor = MaterialColor(new Color(color.r, color.g, color.b, 1));
            var border = Mathf.Clamp01(mat.GetFloatSafe("_RimBorder", 0.5f) ?? 0.5f);
            var blur = Mathf.Clamp01(mat.GetFloatSafe("_RimBlur", 0.1f) ?? 0.1f);
            var power = Mathf.Max(0.001f, mat.GetFloatSafe("_RimFresnelPower", 3f) ?? 3f);
            // lilToon applies its border to pow(1 - N.V, power); XSToon applies smoothstep
            // directly to 1 - N.V. Invert the power at both edges to preserve the rim's width.
            var edge0 = Mathf.Pow(Mathf.Clamp01(border - blur * 0.5f), 1f / power);
            var edge1 = Mathf.Pow(Mathf.Clamp01(border + blur * 0.5f), 1f / power);
            output.RimRange = (edge0 + edge1) * 0.5f;
            output.RimSharpness = Mathf.Max(0.0001f, (edge1 - edge0) * 0.5f);
            output.RimThreshold = 0; // Prevent an additional N.L exponent from changing its width.
            Warn(mat, "リムライトの色・強さ・幅を近似変換しました。照明への応答とマスク・方向別の効果は異なります。");
        }

        private void ReportUnsupported(Material mat)
        {
            foreach (var feature in new[] { "_UseEmission2nd", "_UseBump2ndMap", "_UseMatCap2nd", "_UseRimShade", "_UseBacklight", "_UseGlitter", "_UseAnisotropy", "_UseParallax", "_UseAudioLink" })
                if (Enabled(mat, feature)) Warn(mat, feature + " は XiexeToon への移植対象外です。");
            foreach (var property in new[] { "_ShadowColorTex", "_Shadow2ndColorTex", "_Shadow3rdColorTex", "_ShadowBorderMask", "_ShadowBlurMask", "_OutlineTex" })
                if ((property == "_OutlineTex" ? HasOutline(mat) : Enabled(mat, "_UseShadow")) && HasNonDefaultTexture(mat, property))
                    Warn(mat, property + " による位置ごとの色・影の変化は省略します。");
            foreach (var property in new[] { "_MainTex_ScrollRotate", "_EmissionMap_ScrollRotate", "_EmissionBlink" })
            {
                if (property.StartsWith("_Emission", StringComparison.Ordinal) && !Enabled(mat, "_UseEmission")) continue;
                var value = mat.GetVectorSafe(property, Vector4.zero);
                var animated = property == "_EmissionBlink" ? value.x != 0 : value.x != 0 || value.y != 0 || value.w != 0;
                if (animated) Warn(mat, property + " のアニメーションは静止状態に変換します。");
            }
            if (mat.GetVectorSafe("_DissolveParams", Vector4.zero).x != 0 || (mat.GetFloatSafe("_StencilComp", 8) ?? 8) != 8 || (mat.GetFloatSafe("_StencilPass", 0) ?? 0) != 0)
                Warn(mat, "ディゾルブ・ステンシルによる表示制御は再現されません。");
            if (mat.shader.name.Contains("Fur") || mat.shader.name.Contains("Gem") || mat.shader.name.Contains("Refraction"))
                Warn(mat, "ファー・宝石・屈折シェーダーの専用描画は基本的なトゥーン描画に近似します。");
            if (Enabled(mat, "_UseMain2ndTex") || Enabled(mat, "_UseMain3rdTex"))
                Warn(mat, "第2・第3レイヤーは静止テクスチャに合成します。UV1以降・左右判定・レイヤーのアニメーションは再現されません。");
            if (Enabled(mat, "_UseEmission") && ((mat.GetFloatSafe("_EmissionMap_UVMode", 0) ?? 0) != 0 || (mat.GetFloatSafe("_EmissionMainStrength", 0) ?? 0) != 0 || Enabled(mat, "_EmissionUseGrad")))
                Warn(mat, "発光の UV 切替・メイン色への追従・グラデーションは省略します。");
            if (Enabled(mat, "_UseEmission") && (mat.GetFloatSafe("_EmissionBlendMode", 1) ?? 1) != 1)
                Warn(mat, "発光は加算発光に近似します。元の合成モードとは見え方が異なります。");
        }
    }
}
