// Adapted from Modular Avatar for Resonite, Copyright (c) 2025 bd_, MIT license.
// See Serialization/UPSTREAM-LICENSE.txt.
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using nadena.dev.ndmf.proto.mesh;
using UnityEditor;
using UnityEngine;
using BoneWeight = nadena.dev.ndmf.proto.mesh.BoneWeight;
using Mesh = UnityEngine.Mesh;
using p = nadena.dev.ndmf.proto;

namespace BEPFairyTech.ResoConverter
{
    internal partial class AvatarSerializer
    {
        private ulong nextAssetID = 1;
        private ulong nextObjectID = 1;

        private Dictionary<UnityEngine.Object, p.AssetID> _unityToAsset = new();
        private Dictionary<UnityEngine.Object, IMessage?> _protoAssets = new();
        private Dictionary<UnityEngine.Object, p.ObjectID> _unityToObject = new();
        private Dictionary<Mesh, SkinnedMeshRenderer> _referenceRenderer = new();

        private p.ExportRoot _exportRoot = new();

        private List<UnityEngine.Object> _tempObjects = new();

        private List<IShaderTranslator> _shaderTranslators;

        private Transform? _avatarHead;

        internal readonly List<string> Warnings = new();
        private Transform? _root;

        internal AvatarSerializer()
        {
            _shaderTranslators = new()
            {
                new LiltoonShaderSupport(ImportTexture, Warnings.Add),
                new GenericShaderTranslator(ImportTexture, Warnings.Add),
            };
        }

        private bool ImportTexture(Texture? tex, Texture? importReference, out p.AssetID? assetID, out p.Texture? translatedTex)
        {
            assetID = MapAsset(tex, importReference, out var protoAsset);
            translatedTex = protoAsset as p.Texture;
            return assetID.Id != 0;
        }
        
        private p.AssetID MintAssetID()
        {
            return new p.AssetID() { Id = nextAssetID++ };
        }

        private p.ObjectID MintObjectID()
        {
            return new p.ObjectID() { Id = nextObjectID++ };
        }

        private p.AssetID MapAsset(UnityEngine.Object? asset, UnityEngine.Object? referenceAsset = null)
        {
            return MapAsset(asset, referenceAsset, out _);
        }

        private p.AssetID MapAsset(UnityEngine.Object? asset, UnityEngine.Object? referenceAsset, out IMessage? protoAsset)
        {
            protoAsset = null;
            if (asset == null) return new p.AssetID() { Id = 0 };
            if (_unityToAsset.TryGetValue(asset, out var id))
            {
                protoAsset = _protoAssets.GetValueOrDefault(asset);
                return id;
            }

            referenceAsset = referenceAsset != null ? referenceAsset : asset;

            _unityToAsset[asset] = id = MintAssetID();

            switch (asset)
            {
                case Texture2D tex2d: protoAsset = TranslateTexture2D(tex2d, referenceAsset); break;
                case Material mat: protoAsset = TranslateMaterial(mat); break;
                case Mesh mesh: protoAsset = TranslateMesh(mesh); break;
                default: protoAsset = null; break;
            }

            if (protoAsset != null)
            {
                _protoAssets[asset] = protoAsset;
                
                p.Asset wrapper = new()
                {
                    Name = asset.name,
                    Id = id,
                    Asset_ = Any.Pack(protoAsset),
                };

                var base_asset = asset;
                if (AssetDatabase.Contains(base_asset))
                {
                    var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(base_asset));
                    wrapper.StableId = guid;
                } 

                _exportRoot.Assets.Add(wrapper);
            }

            return id;
        }

        private p.ObjectID MapObject(UnityEngine.Object? obj)
        {
            if (obj == null) return new p.ObjectID() { Id = 0 };
            if (obj is Transform t) obj = t.gameObject;
            var owner = obj is GameObject go ? go.transform : (obj as Component)?.transform;
            if (_root != null && owner != null && !owner.IsChildOf(_root))
            {
                Warnings.Add(obj.name + ": 選択ルート外への参照は移植されません。");
                return new p.ObjectID();
            }
            if (_unityToObject.TryGetValue(obj, out var id)) return id;

            _unityToObject[obj] = id = MintObjectID();

            return id;
        }

        internal Task<p.ExportRoot> Export(GameObject go, SceneAvatarInfo info, bool asAvatar)
        {
            try
            {
                _root = go.transform;
                var animator = go.GetComponent<Animator>();
                if (asAvatar && (animator == null || !animator.isHuman || animator.avatar == null || !animator.avatar.isValid))
                    throw new InvalidOperationException("アバター出力には有効な Humanoid Animator が必要です。3Dモデルアイテムとして出力できます。");
                if (asAvatar) _avatarHead = animator!.GetBoneTransform(HumanBodyBones.Head);
                AddPhysBoneEndpoints(go);
                _exportRoot.Root = CreateTransforms(go.transform);
                if (asAvatar)
                {
                    _exportRoot.Root.Components.Add(new p.Component { Enabled = true, Id = MintObjectID(), Component_ = Any.Pack(new p.RigRoot()) });
                    _exportRoot.Root.Components.Add(new p.Component { Enabled = true, Id = MintObjectID(), Component_ = Any.Pack(TranslateAvatarDescriptor(animator!, info)) });
                }
                _exportRoot.Versions.Add(new p.VersionInfo { PackageName = "BEPFairyTech.ResoConverter", Version = ResoConverter.Version });
                // Translators can finish texture usage flags after ImportTexture returns.
                // Any.Pack takes a snapshot, so refresh those snapshots after all materials.
                var wrappers = _exportRoot.Assets.ToDictionary(asset => asset.Id.Id);
                foreach (var asset in _protoAssets)
                    if (asset.Value != null && wrappers.TryGetValue(_unityToAsset[asset.Key].Id, out var wrapper))
                        wrapper.Asset_ = Any.Pack(asset.Value);
                return Task.FromResult(_exportRoot);
            }
            finally
            {
                foreach (var obj in _tempObjects) if (obj) UnityEngine.Object.DestroyImmediate(obj);
                foreach (var translator in _shaderTranslators) translator.Dispose();
            }
        }

        private p.GameObject CreateTransforms(Transform t)
        {
            var protoObject = new p.GameObject();
            protoObject.Name = t.gameObject.name;
            protoObject.Id = MapObject(t.gameObject);
            protoObject.Enabled = t.gameObject.activeSelf;
            protoObject.LocalTransform = new p.Transform()
            {
                Position = t.localPosition.ToRPC(),
                Rotation = t.localRotation.ToRPC(),
                Scale = t.localScale.ToRPC()
            };

            bool hasVHA = false;
            
            foreach (Component c in t.gameObject.GetComponents<Component>())
            {
                IMessage? protoComponent;
                switch (c)
                {
                    case MeshRenderer mr:
                        protoComponent = TranslateMeshRenderer(mr);
                        break;
                    case SkinnedMeshRenderer smr:
                        protoComponent = TranslateSkinnedMeshRenderer(smr);
                        break;
                    default:
                        if (c == null) { Warnings.Add(t.name + ": Missing Script は移植できません。"); continue; }
                        string typeName = c.GetType().FullName ?? "";
                        if (typeName == "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider") protoComponent = TranslateDynamicCollider(c);
                        else if (typeName == "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone") protoComponent = TranslateDynamicBone(c);
                        else if (typeName.EndsWith("TagVisibleInFirstPerson") || typeName.EndsWith("ModularAvatarVisibleHeadAccessory"))
                        { protoComponent = new p.VisibleInFirstPerson { Visible = true }; hasVHA = true; }
                        else continue;
                        break;
                }

                if (protoComponent == null) continue;

                p.Component wrapper = new p.Component()
                {
                    Enabled = c is Renderer renderer ? renderer.enabled : (c as Behaviour)?.enabled ?? true,
                    Id = MapObject(c),
                    Component_ = Any.Pack(protoComponent)
                };

                protoObject.Components.Add(wrapper);
            }

            if (!hasVHA && _avatarHead == t)
            {
                protoObject.Components.Add(new p.Component()
                {
                    Enabled = true,
                    Id = MintObjectID(),
                    Component_ = Any.Pack(new p.VisibleInFirstPerson() { Visible = false })
                });
            }
            
            foreach (Transform child in t)
            {
                if (!child.CompareTag("EditorOnly")) protoObject.Children.Add(CreateTransforms(child));
            }

            return protoObject;
        }

        private IMessage TranslateAvatarDescriptor(Animator uAnimator, SceneAvatarInfo avDesc)
        {
            var avatarDesc = new p.AvatarDescriptor();

            avatarDesc.EyePosition = avDesc.EyePosition.ToRPC();

            if (uAnimator != null && uAnimator.isHuman) TransferHumanoidBones(avatarDesc, uAnimator);

            if (avDesc.VisemeRenderer != null)
            {
                var visemes = new p.VisemeConfig();
                avatarDesc.VisemeConfig = visemes;
                visemes.VisemeMesh = MapObject(avDesc.VisemeRenderer);
                
                SetVisemeShape(avDesc.VisemeBlendshapes, "sil", (s) => visemes.ShapeSilence = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, "PP", (s) => visemes.ShapePP = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, "FF", (s) => visemes.ShapeFF = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, "TH", (s) => visemes.ShapeTH = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, "DD", (s) => visemes.ShapeDD = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, "kk", (s) => visemes.ShapeKk = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, "CH", (s) => visemes.ShapeCH = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, "SS", (s) => visemes.ShapeSS = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, "nn", (s) => visemes.ShapeNn = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, "RR", (s) => visemes.ShapeRR = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, "aa", (s) => visemes.ShapeAa = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, "E", (s) => visemes.ShapeE = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, "ih", (s) => visemes.ShapeIh = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, "oh", (s) => visemes.ShapeOh = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, "ou", (s) => visemes.ShapeOu = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, "laugh", (s) => visemes.ShapeLaugh = s);
            }

            if (avDesc.BlinkRenderer != null && !string.IsNullOrEmpty(avDesc.BlinkBlendshape))
            {
                avatarDesc.EyelookConfig ??= new p.EyelookConfig();
                avatarDesc.EyelookConfig.Blendshape = new p.BlendshapeEyelidConfig { EyelidMesh = MapObject(avDesc.BlinkRenderer), Blink = avDesc.BlinkBlendshape };
            }
            if (avDesc.JawBone != null)
            {
                avatarDesc.JawBone = MapObject(avDesc.JawBone);
                avatarDesc.JawClosedRotation = avDesc.JawClosedRotation.ToRPC();
                avatarDesc.JawOpenRotation = avDesc.JawOpenRotation.ToRPC();
            }
            return avatarDesc;
            
            
            void SetVisemeShape(Dictionary<string, string> visemeBlendshapes, string name, Action<string> setter)
            {
                if (visemeBlendshapes.TryGetValue(name, out var shape))
                {
                    setter(shape);
                }
            }
        }

        private void TransferHumanoidBones(p.AvatarDescriptor avatarDesc, Animator uAnimator)
        {
            if (avatarDesc.EyelookConfig == null) avatarDesc.EyelookConfig = new();
            avatarDesc.EyelookConfig.LeftEyeTransform = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftEye));
            avatarDesc.EyelookConfig.RightEyeTransform = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightEye));
            
            var bones = avatarDesc.Bones = new();
            bones.Head = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.Head));
            bones.Chest = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.Chest));
            bones.Spine = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.Spine));
            bones.UpperChest = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.UpperChest));
            bones.Neck = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.Neck));
            bones.Hips = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.Hips));

            bones.LeftArm = new();
            bones.LeftArm.Shoulder = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftShoulder));
            bones.LeftArm.UpperArm = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm));
            bones.LeftArm.LowerArm = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm));
            bones.LeftArm.Hand = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftHand));

            bones.LeftArm.Index = new()
            {
                Proximal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftIndexProximal)),
                Intermediate = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftIndexIntermediate)),
                Distal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftIndexDistal))
            };
            bones.LeftArm.Middle = new()
            {
                Proximal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal)),
                Intermediate = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftMiddleIntermediate)),
                Distal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftMiddleDistal))
            };
            bones.LeftArm.Ring = new()
            {
                Proximal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftRingProximal)),
                Intermediate = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftRingIntermediate)),
                Distal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftRingDistal))
            };
            bones.LeftArm.Pinky = new()
            {
                Proximal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftLittleProximal)),
                Intermediate = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftLittleIntermediate)),
                Distal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftLittleDistal))
            };
            bones.LeftArm.Thumb = new()
            {
                Proximal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftThumbProximal)),
                Intermediate = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftThumbIntermediate)),
                Distal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftThumbDistal))
            };
            
            bones.RightArm = new();
            bones.RightArm.Shoulder = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightShoulder));
            bones.RightArm.UpperArm = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm));
            bones.RightArm.LowerArm = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm));
            bones.RightArm.Hand = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightHand));
            bones.RightArm.Index = new()
            {
                Proximal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightIndexProximal)),
                Intermediate = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightIndexIntermediate)),
                Distal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightIndexDistal))
            };
            bones.RightArm.Middle = new()
            {
                Proximal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightMiddleProximal)),
                Intermediate = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightMiddleIntermediate)),
                Distal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightMiddleDistal))
            };
            bones.RightArm.Ring = new()
            {
                Proximal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightRingProximal)),
                Intermediate = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightRingIntermediate)),
                Distal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightRingDistal))
            };
            bones.RightArm.Pinky = new()
            {
                Proximal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightLittleProximal)),
                Intermediate = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightLittleIntermediate)),
                Distal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightLittleDistal))
            };
            bones.RightArm.Thumb = new()
            {
                Proximal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightThumbProximal)),
                Intermediate = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightThumbIntermediate)),
                Distal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightThumbDistal))
            };
            
            bones.LeftLeg = new()
            {
                UpperLeg = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg)),
                LowerLeg = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg)),
                Foot = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftFoot)),
                Toe = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftToes))
            };
            
            bones.RightLeg = new()
            {
                UpperLeg = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg)),
                LowerLeg = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg)),
                Foot = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightFoot)),
                Toe = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightToes))
            };
        }

        private IMessage? TranslateMeshRenderer(MeshRenderer r)
        {
            var meshFilter = r.GetComponent<MeshFilter>();

            var protoMeshRenderer = new p.MeshRenderer();

            var sharedMesh = meshFilter != null ? meshFilter.sharedMesh : null;
            if (sharedMesh == null) return null;

            TranslateRendererCommon(r, sharedMesh, protoMeshRenderer);

            return protoMeshRenderer;
        }

        private IMessage? TranslateSkinnedMeshRenderer(SkinnedMeshRenderer r)
        {
            var protoMeshRenderer = new p.MeshRenderer();

            var sharedMesh = r.sharedMesh;
            if (sharedMesh == null) return null;
            if (_root != null && r.bones.Any(bone => bone != null && !bone.IsChildOf(_root)))
                throw new InvalidOperationException(r.name + ": 選択ルート外のボーンに依存しています。アバター全体を選択するか、3Dモデルのポーズ固定を有効にしてください。");

            if (sharedMesh != null) _referenceRenderer[sharedMesh] = r;

            TranslateRendererCommon(r, sharedMesh, protoMeshRenderer);

            foreach (Transform bone in r.bones)
            {
                protoMeshRenderer.Bones.Add(MapObject(bone?.gameObject));
            }

            if (sharedMesh != null)
            {
                var blendshapes = sharedMesh.blendShapeCount;
                for (int i = 0; i < blendshapes; i++)
                {
                    protoMeshRenderer.BlendshapeWeights.Add(r.GetBlendShapeWeight(i) / 100.0f);
                }
            }

            return protoMeshRenderer;
        }

        private void TranslateRendererCommon(Renderer r, Mesh? sharedMesh, p.MeshRenderer proto)
        {
            proto.Mesh = MapAsset(sharedMesh);
            foreach (var mat in r.sharedMaterials)
            {
                proto.Materials.Add(MapAsset(mat));
            }

            // Unity repeats the last material when there are more submeshes than materials; however FrooxEngine
            // ignores the extra submeshes. Copy the material references to work around this.
            while (sharedMesh != null && sharedMesh.subMeshCount > proto.Materials.Count && proto.Materials.Count > 0)
            {
                proto.Materials.Add(proto.Materials[^1]);
            }
        }
        
        private IMessage TranslateTexture2D(Texture2D tex2d, UnityEngine.Object refAsset)
        {
            var refTex = refAsset as Texture2D;
            if (refTex == null) refTex = tex2d;
            var protoTex = new p.Texture();
            
            // Get texture importer for this texture, if available
            var textureImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(refTex)) as TextureImporter;
            if (textureImporter != null)
            {
                // Only set max resolution if the texture was not baked
                // (if we baked the texture, it's possible we might use a higher resolution after merging multiple
                // layers, and there's also no point going to a higher resolution than the baked texture)
                if (refTex == tex2d && textureImporter.maxTextureSize > 0)
                {
                    protoTex.MaxResolution = (uint) textureImporter.maxTextureSize;
                }

                protoTex.IsNormalMap = textureImporter.textureType == TextureImporterType.NormalMap;
            }
            else
            {
                protoTex.IsNormalMap = false;
            }

            if (AssetDatabase.IsMainAsset(tex2d))
            {
                var filePath = AssetDatabase.GetAssetPath(tex2d);

                protoTex.Bytes = new() { Inline = ByteString.CopyFrom(File.ReadAllBytes(filePath)) };

                bool supported = true;
                if (filePath.ToLowerInvariant().EndsWith(".png")) protoTex.Format = p.TextureFormat.Png;
                else if (filePath.ToLowerInvariant().EndsWith(".jpg") || filePath.ToLowerInvariant().EndsWith(".jpeg"))
                    protoTex.Format = p.TextureFormat.Jpeg;
                else supported = false;

                if (supported) return protoTex;
            }
        
            // Convert to PNG (blit if necessary)
            byte[]? png = null;
            try
            {
                png = tex2d.EncodeToPNG();
            }
            catch (Exception)
            {
                // continue with blit path below.
            }
            if (png == null)
            {
                // Transform texture into an encodable format. We can't use CopyTexture as formats will be different
                // here.
                var tmpTex = new Texture2D(tex2d.width, tex2d.height, TextureFormat.ARGB32, false);
                var tmpRT = RenderTexture.GetTemporary(tex2d.width, tex2d.height, 0, RenderTextureFormat.ARGB32);
                var priorRT = RenderTexture.active;
                try
                {
                    Graphics.Blit(tex2d, tmpRT);
                    RenderTexture.active = tmpRT;
                    
                    tmpTex.ReadPixels(new Rect(0, 0, tex2d.width, tex2d.height), 0, 0);
                }
                finally
                {
                    RenderTexture.active = priorRT;
                    RenderTexture.ReleaseTemporary(tmpRT);
                }

                png = tmpTex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tmpTex);
            }
            protoTex.Bytes = new() { Inline = ByteString.CopyFrom(png) };
            protoTex.Format = p.TextureFormat.Png;

            return protoTex;
        
        }

        private IMessage? TranslateMaterial(Material material)
        {
            foreach (var handler in _shaderTranslators)
            {
                if (handler.TryTranslateMaterial(material, out var protoMat)) return protoMat;
            }

            return null;
        }

        private p.mesh.Mesh TranslateMesh(UnityEngine.Mesh mesh)
        {
            SkinnedMeshRenderer? referenceSMR = _referenceRenderer.GetValueOrDefault(mesh);

            var msgMesh = new p.mesh.Mesh();
            msgMesh.Positions.AddRange(mesh.vertices.Select(v => new p.Vector() { X = v.x, Y = v.y, Z = v.z }));
            msgMesh.Normals.AddRange(mesh.normals.Select(v => new p.Vector() { X = v.x, Y = v.y, Z = v.z }));
            msgMesh.Tangents.AddRange(mesh.tangents.Select(v => new p.Vector() { X = v.x, Y = v.y, Z = v.z, W = v.w }));
            msgMesh.Colors.AddRange(mesh.colors.Select(c => new p.Color() { R = c.r, G = c.g, B = c.b, A = c.a }));

            for (int channel = 0; channel < 8; channel++)
            {
                var uvs = new List<Vector4>();
                mesh.GetUVs(channel, uvs);
                if (uvs.Count == 0 && channel > 0) break;
                var uv = new p.mesh.UVChannel();
                uv.Uvs.AddRange(uvs.Select(v => v.ToRPC()));
                msgMesh.Uvs.Add(uv);
            }
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                if (mesh.GetTopology(i) != MeshTopology.Triangles)
                    throw new InvalidOperationException(mesh.name + ": 三角形以外のメッシュは出力できません。");
                var submesh = new p.mesh.Submesh { Triangles = new() };
                var indices = mesh.GetTriangles(i, true);
                for (int v = 0; v + 2 < indices.Length; v += 3)
                    submesh.Triangles.Triangles.Add(new p.mesh.Triangle { V0 = indices[v], V1 = indices[v + 1], V2 = indices[v + 2] });
                msgMesh.Submeshes.Add(submesh);
            }

            var refBones = referenceSMR?.bones;
            var bindposes = mesh.bindposes;
            for (int i = 0; i < bindposes.Length; i++)
            {
                var refBone = (refBones != null && i < refBones.Length) ? refBones[i] : null;
                var boneName = refBone != null ? refBone.gameObject.name : "Bone" + i;
                var mat = new p.Matrix();
                var pose = bindposes[i];

                mat.Values.Capacity = 16;
                mat.Values.Add(pose.m00);
                mat.Values.Add(pose.m01);
                mat.Values.Add(pose.m02);
                mat.Values.Add(pose.m03);
                mat.Values.Add(pose.m10);
                mat.Values.Add(pose.m11);
                mat.Values.Add(pose.m12);
                mat.Values.Add(pose.m13);
                mat.Values.Add(pose.m20);
                mat.Values.Add(pose.m21);
                mat.Values.Add(pose.m22);
                mat.Values.Add(pose.m23);
                mat.Values.Add(pose.m30);
                mat.Values.Add(pose.m31);
                mat.Values.Add(pose.m32);
                mat.Values.Add(pose.m33);

                msgMesh.Bones.Add(new p.mesh.Bone() { Name = boneName, Bindpose = mat });
            }

            using (var counts = mesh.GetBonesPerVertex())
            using (var weights = mesh.GetAllBoneWeights())
            {
                int offset = 0;
                bool warnedInfluences = false;
                for (int v = 0; v < counts.Length; v++)
                {
                    if (counts[v] > 4 && !warnedInfluences)
                    {
                        Warnings.Add(mesh.name + ": 1頂点に5本以上のボーン影響があります。Resonite出力では上位4本に正規化されます。");
                        warnedInfluences = true;
                    }
                    var vbw = new VertexBoneWeights();
                    for (int i = 0; i < counts[v]; i++)
                    {
                        var weight = weights[offset++];
                        if (weight.weight > 0) vbw.BoneWeights.Add(new BoneWeight { Weight = weight.weight, BoneIndex = (uint)weight.boneIndex });
                    }
                    msgMesh.VertexBoneWeights.Add(vbw);
                }
            }

            Vector3[] delta_position = new Vector3[mesh.vertexCount];
            Vector3[] delta_normal = new Vector3[mesh.vertexCount];
            Vector3[] delta_tangent = new Vector3[mesh.vertexCount];

            int blendshapeCount = mesh.blendShapeCount;
            for (int i = 0; i < blendshapeCount; i++)
            {
                var name = mesh.GetBlendShapeName(i);
                var frames = mesh.GetBlendShapeFrameCount(i);

                var rpcBlendshape = new p.mesh.Blendshape()
                {
                    Name = name,
                    Frames = { }
                };

                for (int f = 0; f < frames; f++)
                {
                    var frame = new p.mesh.BlendshapeFrame();
                    frame.Weight = mesh.GetBlendShapeFrameWeight(i, f) / 100.0f;
                    mesh.GetBlendShapeFrameVertices(i, f, delta_position, delta_normal, delta_tangent);

                    frame.DeltaPositions.AddRange(delta_position.Select(v => v.ToRPC()));
                    if (delta_normal.Any(n => n.sqrMagnitude > 0.0001f))
                    {
                        frame.DeltaNormals.AddRange(delta_normal.Select(v => v.ToRPC()));
                    }

                    if (delta_tangent.Any(t => t.sqrMagnitude > 0.0001f))
                    {
                        frame.DeltaTangents.AddRange(delta_tangent.Select(v => v.ToRPC()));
                    }

                    rpcBlendshape.Frames.Add(frame);
                }

                msgMesh.Blendshapes.Add(rpcBlendshape);
            }

            return msgMesh;
        }
    }

}
