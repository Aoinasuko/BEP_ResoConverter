using System;
using System.Collections.Generic;
using UnityEngine;

namespace BEPFairyTech.ResoConverter
{
    internal sealed class SceneEyeLookInfo
    {
        internal bool Enabled;
        internal SceneEyeRotationInfo Left;
        internal SceneEyeRotationInfo Right;

        internal static SceneEyeLookInfo Read(object descriptor, Animator animator, List<string> warnings)
        {
            // The absence of VRC settings is different from an explicitly disabled eye look.
            if (descriptor == null) return null;
            bool enabled = OptionalComponent.Get(descriptor, "enableEyeLook", false);
            object settings = OptionalComponent.Get(descriptor, "customEyeLookSettings");
            return new SceneEyeLookInfo {
                Enabled = enabled,
                Left = ReadEye("left", HumanBodyBones.LeftEye),
                Right = ReadEye("right", HumanBodyBones.RightEye)
            };

            SceneEyeRotationInfo ReadEye(string side, HumanBodyBones humanoidBone)
            {
                var bone = OptionalComponent.Get<Transform>(settings, side + "Eye");
                if (bone == null && animator != null && animator.isHuman)
                    bone = animator.GetBoneTransform(humanoidBone);
                if (bone == null) return null;
                var straight = enabled ? ReadRotation("eyesLookingStraight", bone.localRotation) : bone.localRotation;
                return new SceneEyeRotationInfo {
                    Bone = bone,
                    Straight = straight,
                    Up = enabled ? ReadRotation("eyesLookingUp", straight) : straight,
                    Down = enabled ? ReadRotation("eyesLookingDown", straight) : straight,
                    Left = enabled ? ReadRotation("eyesLookingLeft", straight) : straight,
                    Right = enabled ? ReadRotation("eyesLookingRight", straight) : straight
                };

                Quaternion ReadRotation(string field, Quaternion fallback)
                {
                    var value = OptionalComponent.Get(OptionalComponent.Get(settings, field), side);
                    if (!(value is Quaternion rotation)) return fallback;
                    float lengthSquared = Quaternion.Dot(rotation, rotation);
                    if (float.IsNaN(lengthSquared) || float.IsInfinity(lengthSquared) || lengthSquared < 0.000001f)
                    {
                        warnings?.Add("Eye Look の「" + field + "/" + side + "」が無効な回転のため、正面または元のボーン姿勢を使用しました。");
                        return fallback;
                    }
                    // These are local rotations captured by the VRC descriptor, not Euler angles.
                    return rotation.normalized;
                }
            }
        }
    }

    internal sealed class SceneEyeRotationInfo
    {
        internal Transform Bone;
        internal Quaternion Straight;
        internal Quaternion Up;
        internal Quaternion Down;
        internal Quaternion Left;
        internal Quaternion Right;
    }

    [Serializable]
    internal sealed class BackendEyeLookSettings
    {
        public bool configured;
        public bool enabled;
        public BackendEyeRotationSettings left;
        public BackendEyeRotationSettings right;
    }

    [Serializable]
    internal sealed class BackendEyeRotationSettings
    {
        public ulong boneId;
        public Quaternion straight;
        public Quaternion up;
        public Quaternion down;
        public Quaternion left;
        public Quaternion right;
    }
}
