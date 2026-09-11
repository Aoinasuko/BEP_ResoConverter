using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Google.Protobuf;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using p = nadena.dev.ndmf.proto;

namespace BEPFairyTech.ResoConverter.Tests
{
    public sealed class PhysBoneLimitTests
    {
        private readonly List<Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in owned.AsEnumerable().Reverse()) if (item != null) Object.DestroyImmediate(item);
            owned.Clear();
        }

        [TestCase("None", p.DynamicBoneLimitType.None)]
        [TestCase("Angle", p.DynamicBoneLimitType.Angle)]
        [TestCase("Hinge", p.DynamicBoneLimitType.Hinge)]
        [TestCase("Polar", p.DynamicBoneLimitType.Polar)]
        public void OptionalReflectionAndWireFormatPreserveLimitKinds(string name, p.DynamicBoneLimitType expected)
        {
            var settings = new TestLimits { limitType = name };
            var message = new p.DynamicBone { LimitType = AvatarSerializer.ReadPhysBoneLimitType(settings) };
            var node = new p.DynamicBoneNode { Bone = new p.ObjectID { Id = 1234 } };
            AvatarSerializer.SamplePhysBoneLimits(settings, node, 1, 3);
            message.Bones.Add(node);
            var restored = p.DynamicBone.Parser.ParseFrom(message.ToByteArray());
            Assert.That(restored.LimitType, Is.EqualTo(expected));
            Assert.That(restored.Bones.Single().HasMaxAngleX, Is.True);
            Assert.That(restored.Bones.Single().MaxAngleX, Is.EqualTo(45));
            Assert.That(restored.Bones.Single().LimitRotation.W, Is.EqualTo(1));
            // Old protobuf data has no limits and must retain its unrestricted behavior.
            var legacy = p.DynamicBone.Parser.ParseFrom(Array.Empty<byte>());
            Assert.That(legacy.LimitType, Is.EqualTo(p.DynamicBoneLimitType.None));
            Assert.That(new p.DynamicBoneNode().HasMaxAngleX, Is.False);
        }

        [Test]
        public void AngularCurvesSampleSegmentsIncludingEndpointsAndUseIndependentRotationAxes()
        {
            var settings = new TestLimits {
                maxAngleX = 60, maxAngleZ = 20,
                maxAngleXCurve = AnimationCurve.Linear(0, 1, 1, .5f),
                maxAngleZCurve = AnimationCurve.Linear(0, 1, 1, 2),
                limitRotation = new Vector3(20, 30, 40),
                limitRotationXCurve = AnimationCurve.Linear(0, 1, 1, .5f),
                limitRotationYCurve = AnimationCurve.Linear(0, .5f, 1, 1),
                limitRotationZCurve = AnimationCurve.Linear(0, 1, 1, 0)
            };
            var nodes = Enumerable.Range(0, 4).Select(depth => {
                var node = new p.DynamicBoneNode();
                AvatarSerializer.SamplePhysBoneLimits(settings, node, depth, 3);
                return node;
            }).ToArray();
            Assert.That(nodes.Select(node => node.MaxAngleX), Is.EqualTo(new[] { 60f, 45f, 30f, 30f }));
            Assert.That(nodes.Select(node => node.MaxAngleZ), Is.EqualTo(new[] { 20f, 30f, 40f, 40f }));
            var midpoint = Rotation(nodes[1]);
            var xyz = Quaternion.AngleAxis(20, Vector3.forward) * Quaternion.AngleAxis(22.5f, Vector3.up) * Quaternion.AngleAxis(15, Vector3.right);
            Assert.That(Quaternion.Angle(midpoint, xyz), Is.LessThan(.05f));
            Assert.That(Quaternion.Angle(midpoint, Quaternion.Euler(15, 22.5f, 20)), Is.GreaterThan(1),
                "The VRC limit frame uses XYZ rather than Unity's default ZXY order.");
            var singleSegment = new p.DynamicBoneNode();
            AvatarSerializer.SamplePhysBoneLimits(settings, singleSegment, 1, 1);
            Assert.That(singleSegment.MaxAngleX, Is.EqualTo(60), "A root plus virtual tip samples the start of the curve.");
        }

        [Test]
        public void EmptyCurvesAndNegativeLimitsFollowVrcSampling()
        {
            var settings = new TestLimits { maxAngleX = -10, maxAngleZ = 25, maxAngleZCurve = new AnimationCurve() };
            var node = new p.DynamicBoneNode();
            AvatarSerializer.SamplePhysBoneLimits(settings, node, 0, 0);
            Assert.That(node.MaxAngleX, Is.Zero);
            Assert.That(node.MaxAngleZ, Is.EqualTo(25));
            Assert.That(Rotation(node), Is.EqualTo(Quaternion.identity));
            Assert.That(AvatarSerializer.ReadPhysBoneLimitType(null), Is.EqualTo(p.DynamicBoneLimitType.None));
        }

        [TestCase("None")]
        [TestCase("Angle")]
        [TestCase("Hinge")]
        [TestCase("Polar")]
        public void InstalledSdkLimitsAndAxesMatchExportWithoutChangingSource(string kind)
        {
            var type = RequireSdk();
            var root = Own(new GameObject("Limit chain"));
            var mid = Child(root.transform, "Mid", new Vector3(.2f, .8f, .1f));
            var tip = Child(mid, "Tip", new Vector3(-.1f, .5f, .2f));
            var component = root.AddComponent(type);
            SetEnum(component, "limitType", kind);
            Set(component, "maxAngleX", 60f);
            Set(component, "maxAngleZ", 20f);
            Set(component, "maxAngleXCurve", AnimationCurve.Linear(0, 1, 1, .5f));
            Set(component, "maxAngleZCurve", AnimationCurve.Linear(0, 1, 1, 2));
            Set(component, "limitRotation", new Vector3(20, 30, 40));
            Set(component, "limitRotationXCurve", AnimationCurve.Linear(0, 1, 1, .5f));
            Set(component, "limitRotationYCurve", AnimationCurve.Linear(0, .5f, 1, 1));
            Set(component, "limitRotationZCurve", AnimationCurve.Linear(0, 1, 1, 0));
            Set(component, "endpointPosition", new Vector3(0, .2f, 0));
            SetEnum(component, "multiChildType", "Average");
            type.GetMethod("InitTransforms").Invoke(component, new object[] { true });
            var sdkManager = OptionalComponent.FindType("VRC.Dynamics.PhysBoneManager");
            var calcAxes = sdkManager.GetMethod("CalcLimitAxis", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(Vector3), typeof(Vector3), typeof(Vector3).MakeByRefType(), typeof(Vector3).MakeByRefType() }, null);
            using (var prepared = ScenePreparer.Prepare(root, false, false, null, ""))
            {
                var serializer = new AvatarSerializer();
                var export = serializer.Export(prepared.Root, prepared.Avatar, false).GetAwaiter().GetResult();
                var chain = DynamicBones(export.Root).Single();
                Assert.That(chain.LimitType.ToString(), Is.EqualTo(kind));
                Assert.That(chain.Bones.Count, Is.EqualTo(4));
                Assert.That(chain.MultiChildType, Is.EqualTo("Average"));
                Assert.That(serializer.Warnings.Any(warning => warning.Contains("角度制限は近似変換の対象外")), Is.False);
                for (int depth = 0; depth < chain.Bones.Count; depth++)
                {
                    var node = chain.Bones[depth];
                    if (kind == "None") { Assert.That(node.HasMaxAngleX, Is.False); continue; }
                    float ratio = (float)type.GetMethod("CalcBoneRatio").Invoke(component, new object[] { depth });
                    var angles = (Vector2)type.GetMethod("CalcMaxAngle").Invoke(component, new object[] { ratio });
                    var euler = (Vector3)type.GetMethod("CalcLimitRotation").Invoke(component, new object[] { ratio });
                    Assert.That(node.MaxAngleX, Is.EqualTo(angles.x).Within(.0001f));
                    Assert.That(node.MaxAngleZ, Is.EqualTo(angles.y).Within(.0001f));
                    var direction = new Vector3(.2f, .8f, .1f).normalized;
                    object[] arguments = { direction, euler, Vector3.zero, Vector3.zero };
                    calcAxes.Invoke(null, arguments);
                    var frame = Quaternion.FromToRotation(Vector3.up, direction) * Rotation(node);
                    Assert.That(Vector3.Distance(frame * Vector3.right, (Vector3)arguments[2]), Is.LessThan(.0001f));
                    Assert.That(Vector3.Distance(frame * Vector3.up, (Vector3)arguments[3]), Is.LessThan(.0001f));
                }
            }
            Assert.That(root.transform.childCount, Is.EqualTo(1));
            Assert.That(tip.childCount, Is.Zero);
            Assert.That(mid.localPosition, Is.EqualTo(new Vector3(.2f, .8f, .1f)));
            Assert.That(OptionalComponent.Get<Vector3>(component, "limitRotation"), Is.EqualTo(new Vector3(20, 30, 40)));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void NestedPhysBoneRootRespectsIgnoreOtherPhysBonesEvenWhenDisabled(bool ignoreOther)
        {
            var type = RequireSdk();
            var root = Own(new GameObject("Outer"));
            var nested = Child(root.transform, "Inner", Vector3.up);
            var tip = Child(nested, "Tip", Vector3.up);
            var outer = root.AddComponent(type);
            Set(outer, "ignoreOtherPhysBones", ignoreOther);
            Set(outer, "endpointPosition", Vector3.zero);
            SetEnum(outer, "limitType", "Angle");
            Set(outer, "maxAngleX", 10f);
            SetEnum(outer, "multiChildType", "First");
            // Use an explicitly referenced root instead of the component's own Transform.
            var host = Child(root.transform, "Other component host", Vector3.zero);
            var inner = host.gameObject.AddComponent(type);
            Set(inner, "rootTransform", nested);
            Set(inner, "ignoreOtherPhysBones", true);
            SetEnum(inner, "limitType", "Angle");
            Set(inner, "maxAngleX", 30f);
            ((Behaviour)inner).enabled = false;
            using (var prepared = ScenePreparer.Prepare(root, false, false, null, ""))
            {
                var serializer = new AvatarSerializer();
                var export = serializer.Export(prepared.Root, prepared.Avatar, false).GetAwaiter().GetResult();
                var chains = DynamicBones(export.Root).ToArray();
                var outerChain = chains.Single(chain => chain.Bones[0].MaxAngleX == 10);
                var innerChain = chains.Single(chain => chain.Bones[0].MaxAngleX == 30);
                Assert.That(serializer.TryGetExportedObjectId(prepared.Root.transform.Find("Inner"), out ulong innerId), Is.True);
                Assert.That(outerChain.Bones.Any(node => node.Bone.Id == innerId), Is.EqualTo(!ignoreOther));
                Assert.That(outerChain.Bones.Select(node => node.Bone.Id).Intersect(innerChain.Bones.Select(node => node.Bone.Id)).Any(), Is.EqualTo(!ignoreOther));
                Assert.That(innerChain.Bones.Count, Is.EqualTo(2));
                Assert.That(outerChain.MultiChildType, Is.EqualTo("First"));
                Assert.That(serializer.Warnings.Count(warning => warning.StartsWith("PhysBoneの角度制限は表示ボーンへ")), Is.EqualTo(1));
                Assert.That(Flatten(export.Root).Any(item => item.Name.StartsWith("__BEP_PhysBoneEnd")), Is.False,
                    "An excluded child with a zero endpoint must not become a virtual endpoint.");
            }
            Assert.That(tip.childCount, Is.Zero);
            Assert.That(OptionalComponent.Get<Transform>(outer, "rootTransform"), Is.Null);
            Assert.That(OptionalComponent.Get<Transform>(inner, "rootTransform"), Is.SameAs(nested));
        }

        [Test]
        public void ExcludingTheOnlyChildWithNoEndpointLeavesAnUnmovingSingleNode()
        {
            var type = RequireSdk();
            var root = Own(new GameObject("Skirt outer"));
            var nested = Child(root.transform, "Skirt inner", Vector3.down);
            Child(nested, "Skirt tip", Vector3.down);
            var outer = root.AddComponent(type);
            Set(outer, "ignoreOtherPhysBones", true);
            Set(outer, "endpointPosition", Vector3.zero);
            SetEnum(outer, "limitType", "Angle");
            Set(outer, "maxAngleX", 10f);
            var inner = nested.gameObject.AddComponent(type);
            SetEnum(inner, "limitType", "Angle");
            Set(inner, "maxAngleX", 30f);
            using (var prepared = ScenePreparer.Prepare(root, false, false, null, ""))
            {
                var serializer = new AvatarSerializer();
                var export = serializer.Export(prepared.Root, prepared.Avatar, false).GetAwaiter().GetResult();
                var chains = DynamicBones(export.Root).ToArray();
                var outerChain = chains.Single(chain => chain.Bones[0].MaxAngleX == 10);
                Assert.That(outerChain.Bones.Count, Is.EqualTo(1));
                Assert.That(outerChain.Bones[0].Bone.Id, Is.EqualTo(outerChain.RootTransform.Id));
                Assert.That(Flatten(export.Root).Any(item => item.Name.StartsWith("__BEP_PhysBoneEnd")), Is.False);
            }
            Assert.That(root.transform.childCount, Is.EqualTo(1));
            Assert.That(nested.childCount, Is.EqualTo(1));
        }

        private Type RequireSdk()
        {
            var type = OptionalComponent.FindType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone");
            if (type == null) Assert.Ignore("VRC SDK is optional; PhysBone integration requires it.");
            return type;
        }
        private T Own<T>(T item) where T : Object { owned.Add(item); return item; }
        private Transform Child(Transform parent, string name, Vector3 localPosition)
        {
            var child = Own(new GameObject(name)).transform;
            child.SetParent(parent, false);
            child.localPosition = localPosition;
            return child;
        }
        private static IEnumerable<p.GameObject> Flatten(p.GameObject root)
        {
            yield return root;
            foreach (var child in root.Children) foreach (var item in Flatten(child)) yield return item;
        }
        private static IEnumerable<p.DynamicBone> DynamicBones(p.GameObject root)
            => Flatten(root).SelectMany(item => item.Components).Where(component => component.Component_.Is(p.DynamicBone.Descriptor))
                .Select(component => component.Component_.Unpack<p.DynamicBone>());
        private static Quaternion Rotation(p.DynamicBoneNode node)
            => new Quaternion(node.LimitRotation.X, node.LimitRotation.Y, node.LimitRotation.Z, node.LimitRotation.W);
        private static FieldInfo Field(object target, string name)
            => target.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
        private static void SetEnum(object target, string name, string value) => Set(target, name, Enum.Parse(Field(target, name).FieldType, value));

        private sealed class TestLimits
        {
            public string limitType = "Angle";
            public float maxAngleX = 45;
            public float maxAngleZ = 45;
            public AnimationCurve maxAngleXCurve;
            public AnimationCurve maxAngleZCurve;
            public Vector3 limitRotation;
            public AnimationCurve limitRotationXCurve;
            public AnimationCurve limitRotationYCurve;
            public AnimationCurve limitRotationZCurve;
        }
    }
}
