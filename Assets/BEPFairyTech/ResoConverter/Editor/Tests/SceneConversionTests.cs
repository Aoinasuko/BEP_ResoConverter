using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using Type = System.Type;
using p = nadena.dev.ndmf.proto;

namespace BEPFairyTech.ResoConverter.Tests
{
    public sealed class SceneConversionTests
    {
        private readonly List<Object> _owned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _owned.AsEnumerable().Reverse()) if (obj != null) Object.DestroyImmediate(obj);
            _owned.Clear();
        }

        [Test]
        public void ClonePreservesWorldScaleAndDoesNotMutateOriginal()
        {
            var parent = Own(new GameObject("Parent"));
            parent.transform.localScale = new Vector3(2, 3, 4);
            var source = Own(new GameObject("Source"));
            source.transform.SetParent(parent.transform, false);
            source.transform.localPosition = new Vector3(1, 2, 3);
            source.transform.localRotation = Quaternion.Euler(0, 20, 0);
            source.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            var expectedPosition = source.transform.position;
            var expectedRotation = source.transform.rotation;
            var expectedScale = source.transform.lossyScale;
            using (var prepared = ScenePreparer.Prepare(source, false, false, null, ""))
            {
                Assert.That((prepared.Root.transform.localScale - expectedScale).magnitude, Is.LessThan(0.0001f));
                Assert.That(prepared.Root.transform.position, Is.EqualTo(Vector3.zero));
                Assert.That(prepared.Root.scene, Is.Not.EqualTo(source.scene));
            }
            Assert.That(source.transform.parent, Is.EqualTo(parent.transform));
            Assert.That(source.transform.position, Is.EqualTo(expectedPosition));
            Assert.That(Quaternion.Angle(source.transform.rotation, expectedRotation), Is.LessThan(0.0001f));
            Assert.That(source.name, Is.EqualTo("Source"));
        }

        [Test]
        public void FixedPoseBakesBoneAndBlendshapeWhileSourceStaysSkinned()
        {
            var source = Own(new GameObject("Posed item"));
            var bone = Own(new GameObject("Bone"));
            bone.transform.SetParent(source.transform, false);
            var mesh = Triangle();
            mesh.bindposes = new[] { Matrix4x4.identity };
            mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, 3).ToArray();
            mesh.AddBlendShapeFrame("Smile", 100, Enumerable.Repeat(Vector3.up, 3).ToArray(), new Vector3[3], new Vector3[3]);
            var skin = source.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = new[] { bone.transform };
            skin.rootBone = bone.transform;
            skin.SetBlendShapeWeight(0, 50);
            bone.transform.localPosition = new Vector3(0, 2, 0);
            var expected = Own(new Mesh());
            skin.BakeMesh(expected, false);
            using (var prepared = ScenePreparer.Prepare(source, false, true, null, ""))
            {
                Assert.That(prepared.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.Empty);
                var baked = prepared.Root.GetComponentInChildren<MeshFilter>(true).sharedMesh;
                Assert.That(baked.blendShapeCount, Is.Zero);
                for (int i = 0; i < expected.vertexCount; i++)
                    Assert.That((baked.vertices[i] - expected.vertices[i]).magnitude, Is.LessThan(0.0001f));
            }
            Assert.That(source.GetComponent<SkinnedMeshRenderer>(), Is.SameAs(skin));
            Assert.That(skin.GetBlendShapeWeight(0), Is.EqualTo(50));
            Assert.That(bone.transform.localPosition, Is.EqualTo(new Vector3(0, 2, 0)));
            Assert.That(skin.sharedMesh, Is.SameAs(mesh));
        }

        [Test]
        public void ItemExportContainsGeometryAndNoAvatarRig()
        {
            var source = Own(new GameObject("Plain item"));
            var renderer = source.AddComponent<MeshRenderer>();
            renderer.enabled = false;
            source.AddComponent<MeshFilter>().sharedMesh = Triangle();
            source.AddComponent<Animator>();
            using (var prepared = ScenePreparer.Prepare(source, false, false, null, ""))
            {
                var result = new AvatarSerializer().Export(prepared.Root, prepared.Avatar, false).GetAwaiter().GetResult();
                Assert.That(result.Root.Components.Any(c => c.Component_.Is(p.AvatarDescriptor.Descriptor)), Is.False);
                Assert.That(result.Root.Components.Any(c => c.Component_.Is(p.RigRoot.Descriptor)), Is.False);
                var meshComponent = result.Root.Components.Single(c => c.Component_.Is(p.MeshRenderer.Descriptor));
                Assert.That(meshComponent.Enabled, Is.False);
                var mesh = result.Assets.Single(a => a.Asset_.Is(p.mesh.Mesh.Descriptor)).Asset_.Unpack<p.mesh.Mesh>();
                Assert.That(mesh.Positions.Count, Is.EqualTo(3));
                Assert.That(mesh.Submeshes[0].Triangles.Triangles[0].V2, Is.EqualTo(2));
            }
        }

        [Test]
        public void AvatarExportRejectsMissingHumanoidWithUsefulMessage()
        {
            var source = Own(new GameObject("Not humanoid"));
            var error = Assert.Throws<InvalidOperationException>(() => new AvatarSerializer().Export(source, new SceneAvatarInfo(), true));
            StringAssert.Contains("Humanoid", error.Message);
        }

        [Test]
        public void ExplicitBlinkAndVisemesReachNativeDescriptor()
        {
            var source = Own(new GameObject("Face"));
            var face = source.AddComponent<SkinnedMeshRenderer>();
            var mesh = Triangle();
            foreach (var name in new[] { "CustomBlink", "vrc.v_aa", "vrc.v_oh" })
                mesh.AddBlendShapeFrame(name, 100, new Vector3[3], new Vector3[3], new Vector3[3]);
            face.sharedMesh = mesh;
            using (var prepared = ScenePreparer.Prepare(source, false, false, face, "CustomBlink"))
            {
                Assert.That(prepared.Avatar.BlinkRenderer, Is.Not.SameAs(face));
                Assert.That(prepared.Avatar.BlinkBlendshape, Is.EqualTo("CustomBlink"));
                Assert.That(prepared.Avatar.VisemeBlendshapes["aa"], Is.EqualTo("vrc.v_aa"));
                var serializer = new AvatarSerializer();
                var method = typeof(AvatarSerializer).GetMethod("TranslateAvatarDescriptor", BindingFlags.NonPublic | BindingFlags.Instance);
                var descriptor = (p.AvatarDescriptor)method.Invoke(serializer, new object[] { null, prepared.Avatar });
                Assert.That(descriptor.EyelookConfig.Blendshape.Blink, Is.EqualTo("CustomBlink"));
                Assert.That(descriptor.EyelookConfig.Blendshape.EyelidMesh.Id, Is.GreaterThan(0));
                Assert.That(descriptor.VisemeConfig.ShapeAa, Is.EqualTo("vrc.v_aa"));
                Assert.That(descriptor.VisemeConfig.ShapeOh, Is.EqualTo("vrc.v_oh"));
                // Dispose translators through the regular exporter, which is a one-shot object.
                serializer.Export(prepared.Root, prepared.Avatar, false).GetAwaiter().GetResult();
            }
        }

        [Test]
        public void EmptyBlinkSelectionDisablesBlink()
        {
            var source = Own(new GameObject("No blink"));
            using (var prepared = ScenePreparer.Prepare(source, false, false, null, ""))
                Assert.That(prepared.Avatar.BlinkRenderer, Is.Null);
        }

        [Test]
        public void InstalledPhysBoneExportsCurvesAndEndpointWithoutTouchingSource()
        {
            var type = OptionalComponent.FindType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone");
            if (type == null) Assert.Ignore("VRC SDK is optional; PhysBone integration test requires it.");
            var source = Own(new GameObject("Dynamics"));
            var tip = Own(new GameObject("Tip"));
            tip.transform.SetParent(source.transform, false);
            tip.transform.localPosition = Vector3.up;
            var pb = source.AddComponent(type);
            SetField(pb, "pull", 0.6f);
            SetField(pb, "pullCurve", AnimationCurve.Linear(0, 1, 1, 0.5f));
            SetField(pb, "radius", 0.04f);
            SetField(pb, "radiusCurve", AnimationCurve.Linear(0, 1, 1, 0));
            SetField(pb, "endpointPosition", new Vector3(0, 0.1f, 0));
            using (var prepared = ScenePreparer.Prepare(source, false, false, null, ""))
            {
                var serializer = new AvatarSerializer();
                var result = serializer.Export(prepared.Root, prepared.Avatar, false).GetAwaiter().GetResult();
                var dynamics = result.Root.Components.Single(c => c.Component_.Is(p.DynamicBone.Descriptor)).Component_.Unpack<p.DynamicBone>();
                Assert.That(dynamics.VrcPull, Is.EqualTo(0.6f).Within(0.0001f));
                Assert.That(dynamics.Bones.Count, Is.EqualTo(3));
                Assert.That(dynamics.Bones.Last().Pull, Is.EqualTo(0.3f).Within(0.0001f));
                Assert.That(dynamics.Bones.Last().Radius, Is.Zero.Within(0.0001f));
                Assert.That(serializer.PhysicsSources.Count, Is.EqualTo(1));
            }
            Assert.That(source.transform.childCount, Is.EqualTo(1));
            Assert.That(tip.transform.childCount, Is.Zero);
            Assert.That(OptionalComponent.Get(pb, "pull", 0f), Is.EqualTo(0.6f));
        }

        [Test]
        public void NdmfBoneProxyKeepsAttachmentAndLeavesOriginalHierarchyUntouched()
        {
            var proxyType = OptionalComponent.FindType("nadena.dev.modular_avatar.core.ModularAvatarBoneProxy");
            RequireModularAvatar(proxyType);
            var source = AvatarRoot("Bone proxy test");
            var target = Child(source.transform, "Head", new Vector3(0, 1.6f, 0));
            var accessory = Child(source.transform, "Accessory", new Vector3(0.1f, 1.7f, 0.2f));
            var proxy = accessory.AddComponent(proxyType);
            proxyType.GetProperty("target").SetValue(proxy, target.transform);
            SetEnumField(proxy, "attachmentMode", "AsChildKeepWorldPose");
            var initialPose = accessory.transform.position;
            using (var prepared = ScenePreparer.Prepare(source, true, false, null, ""))
            {
                var clonedTarget = prepared.Root.transform.Find("Head");
                var clonedAccessory = clonedTarget.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Accessory");
                Assert.That(clonedAccessory.parent, Is.SameAs(clonedTarget));
                Assert.That((clonedAccessory.position - initialPose).magnitude, Is.LessThan(0.0001f));
                Assert.That(clonedAccessory.GetComponent(proxyType), Is.Null);
                Assert.That(prepared.Warnings.Any(w => w.Contains("配置・統合")), Is.True, "Use full NDMF processing, not the fallback.");
            }
            Assert.That(accessory.transform.parent, Is.SameAs(source.transform));
            Assert.That(accessory.transform.position, Is.EqualTo(initialPose));
            Assert.That(accessory.GetComponent(proxyType), Is.SameAs(proxy));
        }

        [Test]
        public void NdmfMergeArmatureMakesAccessoryFollowMatchedBoneWithoutEditingOriginal()
        {
            var mergeType = OptionalComponent.FindType("nadena.dev.modular_avatar.core.ModularAvatarMergeArmature");
            RequireModularAvatar(mergeType);
            var source = AvatarRoot("Merge armature test");
            var skeleton = Child(source.transform, "Armature", Vector3.zero);
            var hips = Child(skeleton.transform, "Hips", Vector3.up);
            var clothing = Child(source.transform, "ClothesArmature", Vector3.zero);
            var clothingHips = Child(clothing.transform, "Hips", Vector3.up);
            var marker = Child(clothingHips.transform, "AccessoryMarker", new Vector3(0.2f, 0, 0));
            marker.AddComponent<MeshFilter>().sharedMesh = Triangle();
            marker.AddComponent<MeshRenderer>();
            var merge = clothing.AddComponent(mergeType);
            var targetReference = OptionalComponent.Get(merge, "mergeTarget");
            targetReference.GetType().GetMethod("Set", new[] { typeof(GameObject) }).Invoke(targetReference, new object[] { skeleton });
            SetEnumField(merge, "LockMode", "NotLocked");
            var initialPosition = marker.transform.position;
            using (var prepared = ScenePreparer.Prepare(source, true, false, null, ""))
            {
                var clonedHips = prepared.Root.transform.Find("Armature/Hips");
                var clonedMarker = prepared.Root.GetComponentsInChildren<MeshFilter>(true).Single().transform;
                Assert.That(clonedMarker.IsChildOf(clonedHips), Is.True);
                Assert.That((clonedMarker.position - initialPosition).magnitude, Is.LessThan(0.0001f));
                Vector3 before = clonedMarker.position;
                clonedHips.position += Vector3.right;
                Assert.That((clonedMarker.position - before - Vector3.right).magnitude, Is.LessThan(0.0001f));
                Assert.That(prepared.Warnings.Any(w => w.Contains("配置・統合")), Is.True);
            }
            Assert.That(marker.transform.parent, Is.SameAs(clothingHips.transform));
            Assert.That(marker.transform.position, Is.EqualTo(initialPosition));
            Assert.That(clothing.GetComponent(mergeType), Is.SameAs(merge));
            Assert.That(hips.transform.parent, Is.SameAs(skeleton.transform));
        }

        [Test]
        public void ExternalSkinBonesRequireFullRootOrFixedPose()
        {
            var root = Own(new GameObject("Selected mesh"));
            var externalBone = Own(new GameObject("External bone"));
            externalBone.transform.position = Vector3.up;
            var mesh = Triangle();
            mesh.bindposes = new[] { Matrix4x4.identity };
            mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, 3).ToArray();
            var skin = root.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = new[] { externalBone.transform };
            using (var prepared = ScenePreparer.Prepare(root, false, false, null, ""))
            {
                var error = Assert.Throws<InvalidOperationException>(() => new AvatarSerializer().Export(prepared.Root, prepared.Avatar, false));
                StringAssert.Contains("ルート外のボーン", error.Message);
            }
            using (var prepared = ScenePreparer.Prepare(root, false, true, null, ""))
            {
                Assert.That(prepared.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.Empty);
                Assert.DoesNotThrow(() => new AvatarSerializer().Export(prepared.Root, prepared.Avatar, false).GetAwaiter().GetResult());
            }
            Assert.That(externalBone.transform.position, Is.EqualTo(Vector3.up));
        }

        private void RequireModularAvatar(Type componentType)
        {
            if (componentType == null || OptionalComponent.FindType("nadena.dev.ndmf.AvatarProcessor") == null ||
                OptionalComponent.FindType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor") == null)
                Assert.Ignore("MA / NDMF / VRC SDK are optional; this integration test requires them.");
        }

        private GameObject AvatarRoot(string name)
        {
            var root = Own(new GameObject(name));
            root.AddComponent<Animator>();
            root.AddComponent(OptionalComponent.FindType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor"));
            return root;
        }

        private GameObject Child(Transform parent, string name, Vector3 position)
        {
            var child = Own(new GameObject(name));
            child.transform.SetParent(parent, false);
            child.transform.localPosition = position;
            return child;
        }

        private static void SetEnumField(object target, string name, string value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            field.SetValue(target, Enum.Parse(field.FieldType, value));
        }

        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }
        private Mesh Triangle()
        {
            var mesh = Own(new Mesh { name = "Triangle" });
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
            mesh.triangles = new[] { 0, 1, 2 };
            return mesh;
        }
        private static void SetField(object target, string name, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field == null) continue;
                field.SetValue(target, value);
                return;
            }
            Assert.Fail("Missing field " + name);
        }
    }
}
