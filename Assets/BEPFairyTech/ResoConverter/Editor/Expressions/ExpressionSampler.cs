using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BEPFairyTech.ResoConverter
{
    internal sealed class SampledBlendShape
    {
        internal SkinnedMeshRenderer SourceRenderer;
        internal string BlendShape;
        internal float Weight;
    }

    internal sealed class ExpressionSample
    {
        internal readonly List<SampledBlendShape> Values = new();
        internal readonly List<string> Warnings = new();
    }

    internal static class ExpressionSampler
    {
        private const string BlendShapePrefix = "blendShape.";

        private sealed class SupportedBinding
        {
            internal EditorCurveBinding Binding;
            internal AnimationCurve Curve;
            internal SkinnedMeshRenderer Renderer;
            internal string Shape;
        }

        internal static void ValidateClip(GameObject source, AnimationClip clip, float sampleTime, string label,
            List<string> errors, List<string> warnings = null)
        {
            ReadBindings(source, clip, sampleTime, label, errors, warnings);
        }

        internal static ExpressionSample Sample(GameObject source, AnimationClip clip, float sampleTime, string label)
        {
            var result = new ExpressionSample();
            var errors = new List<string>();
            var bindings = ReadBindings(source, clip, sampleTime, label, errors, result.Warnings);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));

            Scene preview = default;
            GameObject sampleRoot = null;
            AnimationClip filteredClip = null;
            try
            {
                preview = EditorSceneManager.NewPreviewScene();
                sampleRoot = new GameObject(source.name) { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(sampleRoot, preview);
                var transforms = new Dictionary<Transform, Transform>();
                CopyHierarchy(source.transform, sampleRoot.transform, transforms);
                var renderers = new Dictionary<SkinnedMeshRenderer, SkinnedMeshRenderer>();
                foreach (var binding in bindings)
                {
                    if (renderers.ContainsKey(binding.Renderer)) continue;
                    var copied = transforms[binding.Renderer.transform].gameObject.AddComponent<SkinnedMeshRenderer>();
                    copied.sharedMesh = binding.Renderer.sharedMesh;
                    for (int i = 0; i < copied.sharedMesh.blendShapeCount; i++)
                        copied.SetBlendShapeWeight(i, binding.Renderer.GetBlendShapeWeight(i));
                    renderers.Add(binding.Renderer, copied);
                }

                // Do not copy scripts, controllers, materials or animation events. Only supported
                // BlendShape curves are sampled, so shared assets and the source scene stay untouched.
                filteredClip = new AnimationClip { name = "BEP Expression Sample", hideFlags = HideFlags.HideAndDontSave, wrapMode = WrapMode.ClampForever };
                foreach (var binding in bindings)
                    AnimationUtility.SetEditorCurve(filteredClip, binding.Binding, binding.Curve);
                filteredClip.SampleAnimation(sampleRoot, sampleTime);
                foreach (var binding in bindings)
                {
                    var copied = renderers[binding.Renderer];
                    float weight = copied.GetBlendShapeWeight(copied.sharedMesh.GetBlendShapeIndex(binding.Shape));
                    if (!IsFinite(weight)) throw new InvalidOperationException(label + ": 表情のBlendShape値が有限数ではありません。");
                    result.Values.Add(new SampledBlendShape { SourceRenderer = binding.Renderer, BlendShape = binding.Shape, Weight = weight });
                }
                return result;
            }
            finally
            {
                if (filteredClip != null) Object.DestroyImmediate(filteredClip);
                if (sampleRoot != null) Object.DestroyImmediate(sampleRoot);
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static List<SupportedBinding> ReadBindings(GameObject source, AnimationClip clip, float sampleTime,
            string label, List<string> errors, List<string> warnings)
        {
            var result = new List<SupportedBinding>();
            if (source == null) { errors.Add(label + ": 表情の対象ルートを指定してください。"); return result; }
            if (clip == null) { errors.Add(label + ": AnimationClipを指定してください。"); return result; }
            if (!IsFinite(sampleTime) || sampleTime < 0 || sampleTime > clip.length + 0.00001f)
                errors.Add(label + ": サンプル時刻は0秒からクリップの長さ（" + clip.length.ToString("0.###") + "秒）までで指定してください。");
            int unsupported = AnimationUtility.GetObjectReferenceCurveBindings(clip).Length;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(SkinnedMeshRenderer) || !binding.propertyName.StartsWith(BlendShapePrefix, StringComparison.Ordinal))
                { unsupported++; continue; }
                string targetName = string.IsNullOrEmpty(binding.path) ? source.name : binding.path;
                var target = FindUniqueTransform(source.transform, binding.path);
                if (target == null)
                { errors.Add(label + ": BlendShapeの対象「" + targetName + "」が見つからないか、同名の階層が複数あります。"); continue; }
                var renderers = target.GetComponents<SkinnedMeshRenderer>();
                if (renderers.Length != 1 || renderers[0].sharedMesh == null)
                { errors.Add(label + ": 「" + targetName + "」にメッシュを持つSkinnedMeshRendererを1つ配置してください。"); continue; }
                string shape = binding.propertyName.Substring(BlendShapePrefix.Length);
                if (renderers[0].sharedMesh.GetBlendShapeIndex(shape) < 0)
                { errors.Add(label + ": 「" + targetName + "」にBlendShape「" + shape + "」が見つかりません。"); continue; }
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0)
                { errors.Add(label + ": BlendShape「" + shape + "」にキーフレームがありません。"); continue; }
                if (IsFinite(sampleTime) && !IsFinite(curve.Evaluate(sampleTime)))
                { errors.Add(label + ": BlendShape「" + shape + "」の値が有限数ではありません。"); continue; }
                result.Add(new SupportedBinding { Binding = binding, Curve = curve, Renderer = renderers[0], Shape = shape });
            }
            if (unsupported != 0)
                warnings?.Add(label + ": BlendShape以外のカーブ（" + unsupported + "件）は表情へ移植しません。オブジェクトの有効・無効、Rendererの表示、材質、Transform、Animatorの制御には対応していません。");
            if (clip.events.Length != 0)
                warnings?.Add(label + ": Animation Eventは表情へ移植しません。");
            if (result.Count == 0) errors.Add(label + ": 対象アバターに適用できるBlendShapeカーブがありません。");
            return result;
        }

        private static Transform FindUniqueTransform(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;
            foreach (string segment in path.Split('/'))
            {
                Transform match = null;
                foreach (Transform child in root)
                    if (child.name == segment)
                    {
                        if (match != null) return null;
                        match = child;
                    }
                if (match == null) return null;
                root = match;
            }
            return root;
        }

        private static void CopyHierarchy(Transform source, Transform target, Dictionary<Transform, Transform> transforms)
        {
            transforms.Add(source, target);
            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
            // The sampling skeleton stays active, allowing clips to target initially inactive face meshes.
            foreach (Transform child in source)
            {
                var copy = new GameObject(child.name) { hideFlags = HideFlags.HideAndDontSave };
                copy.transform.SetParent(target, false);
                CopyHierarchy(child, copy.transform, transforms);
            }
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
