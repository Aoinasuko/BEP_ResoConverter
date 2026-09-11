using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BEPFairyTech.ResoConverter
{
    public sealed partial class ResoConverterWindow
    {
        private static readonly string[] HandPoseLabels = {
            "指定なし（どの形でも）", "Idle（自然な手）", "Fist（握り）", "Open（開き）",
            "Point（指差し）", "Victory（ピース）", "RockNRoll（ロック）",
            "HandGun（指鉄砲）", "ThumbsUp（サムズアップ）"
        };

        private void DrawExpressions()
        {
            if (options.HandExpressions == null) options.HandExpressions = new List<HandExpressionSettings>();
            if (options.MenuExpressions == null) options.MenuExpressions = new List<MenuExpressionSettings>();
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("表情の切り替え", EditorStyles.boldLabel);
            options.EnableHandExpressions = EditorGUILayout.ToggleLeft(
                "両手の形に合わせて表情を変える", options.EnableHandExpressions);
            if (options.EnableHandExpressions)
            {
                EditorGUILayout.LabelField("左手・右手の両方の条件で判定します。複数の条件に一致すると、上の行を優先します。", EditorStyles.wordWrappedMiniLabel);
                for (int i = 0; i < options.HandExpressions.Count; i++)
                {
                    if (options.HandExpressions[i] == null) options.HandExpressions[i] = new HandExpressionSettings();
                    var entry = options.HandExpressions[i];
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        if (DrawExpressionRowHeader("ハンドサイン " + (i + 1), options.HandExpressions, i)) break;
                        entry.Left = DrawHandPose("左手", entry.Left);
                        entry.Right = DrawHandPose("右手", entry.Right);
                        DrawExpressionClip(entry);
                    }
                }
                if (GUILayout.Button("ハンドサインの表情を追加")) options.HandExpressions.Add(new HandExpressionSettings());
            }

            EditorGUILayout.Space(6);
            options.EnableMenuExpressions = EditorGUILayout.ToggleLeft(
                "メニューから表情を変えられるようにする", options.EnableMenuExpressions);
            if (options.EnableMenuExpressions)
            {
                EditorGUILayout.LabelField("Resoniteのメニューから「表情」を開き、選んだ表情を固定します。1件以上設定すると「初期状態」が自動で追加されます。", EditorStyles.wordWrappedMiniLabel);
                for (int i = 0; i < options.MenuExpressions.Count; i++)
                {
                    if (options.MenuExpressions[i] == null) options.MenuExpressions[i] = new MenuExpressionSettings();
                    var entry = options.MenuExpressions[i];
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        if (DrawExpressionRowHeader("メニューの表情 " + (i + 1), options.MenuExpressions, i)) break;
                        entry.Name = EditorGUILayout.TextField("表示名", entry.Name ?? "");
                        DrawExpressionClip(entry);
                    }
                }
                if (GUILayout.Button("メニューの表情を追加")) options.MenuExpressions.Add(new MenuExpressionSettings());
                if (options.EnableHandExpressions)
                    EditorGUILayout.HelpBox("メニューで固定した表情を優先します。「初期状態」もハンドサインより優先し、「ハンドサインに戻す」で手に連動する表情へ戻ります。", MessageType.Info);
            }

            if (options.EnableHandExpressions || options.EnableMenuExpressions)
                EditorGUILayout.HelpBox("表情用AnimationClipの指定時刻のBlendShape値を使います（初期値は0秒）。時間に沿った再生や、材質・Transform・表示切り替えのキーは対象外です。クリップで変更していない瞬き・口パクは継続します。", MessageType.None);
        }

        private static ExpressionHandPose DrawHandPose(string label, ExpressionHandPose pose)
        {
            int selected = Mathf.Clamp((int)pose + 1, 0, HandPoseLabels.Length - 1);
            return (ExpressionHandPose)(EditorGUILayout.Popup(label, selected, HandPoseLabels) - 1);
        }

        private static void DrawExpressionClip(ExpressionClipSettings entry)
        {
            var clip = (AnimationClip)EditorGUILayout.ObjectField("表情アニメーション", entry.Clip, typeof(AnimationClip), false);
            if (clip != entry.Clip)
            {
                entry.Clip = clip;
                entry.SampleTime = clip == null ? 0 : Mathf.Clamp(entry.SampleTime, 0, clip.length);
            }
            using (new EditorGUI.DisabledScope(entry.Clip == null))
                entry.SampleTime = EditorGUILayout.FloatField(new GUIContent("使用する時刻（秒）", "この時刻の表情を静止状態で使います。"), entry.SampleTime);
        }

        private bool DrawExpressionRowHeader<T>(string title, List<T> entries, int index)
        {
            bool changed = false;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(index == 0))
                    if (GUILayout.Button("上へ", GUILayout.Width(42)))
                    { var prior = entries[index - 1]; entries[index - 1] = entries[index]; entries[index] = prior; changed = true; }
                using (new EditorGUI.DisabledScope(index == entries.Count - 1))
                    if (GUILayout.Button("下へ", GUILayout.Width(42)))
                    { var next = entries[index + 1]; entries[index + 1] = entries[index]; entries[index] = next; changed = true; }
                if (GUILayout.Button("削除", GUILayout.Width(42))) { entries.RemoveAt(index); changed = true; }
            }
            if (changed) { GUI.changed = true; Repaint(); }
            return changed;
        }
    }
}
