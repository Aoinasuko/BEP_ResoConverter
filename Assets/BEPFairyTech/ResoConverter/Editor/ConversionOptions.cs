using System;
using System.Collections.Generic;
using UnityEngine;

namespace BEPFairyTech.ResoConverter
{
    public enum ExportKind { Avatar, Model }
    public enum AvatarSizeMode { ResoniteStandard, SourceSize }

    [Serializable]
    public sealed class ConversionOptions
    {
        public ExportKind Kind = ExportKind.Avatar;
        public bool LockSaving = true;
        public AvatarSizeMode SizeMode = AvatarSizeMode.ResoniteStandard;
        public bool FreezePose = true;
        public bool ProcessModularAvatar = true;
        public SkinnedMeshRenderer BlinkRenderer;
        public string BlinkShape = "";
        public bool EnableHandExpressions;
        public bool EnableMenuExpressions;
        public List<HandExpressionSettings> HandExpressions = new();
        public List<MenuExpressionSettings> MenuExpressions = new();
        public string ResonitePath = "";
    }

    [Serializable]
    internal sealed class BackendSettings
    {
        public bool lockSaving;
        public bool asAvatar;
        public bool useStandardSize;
        public float standardHeight = 1.8f;
        public BackendExpressionSettings expressions;
    }

    [Serializable]
    public sealed class ConversionReport
    {
        public string toolVersion = ResoConverter.Version;
        public string sourceName;
        public string outputPath;
        public string exportedAtUtc;
        public string outputKind;
        public bool lockSaving;
        public bool frozenPose;
        public string sizeMode;
        public string blinkShape;
        public int renderers;
        public int triangles;
        public string[] warnings;
        public SourceComponentSettings[] physBoneSourceSettings;
    }

    [Serializable]
    public sealed class SourceComponentSettings
    {
        public string hierarchyPath;
        public string componentType;
        public string unitySettingsJson;
    }
}
