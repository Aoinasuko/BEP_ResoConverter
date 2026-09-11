using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BEPFairyTech.ResoConverter.Tests
{
    public sealed class ExpressionTests
    {
        private readonly List<Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var value in owned.AsEnumerable().Reverse()) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        [Test]
        public void ExpressionOptionsAreOptInAndRoundTripEightPoseNames()
        {
            var options = new ConversionOptions();
            Assert.That(options.EnableHandExpressions, Is.False);
            Assert.That(options.EnableMenuExpressions, Is.False);
            Assert.That(options.HandExpressions, Is.Empty);
            Assert.That(options.MenuExpressions, Is.Empty);
            foreach (ExpressionHandPose pose in Enum.GetValues(typeof(ExpressionHandPose)))
                options.HandExpressions.Add(new HandExpressionSettings { Left = pose, Right = ExpressionHandPose.Any, SampleTime = 0.25f });
            var restored = JsonUtility.FromJson<ConversionOptions>(JsonUtility.ToJson(options));
            Assert.That(restored.HandExpressions.Select(row => row.Left), Is.EqualTo(Enum.GetValues(typeof(ExpressionHandPose))));
            Assert.That(restored.HandExpressions.All(row => row.SampleTime == 0.25f), Is.True, "Inherited clip settings must be serialized by Unity.");
            Assert.That(new ExpressionClipSettings().SampleTime, Is.Zero);
        }

        [Test]
        public void SamplingUsesSelectedTimeAndLeavesSourceAndSharedAssetsUntouched()
        {
            var source = Source(out var face);
            face.SetBlendShapeWeight(0, 17);
            face.gameObject.SetActive(false);
            face.transform.localPosition = new Vector3(1, 2, 3);
            var material = Own(new Material(Shader.Find("Standard")));
            material.color = Color.magenta;
            face.sharedMaterial = material;
            var clip = Clip("Face", "Smile", AnimationCurve.Linear(0, 0, 1, 100));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Face", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 1));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Face", typeof(Transform), "m_LocalPosition.x"), AnimationCurve.Constant(0, 1, 200));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Face", typeof(SkinnedMeshRenderer), "material._Color.r"), AnimationCurve.Constant(0, 1, 0));
            var sample = ExpressionSampler.Sample(source, clip, 0.75f, "Sample");
            Assert.That(sample.Values.Count, Is.EqualTo(1));
            Assert.That(sample.Values[0].SourceRenderer, Is.SameAs(face));
            Assert.That(sample.Values[0].Weight, Is.EqualTo(75).Within(0.001f));
            Assert.That(sample.Warnings.Any(w => w.Contains("3件")), Is.True);
            Assert.That(face.GetBlendShapeWeight(0), Is.EqualTo(17));
            Assert.That(face.gameObject.activeSelf, Is.False);
            Assert.That(face.transform.localPosition, Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(face.sharedMaterial, Is.SameAs(material));
            Assert.That(material.color, Is.EqualTo(Color.magenta));
        }

        [Test]
        public void SparseClipsAreIndependentAndShortShapeCurvesHoldTheirLastValue()
        {
            var source = Source(out var face);
            var first = Clip("Face", "Smile", AnimationCurve.Linear(0, 0, 0.5f, 150));
            AnimationUtility.SetEditorCurve(first, EditorCurveBinding.FloatCurve("Face", typeof(Transform), "m_LocalPosition.x"), AnimationCurve.Linear(0, 0, 2, 1));
            var second = Clip("Face", "Sad", AnimationCurve.Constant(0, 1, 30));
            Assert.That(ExpressionSampler.Sample(source, first, 1, "First").Values.Single().Weight, Is.EqualTo(150).Within(0.001f));
            var sample = ExpressionSampler.Sample(source, second, 0, "Second");
            Assert.That(sample.Values.Select(value => value.BlendShape), Is.EqualTo(new[] { "Sad" }));
            Assert.That(sample.Values.Single().Weight, Is.EqualTo(30).Within(0.001f));
            Assert.That(face.GetBlendShapeWeight(0), Is.Zero);
            Assert.That(face.GetBlendShapeWeight(1), Is.Zero);
        }

        [Test]
        public void InvalidTimesAndUnresolvedBindingsProduceActionableErrors()
        {
            var source = Source(out _);
            var clip = Clip("Face", "Smile", AnimationCurve.Linear(0, 0, 1, 100));
            foreach (float time in new[] { -1f, float.NaN, float.PositiveInfinity, 2f })
            {
                var errors = new List<string>();
                ExpressionSampler.ValidateClip(source, clip, time, "Invalid time", errors);
                Assert.That(errors.Any(error => error.Contains("サンプル時刻")), Is.True);
            }
            var missing = Clip("Missing", "Smile", AnimationCurve.Constant(0, 1, 50));
            var missingShape = Clip("Face", "DoesNotExist", AnimationCurve.Constant(0, 1, 50));
            Assert.That(Assert.Throws<InvalidOperationException>(() => ExpressionSampler.Sample(source, missing, 0, "Missing path")).Message, Does.Contain("Missing"));
            Assert.That(Assert.Throws<InvalidOperationException>(() => ExpressionSampler.Sample(source, missingShape, 0, "Missing shape")).Message, Does.Contain("DoesNotExist"));
            var duplicate = Own(new GameObject("Face"));
            duplicate.transform.SetParent(source.transform, false);
            Assert.That(Assert.Throws<InvalidOperationException>(() => ExpressionSampler.Sample(source, clip, 0, "Ambiguous")).Message, Does.Contain("同名"));
        }

        [Test]
        public void MissingAndUnsupportedOnlyClipsAreRejectedOnlyWhenFeatureIsEnabled()
        {
            var source = Source(out _);
            var options = new ConversionOptions();
            options.HandExpressions.Add(new HandExpressionSettings());
            Assert.That(ExpressionExporter.Validate(source, options), Is.Empty);
            options.EnableHandExpressions = true;
            Assert.That(ExpressionExporter.Validate(source, options).Any(error => error.Contains("AnimationClip")), Is.True);
            var clip = Own(new AnimationClip());
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Face", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 0));
            options.HandExpressions[0].Clip = clip;
            Assert.That(ExpressionExporter.Validate(source, options).Any(error => error.Contains("BlendShapeカーブ")), Is.True);
            options.Kind = ExportKind.Model;
            Assert.That(ExpressionExporter.Validate(source, options), Is.Empty);
        }

        [Test]
        public void MenuNamesCannotCollideWithResetEntryOrAnotherExpression()
        {
            var source = Source(out _);
            var options = new ConversionOptions { EnableMenuExpressions = true };
            var clip = Clip("Face", "Smile", AnimationCurve.Constant(0, 1, 50));
            foreach (string name in new[] { "初期状態", "ハンドサインに戻す", "Happy", " Happy " })
                options.MenuExpressions.Add(new MenuExpressionSettings { Name = name, Clip = clip });
            var errors = ExpressionExporter.Validate(source, options);
            Assert.That(errors.Count(error => error.Contains("自動で作成")), Is.EqualTo(2));
            Assert.That(errors.Count(error => error.Contains("重複")), Is.EqualTo(1));
        }

        [Test]
        public void ExportUsesPreparedBaselineAndComponentIdentityAfterHierarchyMoves()
        {
            var source = Source(out var face);
            face.SetBlendShapeWeight(0, 17);
            var smile = Clip("Face", "Smile", AnimationCurve.Constant(0, 1, 150));
            var sad = Clip("Face", "Sad", AnimationCurve.Constant(0, 1, 30));
            var options = new ConversionOptions { EnableHandExpressions = true, EnableMenuExpressions = true };
            options.HandExpressions.Add(new HandExpressionSettings { Left = ExpressionHandPose.Victory, Clip = smile });
            options.HandExpressions.Add(new HandExpressionSettings { Left = ExpressionHandPose.RockNRoll, Clip = sad });
            options.MenuExpressions.Add(new MenuExpressionSettings { Name = " Smile ", Clip = smile });
            using (var prepared = ScenePreparer.Prepare(source, false, false, null, ""))
            {
                var copiedFace = prepared.SourceRenderers[face];
                var moved = new GameObject("New Parent");
                moved.transform.SetParent(prepared.Root.transform, false);
                copiedFace.transform.SetParent(moved.transform, false);
                copiedFace.SetBlendShapeWeight(0, 42);
                var serializer = new AvatarSerializer();
                serializer.Export(prepared.Root, prepared.Avatar, false).GetAwaiter().GetResult();
                var data = ExpressionExporter.Build(source, options, prepared, serializer, new List<string>());
                Assert.That(data.targets.Length, Is.EqualTo(2));
                Assert.That(serializer.TryGetExportedObjectId(copiedFace, out ulong id), Is.True);
                Assert.That(data.targets[0].rendererId, Is.EqualTo(id));
                Assert.That(data.targets[0].baseline, Is.EqualTo(0.42f).Within(0.0001f));
                Assert.That(data.handRules[0].left, Is.EqualTo("Victory"));
                Assert.That(data.handRules[0].right, Is.EqualTo("Any"));
                Assert.That(data.handRules[0].values.Single().value, Is.EqualTo(1.5f));
                Assert.That(data.handRules[1].values.Single().target, Is.EqualTo(1));
                Assert.That(data.menuEntries.Single().values.Single().target, Is.Zero);
                Assert.That(data.menuEntries.Single().name, Is.EqualTo("Smile"));
                var roundTrip = JsonUtility.FromJson<BackendExpressionSettings>(JsonUtility.ToJson(data));
                Assert.That(roundTrip.targets[0].rendererId, Is.EqualTo(id));
                Assert.That(roundTrip.handRules.Length, Is.EqualTo(2));
            }
            Assert.That(face.transform.parent, Is.SameAs(source.transform));
            Assert.That(face.GetBlendShapeWeight(0), Is.EqualTo(17));
        }

        [Test]
        public void RemovedOrExcludedExpressionTargetsFailInsteadOfMatchingAnotherMesh()
        {
            var source = Source(out var face);
            var options = new ConversionOptions { EnableHandExpressions = true };
            options.HandExpressions.Add(new HandExpressionSettings { Clip = Clip("Face", "Smile", AnimationCurve.Constant(0, 1, 50)) });
            using (var prepared = ScenePreparer.Prepare(source, false, false, null, ""))
            {
                prepared.SourceRenderers[face].gameObject.tag = "EditorOnly";
                var serializer = new AvatarSerializer();
                serializer.Export(prepared.Root, prepared.Avatar, false).GetAwaiter().GetResult();
                Assert.That(Assert.Throws<InvalidOperationException>(() => ExpressionExporter.Build(source, options, prepared, serializer, new List<string>())).Message, Does.Contain("EditorOnly"));
                Object.DestroyImmediate(prepared.SourceRenderers[face]);
                Assert.That(Assert.Throws<InvalidOperationException>(() => ExpressionExporter.Build(source, options, prepared, serializer, new List<string>())).Message, Does.Contain("削除・置換"));
            }
        }

        [Test]
        public void InstalledMaBoneProxyRetainsExpressionTargetThroughNdmf()
        {
            var proxyType = OptionalComponent.FindType("nadena.dev.modular_avatar.core.ModularAvatarBoneProxy");
            var descriptorType = OptionalComponent.FindType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (proxyType == null || descriptorType == null || OptionalComponent.FindType("nadena.dev.ndmf.AvatarProcessor") == null)
                Assert.Ignore("Optional MA/NDMF packages are not installed.");
            var source = Source(out var face);
            source.AddComponent<Animator>();
            source.AddComponent(descriptorType);
            var target = Own(new GameObject("Head"));
            target.transform.SetParent(source.transform, false);
            var proxy = face.gameObject.AddComponent(proxyType);
            proxyType.GetProperty("target").SetValue(proxy, target.transform);
            var options = new ConversionOptions { EnableMenuExpressions = true };
            options.MenuExpressions.Add(new MenuExpressionSettings { Clip = Clip("Face", "Smile", AnimationCurve.Constant(0, 1, 75)) });
            using (var prepared = ScenePreparer.Prepare(source, true, false, null, ""))
            {
                var copiedFace = prepared.SourceRenderers[face];
                Assert.That(copiedFace.transform.IsChildOf(prepared.Root.transform.Find("Head")), Is.True);
                var serializer = new AvatarSerializer();
                serializer.Export(prepared.Root, prepared.Avatar, false).GetAwaiter().GetResult();
                var data = ExpressionExporter.Build(source, options, prepared, serializer, new List<string>());
                Assert.That(data.menuEntries.Single().values.Single().value, Is.EqualTo(0.75f));
                Assert.That(prepared.Warnings.Any(warning => warning.Contains("配置・統合")), Is.True);
            }
            Assert.That(face.transform.parent, Is.SameAs(source.transform));
        }

        private GameObject Source(out SkinnedMeshRenderer face)
        {
            var source = Own(new GameObject("Expression Source"));
            var host = Own(new GameObject("Face"));
            host.transform.SetParent(source.transform, false);
            face = host.AddComponent<SkinnedMeshRenderer>();
            var mesh = Own(new Mesh { name = "Expression Mesh" });
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.normals = Enumerable.Repeat(Vector3.forward, 3).ToArray();
            foreach (string shape in new[] { "Smile", "Sad" })
                mesh.AddBlendShapeFrame(shape, 100, Enumerable.Repeat(Vector3.forward * 0.1f, 3).ToArray(), new Vector3[3], new Vector3[3]);
            face.sharedMesh = mesh;
            return source;
        }

        private AnimationClip Clip(string path, string shape, AnimationCurve curve)
        {
            var clip = Own(new AnimationClip());
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + shape), curve);
            return clip;
        }

        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }
    }
}
