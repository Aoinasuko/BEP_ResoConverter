using System;
using NUnit.Framework;
using UnityEngine;
using p = nadena.dev.ndmf.proto;
using Object = UnityEngine.Object;

namespace BEPFairyTech.ResoConverter.Tests
{
    public class MaterialShadowTests
    {
        private sealed class Probe : GenericShaderTranslator
        {
            public Probe() : base(Import) { }
            public Texture2D Render(Material material, Texture texture, int pass = 0) => Bake(material, texture, pass, false, 8, 4);
        }

        private static bool Import(Texture texture, Texture reference, out p.AssetID id, out p.Texture translated)
        {
            id = new p.AssetID { Id = texture != null ? 1u : 0u }; translated = new p.Texture(); return texture != null;
        }

        private static void RequireGpu()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Shadow ramp baking requires a graphics device.");
        }

        private static Color ReadLinear(Texture2D texture) => QualitySettings.activeColorSpace == ColorSpace.Linear
            ? texture.GetPixel(0, 0).linear : texture.GetPixel(0, 0);

        [TestCase(0f)]
        [TestCase(.5f)]
        [TestCase(1f)]
        public void ShadowRampShaderStrengthHasMeasuredGpuEffectWithoutOptionalPackages(float strength)
        {
            RequireGpu();
            var shader = Shader.Find("Hidden/BEPFairyTech/ResoConverter/ShadowRamp");
            Assert.That(shader != null && shader.isSupported, Is.True);
            var material = new Material(shader);
            try
            {
                material.SetFloat("_ShadowStrength", strength);
                material.SetColor("_ShadowColor", Color.black);
                material.SetColor("_Shadow2ndColor", Color.black);
                material.SetColor("_Shadow3rdColor", Color.black);
                material.SetColor("_ShadowBorderColor", Color.black);
                using (var probe = new Probe())
                    Assert.That(ReadLinear(probe.Render(material, Texture2D.whiteTexture)).r,
                        Is.EqualTo(1f - strength).Within(.015f));
            }
            finally { Object.DestroyImmediate(material); }
        }

        [TestCase(0f)]
        [TestCase(.5f)]
        [TestCase(1f)]
        public void MobileRampShaderRetainsShadowBoostAndAppliesStrengthWithoutOptionalPackages(float strength)
        {
            RequireGpu();
            var shader = Shader.Find("Hidden/BEPFairyTech/ResoConverter/MobileToonRamp");
            Assert.That(shader != null && shader.isSupported, Is.True);
            var material = new Material(shader);
            try
            {
                material.SetFloat("_Strength", strength);
                material.SetFloat("_ShadowAlbedo", .2f);
                material.SetFloat("_ShadowBoost", .1f);
                using (var probe = new Probe())
                    Assert.That(ReadLinear(probe.Render(material, Texture2D.blackTexture)).r,
                        Is.EqualTo(Mathf.Lerp(1, .28f, strength)).Within(.015f));
            }
            finally { Object.DestroyImmediate(material); }
        }

        [TestCase(0f)]
        [TestCase(.5f)]
        [TestCase(1f)]
        public void LiltoonShadowStrengthScalesExistingSettingWithoutChangingSource(float strength)
        {
            RequireGpu();
            var shader = Shader.Find("lilToon");
            if (shader == null) Assert.Ignore("Optional lilToon is not installed.");
            var material = new Material(shader);
            try
            {
                material.SetFloat("_UseShadow", 1); material.SetFloat("_ShadowStrength", .6f);
                material.SetColor("_ShadowColor", Color.black);
                material.SetColor("_Shadow2ndColor", Color.black);
                material.SetColor("_Shadow3rdColor", Color.black);
                material.SetColor("_ShadowBorderColor", Color.black);
                Color? baked = null;
                bool Capture(Texture texture, Texture reference, out p.AssetID id, out p.Texture translated)
                {
                    if (texture != null && texture.name.EndsWith("_ShadowRamp", StringComparison.Ordinal)) baked = ReadLinear((Texture2D)texture);
                    return Import(texture, reference, out id, out translated);
                }
                using (var converter = new LiltoonShaderSupport(Capture, _ => { }, strength))
                    Assert.That(converter.TryTranslateMaterial(material, out _), Is.True);
                Assert.That(baked.HasValue, Is.True);
                Assert.That(baked.Value.r, Is.EqualTo(1 - .6f * strength).Within(.015f));
                Assert.That(material.GetFloat("_ShadowStrength"), Is.EqualTo(.6f));
                Assert.That(material.GetColor("_ShadowColor"), Is.EqualTo(Color.black));
                Assert.That(material.shader, Is.EqualTo(shader));
            }
            finally { Object.DestroyImmediate(material); }
        }

        [TestCase(0f)]
        [TestCase(.5f)]
        [TestCase(1f)]
        public void MobileToonUsesToonCategoryAndDisabledFeaturesStayDisabled(float strength)
        {
            RequireGpu();
            var shader = Shader.Find("VRChat/Mobile/Toon Standard");
            if (shader == null) Assert.Ignore("Optional VRChat SDK is not installed.");
            var material = new Material(shader);
            try
            {
                material.SetTexture("_Ramp", Texture2D.blackTexture);
                material.SetFloat("_ShadowAlbedo", .2f); material.SetFloat("_ShadowBoost", .1f);
                material.SetFloat("_Culling", 0);
                material.DisableKeyword("USE_SPECULAR"); material.DisableKeyword("USE_NORMAL_MAPS");
                material.SetTexture("_BumpMap", Texture2D.whiteTexture);
                material.SetColor("_EmissionColor", new Color(.4f, .2f, .1f, 1)); material.SetFloat("_EmissionStrength", .5f);
                Color? baked = null;
                bool Capture(Texture texture, Texture reference, out p.AssetID id, out p.Texture translated)
                {
                    if (texture != null && texture.name.EndsWith("_MobileToonShadowRamp", StringComparison.Ordinal)) baked = ReadLinear((Texture2D)texture);
                    return Import(texture, reference, out id, out translated);
                }
                using (var converter = new VrchatMobileToonShaderSupport(Capture, _ => { }, strength))
                {
                    Assert.That(converter.TryTranslateMaterial(material, out var output), Is.True);
                    Assert.That(output.Category, Is.EqualTo(p.MaterialCategory.Toon));
                    Assert.That(output.CullMode, Is.EqualTo(p.CullMode.None));
                    Assert.That(output.NormalMap, Is.Null);
                    Assert.That(output.Metallic, Is.Zero); Assert.That(output.Smoothness, Is.Zero);
                    Assert.That(output.Reflectivity, Is.Zero);
                    Assert.That(output.EmissionColor.R, Is.EqualTo(.2f).Within(.001f));
                    Assert.That(output.ShadowRamp, Is.Not.Null);
                }
                Assert.That(baked.HasValue, Is.True);
                Assert.That(baked.Value.r, Is.EqualTo(Mathf.Lerp(1, .28f, strength)).Within(.015f));
                Assert.That(material.GetTexture("_Ramp"), Is.EqualTo(Texture2D.blackTexture));
                Assert.That(material.GetFloat("_ShadowAlbedo"), Is.EqualTo(.2f));
                Assert.That(material.GetFloat("_ShadowBoost"), Is.EqualTo(.1f));
                Assert.That(material.shader, Is.EqualTo(shader));
            }
            finally { Object.DestroyImmediate(material); }
        }

        [TestCase(0f, 1f)]
        [TestCase(.5f, .5f)]
        [TestCase(1f, 0f)]
        public void MultiplyOpacityBakesAlphaIntoWhiteInsteadOfOpaqueBlack(float alpha, float expected)
        {
            RequireGpu();
            var material = new Material(Shader.Find("Hidden/BEPFairyTech/ResoConverter/Bake"));
            var source = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            try
            {
                source.SetPixel(0, 0, new Color(0, 0, 0, alpha)); source.Apply();
                using (var probe = new Probe())
                {
                    var pixel = ReadLinear(probe.Render(material, source, 5));
                    Assert.That(pixel.r, Is.EqualTo(expected).Within(.015f));
                    Assert.That(pixel.a, Is.EqualTo(1).Within(.001f));
                }
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(material); }
        }

        [Test]
        public void MobileMultiplyRetainsUnlitMultiplyAndDoesNotWriteDepth()
        {
            var shader = Shader.Find("VRChat/Mobile/Particles/Multiply");
            if (shader == null) Assert.Ignore("Optional VRChat SDK is not installed.");
            var material = new Material(shader);
            try
            {
                using (var converter = new GenericShaderTranslator(Import, _ => { }))
                {
                    Assert.That(converter.TryTranslateMaterial(material, out var output), Is.True);
                    Assert.That(output.Category, Is.EqualTo(p.MaterialCategory.Unlit));
                    Assert.That(output.BlendMode, Is.EqualTo(p.BlendMode.Multiply));
                    Assert.That(output.CullMode, Is.EqualTo(p.CullMode.None));
                    Assert.That(output.ZWrite, Is.False);
                }
                Assert.That(material.shader, Is.EqualTo(shader));
            }
            finally { Object.DestroyImmediate(material); }
        }
    }
}
