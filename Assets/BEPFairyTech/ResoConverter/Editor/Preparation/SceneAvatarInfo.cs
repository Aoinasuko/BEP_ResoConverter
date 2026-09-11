using System.Collections.Generic;
using UnityEngine;

namespace BEPFairyTech.ResoConverter
{
    internal sealed class SceneAvatarInfo
    {
        internal Vector3 EyePosition;
        internal SkinnedMeshRenderer VisemeRenderer;
        internal readonly Dictionary<string, string> VisemeBlendshapes = new();
        internal SkinnedMeshRenderer BlinkRenderer;
        internal string BlinkBlendshape;
        internal Transform JawBone;
        internal Quaternion JawClosedRotation = Quaternion.identity;
        internal Quaternion JawOpenRotation = Quaternion.identity;
        internal SceneEyeLookInfo EyeLook;
        internal readonly List<string> Warnings = new();

        internal static SceneAvatarInfo Read(GameObject root, SkinnedMeshRenderer blinkRenderer, string blinkShape)
        {
            var info = new SceneAvatarInfo();
            var descriptor = OptionalComponent.On(root, "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            var animator = root.GetComponent<Animator>();
            info.EyeLook = SceneEyeLookInfo.Read(descriptor, animator, info.Warnings);
            // VRC stores the viewpoint as a world-distance offset from the avatar root.
            // Convert it to root coordinates so root scale is not applied twice in Resonite.
            if (descriptor != null)
                info.EyePosition = root.transform.InverseTransformVector(OptionalComponent.Get(descriptor, "ViewPosition", Vector3.zero));
            else if (animator != null && animator.isHuman)
            {
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null) info.EyePosition = root.transform.InverseTransformPoint(head.position) + new Vector3(0, 0.08f, 0.08f);
            }
            if (info.EyePosition.y <= 0.001f)
            {
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                float height = 1.6f;
                foreach (var renderer in renderers)
                    height = Mathf.Max(height, root.transform.InverseTransformPoint(renderer.bounds.max).y);
                info.EyePosition = new Vector3(0, height * 0.92f, 0.08f);
                info.Warnings.Add("視点が未設定のため、頭部またはメッシュの高さから推定しました。");
            }

            string style = OptionalComponent.Get(descriptor, "lipSync")?.ToString() ?? "Default";
            var face = OptionalComponent.Get<SkinnedMeshRenderer>(descriptor, "VisemeSkinnedMesh");
            if (style == "VisemeBlendShape" && face != null && face.sharedMesh != null)
            {
                string[] names = { "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "ih", "oh", "ou" };
                var shapes = OptionalComponent.Get<string[]>(descriptor, "VisemeBlendShapes");
                info.VisemeRenderer = face;
                for (int i = 0; shapes != null && i < names.Length && i < shapes.Length; i++)
                    if (!string.IsNullOrEmpty(shapes[i]) && face.sharedMesh.GetBlendShapeIndex(shapes[i]) >= 0)
                        info.VisemeBlendshapes[names[i]] = shapes[i];
            }
            else if (style == "JawFlapBlendShape" && face != null && face.sharedMesh != null)
            {
                string shape = OptionalComponent.Get<string>(descriptor, "MouthOpenBlendShapeName");
                if (!string.IsNullOrEmpty(shape) && face.sharedMesh.GetBlendShapeIndex(shape) >= 0)
                {
                    info.VisemeRenderer = face;
                    foreach (string name in new[] { "aa", "E", "ih", "oh", "ou" }) info.VisemeBlendshapes[name] = shape;
                    info.Warnings.Add("Jaw Flap BlendShape を母音の口形へ割り当てました。音量だけの開閉とは動きが異なります。");
                }
            }
            else if (style == "JawFlapBone")
            {
                info.JawBone = OptionalComponent.Get<Transform>(descriptor, "lipSyncJawBone");
                info.JawClosedRotation = OptionalComponent.Get(descriptor, "lipSyncJawClosed", Quaternion.identity);
                info.JawOpenRotation = OptionalComponent.Get(descriptor, "lipSyncJawOpen", Quaternion.identity);
            }
            else if (style == "VisemeParameterOnly")
                info.Warnings.Add("Viseme Parameter Only の Animator 制御は口パクへ自動変換できません。");

            if (descriptor == null || style == "Default")
            {
                foreach (var meshRenderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (meshRenderer.sharedMesh == null) continue;
                    var matches = new Dictionary<string, string>();
                    foreach (string name in new[] { "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "ih", "oh", "ou" })
                        foreach (string candidate in new[] { "vrc.v_" + name.ToLowerInvariant(), "v_" + name.ToLowerInvariant() })
                            if (meshRenderer.sharedMesh.GetBlendShapeIndex(candidate) >= 0) { matches[name] = candidate; break; }
                    if (matches.Count <= info.VisemeBlendshapes.Count) continue;
                    info.VisemeRenderer = meshRenderer;
                    info.VisemeBlendshapes.Clear();
                    foreach (var pair in matches) info.VisemeBlendshapes.Add(pair.Key, pair.Value);
                }
            }

            if (!string.IsNullOrEmpty(blinkShape))
            {
                if (blinkRenderer != null && blinkRenderer.sharedMesh != null && blinkRenderer.sharedMesh.GetBlendShapeIndex(blinkShape) >= 0)
                { info.BlinkRenderer = blinkRenderer; info.BlinkBlendshape = blinkShape; }
                else info.Warnings.Add("選択した瞬き BlendShape が前処理後のメッシュに見つかりません。");
            }
            return info;
        }
    }
}
