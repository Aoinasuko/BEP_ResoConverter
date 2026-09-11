using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BEPFairyTech.ResoConverter
{
    public sealed partial class ResoConverterWindow
    {
        internal const string PresetFolder = "Assets/BEPFairyTech/ResoConverterPresets";
        [SerializeField] private ResoConverterPreset preset;
        private string presetMessage;
        private string[] presetWarnings = Array.Empty<string>();
        private bool presetFailed;

        private void DrawPresets()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("出力設定プリセット", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var selected = (ResoConverterPreset)EditorGUILayout.ObjectField("プリセット", preset, typeof(ResoConverterPreset), false);
            if (EditorGUI.EndChangeCheck())
            {
                preset = selected;
                presetMessage = null;
                presetWarnings = Array.Empty<string>();
            }
            bool hasSceneSource = source != null && !EditorUtility.IsPersistent(source) && source.scene.IsValid();
            using (new EditorGUI.DisabledScope(!hasSceneSource))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("新規保存…")) SaveNewPreset();
                using (new EditorGUI.DisabledScope(preset == null || !EditorUtility.IsPersistent(preset)))
                    if (GUILayout.Button("上書き保存")) OverwritePreset();
                using (new EditorGUI.DisabledScope(preset == null))
                    if (GUILayout.Button("復元")) RestorePreset();
            }
            EditorGUILayout.LabelField("対象を選んでからプリセットを復元します。表情・瞬き・サイズなどを保存でき、復元はUndoで戻せます。", EditorStyles.wordWrappedMiniLabel);
            if (!hasSceneSource)
                EditorGUILayout.LabelField("Hierarchyから変換対象を指定してください。", EditorStyles.wordWrappedMiniLabel);
            if (!string.IsNullOrEmpty(presetMessage))
                EditorGUILayout.HelpBox(presetMessage, presetFailed ? MessageType.Error : MessageType.Info);
            if (presetWarnings != null && presetWarnings.Length > 0)
                EditorGUILayout.HelpBox(string.Join("\n", presetWarnings), MessageType.Warning);
        }

        private void SaveNewPreset()
        {
            var snapshot = CreateInstance<ResoConverterPreset>();
            try
            {
                if (!snapshot.TryCapture(source, options, out var warnings, out var failure))
                { ShowPresetResult(failure, warnings, true); return; }
                EnsurePresetFolder();
                string name = new string((source.name + " 出力設定").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
                string suggested = AssetDatabase.GenerateUniqueAssetPath(PresetFolder + "/" + name + ".asset");
                string path = EditorUtility.SaveFilePanelInProject("出力設定プリセットを保存", Path.GetFileNameWithoutExtension(suggested), "asset",
                    "プリセットの保存場所を選んでください。", PresetFolder);
                if (string.IsNullOrEmpty(path)) return;
                // New Save always creates a separate asset. The explicit overwrite
                // action is reserved for updating the selected preset.
                path = AssetDatabase.GenerateUniqueAssetPath(path);
                AssetDatabase.CreateAsset(snapshot, path);
                if (!EditorUtility.IsPersistent(snapshot) || AssetDatabase.GetAssetPath(snapshot) != path)
                    throw new IOException("プリセットを保存できませんでした。保存先とUnityのConsoleを確認してください。");
                AssetDatabase.SaveAssetIfDirty(snapshot);
                preset = snapshot;
                ShowPresetResult("保存しました: " + path, warnings, false);
            }
            catch (Exception e) { ShowPresetResult(e.Message, Array.Empty<string>(), true); }
            finally { if (snapshot != null && !EditorUtility.IsPersistent(snapshot)) DestroyImmediate(snapshot); }
        }

        private void OverwritePreset()
        {
            if (preset == null) return;
            try
            {
                Undo.RecordObject(preset, "出力設定プリセットを上書き");
                if (!preset.TryCapture(source, options, out var warnings, out var failure))
                { ShowPresetResult(failure, warnings, true); return; }
                EditorUtility.SetDirty(preset);
                AssetDatabase.SaveAssetIfDirty(preset);
                ShowPresetResult("上書き保存しました: " + AssetDatabase.GetAssetPath(preset), warnings, false);
            }
            catch (Exception e) { ShowPresetResult(e.Message, Array.Empty<string>(), true); }
        }

        private void RestorePreset()
        {
            if (preset == null) return;
            try
            {
                if (!preset.TryRestore(source, options, out var restored, out var warnings, out var failure))
                { ShowPresetResult(failure, warnings, true); return; }
                Undo.RecordObject(this, "出力設定プリセットを復元");
                options = restored;
                error = null;
                ShowPresetResult("「" + preset.name + "」を復元しました。Resonite本体の場所は、このPCの設定を維持しています。", warnings, false);
                Repaint();
            }
            catch (Exception e) { ShowPresetResult(e.Message, Array.Empty<string>(), true); }
        }

        private void ShowPresetResult(string message, string[] warnings, bool failed)
        {
            presetMessage = message;
            presetWarnings = warnings ?? Array.Empty<string>();
            presetFailed = failed;
        }

        private static void EnsurePresetFolder()
        {
            string parent = "Assets";
            foreach (string segment in PresetFolder.Split('/').Skip(1))
            {
                string path = parent + "/" + segment;
                if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, segment);
                parent = path;
            }
        }
    }
}
