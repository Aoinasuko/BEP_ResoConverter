using System;
using System.Collections.Generic;
using UnityEngine;

namespace BEPFairyTech.ResoConverter
{
    internal static class ExpressionExporter
    {
        internal static List<string> Validate(GameObject source, ConversionOptions options)
        {
            var errors = new List<string>();
            if (options == null || options.Kind != ExportKind.Avatar) return errors;
            if (options.EnableHandExpressions)
            {
                if (options.HandExpressions == null || options.HandExpressions.Count == 0)
                    errors.Add("手の形による表情を有効にする場合、表情を1件以上設定してください。");
                else for (int i = 0; i < options.HandExpressions.Count; i++)
                {
                    var row = options.HandExpressions[i];
                    string label = "手の表情 " + (i + 1);
                    if (row == null) { errors.Add(label + ": 表情を設定してください。"); continue; }
                    if (!Enum.IsDefined(typeof(ExpressionHandPose), row.Left) || !Enum.IsDefined(typeof(ExpressionHandPose), row.Right))
                        errors.Add(label + ": 左右の手の形を選び直してください。");
                    ExpressionSampler.ValidateClip(source, row.Clip, row.SampleTime, label, errors);
                }
            }
            if (options.EnableMenuExpressions)
            {
                if (options.MenuExpressions == null || options.MenuExpressions.Count == 0)
                    errors.Add("メニューによる表情を有効にする場合、表情を1件以上設定してください。");
                else
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < options.MenuExpressions.Count; i++)
                    {
                        var row = options.MenuExpressions[i];
                        string label = "メニューの表情 " + (i + 1);
                        if (row == null) { errors.Add(label + ": 表情を設定してください。"); continue; }
                        if (string.IsNullOrWhiteSpace(row.Name)) errors.Add(label + ": メニュー名を入力してください。");
                        else if (row.Name.Trim() == "初期状態" || row.Name.Trim() == "ハンドサインに戻す")
                            errors.Add(label + ": 「" + row.Name.Trim() + "」は自動で作成するメニュー項目のため、別の名前を入力してください。");
                        else if (!names.Add(row.Name.Trim())) errors.Add(label + ": メニュー名「" + row.Name.Trim() + "」が重複しています。");
                        ExpressionSampler.ValidateClip(source, row.Clip, row.SampleTime, label, errors);
                    }
                }
            }
            return errors;
        }

        internal static BackendExpressionSettings Build(GameObject source, ConversionOptions options,
            PreparedScene prepared, AvatarSerializer serializer, List<string> warnings)
        {
            var result = new BackendExpressionSettings();
            if (options == null || options.Kind != ExportKind.Avatar) return result;
            result.handEnabled = options.EnableHandExpressions;
            result.menuEnabled = options.EnableMenuExpressions;
            if (!result.handEnabled && !result.menuEnabled) return result;
            var errors = Validate(source, options);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
            if (prepared == null) throw new ArgumentNullException(nameof(prepared));
            if (serializer == null) throw new ArgumentNullException(nameof(serializer));

            var targets = new List<BackendExpressionTarget>();
            var indices = new Dictionary<(ulong renderer, string shape), int>();
            var handRules = new List<BackendHandExpression>();
            var menuEntries = new List<BackendMenuExpression>();
            if (result.handEnabled)
            {
                for (int i = 0; i < options.HandExpressions.Count; i++)
                {
                    var row = options.HandExpressions[i];
                    handRules.Add(new BackendHandExpression {
                        left = row.Left.ToString(), right = row.Right.ToString(),
                        values = SampleValues(row, "手の表情 " + (i + 1))
                    });
                    for (int earlier = 0; earlier < i; earlier++)
                    {
                        var prior = options.HandExpressions[earlier];
                        if ((prior.Left == ExpressionHandPose.Any || prior.Left == row.Left) &&
                            (prior.Right == ExpressionHandPose.Any || prior.Right == row.Right))
                        {
                            warnings?.Add("手の表情 " + (i + 1) + ": 上の行（" + (earlier + 1) + "）が常に優先される条件です。行の順序と左右の手の条件を確認してください。");
                            break;
                        }
                    }
                }
            }
            if (result.menuEnabled)
                for (int i = 0; i < options.MenuExpressions.Count; i++)
                {
                    var row = options.MenuExpressions[i];
                    menuEntries.Add(new BackendMenuExpression { name = row.Name.Trim(), values = SampleValues(row, "メニューの表情 " + (i + 1)) });
                }
            result.targets = targets.ToArray();
            result.handRules = handRules.ToArray();
            result.menuEntries = menuEntries.ToArray();
            return result;

            BackendExpressionValue[] SampleValues(ExpressionClipSettings row, string label)
            {
                var sample = ExpressionSampler.Sample(source, row.Clip, row.SampleTime, label);
                warnings?.AddRange(sample.Warnings);
                var values = new List<BackendExpressionValue>();
                foreach (var value in sample.Values)
                {
                    if (!prepared.SourceRenderers.TryGetValue(value.SourceRenderer, out var renderer) || renderer == null || renderer.sharedMesh == null)
                        throw new InvalidOperationException(label + ": 表情の対象「" + value.SourceRenderer.name + "」が前処理で削除・置換されました。対象メッシュを統合する設定を確認してください。");
                    int shapeIndex = renderer.sharedMesh.GetBlendShapeIndex(value.BlendShape);
                    if (shapeIndex < 0)
                        throw new InvalidOperationException(label + ": 前処理後のメッシュにBlendShape「" + value.BlendShape + "」がありません。");
                    if (!serializer.TryGetExportedObjectId(renderer, out ulong id))
                        throw new InvalidOperationException(label + ": 表情の対象「" + value.SourceRenderer.name + "」が出力されません。EditorOnlyなどの除外設定を確認してください。");
                    var key = (id, value.BlendShape);
                    if (!indices.TryGetValue(key, out int index))
                    {
                        float baseline = renderer.GetBlendShapeWeight(shapeIndex) / 100f;
                        if (float.IsNaN(baseline) || float.IsInfinity(baseline))
                            throw new InvalidOperationException(label + ": 前処理後のBlendShape初期値が有限数ではありません。");
                        index = targets.Count;
                        indices.Add(key, index);
                        targets.Add(new BackendExpressionTarget { rendererId = id, blendShape = value.BlendShape, baseline = baseline });
                    }
                    values.Add(new BackendExpressionValue { target = index, value = value.Weight / 100f });
                }
                return values.ToArray();
            }
        }
    }
}
