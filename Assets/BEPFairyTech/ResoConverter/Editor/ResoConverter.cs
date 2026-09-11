using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using UnityEditor;
using UnityEngine;

namespace BEPFairyTech.ResoConverter
{
    public static class ResoConverter
    {
        public const string Version = "0.3.1";
        public static bool IsBusy { get; private set; }
        public static string Status { get; private set; }
        public static ConversionReport LastReport { get; private set; }
        public static string LastError { get; private set; }
        private static CancellationTokenSource cancellation;
        private static readonly ConcurrentQueue<string> Messages = new ConcurrentQueue<string>();

        public static void Cancel() => cancellation?.Cancel();

        public static List<string> Validate(GameObject source, ConversionOptions options)
        {
            var problems = new List<string>();
            if (source == null) { problems.Add("Hierarchyから変換対象を指定してください。"); return problems; }
            if (EditorUtility.IsPersistent(source) || !source.scene.IsValid() || !source.scene.isLoaded)
                problems.Add("Project内のPrefabではなく、シーン上のオブジェクトを指定してください。");
            else if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(source.scene))
                problems.Add("Prefab編集モードやプレビューではなく、通常のシーンに配置したオブジェクトを指定してください。");
            if (EditorApplication.isPlayingOrWillChangePlaymode) problems.Add("Play Modeを終了してから変換してください。");
            if (float.IsNaN(options.ToonShadowStrength) || float.IsInfinity(options.ToonShadowStrength) || options.ToonShadowStrength < 0 || options.ToonShadowStrength > 1)
                problems.Add("トゥーン影の濃さは0～1で指定してください。");
            if (!source.GetComponentsInChildren<Renderer>(true).Any(r =>
                (r is SkinnedMeshRenderer skin && skin.sharedMesh != null && skin.sharedMesh.vertexCount > 0) ||
                (r is MeshRenderer && r.GetComponent<MeshFilter>()?.sharedMesh != null && r.GetComponent<MeshFilter>().sharedMesh.vertexCount > 0)))
                problems.Add("変換できるメッシュがありません。");
            if (options.Kind == ExportKind.Avatar)
            {
                var animator = source.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.isHuman)
                    problems.Add("アバター出力には、対象ルートのAnimatorに有効なHumanoid Avatarが必要です。");
                if (!string.IsNullOrEmpty(options.BlinkShape))
                {
                    if (options.BlinkRenderer == null || !options.BlinkRenderer.transform.IsChildOf(source.transform) ||
                        options.BlinkRenderer.sharedMesh == null || options.BlinkRenderer.sharedMesh.GetBlendShapeIndex(options.BlinkShape) < 0)
                        problems.Add("瞬き用BlendShapeを選び直してください。");
                }
                problems.AddRange(ExpressionExporter.Validate(source, options));
                if (!BackendRunner.SupportsAvatarEyeLook() ||
                    ((options.EnableHandExpressions || options.EnableMenuExpressions) && !BackendRunner.SupportsFacialExpressions()))
                    problems.Add("目の動き・瞬きの修正に対応した変換エンジンがありません。ResoConverter v0.3.1以降のパッケージを、変換エンジンも含めてインポートしてください。");
            }
            if (!BackendRunner.IsResoniteFolder(options.ResonitePath))
                problems.Add("Resonite本体のフォルダーを指定してください（FrooxEngine.dllがある場所）。");
            return problems;
        }

        public static async Task<ConversionReport> ExportAsync(GameObject source, ConversionOptions options, string outputPath)
        {
            if (IsBusy) throw new InvalidOperationException("変換はすでに実行中です。");
            var errors = Validate(source, options);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
            if (string.IsNullOrWhiteSpace(outputPath) || !outputPath.EndsWith(".resonitepackage", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("出力先に.resonitepackageファイルを指定してください。");
            outputPath = Path.GetFullPath(outputPath);
            string assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (outputPath.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("出力先はUnityのAssetsフォルダー外にしてください。");

            IsBusy = true;
            LastError = null;
            LastReport = null;
            Status = "シーンのコピーを準備しています…";
            cancellation = new CancellationTokenSource();
            EditorApplication.update += DrainMessages;
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
            EditorApplication.quitting += Cancel;
            string work = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "Library/BEPFairyTech/ResoConverter/Runs", Guid.NewGuid().ToString("N"));
            bool reloadLocked = false;
            try
            {
                Directory.CreateDirectory(work);
                EditorApplication.LockReloadAssemblies();
                reloadLocked = true;
                var report = new ConversionReport {
                    sourceName = source.name, outputPath = outputPath, exportedAtUtc = DateTime.UtcNow.ToString("O"),
                    outputKind = options.Kind.ToString(), lockSaving = options.LockSaving,
                    frozenPose = options.Kind == ExportKind.Model && options.FreezePose,
                    sizeMode = options.Kind == ExportKind.Avatar ? options.SizeMode.ToString() : "SourceSize",
                    blinkShape = options.Kind == ExportKind.Avatar ? options.BlinkShape : "",
                    toonShadowStrength = options.ToonShadowStrength
                };
                var warnings = new List<string>();
                warnings.Add("UnityとResoniteの物理・シェーダーは異なるため、揺れ方と見た目は近似です。取り込み後に確認してください。");
                using (var prepared = ScenePreparer.Prepare(source, options.ProcessModularAvatar,
                    report.frozenPose, options.BlinkRenderer, options.BlinkShape))
                {
                    warnings.AddRange(prepared.Warnings);
                    report.renderers = prepared.Root.GetComponentsInChildren<Renderer>(true).Length;
                    report.triangles = CountTriangles(prepared.Root);
                    Status = "メッシュ・マテリアル・揺れ物を変換しています…";
                    var serializer = new AvatarSerializer(options.ToonShadowStrength);
                    var root = await serializer.Export(prepared.Root, prepared.Avatar, options.Kind == ExportKind.Avatar);
                    var expressions = ExpressionExporter.Build(source, options, prepared, serializer, warnings);
                    var eyeLook = serializer.BuildEyeLookSettings(prepared.Avatar, options.Kind == ExportKind.Avatar);
                    warnings.AddRange(serializer.Warnings);
                    report.physBoneSourceSettings = serializer.PhysicsSources.Select(p => new SourceComponentSettings {
                        hierarchyPath = p.hierarchyPath, componentType = p.componentType, unitySettingsJson = p.unitySettingsJson
                    }).ToArray();
                    cancellation.Token.ThrowIfCancellationRequested();
                    string input = Path.Combine(work, "scene.pb");
                    File.WriteAllBytes(input, root.ToByteArray());
                    string settings = Path.Combine(work, "settings.json");
                    File.WriteAllText(settings, JsonUtility.ToJson(new BackendSettings {
                        lockSaving = options.LockSaving, asAvatar = options.Kind == ExportKind.Avatar,
                        useStandardSize = options.Kind == ExportKind.Avatar && options.SizeMode == AvatarSizeMode.ResoniteStandard,
                        expressions = expressions, eyeLook = eyeLook
                    }, true));
                    string package = Path.Combine(work, "output.resonitepackage");
                    Status = "Resonite形式を生成しています…";
                    await BackendRunner.Run(input, package, settings, work, options.ResonitePath, Messages, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                    string temporary = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.Copy(package, temporary);
                        if (File.Exists(outputPath)) File.Replace(temporary, outputPath, null);
                        else File.Move(temporary, outputPath);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                report.warnings = warnings.Distinct().ToArray();
                LastReport = report;
                Status = "変換が完了しました";
                try { File.WriteAllText(outputPath + ".report.json", JsonUtility.ToJson(report, true)); }
                catch (IOException e) { Debug.LogWarning("変換レポートを保存できませんでした: " + e.Message); }
                catch (UnauthorizedAccessException e) { Debug.LogWarning("変換レポートを保存できませんでした: " + e.Message); }
                Debug.Log("[BEP ResoConverter] " + outputPath);
                return report;
            }
            catch (OperationCanceledException) { Status = "変換をキャンセルしました"; throw; }
            catch (Exception e) { LastError = e.Message; Status = "変換に失敗しました"; throw; }
            finally
            {
                if (reloadLocked) EditorApplication.UnlockReloadAssemblies();
                EditorApplication.update -= DrainMessages;
                AssemblyReloadEvents.beforeAssemblyReload -= Cancel;
                EditorApplication.quitting -= Cancel;
                cancellation.Dispose();
                cancellation = null;
                IsBusy = false;
                while (Messages.TryDequeue(out _)) { }
            }
        }

        private static void DrainMessages() { while (Messages.TryDequeue(out var message)) Status = message; }

        private static int CountTriangles(GameObject root)
        {
            long count = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    if (mesh.GetTopology(sub) == MeshTopology.Triangles) count += mesh.GetIndexCount(sub) / 3;
            }
            return (int)Math.Min(count, int.MaxValue);
        }
    }
}
