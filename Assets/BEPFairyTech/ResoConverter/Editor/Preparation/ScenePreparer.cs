using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BEPFairyTech.ResoConverter
{
    internal sealed class PreparedScene : IDisposable
    {
        internal GameObject Root;
        internal SceneAvatarInfo Avatar;
        internal readonly List<string> Warnings = new();
        internal readonly List<Object> OwnedAssets = new();
        internal Scene PreviewScene;
        public void Dispose()
        {
            if (Root != null) Object.DestroyImmediate(Root);
            foreach (var asset in OwnedAssets) if (asset != null) Object.DestroyImmediate(asset);
            if (PreviewScene.IsValid()) EditorSceneManager.ClosePreviewScene(PreviewScene);
        }
    }

    internal static class ScenePreparer
    {
        internal static PreparedScene Prepare(GameObject source, bool processModularAvatar, bool freezePose,
            SkinnedMeshRenderer blinkRenderer, string blinkShape)
        {
            if (source == null || !source.scene.IsValid() || !source.scene.isLoaded)
                throw new InvalidOperationException("シーン上に配置したモデルを選択してください。");
            var prepared = new PreparedScene();
            try
            {
                prepared.PreviewScene = EditorSceneManager.NewPreviewScene();
                prepared.Root = Object.Instantiate(source);
                prepared.Root.name = source.name;
                SceneManager.MoveGameObjectToScene(prepared.Root, prepared.PreviewScene);
                // A selected child can have scaled parents; export its actual world size.
                prepared.Root.transform.localScale = source.transform.lossyScale;
                var clonedBlink = ResolveClone(source.transform, prepared.Root.transform, blinkRenderer);
                var poses = prepared.Root.GetComponentsInChildren<Transform>(true)
                    .Select(t => new PoseSnapshot(t)).ToArray();
                var shapes = prepared.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Select(renderer => new BlendshapeSnapshot(renderer)).ToArray();
                if (processModularAvatar) BakeModularAvatar(prepared);
                if (freezePose)
                {
                    foreach (var pose in poses) pose.Restore();
                    foreach (var shape in shapes) shape.Restore();
                    FreezeMeshes(prepared);
                }
                prepared.Avatar = SceneAvatarInfo.Read(prepared.Root, clonedBlink, blinkShape);
                prepared.Root.transform.position = Vector3.zero;
                prepared.Root.transform.rotation = Quaternion.identity;
                prepared.Warnings.AddRange(prepared.Avatar.Warnings);
                foreach (var component in prepared.Root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null) prepared.Warnings.Add("Missing Script が存在します。削除済みパッケージの設定は復元できません。");
                    else if ((component.GetType().FullName ?? "").StartsWith("VRC.SDK3.Dynamics.Constraint"))
                        prepared.Warnings.Add("VRC Constraint の継続的な駆動は移植されません。現在の Transform の状態を出力します。");
                }
                return prepared;
            }
            catch { prepared.Dispose(); throw; }
        }

        private static SkinnedMeshRenderer ResolveClone(Transform source, Transform clone, SkinnedMeshRenderer target)
        {
            if (target == null || !target.transform.IsChildOf(source)) return null;
            var indices = new Stack<int>();
            for (var t = target.transform; t != source; t = t.parent) indices.Push(t.GetSiblingIndex());
            while (indices.Count > 0) clone = clone.GetChild(indices.Pop());
            int componentIndex = Array.IndexOf(target.GetComponents<SkinnedMeshRenderer>(), target);
            return clone.GetComponents<SkinnedMeshRenderer>().ElementAtOrDefault(componentIndex);
        }

        private static void BakeModularAvatar(PreparedScene prepared)
        {
            bool hasMA = prepared.Root.GetComponentsInChildren<Component>(true).Any(c => c != null &&
                (c.GetType().FullName ?? "").StartsWith("nadena.dev.modular_avatar.core.ModularAvatar"));
            if (!hasMA) return;
            var processor = OptionalComponent.FindType("nadena.dev.ndmf.AvatarProcessor");
            var phase = OptionalComponent.FindType("nadena.dev.ndmf.BuildPhase");
            var scopeType = OptionalComponent.FindType("nadena.dev.ndmf.OverrideTemporaryDirectoryScope");
            if (processor == null) { BakeSimpleModularAvatar(prepared); return; }
            IDisposable scope = scopeType == null ? null : (IDisposable)Activator.CreateInstance(scopeType, new object[] { null });
            try
            {
                // Stop before optional optimizer plugins can delete user-selected blink shapes.
                var method = phase == null ? null : processor.GetMethod("ProcessAvatar", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(GameObject), phase }, null);
                var transformPhase = phase?.GetField("Transforming", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                    ?? phase?.GetProperty("Transforming", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (method != null && transformPhase != null)
                {
                    var context = method.Invoke(null, new[] { (object)prepared.Root, transformPhase });
                    // NDMF can record a failed pass without throwing its exception outward.
                    if (context != null && !OptionalComponent.Get(context, "Successful", true))
                        throw new InvalidOperationException("Modular Avatar / NDMF の前処理でエラーが報告されました。NDMF Console の内容を修正してから再実行してください。");
                }
                else
                {
                    method = processor.GetMethod("ProcessAvatar", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameObject) }, null);
                    if (method == null) { BakeSimpleModularAvatar(prepared); return; }
                    prepared.Warnings.Add("この NDMF では全ビルド工程を使用しました。最適化設定により形状が変わる場合があります。");
                    method.Invoke(null, new object[] { prepared.Root });
                }
                prepared.Warnings.Add("Modular Avatar の配置・統合を複製上で適用しました。Expression Menu / Animator の操作ロジックは移植対象外です。");
            }
            catch (TargetInvocationException e)
            { throw new InvalidOperationException("Modular Avatar の前処理に失敗しました。元のシーンは変更していません。", e.InnerException ?? e); }
            finally { scope?.Dispose(); }
        }

        private static void BakeSimpleModularAvatar(PreparedScene prepared)
        {
            var components = prepared.Root.GetComponentsInChildren<Component>(true).Where(c => c != null).ToArray();
            foreach (var component in components)
            {
                string name = component.GetType().Name;
                if (name == "ModularAvatarBoneProxy")
                {
                    var target = OptionalComponent.Get<Transform>(component, "target");
                    if (target == null || !target.IsChildOf(prepared.Root.transform) || target.IsChildOf(component.transform))
                        throw new InvalidOperationException(component.name + ": Bone Proxy の対象が不正です。");
                    string mode = OptionalComponent.Get(component, "attachmentMode")?.ToString();
                    var position = component.transform.position;
                    var rotation = component.transform.rotation;
                    component.transform.SetParent(target, true);
                    component.transform.localPosition = Vector3.zero;
                    component.transform.localRotation = Quaternion.identity;
                    if (mode == "AsChildKeepWorldPose" || mode == "AsChildKeepPosition") component.transform.position = position;
                    if (mode == "AsChildKeepWorldPose" || mode == "AsChildKeepRotation") component.transform.rotation = rotation;
                    if (OptionalComponent.Get(component, "matchScale", false)) component.transform.localScale = Vector3.one;
                }
                else if (name == "ModularAvatarMergeArmature")
                {
                    var method = component.GetType().GetMethod("GetBonesMapping", Type.EmptyTypes);
                    var pairs = method?.Invoke(component, null) as System.Collections.IEnumerable;
                    var mapping = new List<(Transform target, Transform source)>();
                    if (pairs != null) foreach (var pair in pairs)
                    {
                        var target = OptionalComponent.Get<Transform>(pair, "Item1");
                        var source = OptionalComponent.Get<Transform>(pair, "Item2");
                        if (target != null && source != null && target.IsChildOf(prepared.Root.transform) && !target.IsChildOf(source)) mapping.Add((target, source));
                    }
                    foreach (var pair in mapping) pair.source.SetParent(pair.target, true);
                }
                else if (name.StartsWith("ModularAvatar")) prepared.Warnings.Add(component.name + ": NDMF がないため " + name + " は未適用です。");
            }
            prepared.Warnings.Add("NDMF がないため Bone Proxy / Merge Armature の基本追従だけを適用しました。");
        }

        private static void FreezeMeshes(PreparedScene prepared)
        {
            foreach (var skin in prepared.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null) continue;
                var mesh = new Mesh { name = skin.sharedMesh.name + " (posed)" };
                skin.BakeMesh(mesh, false);
                prepared.OwnedAssets.Add(mesh);
                var host = new GameObject(skin.gameObject.name + " (Fixed Pose)");
                host.transform.SetParent(skin.transform, false);
                host.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = host.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = skin.sharedMaterials;
                renderer.enabled = skin.enabled;
                renderer.shadowCastingMode = skin.shadowCastingMode;
                renderer.receiveShadows = skin.receiveShadows;
                Object.DestroyImmediate(skin);
            }
            foreach (var component in prepared.Root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                string type = component.GetType().FullName ?? "";
                if (component is Animator || type.StartsWith("VRC.SDK3.Dynamics.PhysBone")) Object.DestroyImmediate(component);
            }
        }

        private sealed class PoseSnapshot
        {
            readonly Transform transform, parent;
            readonly Vector3 position, scale;
            readonly Quaternion rotation;
            internal PoseSnapshot(Transform t) { transform = t; parent = t.parent; position = t.localPosition; rotation = t.localRotation; scale = t.localScale; }
            internal void Restore()
            {
                if (transform == null || transform.parent != parent) return;
                transform.localPosition = position; transform.localRotation = rotation; transform.localScale = scale;
            }
        }

        private sealed class BlendshapeSnapshot
        {
            readonly SkinnedMeshRenderer renderer;
            readonly Dictionary<string, float> weights = new();
            internal BlendshapeSnapshot(SkinnedMeshRenderer value)
            {
                renderer = value;
                if (value.sharedMesh == null) return;
                for (int i = 0; i < value.sharedMesh.blendShapeCount; i++)
                    weights[value.sharedMesh.GetBlendShapeName(i)] = value.GetBlendShapeWeight(i);
            }
            internal void Restore()
            {
                if (renderer == null || renderer.sharedMesh == null) return;
                foreach (var pair in weights)
                {
                    int index = renderer.sharedMesh.GetBlendShapeIndex(pair.Key);
                    if (index >= 0) renderer.SetBlendShapeWeight(index, pair.Value);
                }
            }
        }
    }
}
