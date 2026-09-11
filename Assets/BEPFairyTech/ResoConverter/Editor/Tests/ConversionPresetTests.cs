using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BEPFairyTech.ResoConverter.Tests
{
    public sealed class ConversionPresetTests
    {
        private readonly List<Object> owned = new();
        private string assetFolder;

        [SetUp]
        public void SetUp()
        {
            assetFolder = "Assets/__ResoConverterPresetTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", assetFolder.Substring("Assets/".Length));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var value in owned.AsEnumerable().Reverse())
                if (value != null && !EditorUtility.IsPersistent(value)) Object.DestroyImmediate(value);
            owned.Clear();
            if (!string.IsNullOrEmpty(assetFolder)) AssetDatabase.DeleteAsset(assetFolder);
        }

        [TestCase(ExportKind.Avatar)]
        [TestCase(ExportKind.Model)]
        public void SavedAssetRestoresAllOptionsAndClipSubassetsOntoAnotherAvatar(ExportKind kind)
        {
            var source = Avatar("Original", out var sourceFace);
            var target = Avatar("Another instance", out var targetFace);
            var mainClip = PersistentClip("Expressions.asset", "Main expression");
            var subClip = new AnimationClip { name = "Embedded expression" };
            AssetDatabase.AddObjectToAsset(subClip, mainClip);
            AssetDatabase.SaveAssets();
            Assert.That(AssetDatabase.IsSubAsset(subClip), Is.True);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(subClip, out string subGuid, out long subId);

            var options = AllOptions(sourceFace, mainClip, subClip);
            options.Kind = kind;
            var preset = Capture(source, options);
            string path = assetFolder + "/Avatar output.asset";
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(preset);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            preset = AssetDatabase.LoadAssetAtPath<ResoConverterPreset>(path);
            Assert.That(preset, Is.Not.Null);
            Assert.That(preset.SchemaVersion, Is.EqualTo(ResoConverterPreset.CurrentSchemaVersion));

            var current = new ConversionOptions { ResonitePath = "D:/ThisPC/Resonite" };
            var restored = Restore(preset, target, current, out var warnings);
            Assert.That(warnings, Is.Empty);
            Assert.That(restored.Kind, Is.EqualTo(kind));
            Assert.That(restored.LockSaving, Is.False);
            Assert.That(restored.SizeMode, Is.EqualTo(AvatarSizeMode.SourceSize));
            Assert.That(restored.FreezePose, Is.False);
            Assert.That(restored.ProcessModularAvatar, Is.False);
            Assert.That(restored.ToonShadowStrength, Is.EqualTo(0.23f));
            Assert.That(restored.BlinkRenderer, Is.SameAs(targetFace));
            Assert.That(restored.BlinkShape, Is.EqualTo("Blink"));
            Assert.That(restored.EnableHandExpressions, Is.True);
            Assert.That(restored.UseControllerHandPoses, Is.False);
            Assert.That(restored.EnableMenuExpressions, Is.True);
            Assert.That(restored.ResonitePath, Is.EqualTo(current.ResonitePath));
            Assert.That(restored.HandExpressions.Select(row => row.Left),
                Is.EqualTo(new[] { ExpressionHandPose.Victory, ExpressionHandPose.Any }));
            Assert.That(restored.HandExpressions.Select(row => row.Right),
                Is.EqualTo(new[] { ExpressionHandPose.Any, ExpressionHandPose.ThumbsUp }));
            Assert.That(restored.HandExpressions.Select(row => row.SampleTime), Is.EqualTo(new[] { 0.25f, 0.75f }));
            Assert.That(restored.MenuExpressions.Select(row => row.Name), Is.EqualTo(new[] { "微笑み", "目を閉じる" }));
            Assert.That(restored.MenuExpressions.Select(row => row.SampleTime), Is.EqualTo(new[] { 0.5f, 1.25f }));
            Assert.That(restored.HandExpressions[0].Clip, Is.SameAs(mainClip));
            Assert.That(restored.MenuExpressions[1].Clip, Is.SameAs(mainClip));
            Assert.That(restored.HandExpressions[1].Clip, Is.SameAs(restored.MenuExpressions[0].Clip));
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(restored.HandExpressions[1].Clip, out string restoredGuid, out long restoredId);
            Assert.That(restoredGuid, Is.EqualTo(subGuid));
            Assert.That(restoredId, Is.EqualTo(subId), "Embedded clips must keep their local file ID, not just their asset path.");
        }

        [Test]
        public void CaptureAndRestoreCopyRowsAndNeverMutatePresetCurrentSettingsOrAvatar()
        {
            var source = Avatar("Original", out var face);
            face.SetBlendShapeWeight(0, 37);
            face.transform.localPosition = new Vector3(1, 2, 3);
            var clip = PersistentClip("Smile.anim", "Smile");
            var options = AllOptions(face, clip, clip);
            var preset = Capture(source, options);
            string presetBefore = EditorJsonUtility.ToJson(preset);
            options.HandExpressions[0].Left = ExpressionHandPose.Fist;
            options.MenuExpressions[0].Name = "Changed after capture";
            options.MenuExpressions.Add(new MenuExpressionSettings());
            options.BlinkShape = "Changed after capture";

            var current = new ConversionOptions {
                ResonitePath = "E:/Local machine/Resonite", BlinkRenderer = face, BlinkShape = "Smile",
                MenuExpressions = new List<MenuExpressionSettings> { new() { Name = "Current settings", Clip = clip } }
            };
            string currentBefore = JsonUtility.ToJson(current);
            var first = Restore(preset, source, current, out _);
            Assert.That(first.HandExpressions[0].Left, Is.EqualTo(ExpressionHandPose.Victory));
            Assert.That(first.MenuExpressions.Select(row => row.Name), Is.EqualTo(new[] { "微笑み", "目を閉じる" }));
            Assert.That(first.BlinkShape, Is.EqualTo("Blink"));
            first.HandExpressions[0].SampleTime = 999;
            first.MenuExpressions[0].Name = "Changed after restore";
            first.MenuExpressions.Clear();
            first.ToonShadowStrength = 1;
            var second = Restore(preset, source, current, out _);
            Assert.That(second.HandExpressions[0].SampleTime, Is.EqualTo(0.25f));
            Assert.That(second.MenuExpressions.Select(row => row.Name), Is.EqualTo(new[] { "微笑み", "目を閉じる" }));
            Assert.That(second.ToonShadowStrength, Is.EqualTo(0.23f));
            Assert.That(second.HandExpressions, Is.Not.SameAs(first.HandExpressions));
            Assert.That(second.HandExpressions[0], Is.Not.SameAs(first.HandExpressions[0]));
            Assert.That(second.ResonitePath, Is.EqualTo(current.ResonitePath));
            Assert.That(EditorJsonUtility.ToJson(preset), Is.EqualTo(presetBefore));
            Assert.That(JsonUtility.ToJson(current), Is.EqualTo(currentBefore));
            Assert.That(face.GetBlendShapeWeight(0), Is.EqualTo(37));
            Assert.That(face.transform.localPosition, Is.EqualTo(new Vector3(1, 2, 3)));
        }

        [Test]
        public void BlinkOnAvatarRootRebindsToNewRootInsteadOfOldSceneReference()
        {
            var source = Avatar("Original", out var sourceFace, true);
            var target = Avatar("Different root name", out var targetFace, true);
            var preset = Capture(source, BlinkOptions(sourceFace));
            Object.DestroyImmediate(source);
            var restored = Restore(preset, target, new ConversionOptions(), out var warnings);
            Assert.That(warnings, Is.Empty);
            Assert.That(restored.BlinkRenderer, Is.SameAs(targetFace));
            Assert.That(restored.BlinkShape, Is.EqualTo("Blink"));
        }

        [Test]
        public void MissingBlinkPathWarnsAndKeepsRequestedShapeWithoutBindingDifferentFace()
        {
            var source = Avatar("Original", out var sourceFace);
            var target = Avatar("Target", out var targetFace);
            targetFace.gameObject.name = "Another Face";
            var preset = Capture(source, BlinkOptions(sourceFace));
            var restored = Restore(preset, target, BlinkOptions(targetFace), out var warnings);
            Assert.That(warnings, Is.Not.Empty);
            Assert.That(restored.BlinkRenderer, Is.Null);
            Assert.That(restored.BlinkShape, Is.EqualTo("Blink"));
        }

        [Test]
        public void MissingBlinkShapeWarnsAndPreservesSelectionForRepair()
        {
            var source = Avatar("Original", out var sourceFace);
            var target = Avatar("Target", out var targetFace);
            targetFace.sharedMesh = MeshWithShapes("Smile");
            var preset = Capture(source, BlinkOptions(sourceFace));
            var restored = Restore(preset, target, new ConversionOptions(), out var warnings);
            Assert.That(warnings, Is.Not.Empty);
            Assert.That(restored.BlinkRenderer, Is.Null);
            Assert.That(restored.BlinkShape, Is.EqualTo("Blink"));
        }

        [Test]
        public void MissingBlinkMeshWarnsInsteadOfSilentlyChoosingAnotherRenderer()
        {
            var source = Avatar("Original", out var sourceFace);
            var target = Avatar("Target", out var targetFace);
            targetFace.sharedMesh = null;
            var preset = Capture(source, BlinkOptions(sourceFace));
            var restored = Restore(preset, target, new ConversionOptions(), out var warnings);
            Assert.That(warnings, Is.Not.Empty);
            Assert.That(restored.BlinkRenderer, Is.Null);
            Assert.That(restored.BlinkShape, Is.EqualTo("Blink"));
        }

        [Test]
        public void DisabledBlinkRestoresWithoutReusingCurrentRenderer()
        {
            var source = Avatar("Original", out var face, true);
            var preset = Capture(source, new ConversionOptions());
            string path = assetFolder + "/No blink.asset";
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(preset);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            preset = AssetDatabase.LoadAssetAtPath<ResoConverterPreset>(path);
            var restored = Restore(preset, source, BlinkOptions(face), out var warnings);
            Assert.That(warnings, Is.Empty);
            Assert.That(restored.BlinkShape, Is.Empty);
            Assert.That(restored.BlinkRenderer, Is.Null);
        }

        [Test]
        public void CaptureRejectsAmbiguousBlinkSiblingNamesAndLeavesExistingPresetUntouched()
        {
            var source = Avatar("Original", out var face);
            var preset = Capture(source, BlinkOptions(face));
            string before = EditorJsonUtility.ToJson(preset);
            var duplicate = new GameObject(face.name);
            duplicate.transform.SetParent(face.transform.parent, false);
            Assert.That(preset.TryCapture(source, BlinkOptions(face), out _, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(EditorJsonUtility.ToJson(preset), Is.EqualTo(before));
        }

        [Test]
        public void RestoreRejectsAmbiguousBlinkSiblingNamesAndKeepsCurrentSettingsUntouched()
        {
            var source = Avatar("Original", out var sourceFace);
            var target = Avatar("Target", out var targetFace);
            var preset = Capture(source, BlinkOptions(sourceFace));
            var duplicate = new GameObject(targetFace.name);
            duplicate.transform.SetParent(targetFace.transform.parent, false);
            var current = BlinkOptions(targetFace);
            string before = JsonUtility.ToJson(current);
            Assert.That(preset.TryRestore(target, current, out _, out _, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(JsonUtility.ToJson(current), Is.EqualTo(before));
        }

        [Test]
        public void CaptureRejectsBlinkOutsideSelectedAvatarWithoutReplacingPreviousPreset()
        {
            var source = Avatar("Original", out var face);
            Avatar("Unrelated avatar", out var outsideFace);
            var preset = Capture(source, BlinkOptions(face));
            string before = EditorJsonUtility.ToJson(preset);
            Assert.That(preset.TryCapture(source, BlinkOptions(outsideFace), out _, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(EditorJsonUtility.ToJson(preset), Is.EqualTo(before));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CaptureRejectsUnsavedExpressionClipsEvenInDisabledRows(bool handRow)
        {
            var source = Avatar("Original", out var face);
            var preset = Capture(source, BlinkOptions(face));
            string before = EditorJsonUtility.ToJson(preset);
            var unsaved = Own(new AnimationClip { name = "Unsaved expression" });
            var options = BlinkOptions(face);
            if (handRow) options.HandExpressions.Add(new HandExpressionSettings { Clip = unsaved });
            else options.MenuExpressions.Add(new MenuExpressionSettings { Clip = unsaved, Name = "Unsaved" });
            Assert.That(preset.TryCapture(source, options, out _, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(EditorJsonUtility.ToJson(preset), Is.EqualTo(before));
        }

        [Test]
        public void DeletedClipProducesWarningAndRetainsExpressionRowsOrderNamesAndTimes()
        {
            var source = Avatar("Original", out var face);
            var clip = PersistentClip("Will be deleted.anim", "Removed expression");
            var preset = Capture(source, AllOptions(face, clip, clip));
            string path = assetFolder + "/Preset.asset";
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(clip));
            Resources.UnloadAsset(preset);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            preset = AssetDatabase.LoadAssetAtPath<ResoConverterPreset>(path);
            var restored = Restore(preset, source, new ConversionOptions(), out var warnings);
            Assert.That(warnings, Is.Not.Empty);
            Assert.That(restored.HandExpressions.Count, Is.EqualTo(2));
            Assert.That(restored.HandExpressions.All(row => row.Clip == null), Is.True);
            Assert.That(restored.HandExpressions.Select(row => row.SampleTime), Is.EqualTo(new[] { 0.25f, 0.75f }));
            Assert.That(restored.MenuExpressions.Select(row => row.Name), Is.EqualTo(new[] { "微笑み", "目を閉じる" }));
            Assert.That(restored.MenuExpressions.Select(row => row.SampleTime), Is.EqualTo(new[] { 0.5f, 1.25f }));
            Assert.That(restored.MenuExpressions.All(row => row.Clip == null), Is.True);
        }

        [Test]
        public void FutureSchemaVersionFailsWithoutChangingCurrentSettings()
        {
            var source = Avatar("Original", out var face);
            var preset = Capture(source, BlinkOptions(face));
            var serialized = new SerializedObject(preset);
            serialized.FindProperty("schemaVersion").intValue = ResoConverterPreset.CurrentSchemaVersion + 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var current = new ConversionOptions { ResonitePath = "C:/Local Resonite", LockSaving = false };
            string currentBefore = JsonUtility.ToJson(current);
            string presetBefore = EditorJsonUtility.ToJson(preset);
            Assert.That(preset.TryRestore(source, current, out _, out _, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(JsonUtility.ToJson(current), Is.EqualTo(currentBefore));
            Assert.That(EditorJsonUtility.ToJson(preset), Is.EqualTo(presetBefore));
            Assert.That(preset.TryCapture(source, current, out _, out error), Is.False,
                "Overwriting an unsupported future preset must not discard unknown settings.");
            Assert.That(error, Is.Not.Empty);
            Assert.That(EditorJsonUtility.ToJson(preset), Is.EqualTo(presetBefore));
        }

        [Test]
        public void CompatibleReplacementMeshRebindsEvenWhenItsAssetNameChanges()
        {
            var source = Avatar("Original", out var sourceFace);
            var target = Avatar("Target", out var targetFace);
            targetFace.sharedMesh.name = "Updated face mesh";
            var preset = Capture(source, BlinkOptions(sourceFace));
            var restored = Restore(preset, target, new ConversionOptions(), out var warnings);
            Assert.That(warnings, Is.Empty);
            Assert.That(restored.BlinkRenderer, Is.SameAs(targetFace));
            Assert.That(restored.BlinkShape, Is.EqualTo("Blink"));
        }

        [Test]
        public void SlashInObjectNameIsPreservedInsteadOfBeingInterpretedAsHierarchySeparator()
        {
            var source = Avatar("Original", out var sourceFace);
            var target = Avatar("Target", out var targetFace);
            sourceFace.name = targetFace.name = "Face/Expression";
            var misleadingParent = new GameObject("Face");
            misleadingParent.transform.SetParent(targetFace.transform.parent, false);
            var misleadingChild = new GameObject("Expression");
            misleadingChild.transform.SetParent(misleadingParent.transform, false);
            var misleadingRenderer = misleadingChild.AddComponent<SkinnedMeshRenderer>();
            misleadingRenderer.sharedMesh = targetFace.sharedMesh;
            var preset = Capture(source, BlinkOptions(sourceFace));
            var restored = Restore(preset, target, new ConversionOptions(), out var warnings);
            Assert.That(warnings, Is.Empty);
            Assert.That(restored.BlinkRenderer, Is.SameAs(targetFace));
            Assert.That(restored.BlinkRenderer, Is.Not.SameAs(misleadingRenderer));
        }

        [Test]
        public void NullListsAndRowsAreNormalizedWithoutMutatingSourceSettings()
        {
            var source = Avatar("Original", out _);
            var options = new ConversionOptions { HandExpressions = null, MenuExpressions = new List<MenuExpressionSettings> { null } };
            var preset = Own(ScriptableObject.CreateInstance<ResoConverterPreset>());
            Assert.That(preset.TryCapture(source, options, out var captureWarnings, out var error), Is.True, error);
            Assert.That(captureWarnings, Is.Not.Empty);
            Assert.That(options.HandExpressions, Is.Null);
            Assert.That(options.MenuExpressions.Single(), Is.Null);
            var restored = Restore(preset, source, new ConversionOptions(), out _);
            Assert.That(restored.HandExpressions, Is.Not.Null.And.Empty);
            Assert.That(restored.MenuExpressions.Count, Is.EqualTo(1));
            Assert.That(restored.MenuExpressions.Single(), Is.Not.Null);
        }

        [Test]
        public void WindowRestoreCanBeUndoneWithoutChangingAvatarOrPreset()
        {
            var source = Avatar("Original", out var face);
            var clip = PersistentClip("Window smile.anim", "Smile");
            var preset = Capture(source, AllOptions(face, clip, clip));
            var current = new ConversionOptions { BlinkRenderer = face, BlinkShape = "Smile", ResonitePath = "D:/Local Resonite" };
            string before = JsonUtility.ToJson(current);
            string presetBefore = EditorJsonUtility.ToJson(preset);
            face.SetBlendShapeWeight(0, 41);
            var window = Own(ScriptableObject.CreateInstance<ResoConverterWindow>());
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var windowType = typeof(ResoConverterWindow);
            var optionsField = windowType.GetField("options", flags);
            windowType.GetField("source", flags).SetValue(window, source);
            windowType.GetField("preset", flags).SetValue(window, preset);
            optionsField.SetValue(window, current);
            Undo.IncrementCurrentGroup();
            try
            {
                windowType.GetMethod("RestorePreset", flags).Invoke(window, null);
                Undo.FlushUndoRecordObjects();
                var restored = (ConversionOptions)optionsField.GetValue(window);
                Assert.That(restored.MenuExpressions.Count, Is.EqualTo(2));
                Assert.That(restored.BlinkShape, Is.EqualTo("Blink"));
                Undo.PerformUndo();
                Assert.That(JsonUtility.ToJson((ConversionOptions)optionsField.GetValue(window)), Is.EqualTo(before));
                Assert.That(windowType.GetField("source", flags).GetValue(window), Is.SameAs(source));
                Assert.That(EditorJsonUtility.ToJson(preset), Is.EqualTo(presetBefore));
                Assert.That(face.GetBlendShapeWeight(0), Is.EqualTo(41));
            }
            finally { Undo.ClearUndo(window); }
        }

        [Test]
        public void ResoniteInstallationPathIsNeverStoredInPresetAsset()
        {
            var source = Avatar("Original", out _);
            const string privatePath = "Z:/PrivateDevicePath/UniqueUserName/Resonite";
            var preset = Capture(source, new ConversionOptions { ResonitePath = privatePath });
            string path = assetFolder + "/Portable preset.asset";
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            Assert.That(EditorJsonUtility.ToJson(preset), Does.Not.Contain(privatePath));
            Assert.That(System.IO.File.ReadAllText(path), Does.Not.Contain("UniqueUserName"));
            var restored = Restore(preset, source, new ConversionOptions { ResonitePath = "D:/Resonite" }, out _);
            Assert.That(restored.ResonitePath, Is.EqualTo("D:/Resonite"));
        }

        private ResoConverterPreset Capture(GameObject source, ConversionOptions options)
        {
            var preset = Own(ScriptableObject.CreateInstance<ResoConverterPreset>());
            Assert.That(preset.TryCapture(source, options, out var warnings, out var error), Is.True, error);
            Assert.That(warnings, Is.Empty);
            return preset;
        }

        private static ConversionOptions Restore(ResoConverterPreset preset, GameObject source, ConversionOptions current, out string[] warnings)
        {
            Assert.That(preset.TryRestore(source, current, out var restored, out warnings, out var error), Is.True, error);
            Assert.That(restored, Is.Not.Null);
            return restored;
        }

        private static ConversionOptions BlinkOptions(SkinnedMeshRenderer face) =>
            new() { BlinkRenderer = face, BlinkShape = "Blink" };

        private static ConversionOptions AllOptions(SkinnedMeshRenderer face, AnimationClip main, AnimationClip embedded) => new() {
            Kind = ExportKind.Avatar, LockSaving = false, SizeMode = AvatarSizeMode.SourceSize,
            FreezePose = false, ProcessModularAvatar = false, ToonShadowStrength = 0.23f,
            BlinkRenderer = face, BlinkShape = "Blink", EnableHandExpressions = true,
            UseControllerHandPoses = false, EnableMenuExpressions = true, ResonitePath = "Z:/OriginalMachine/Resonite",
            HandExpressions = new List<HandExpressionSettings> {
                new() { Left = ExpressionHandPose.Victory, Right = ExpressionHandPose.Any, Clip = main, SampleTime = 0.25f },
                new() { Left = ExpressionHandPose.Any, Right = ExpressionHandPose.ThumbsUp, Clip = embedded, SampleTime = 0.75f }
            },
            MenuExpressions = new List<MenuExpressionSettings> {
                new() { Name = "微笑み", Clip = embedded, SampleTime = 0.5f },
                new() { Name = "目を閉じる", Clip = main, SampleTime = 1.25f }
            }
        };

        private GameObject Avatar(string name, out SkinnedMeshRenderer face, bool faceOnRoot = false)
        {
            var root = Own(new GameObject(name));
            var faceObject = root;
            if (!faceOnRoot)
            {
                var model = new GameObject("Model");
                model.transform.SetParent(root.transform, false);
                faceObject = new GameObject("Face");
                faceObject.transform.SetParent(model.transform, false);
            }
            face = faceObject.AddComponent<SkinnedMeshRenderer>();
            face.sharedMesh = MeshWithShapes("Blink", "Smile");
            return root;
        }

        private Mesh MeshWithShapes(params string[] shapes)
        {
            var mesh = Own(new Mesh { name = "Face mesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } });
            foreach (string shape in shapes)
                mesh.AddBlendShapeFrame(shape, 100, new[] { Vector3.one * 0.01f, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
            return mesh;
        }

        private AnimationClip PersistentClip(string filename, string name)
        {
            var clip = new AnimationClip { name = name };
            AssetDatabase.CreateAsset(clip, assetFolder + "/" + filename);
            return clip;
        }

        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }
    }
}
