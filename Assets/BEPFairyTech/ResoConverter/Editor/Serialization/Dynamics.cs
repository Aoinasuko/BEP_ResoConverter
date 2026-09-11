// Adapted from Modular Avatar for Resonite, Copyright (c) 2025 bd_, MIT license.
// See UPSTREAM-LICENSE.txt. VRC settings are read without a package reference.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using UnityEditor;
using UnityEngine;
using p = nadena.dev.ndmf.proto;

namespace BEPFairyTech.ResoConverter
{
    [Serializable]
    internal sealed class PhysBoneSourceSettings
    {
        public string hierarchyPath;
        public string componentType;
        public string unitySettingsJson;
    }

    internal partial class AvatarSerializer
    {
        internal readonly List<PhysBoneSourceSettings> PhysicsSources = new();
        private readonly Dictionary<Component, List<(Transform bone, int depth)>> _dynamicNodes = new();

        private void AddPhysBoneEndpoints(GameObject root)
        {
            var physBones = root.GetComponentsInChildren<Component>(true)
                .Where(component => component != null && component.GetType().FullName == "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone")
                .ToArray();
            foreach (var component in physBones)
            {
                var target = OptionalComponent.Get<Transform>(component, "rootTransform");
                if (target == null) target = component.transform;
                if (!target.IsChildOf(root.transform)) { Warnings.Add(component.name + ": ルート外の PhysBone は省略しました。"); continue; }
                var ignore = new HashSet<Transform>();
                if (OptionalComponent.Get(component, "ignoreTransforms") is IEnumerable ignored)
                    foreach (var item in ignored) if (item is Transform transform) ignore.Add(transform);
                if (OptionalComponent.Get(component, "ignoreOtherPhysBones", false))
                    foreach (var other in physBones)
                    {
                        if (other == component) continue;
                        var otherRoot = OptionalComponent.Get<Transform>(other, "rootTransform");
                        if (otherRoot == null) otherRoot = other.transform;
                        // Match VRC's boundary at another PhysBone's root, including all descendants.
                        // Do not exclude this chain's own root when two components share it.
                        if (otherRoot != target) ignore.Add(otherRoot);
                    }
                var nodes = new List<(Transform bone, int depth)>();
                string multiChild = OptionalComponent.Get(component, "multiChildType")?.ToString();
                var endpoint = OptionalComponent.Get(component, "endpointPosition", Vector3.zero);
                Traverse(target, 0);
                _dynamicNodes[component] = nodes;

                void Traverse(Transform bone, int depth)
                {
                    if (ignore.Contains(bone) || bone.CompareTag("EditorOnly")) return;
                    var children = bone.Cast<Transform>().Where(t => !ignore.Contains(t) && !t.CompareTag("EditorOnly") && !t.name.StartsWith("__BEP_PhysBoneEnd")).ToArray();
                    if (children.Length < 2 || multiChild != "Ignore") nodes.Add((bone, depth));
                    foreach (var child in children) Traverse(child, depth + 1);
                    if (children.Length == 0 && endpoint.sqrMagnitude > 1e-12f)
                    {
                        var end = new GameObject("__BEP_PhysBoneEnd");
                        end.transform.SetParent(bone, false);
                        end.transform.localPosition = endpoint;
                        nodes.Add((end.transform, depth + 1));
                        _tempObjects.Add(end);
                    }
                }
            }
        }

        private IMessage TranslateDynamicBone(Component component)
        {
            if (!_dynamicNodes.TryGetValue(component, out var nodes) || nodes.Count == 0) return null;
            var root = OptionalComponent.Get<Transform>(component, "rootTransform");
            if (root == null) root = component.transform;
            int maxDepth = Math.Max(1, nodes.Max(n => n.depth));
            var message = new p.DynamicBone
            {
                RootTransform = MapObject(root),
                IsGrabbable = OptionalComponent.Get(component, "allowGrabbing")?.ToString() == "True",
                VrcParameters = true,
                VrcPull = OptionalComponent.Get(component, "pull", 0.2f),
                VrcSpring = OptionalComponent.Get(component, "spring", 0.2f),
                VrcStiffness = OptionalComponent.Get(component, "stiffness", 0.2f),
                VrcGravity = OptionalComponent.Get(component, "gravity", 0f),
                VrcImmobile = OptionalComponent.Get(component, "immobile", 0f),
                LimitType = ReadPhysBoneLimitType(component),
                MultiChildType = OptionalComponent.Get(component, "multiChildType")?.ToString() ?? "Ignore"
            };
            foreach (var item in nodes)
            {
                float depth = (float)item.depth / maxDepth;
                var bone = new p.DynamicBoneNode
                {
                    Bone = MapObject(item.bone),
                    Radius = Mathf.Max(0, Sample(component, "radius", depth, 0.02f)),
                    Pull = Sample(component, "pull", depth, 0.2f),
                    Spring = Sample(component, "spring", depth, 0.2f),
                    Stiffness = Sample(component, "stiffness", depth, 0.2f),
                    Gravity = Sample(component, "gravity", depth, 0f),
                    Immobile = Sample(component, "immobile", depth, 0f)
                };
                if (message.LimitType != p.DynamicBoneLimitType.None)
                    SamplePhysBoneLimits(component, bone, item.depth, maxDepth);
                message.Bones.Add(bone);
            }
            if (OptionalComponent.Get(component, "allowCollision")?.ToString() != "False" &&
                OptionalComponent.Get(component, "colliders") is IEnumerable colliders)
                foreach (var value in colliders)
                    if (value is Component collider && collider != null && collider.transform.IsChildOf(_root))
                        message.Colliders.Add(MapObject(collider));

            PhysicsSources.Add(new PhysBoneSourceSettings
            {
                hierarchyPath = AnimationUtility.CalculateTransformPath(component.transform, _root),
                componentType = component.GetType().FullName,
                unitySettingsJson = EditorJsonUtility.ToJson(component)
            });
            if (!Warnings.Contains("PhysBone は Resonite DynamicBone へ近似変換されます。計算方式が異なるため揺れ方の完全一致は保証されません。"))
                Warnings.Add("PhysBone は Resonite DynamicBone へ近似変換されます。計算方式が異なるため揺れ方の完全一致は保証されません。");
            string originalLimit = OptionalComponent.Get(component, "limitType")?.ToString();
            if (originalLimit != null && originalLimit != "None" && message.LimitType == p.DynamicBoneLimitType.None)
                Warnings.Add(component.name + ": 未知の PhysBone 角度制限「" + originalLimit + "」は省略し、元設定を変換レポートに保存します。");
            const string limitWarning = "PhysBoneの角度制限は表示ボーンへ適用します。衝突・つかみの計算位置は制限前の物理ボーンを使うため、制限付近で見た目とずれる場合があります。";
            if (message.LimitType != p.DynamicBoneLimitType.None && !Warnings.Contains(limitWarning)) Warnings.Add(limitWarning);
            if (OptionalComponent.Get(component, "gravityFalloff", 0f) != 0 || OptionalComponent.Get(component, "maxStretch", 0f) != 0 || OptionalComponent.Get(component, "maxSquish", 0f) != 0)
                Warnings.Add(component.name + ": Gravity Falloff / Stretch / Squish は元設定を記録し、動作は Resonite 側の近似設定を使用します。");
            return message;
        }

        internal static p.DynamicBoneLimitType ReadPhysBoneLimitType(object component)
        {
            return OptionalComponent.Get(component, "limitType")?.ToString() switch {
                "Angle" => p.DynamicBoneLimitType.Angle,
                "Hinge" => p.DynamicBoneLimitType.Hinge,
                "Polar" => p.DynamicBoneLimitType.Polar,
                _ => p.DynamicBoneLimitType.None
            };
        }

        internal static void SamplePhysBoneLimits(object component, p.DynamicBoneNode bone, int depth, int maxDepth)
        {
            // VRC applies angular curves to simulated segments, rather than their terminal tip.
            float ratio = maxDepth <= 1 ? 0 : Mathf.Clamp01((float)depth / (maxDepth - 1));
            bone.MaxAngleX = Mathf.Max(0, Sample(component, "maxAngleX", ratio, 45));
            bone.MaxAngleZ = Mathf.Max(0, Sample(component, "maxAngleZ", ratio, 45));
            var rotation = OptionalComponent.Get(component, "limitRotation", Vector3.zero);
            rotation.x *= SampleCurve(component, "limitRotationXCurve", ratio);
            rotation.y *= SampleCurve(component, "limitRotationYCurve", ratio);
            rotation.z *= SampleCurve(component, "limitRotationZCurve", ratio);
            // VRC uses EulerXYZ; Unity's Quaternion.Euler instead uses ZXY.
            bone.LimitRotation = (Quaternion.AngleAxis(rotation.z, Vector3.forward) *
                Quaternion.AngleAxis(rotation.y, Vector3.up) * Quaternion.AngleAxis(rotation.x, Vector3.right)).ToRPC();
        }

        private static float Sample(object component, string name, float depth, float fallback)
        {
            float value = OptionalComponent.Get(component, name, fallback);
            return value * SampleCurve(component, name + "Curve", depth);
        }

        private static float SampleCurve(object component, string name, float ratio)
        {
            var curve = OptionalComponent.Get<AnimationCurve>(component, name);
            return curve != null && curve.length > 0 ? curve.Evaluate(ratio) : 1f;
        }

        private IMessage TranslateDynamicCollider(Component component)
        {
            PhysicsSources.Add(new PhysBoneSourceSettings
            {
                hierarchyPath = AnimationUtility.CalculateTransformPath(component.transform, _root),
                componentType = component.GetType().FullName,
                unitySettingsJson = EditorJsonUtility.ToJson(component)
            });
            if (OptionalComponent.Get(component, "insideBounds", false))
            { Warnings.Add(component.name + ": 内側拘束コライダーは未対応のため省略しました。"); return null; }
            string shape = OptionalComponent.Get(component, "shapeType")?.ToString();
            if (shape != "Sphere" && shape != "Capsule")
            { Warnings.Add(component.name + ": " + shape + " コライダーは未対応のため省略しました。"); return null; }
            var root = OptionalComponent.Get<Transform>(component, "rootTransform");
            if (root == null) root = component.transform;
            if (!root.IsChildOf(_root)) { Warnings.Add(component.name + ": ルート外のコライダーは省略しました。"); return null; }
            float radius = OptionalComponent.Get(component, "radius", 0f);
            if (radius <= 0f) { Warnings.Add(component.name + ": 半径 0 のコライダーは省略しました。"); return null; }
            var rotation = OptionalComponent.Get(component, "rotation");
            Quaternion rotationValue = rotation is Quaternion quaternion ? quaternion :
                rotation is Vector3 euler ? Quaternion.Euler(euler) : Quaternion.identity;
            return new p.DynamicCollider
            {
                TargetTransform = MapObject(root),
                Type = shape == "Capsule" ? p.ColliderType.Capsule : p.ColliderType.Sphere,
                Radius = radius,
                Height = Mathf.Max(0, OptionalComponent.Get(component, "height", 0f)),
                PositionOffset = OptionalComponent.Get(component, "position", Vector3.zero).ToRPC(),
                RotationOffset = rotationValue.ToRPC()
            };
        }
    }
}
