using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BEPFairyTech.ResoConverter.Tests
{
    public sealed class EyeLookTests
    {
        private readonly List<Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in owned.AsEnumerable().Reverse()) if (item != null) Object.DestroyImmediate(item);
            owned.Clear();
        }

        [Test]
        public void MissingDescriptorAndItemExportDoNotRequestEyeLook()
        {
            var root = Own(new GameObject("No eye settings"));
            var info = SceneAvatarInfo.Read(root, null, "");
            Assert.That(info.EyeLook, Is.Null);
            var serializer = new AvatarSerializer();
            serializer.Export(root, info, false).GetAwaiter().GetResult();
            Assert.That(serializer.BuildEyeLookSettings(info, true), Is.Null);
            string json = JsonUtility.ToJson(new BackendSettings {
                asAvatar = true, eyeLook = serializer.BuildEyeLookSettings(info, true)
            });
            var restored = JsonUtility.FromJson<BackendSettings>(json);
            Assert.That(restored.eyeLook == null || !restored.eyeLook.configured, Is.True,
                "Unity can serialize a null inline class as a default object; native fallback must still apply.");
            info.EyeLook = new SceneEyeLookInfo { Enabled = true };
            Assert.That(serializer.BuildEyeLookSettings(info, false), Is.Null);
        }

        [Test]
        public void VrcLocalRotationsAndCustomBoneReferencesReachBackendJson()
        {
            var root = Own(new GameObject("Eye avatar"));
            var left = Eye(root, "Left");
            var right = Eye(root, "Right");
            var descriptor = Descriptor(left, right);
            descriptor.customEyeLookSettings.eyesLookingStraight = Pair(175, 0, 0);
            descriptor.customEyeLookSettings.eyesLookingUp = Pair(175, 0, 0);
            descriptor.customEyeLookSettings.eyesLookingDown = Pair(175, 0, 0);
            descriptor.customEyeLookSettings.eyesLookingLeft = Pair(175, 1.7f, 0);
            descriptor.customEyeLookSettings.eyesLookingRight = Pair(175, -1.9f, 0);
            // The second eye need not have the same limit or axis as the first.
            descriptor.customEyeLookSettings.eyesLookingLeft.right = Quaternion.Euler(176, 2.1f, 0.3f);
            var warnings = new List<string>();
            var info = new SceneAvatarInfo { EyeLook = SceneEyeLookInfo.Read(descriptor, null, warnings) };
            var serializer = new AvatarSerializer();
            serializer.Export(root, info, false).GetAwaiter().GetResult();
            var data = serializer.BuildEyeLookSettings(info, true);
            var settings = new BackendSettings { asAvatar = true, eyeLook = data };
            var restored = JsonUtility.FromJson<BackendSettings>(JsonUtility.ToJson(settings)).eyeLook;
            Assert.That(restored.configured, Is.True);
            Assert.That(restored.enabled, Is.True);
            Assert.That(serializer.TryGetExportedObjectId(left, out ulong leftId), Is.True);
            Assert.That(serializer.TryGetExportedObjectId(right, out ulong rightId), Is.True);
            Assert.That(restored.left.boneId, Is.EqualTo(leftId));
            Assert.That(restored.right.boneId, Is.EqualTo(rightId));
            AssertRotation(restored.left.straight, descriptor.customEyeLookSettings.eyesLookingStraight.left);
            AssertRotation(restored.left.up, restored.left.straight);
            AssertRotation(restored.left.down, restored.left.straight);
            AssertRotation(restored.left.left, descriptor.customEyeLookSettings.eyesLookingLeft.left);
            AssertRotation(restored.left.right, descriptor.customEyeLookSettings.eyesLookingRight.left);
            AssertRotation(restored.right.left, descriptor.customEyeLookSettings.eyesLookingLeft.right);
            Assert.That(Quaternion.Angle(restored.left.straight, restored.left.left), Is.EqualTo(1.7f).Within(0.01f));
            Assert.That(Quaternion.Angle(restored.left.straight, restored.left.right), Is.EqualTo(1.9f).Within(0.01f));
            Assert.That(warnings, Is.Empty);
            AssertRotation(left.localRotation, Quaternion.identity);
            AssertRotation(right.localRotation, Quaternion.identity);
        }

        [Test]
        public void ExplicitlyDisabledEyeLookPreservesOriginalBonePose()
        {
            var root = Own(new GameObject("Disabled eye look"));
            var left = Eye(root, "Left");
            left.localRotation = Quaternion.Euler(12, 23, 34);
            var descriptor = Descriptor(left, null);
            descriptor.enableEyeLook = false;
            descriptor.customEyeLookSettings.eyesLookingStraight = Pair(90, 0, 0);
            var data = SceneEyeLookInfo.Read(descriptor, null, new List<string>());
            Assert.That(data.Enabled, Is.False);
            Assert.That(data.Right, Is.Null);
            AssertRotation(data.Left.Straight, left.localRotation);
            AssertRotation(data.Left.Up, left.localRotation);
            AssertRotation(data.Left.Left, left.localRotation);
            var serializer = new AvatarSerializer();
            var info = new SceneAvatarInfo { EyeLook = data };
            serializer.Export(root, info, false).GetAwaiter().GetResult();
            var restored = JsonUtility.FromJson<BackendSettings>(JsonUtility.ToJson(new BackendSettings {
                asAvatar = true, eyeLook = serializer.BuildEyeLookSettings(info, true)
            })).eyeLook;
            Assert.That(restored.configured, Is.True);
            Assert.That(restored.enabled, Is.False);
            Assert.That(restored.left.boneId, Is.GreaterThan(0));
            AssertRotation(restored.left.straight, left.localRotation);
        }

        [Test]
        public void InvalidEyeRotationsFallBackAndExcludedBonesAreNeverReferenced()
        {
            var root = Own(new GameObject("Excluded eyes"));
            var external = Own(new GameObject("External eye")).transform;
            var excluded = Eye(root, "Excluded eye");
            excluded.gameObject.tag = "EditorOnly";
            excluded.localRotation = Quaternion.Euler(10, 15, 20);
            var descriptor = Descriptor(excluded, external);
            descriptor.customEyeLookSettings.eyesLookingStraight.left = default;
            descriptor.customEyeLookSettings.eyesLookingUp.left = new Quaternion(float.NaN, 0, 0, 1);
            var warnings = new List<string>();
            var info = new SceneAvatarInfo { EyeLook = SceneEyeLookInfo.Read(descriptor, null, warnings) };
            AssertRotation(info.EyeLook.Left.Straight, excluded.localRotation);
            AssertRotation(info.EyeLook.Left.Up, excluded.localRotation);
            Assert.That(warnings.Count, Is.EqualTo(2));
            var serializer = new AvatarSerializer();
            serializer.Export(root, info, false).GetAwaiter().GetResult();
            var data = serializer.BuildEyeLookSettings(info, true);
            Assert.That(data.left, Is.Null);
            Assert.That(data.right, Is.Null);
            Assert.That(serializer.Warnings.Count(warning => warning.Contains("Eye Look")), Is.EqualTo(2));
        }

        [Test]
        public void InstalledVrcDescriptorReadsClonedEyeSettingsWithoutChangingSource()
        {
            var type = OptionalComponent.FindType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (type == null) Assert.Ignore("VRC SDK is optional; this integration test requires it.");
            var source = Own(new GameObject("VRC eye settings"));
            var left = Eye(source, "Left eye");
            var right = Eye(source, "Right eye");
            var component = source.AddComponent(type);
            SetField(component, "enableEyeLook", true);
            object settings = OptionalComponent.Get(component, "customEyeLookSettings");
            SetField(settings, "leftEye", left);
            SetField(settings, "rightEye", right);
            foreach (string field in new[] { "eyesLookingStraight", "eyesLookingUp", "eyesLookingDown", "eyesLookingLeft", "eyesLookingRight" })
            {
                object pair = OptionalComponent.Get(settings, field);
                if (pair == null) pair = Activator.CreateInstance(settings.GetType().GetField(field).FieldType);
                SetField(pair, "left", Quaternion.Euler(175, field == "eyesLookingLeft" ? 1.7f : 0, 0));
                SetField(pair, "right", Quaternion.Euler(175, field == "eyesLookingRight" ? -1.9f : 0, 0));
                SetField(settings, field, pair);
            }
            SetField(component, "customEyeLookSettings", settings);
            using (var prepared = ScenePreparer.Prepare(source, false, false, null, ""))
            {
                Assert.That(prepared.Avatar.EyeLook.Enabled, Is.True);
                Assert.That(prepared.Avatar.EyeLook.Left.Bone, Is.SameAs(prepared.Root.transform.Find("Left eye")));
                Assert.That(prepared.Avatar.EyeLook.Right.Bone, Is.SameAs(prepared.Root.transform.Find("Right eye")));
                AssertRotation(prepared.Avatar.EyeLook.Left.Left, Quaternion.Euler(175, 1.7f, 0));
                AssertRotation(prepared.Avatar.EyeLook.Right.Right, Quaternion.Euler(175, -1.9f, 0));
            }
            AssertRotation(left.localRotation, Quaternion.identity);
            Assert.That(OptionalComponent.Get<Transform>(OptionalComponent.Get(component, "customEyeLookSettings"), "leftEye"), Is.SameAs(left));
        }

        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }
        private Transform Eye(GameObject root, string name)
        {
            var result = Own(new GameObject(name)).transform;
            result.SetParent(root.transform, false);
            return result;
        }
        private static void AssertRotation(Quaternion actual, Quaternion expected)
            => Assert.That(Quaternion.Angle(actual, expected), Is.LessThan(0.05f));
        private static TestDescriptor Descriptor(Transform left, Transform right) => new TestDescriptor {
            enableEyeLook = true,
            customEyeLookSettings = new TestEyeSettings { leftEye = left, rightEye = right }
        };
        private static TestRotationPair Pair(float x, float y, float z) => new TestRotationPair {
            left = Quaternion.Euler(x, y, z), right = Quaternion.Euler(x, y, z)
        };
        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        // Test the optional reflection contract even in projects without the VRC package.
        private sealed class TestDescriptor
        {
            public bool enableEyeLook;
            public TestEyeSettings customEyeLookSettings;
        }
        private sealed class TestEyeSettings
        {
            public Transform leftEye;
            public Transform rightEye;
            public TestRotationPair eyesLookingStraight = Pair(0, 0, 0);
            public TestRotationPair eyesLookingUp = Pair(0, 0, 0);
            public TestRotationPair eyesLookingDown = Pair(0, 0, 0);
            public TestRotationPair eyesLookingLeft = Pair(0, 0, 0);
            public TestRotationPair eyesLookingRight = Pair(0, 0, 0);
        }
        private struct TestRotationPair
        {
            public Quaternion left;
            public Quaternion right;
        }
    }
}
