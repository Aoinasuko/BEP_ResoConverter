using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BEPFairyTech.ResoConverter
{
    public sealed partial class ResoConverterWindow : EditorWindow
    {
        [SerializeField] private GameObject source;
        [SerializeField] private ConversionOptions options = new ConversionOptions();
        [SerializeField] private string lastOutputPath;
        private Vector2 scroll;
        private string error;
        private string clipboardMessage;
        private bool showWarnings;

        [MenuItem("BEP Fairy Tech/ResoConverter", false, 100)]
        public static void Open()
        {
            var window = GetWindow<ResoConverterWindow>("ResoConverter");
            window.minSize = new Vector2(440, 540);
            if (window.position.height < 700) window.position = new Rect(500, 160, 510, 730);
            window.Show();
        }

        private void OnEnable()
        {
            if (options == null) options = new ConversionOptions();
            if (string.IsNullOrEmpty(options.ResonitePath)) options.ResonitePath = BackendRunner.FindResonite();
            if (source == null && Selection.activeGameObject != null && !EditorUtility.IsPersistent(Selection.activeGameObject))
                SetSource(Selection.activeGameObject);
            EditorApplication.update += RefreshProgress;
        }
        private void OnDisable() => EditorApplication.update -= RefreshProgress;
        private void RefreshProgress() { if (ResoConverter.IsBusy) Repaint(); }

        private void SetSource(GameObject next)
        {
            if (next == source) return;
            source = next;
            options.BlinkRenderer = null;
            options.BlinkShape = "";
            error = null;
            if (source == null) return;
            foreach (var renderer in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null) continue;
                for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                {
                    string shape = renderer.sharedMesh.GetBlendShapeName(i);
                    if (shape.Equals("blink", StringComparison.OrdinalIgnoreCase) || shape == "まばたき" ||
                        shape.Equals("vrc.blink", StringComparison.OrdinalIgnoreCase))
                    { options.BlinkRenderer = renderer; options.BlinkShape = shape; return; }
                }
            }
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.Space(12);
            GUILayout.Label("BEP Fairy Tech", EditorStyles.miniLabel);
            GUILayout.Label("ResoConverter", new GUIStyle(EditorStyles.boldLabel) { fontSize = 24 });
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("シーン上のモデルをResoniteへ", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(".resonitepackageを書き出し、出力ファイルのパスを文字列としてコピーできます。Resoniteで貼り付けるか、ファイルをドラッグ＆ドロップして取り込んでください。", MessageType.Info);

            using (new EditorGUI.DisabledScope(ResoConverter.IsBusy))
            {
                EditorGUILayout.Space(8);
                var next = (GameObject)EditorGUILayout.ObjectField("変換対象（Hierarchy）", source, typeof(GameObject), true);
                if (next != source) SetSource(next);
                if (GUILayout.Button("Hierarchyで選択中のオブジェクトを使う")) SetSource(Selection.activeGameObject);
                EditorGUILayout.Space(8);
                options.Kind = (ExportKind)GUILayout.Toolbar((int)options.Kind, new[] { "アバター", "3Dモデル・アイテム" });
                EditorGUILayout.Space(8);
                options.LockSaving = EditorGUILayout.ToggleLeft("他の人による保存を制限する", options.LockSaving);
                EditorGUILayout.LabelField("チェックを外すと、他の人も保存できる形式で出力します。", EditorStyles.wordWrappedMiniLabel);
                options.ProcessModularAvatar = EditorGUILayout.ToggleLeft("Modular Avatarのボーン追従・結合を反映", options.ProcessModularAvatar);

                if (options.Kind == ExportKind.Avatar)
                {
                    EditorGUILayout.Space(8);
                    EditorGUILayout.LabelField("アバター設定", EditorStyles.boldLabel);
                    options.SizeMode = (AvatarSizeMode)EditorGUILayout.Popup("サイズ", (int)options.SizeMode,
                        new[] { "標準サイズ（高さ1.8m）", "元アバターのサイズ" });
                    DrawBlink();
                    EditorGUILayout.LabelField("口パク：VRC Avatar DescriptorのViseme設定を自動変換します。", EditorStyles.wordWrappedMiniLabel);
                    DrawExpressions();
                }
                else
                {
                    EditorGUILayout.Space(8);
                    options.FreezePose = EditorGUILayout.ToggleLeft("現在のポーズと表情で固定する", options.FreezePose);
                    EditorGUILayout.LabelField("固定するとスキニングを焼き込み、置物として出力します。", EditorStyles.wordWrappedMiniLabel);
                }

                EditorGUILayout.Space(12);
                EditorGUILayout.LabelField("Resonite本体", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    options.ResonitePath = EditorGUILayout.TextField(options.ResonitePath ?? "");
                    if (GUILayout.Button("参照…", GUILayout.Width(65)))
                    {
                        string folder = EditorUtility.OpenFolderPanel("Resoniteのインストール先", options.ResonitePath, "");
                        if (!string.IsNullOrEmpty(folder))
                        {
                            options.ResonitePath = folder;
                            EditorPrefs.SetString("BEPFairyTech.ResoConverter.ResonitePath", folder);
                        }
                    }
                }
                EditorGUILayout.LabelField("ローカルのResonite本体を使って変換します。ログインは不要です。", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(10);
                EditorGUILayout.HelpBox("lilToonはResoniteのToon材質へ近似変換します。揺れ物も異なる物理エンジンへの変換となるため、取り込み後に見た目と動きを確認してください。", MessageType.None);

                var problems = ResoConverter.Validate(source, options);
                foreach (string problem in problems) EditorGUILayout.HelpBox(problem, MessageType.Warning);
                using (new EditorGUI.DisabledScope(problems.Count != 0))
                    if (GUILayout.Button("Resonite形式に変換…", GUILayout.Height(38))) Export();
            }

            string copyPath = ResoConverter.LastReport?.outputPath ?? lastOutputPath;
            using (new EditorGUI.DisabledScope(ResoConverter.IsBusy || string.IsNullOrEmpty(copyPath) || !File.Exists(copyPath)))
            {
                if (GUILayout.Button("成果物を文字列としてコピー", GUILayout.Height(30)))
                {
                    try
                    {
                        ResoniteClipboard.CopyPackage(copyPath);
                        error = null;
                        clipboardMessage = "出力ファイルのパスを文字列としてコピーしました。Resoniteで貼り付けてください。貼り付けボタンが反応しない場合は、ダッシュを閉じてCtrl+Vを押すか、Filesタブから出力ファイルを開いてください。";
                    }
                    catch (Exception e) { error = e.Message; clipboardMessage = null; }
                }
            }
            EditorGUILayout.LabelField(string.IsNullOrEmpty(copyPath) ? "変換が完了するとコピーできます。" :
                "コピーするパス: " + copyPath, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("コピーする文字列は出力ファイルの場所を参照します。取り込みが完了するまで、元の.resonitepackageファイルを移動・削除しないでください。", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("貼り付けボタンが反応しない場合は、ダッシュを閉じてCtrl+Vを押すか、ファイルをドラッグ＆ドロップ、またはFilesタブから開いてください。", EditorStyles.wordWrappedMiniLabel);
            if (!string.IsNullOrEmpty(clipboardMessage)) EditorGUILayout.HelpBox(clipboardMessage, MessageType.Info);

            if (ResoConverter.IsBusy)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox(ResoConverter.Status ?? "変換中…", MessageType.Info);
                if (GUILayout.Button("キャンセル")) ResoConverter.Cancel();
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (!ResoConverter.IsBusy && ResoConverter.LastReport != null)
            {
                var report = ResoConverter.LastReport;
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox("変換完了: " + Path.GetFileName(report.outputPath) + "\n" + report.renderers + " renderers / " + report.triangles.ToString("N0") + " triangles", MessageType.Info);
                if (GUILayout.Button("出力ファイルを表示")) EditorUtility.RevealInFinder(report.outputPath);
                showWarnings = EditorGUILayout.Foldout(showWarnings, "変換レポート（注意点 " + report.warnings.Length + "件）", true);
                if (showWarnings) foreach (string warning in report.warnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }
            EditorGUILayout.Space(12);
            EditorGUILayout.EndScrollView();
        }

        private void DrawBlink()
        {
            if (source == null) return;
            var renderers = source.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0).ToArray();
            var labels = new[] { "使用しない" }.Concat(renderers.Select(r => r.transform == source.transform ? source.name : AnimationUtility.CalculateTransformPath(r.transform, source.transform))).ToArray();
            int selected = Array.IndexOf(renderers, options.BlinkRenderer) + 1;
            int next = EditorGUILayout.Popup("瞬きのメッシュ", selected, labels);
            if (next != selected)
            {
                options.BlinkRenderer = next == 0 ? null : renderers[next - 1];
                options.BlinkShape = "";
            }
            if (options.BlinkRenderer == null || options.BlinkRenderer.sharedMesh == null) return;
            var mesh = options.BlinkRenderer.sharedMesh;
            var names = new[] { "選択してください" }.Concat(Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName)).ToArray();
            int shape = Math.Max(0, Array.IndexOf(names, options.BlinkShape));
            int choice = EditorGUILayout.Popup("瞬きのBlendShape", shape, names);
            options.BlinkShape = choice == 0 ? "" : names[choice];
        }

        private async void Export()
        {
            string folder = EditorPrefs.GetString("BEPFairyTech.ResoConverter.OutputFolder", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            string filename = string.Concat(source.name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            string path = EditorUtility.SaveFilePanel("Resonite形式に変換", folder, filename, "resonitepackage");
            if (string.IsNullOrEmpty(path)) return;
            error = null;
            clipboardMessage = null;
            EditorPrefs.SetString("BEPFairyTech.ResoConverter.OutputFolder", Path.GetDirectoryName(path));
            try
            {
                await ResoConverter.ExportAsync(source, options, path);
                lastOutputPath = path;
            }
            catch (OperationCanceledException) { error = null; }
            catch (Exception e) { error = e.Message; Debug.LogException(e); }
            Repaint();
        }
    }
}
