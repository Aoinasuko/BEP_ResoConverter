using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BEPFairyTech.ResoConverter.Tests
{
    public sealed class ConverterValidationTests
    {
        private GameObject source;
        private string installation;
        private string prefabPath;

        [SetUp]
        public void SetUp()
        {
            installation = Path.Combine(Path.GetTempPath(), "BEPResoValidation_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(installation);
            // Validation checks the installation marker, without starting or requiring Resonite.
            File.WriteAllBytes(Path.Combine(installation, "FrooxEngine.dll"), Array.Empty<byte>());
        }

        [TearDown]
        public void TearDown()
        {
            if (source != null) Object.DestroyImmediate(source);
            if (!string.IsNullOrEmpty(prefabPath)) AssetDatabase.DeleteAsset(prefabPath);
            if (!string.IsNullOrEmpty(installation))
            {
                File.Delete(Path.Combine(installation, "FrooxEngine.dll"));
                Directory.Delete(installation);
            }
        }

        [Test]
        public void NewOptionsDefaultToRestrictedSavingAndStandardAvatarSize()
        {
            var options = new ConversionOptions();
            Assert.That(options.LockSaving, Is.True);
            Assert.That(options.Kind, Is.EqualTo(ExportKind.Avatar));
            Assert.That(options.SizeMode, Is.EqualTo(AvatarSizeMode.ResoniteStandard));
            Assert.That(options.FreezePose, Is.True);
            Assert.That(options.ProcessModularAvatar, Is.True);
        }

        [Test]
        public void NullSourceReturnsAnActionableValidationProblem()
        {
            var problems = ResoConverter.Validate(null, ModelOptions());
            Assert.That(problems, Has.Count.EqualTo(1));
            Assert.That(problems[0], Does.Contain("Hierarchy"));
        }

        [Test]
        public void ScenePrimitiveCanBeExportedAsAnItemWithoutAvatarPackages()
        {
            source = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Assert.That(ResoConverter.Validate(source, ModelOptions()), Is.Empty);
        }

        [Test]
        public void ProjectPrefabAssetIsRejectedEvenIfItHasAValidMesh()
        {
            source = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prefabPath = "Assets/__BEPResoValidation_" + Guid.NewGuid().ToString("N") + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
            Assert.That(EditorUtility.IsPersistent(prefab), Is.True);
            var problems = ResoConverter.Validate(prefab, ModelOptions());
            Assert.That(problems.Any(problem => problem.Contains("シーン上")), Is.True);
        }

        private ConversionOptions ModelOptions() => new ConversionOptions
        {
            Kind = ExportKind.Model,
            ResonitePath = installation
        };
    }
}
