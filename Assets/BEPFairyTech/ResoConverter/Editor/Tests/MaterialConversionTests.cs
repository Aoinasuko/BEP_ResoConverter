using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using p = nadena.dev.ndmf.proto;

namespace BEPFairyTech.ResoConverter.Tests
{
    public class MaterialConversionTests
    {
        private static bool Import(Texture texture, Texture reference, out p.AssetID id, out p.Texture translated)
        {
            id = new p.AssetID { Id = texture != null ? 1u : 0u };
            translated = new p.Texture();
            return texture != null;
        }

        [TestCase(0, p.BlendMode.Opaque)]
        [TestCase(1, p.BlendMode.Cutout)]
        [TestCase(2, p.BlendMode.Fade)]
        [TestCase(3, p.BlendMode.Transparent)]
        public void StandardRenderingModesDoNotRequireOptionalPackages(int mode, p.BlendMode expected)
        {
            var material = new Material(Shader.Find("Standard"));
            try
            {
                material.SetFloat("_Mode", mode);
                using (var translator = new GenericShaderTranslator(Import))
                {
                    Assert.That(translator.TryTranslateMaterial(material, out var result), Is.True);
                    Assert.That(result.BlendMode, Is.EqualTo(expected));
                }
            }
            finally { Object.DestroyImmediate(material); }
        }

        [Test]
        public void LiltoonRetainsSourceColorAndDisabledFeatureBlocksStayDisabled()
        {
            var shader = Shader.Find("lilToon");
            if (shader == null) Assert.Ignore("Optional lilToon is not installed.");
            var material = new Material(shader);
            var original = new Color(0.23f, 0.47f, 0.81f, 0.65f);
            material.SetColor("_Color", original);
            // Material color storage may round-trip a channel through Unity's color-space conversion.
            var storedBefore = material.GetColor("_Color");
            material.SetFloat("_UseEmission", 0);
            material.SetColor("_EmissionColor", Color.white);
            material.SetFloat("_UseBumpMap", 0);
            material.SetTexture("_BumpMap", Texture2D.whiteTexture);
            var warnings = new List<string>();
            try
            {
                using (var translator = new LiltoonShaderSupport(Import, warnings.Add))
                {
                    Assert.That(translator.TryTranslateMaterial(material, out var result), Is.True);
                    Assert.That(result.MainColor.R, Is.EqualTo(original.r).Within(0.001f));
                    Assert.That(result.MainColor.A, Is.EqualTo(original.a).Within(0.001f));
                    Assert.That(result.EmissionColor.R, Is.Zero);
                    Assert.That(result.NormalMap, Is.Null);
                    Assert.That(result.Category, Is.EqualTo(p.MaterialCategory.Toon));
                    Assert.That(result.ShadowRamp, Is.Not.Null);
                    var storedAfter = material.GetColor("_Color");
                    Assert.That(storedAfter.r, Is.EqualTo(storedBefore.r).Within(0.000001f));
                    Assert.That(storedAfter.g, Is.EqualTo(storedBefore.g).Within(0.000001f));
                    Assert.That(storedAfter.b, Is.EqualTo(storedBefore.b).Within(0.000001f));
                    Assert.That(storedAfter.a, Is.EqualTo(storedBefore.a).Within(0.000001f));
                }
            }
            finally { Object.DestroyImmediate(material); }
        }

        private sealed class BakeProbe : GenericShaderTranslator
        {
            public BakeProbe() : base(Import) { }
            public Texture2D Alpha(Material material, Texture source) => Bake(material, source, 0, true);
        }

        [TestCase(1, 0.3f)]
        [TestCase(2, 0.12f)]
        [TestCase(3, 0.7f)]
        [TestCase(4, 0.1f)]
        public void AlphaMaskModesPreserveReplaceMultiplyAddSubtract(int mode, float expected)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("GPU baking requires a graphics device.");
            var shader = Shader.Find("Hidden/BEPFairyTech/ResoConverter/Bake");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
            var material = new Material(shader);
            var source = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var mask = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            try
            {
                source.SetPixel(0, 0, new Color(0.6f, 0.7f, 0.8f, 0.4f)); source.Apply();
                mask.SetPixel(0, 0, new Color(0.3f, 0.3f, 0.3f, 1)); mask.Apply();
                material.SetTexture("_Mask", mask);
                material.SetFloat("_AlphaMode", mode);
                using (var probe = new BakeProbe())
                {
                    var pixel = probe.Alpha(material, source).GetPixel(0, 0);
                    Assert.That(pixel.a, Is.EqualTo(expected).Within(0.015f));
                    Assert.That(pixel.r, Is.EqualTo(0.6f).Within(0.015f));
                }
            }
            finally { Object.DestroyImmediate(material); Object.DestroyImmediate(source); Object.DestroyImmediate(mask); }
        }
    }
}
