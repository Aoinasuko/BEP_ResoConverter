using System;
using UnityEngine;

namespace BEPFairyTech.ResoConverter
{
    public enum ExpressionHandPose { Any = -1, Idle = 0, Fist = 1, Open = 2, Point = 3, Victory = 4, RockNRoll = 5, HandGun = 6, ThumbsUp = 7 }

    [Serializable]
    public class ExpressionClipSettings
    {
        public AnimationClip Clip;
        public float SampleTime;
    }

    [Serializable]
    public sealed class HandExpressionSettings : ExpressionClipSettings
    {
        public ExpressionHandPose Left = ExpressionHandPose.Any;
        public ExpressionHandPose Right = ExpressionHandPose.Any;
    }

    [Serializable]
    public sealed class MenuExpressionSettings : ExpressionClipSettings
    {
        public string Name = "表情";
    }

    [Serializable]
    internal sealed class BackendExpressionSettings
    {
        public bool handEnabled;
        public bool menuEnabled;
        public BackendExpressionTarget[] targets = Array.Empty<BackendExpressionTarget>();
        public BackendHandExpression[] handRules = Array.Empty<BackendHandExpression>();
        public BackendMenuExpression[] menuEntries = Array.Empty<BackendMenuExpression>();
    }

    [Serializable]
    internal sealed class BackendExpressionTarget
    {
        public ulong rendererId;
        public string blendShape;
        public float baseline;
    }

    [Serializable]
    internal sealed class BackendExpressionValue
    {
        public int target;
        public float value;
    }

    [Serializable]
    internal sealed class BackendHandExpression
    {
        public string left;
        public string right;
        public BackendExpressionValue[] values;
    }

    [Serializable]
    internal sealed class BackendMenuExpression
    {
        public string name;
        public BackendExpressionValue[] values;
    }
}
