using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BEPFairyTech.ResoConverter
{
    /// <summary>Project asset containing export settings, without scene or machine references.</summary>
    public sealed class ResoConverterPreset : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;

        [SerializeField, HideInInspector] private int schemaVersion = CurrentSchemaVersion;
        [SerializeField, HideInInspector] private bool hasSettings;
        [SerializeField, HideInInspector] private PresetData data;

        public int SchemaVersion => schemaVersion;

        /// <summary>Validates a detached snapshot before replacing any saved settings.</summary>
        public bool TryCapture(GameObject source, ConversionOptions options, out string[] warnings, out string error)
        {
            var notices = new List<string>();
            warnings = Array.Empty<string>();
            error = null;
            if (!CheckVersion(out error) || !CheckSource(source, out error)) return false;
            if (options == null) { error = "保存する出力設定がありません。"; return false; }

            var snapshot = new PresetData
            {
                Kind = options.Kind,
                LockSaving = options.LockSaving,
                SizeMode = options.SizeMode,
                FreezePose = options.FreezePose,
                ProcessModularAvatar = options.ProcessModularAvatar,
                ToonShadowStrength = options.ToonShadowStrength,
                BlinkShape = options.BlinkShape ?? "",
                EnableHandExpressions = options.EnableHandExpressions,
                UseControllerHandPoses = options.UseControllerHandPoses,
                EnableMenuExpressions = options.EnableMenuExpressions
            };
            if (!TryCaptureBlink(source, options.BlinkRenderer, snapshot, notices, out error)) return false;

            foreach (var row in options.HandExpressions ?? new List<HandExpressionSettings>())
            {
                if (!CheckClip(row?.Clip, "ハンドサイン表情", snapshot.HandExpressions.Count, out error)) return false;
                if (row == null) notices.Add($"ハンドサイン表情の{snapshot.HandExpressions.Count + 1}行目が空のため、未設定の行として保存しました。");
                snapshot.HandExpressions.Add(new StoredHandExpression
                {
                    Left = row?.Left ?? ExpressionHandPose.Any,
                    Right = row?.Right ?? ExpressionHandPose.Any,
                    Clip = row?.Clip,
                    HadClip = row?.Clip != null,
                    ClipName = row?.Clip != null ? row.Clip.name : "",
                    SampleTime = row?.SampleTime ?? 0
                });
            }
            foreach (var row in options.MenuExpressions ?? new List<MenuExpressionSettings>())
            {
                if (!CheckClip(row?.Clip, "メニュー表情", snapshot.MenuExpressions.Count, out error)) return false;
                if (row == null) notices.Add($"メニュー表情の{snapshot.MenuExpressions.Count + 1}行目が空のため、未設定の行として保存しました。");
                snapshot.MenuExpressions.Add(new StoredMenuExpression
                {
                    Name = row?.Name ?? "表情",
                    Clip = row?.Clip,
                    HadClip = row?.Clip != null,
                    ClipName = row?.Clip != null ? row.Clip.name : "",
                    SampleTime = row?.SampleTime ?? 0
                });
            }

            data = snapshot;
            hasSettings = true;
            schemaVersion = CurrentSchemaVersion;
            warnings = notices.ToArray();
            return true;
        }

        /// <summary>Returns a new settings instance; neither the preset nor current settings are changed.</summary>
        public bool TryRestore(GameObject source, ConversionOptions current, out ConversionOptions restored,
            out string[] warnings, out string error)
        {
            restored = null;
            warnings = Array.Empty<string>();
            error = null;
            if (!CheckVersion(out error) || !CheckSource(source, out error)) return false;
            if (!hasSettings || data == null) { error = "このプリセットには出力設定が保存されていません。"; return false; }
            var notices = new List<string>();
            if (!TryResolveBlink(source, data, notices, out var blinkRenderer, out error)) return false;

            var result = new ConversionOptions
            {
                Kind = data.Kind,
                LockSaving = data.LockSaving,
                SizeMode = data.SizeMode,
                FreezePose = data.FreezePose,
                ProcessModularAvatar = data.ProcessModularAvatar,
                ToonShadowStrength = data.ToonShadowStrength,
                BlinkRenderer = blinkRenderer,
                BlinkShape = data.BlinkShape ?? "",
                EnableHandExpressions = data.EnableHandExpressions,
                UseControllerHandPoses = data.UseControllerHandPoses,
                EnableMenuExpressions = data.EnableMenuExpressions,
                ResonitePath = current?.ResonitePath ?? ""
            };
            foreach (var row in data.HandExpressions ?? new List<StoredHandExpression>())
            {
                if (row == null)
                {
                    notices.Add($"ハンドサイン表情の{result.HandExpressions.Count + 1}行目が空のため、未設定の行として復元しました。");
                    result.HandExpressions.Add(new HandExpressionSettings());
                    continue;
                }
                WarnMissingClip(row, "ハンドサイン表情", result.HandExpressions.Count, notices);
                result.HandExpressions.Add(new HandExpressionSettings
                    { Left = row.Left, Right = row.Right, Clip = row.Clip, SampleTime = row.SampleTime });
            }
            foreach (var row in data.MenuExpressions ?? new List<StoredMenuExpression>())
            {
                if (row == null)
                {
                    notices.Add($"メニュー表情の{result.MenuExpressions.Count + 1}行目が空のため、未設定の行として復元しました。");
                    result.MenuExpressions.Add(new MenuExpressionSettings());
                    continue;
                }
                WarnMissingClip(row, "メニュー表情", result.MenuExpressions.Count, notices);
                result.MenuExpressions.Add(new MenuExpressionSettings
                    { Name = row.Name, Clip = row.Clip, SampleTime = row.SampleTime });
            }
            restored = result;
            warnings = notices.ToArray();
            return true;
        }

        private bool CheckVersion(out string error)
        {
            error = null;
            if (schemaVersion == CurrentSchemaVersion) return true;
            error = schemaVersion > CurrentSchemaVersion
                ? $"このプリセットは新しい形式（{schemaVersion}）で保存されています。ResoConverterを更新してください。"
                : $"このプリセットの形式（{schemaVersion}）には対応していません。";
            return false;
        }

        private static bool CheckSource(GameObject source, out string error)
        {
            error = null;
            if (source != null && !EditorUtility.IsPersistent(source) && source.scene.IsValid()) return true;
            error = "Hierarchy上の変換対象を選択してからプリセットを保存・復元してください。";
            return false;
        }

        private static bool CheckClip(AnimationClip clip, string label, int index, out string error)
        {
            error = null;
            if (clip == null || (EditorUtility.IsPersistent(clip) && AssetDatabase.Contains(clip))) return true;
            error = $"{label}の{index + 1}行目「{clip.name}」は一時的なAnimationClipです。先にProject内のアセットとして保存してください。";
            return false;
        }

        private static void WarnMissingClip(StoredClip row, string label, int index, List<string> notices)
        {
            if (row.HadClip && row.Clip == null)
                notices.Add($"{label}の{index + 1}行目のAnimationClip「{row.ClipName}」が見つかりません。行は保持しています。アニメーションを再指定してください。");
        }

        private static bool TryCaptureBlink(GameObject source, SkinnedMeshRenderer renderer, PresetData snapshot,
            List<string> notices, out string error)
        {
            error = null;
            if (renderer == null)
            {
                if (!string.IsNullOrEmpty(snapshot.BlinkShape))
                    notices.Add($"瞬き「{snapshot.BlinkShape}」のメッシュが未設定です。blendshape名は保持しています。");
                return true;
            }
            if (!renderer.transform.IsChildOf(source.transform))
            {
                error = "瞬き用メッシュが変換対象の外にあります。対象内のメッシュを選択してください。";
                return false;
            }
            var segments = new List<string>();
            for (var node = renderer.transform; node != source.transform; node = node.parent)
            {
                if (MatchingChildren(node.parent, node.name).Count != 1)
                {
                    error = $"瞬き用メッシュへの階層に同名の子「{node.name}」が複数あります。名前を一意にしてから保存してください。";
                    return false;
                }
                segments.Add(node.name);
            }
            segments.Reverse();
            var components = renderer.GetComponents<SkinnedMeshRenderer>();
            var locator = new BlinkLocator
            {
                TransformNames = segments.ToArray(),
                RendererCount = components.Length,
                MeshName = renderer.sharedMesh != null ? renderer.sharedMesh.name : "",
                MeshVertexCount = renderer.sharedMesh != null ? renderer.sharedMesh.vertexCount : -1,
                BlendShapeNames = ShapeNames(renderer.sharedMesh)
            };
            if (components.Length > 1 && (renderer.sharedMesh == null || components.Count(c => MatchesMesh(c.sharedMesh, locator)) != 1))
            {
                error = "瞬き用オブジェクトに同じメッシュ構成のSkinnedMeshRendererが複数あり、復元先を特定できません。別のGameObjectへ分けてください。";
                return false;
            }
            snapshot.Blink = locator;
            snapshot.HasBlinkRenderer = true;
            WarnBlinkMesh(renderer, snapshot.BlinkShape, notices);
            return true;
        }

        private static bool TryResolveBlink(GameObject source, PresetData snapshot, List<string> notices,
            out SkinnedMeshRenderer renderer, out string error)
        {
            renderer = null;
            error = null;
            var locator = snapshot.Blink;
            if (!snapshot.HasBlinkRenderer || locator == null)
            {
                if (!string.IsNullOrEmpty(snapshot.BlinkShape))
                    notices.Add($"瞬き「{snapshot.BlinkShape}」のメッシュが保存されていません。blendshape名は保持しています。メッシュを再指定してください。");
                return true;
            }
            var target = source.transform;
            foreach (string segment in locator.TransformNames ?? Array.Empty<string>())
            {
                var matches = MatchingChildren(target, segment);
                if (matches.Count == 0)
                {
                    notices.Add($"瞬き用メッシュの階層「{string.Join(" → ", locator.TransformNames)}」が見つかりません。blendshape名は保持しています。メッシュを再指定してください。");
                    return true;
                }
                if (matches.Count > 1)
                {
                    error = $"復元先の階層に同名の子「{segment}」が複数あり、瞬き用メッシュを特定できません。名前を一意にしてください。";
                    return false;
                }
                target = matches[0];
            }
            var components = target.GetComponents<SkinnedMeshRenderer>();
            if (components.Length == 0)
            {
                notices.Add($"瞬き用の「{target.name}」にSkinnedMeshRendererがありません。メッシュを再指定してください。");
                return true;
            }
            if (components.Length != locator.RendererCount)
            {
                error = $"瞬き用の「{target.name}」のSkinnedMeshRenderer数が保存時と異なるため、復元先を特定できません（保存時{locator.RendererCount}、現在{components.Length}）。";
                return false;
            }
            SkinnedMeshRenderer resolved;
            if (components.Length == 1) resolved = components[0];
            else
            {
                var matches = components.Where(c => MatchesMesh(c.sharedMesh, locator)).ToArray();
                if (matches.Length > 1)
                {
                    error = $"瞬き用の「{target.name}」に同じメッシュ構成のSkinnedMeshRendererが複数あり、復元先を特定できません。";
                    return false;
                }
                if (matches.Length == 0)
                {
                    notices.Add($"瞬き用の「{target.name}」に保存時のメッシュ「{locator.MeshName}」が見つかりません。メッシュを再指定してください。");
                    return true;
                }
                resolved = matches[0];
            }
            if (WarnBlinkMesh(resolved, snapshot.BlinkShape, notices)) renderer = resolved;
            return true;
        }

        private static bool WarnBlinkMesh(SkinnedMeshRenderer renderer, string shape, List<string> notices)
        {
            if (renderer.sharedMesh == null)
            {
                notices.Add($"瞬き用の「{renderer.name}」にメッシュがありません。blendshape名は保持しています。メッシュを再指定してください。");
                return false;
            }
            if (!string.IsNullOrEmpty(shape) && renderer.sharedMesh.GetBlendShapeIndex(shape) < 0)
            {
                notices.Add($"瞬き用の「{renderer.name}」にblendshape「{shape}」がありません。名前は保持しています。メッシュとblendshapeを再指定してください。");
                return false;
            }
            return true;
        }

        private static List<Transform> MatchingChildren(Transform parent, string name)
        {
            var matches = new List<Transform>();
            for (int i = 0; i < parent.childCount; i++)
                if (string.Equals(parent.GetChild(i).name, name, StringComparison.Ordinal)) matches.Add(parent.GetChild(i));
            return matches;
        }

        private static string[] ShapeNames(Mesh mesh)
        {
            if (mesh == null) return Array.Empty<string>();
            var names = new string[mesh.blendShapeCount];
            for (int i = 0; i < names.Length; i++) names[i] = mesh.GetBlendShapeName(i);
            return names;
        }

        private static bool MatchesMesh(Mesh mesh, BlinkLocator locator) => mesh != null &&
            string.Equals(mesh.name, locator.MeshName, StringComparison.Ordinal) && mesh.vertexCount == locator.MeshVertexCount &&
            ShapeNames(mesh).SequenceEqual(locator.BlendShapeNames ?? Array.Empty<string>(), StringComparer.Ordinal);

        [Serializable]
        private sealed class PresetData
        {
            public ExportKind Kind;
            public bool LockSaving;
            public AvatarSizeMode SizeMode;
            public bool FreezePose;
            public bool ProcessModularAvatar;
            public float ToonShadowStrength;
            public bool HasBlinkRenderer;
            public BlinkLocator Blink;
            public string BlinkShape;
            public bool EnableHandExpressions;
            public bool UseControllerHandPoses;
            public bool EnableMenuExpressions;
            public List<StoredHandExpression> HandExpressions = new();
            public List<StoredMenuExpression> MenuExpressions = new();
        }

        [Serializable]
        private sealed class BlinkLocator
        {
            public string[] TransformNames;
            public int RendererCount;
            public string MeshName;
            public int MeshVertexCount;
            public string[] BlendShapeNames;
        }

        [Serializable]
        private class StoredClip
        {
            public AnimationClip Clip;
            public bool HadClip;
            public string ClipName;
            public float SampleTime;
        }

        [Serializable]
        private sealed class StoredHandExpression : StoredClip
        {
            public ExpressionHandPose Left;
            public ExpressionHandPose Right;
        }

        [Serializable]
        private sealed class StoredMenuExpression : StoredClip
        {
            public string Name;
        }
    }
}
