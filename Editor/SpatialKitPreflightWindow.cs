using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProjectionSpatialKit.Editor
{
    /// <summary>
    /// The preflight report window: run the checks, read the verdict, jump to the object that
    /// caused it, and export the whole thing as Markdown to take to the site / paste into a
    /// ticket. The report — not the preview — is the deliverable.
    /// </summary>
    internal sealed class SpatialKitPreflightWindow : EditorWindow
    {
        private List<SpatialKitPreflight.Finding> findings;
        private Vector2 scroll;
        private bool showInfo = true;

        [MenuItem("Projection Spatial Kit/Preflight Check", priority = 2)]
        internal static void Open()
        {
            SpatialKitPreflightWindow window = GetWindow<SpatialKitPreflightWindow>(false, "Preflight");
            window.minSize = new Vector2(460f, 320f);
            window.Run();
            window.Show();
        }

        private void OnEnable()
        {
            if (findings == null)
            {
                Run();
            }
        }

        private void Run()
        {
            findings = SpatialKitPreflight.Run();
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (findings == null)
            {
                EditorGUILayout.HelpBox("まだ検査していません。『再検査』を押してください。", MessageType.Info);
                return;
            }

            DrawVerdict();
            if (findings.Count == 0)
            {
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            string category = null;
            foreach (SpatialKitPreflight.Finding finding in findings)
            {
                if (!showInfo && finding.Severity <= SpatialKitPreflight.Severity.Info)
                {
                    continue;
                }
                if (finding.Category != category)
                {
                    category = finding.Category;
                    EditorGUILayout.Space(6f);
                    GUILayout.Label(category, EditorStyles.boldLabel);
                }
                DrawFinding(finding);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("再検査", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                {
                    Run();
                }
                showInfo = GUILayout.Toggle(showInfo, "情報も表示", EditorStyles.toolbarButton, GUILayout.Width(80f));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Markdown をコピー", EditorStyles.toolbarButton, GUILayout.Width(130f)))
                {
                    EditorGUIUtility.systemCopyBuffer = BuildMarkdown();
                    ShowNotification(new GUIContent("コピーしました"));
                }
                if (GUILayout.Button("Markdown を保存…", EditorStyles.toolbarButton, GUILayout.Width(130f)))
                {
                    SaveMarkdown();
                }
            }
        }

        private void DrawVerdict()
        {
            int fail = 0, warn = 0, info = 0;
            foreach (SpatialKitPreflight.Finding finding in findings)
            {
                switch (finding.Severity)
                {
                    case SpatialKitPreflight.Severity.Fail: fail++; break;
                    case SpatialKitPreflight.Severity.Warn: warn++; break;
                    default: info++; break;
                }
            }

            string summary = fail > 0
                ? $"現地で問題になる項目が {fail} 件あります（警告 {warn} / 情報 {info}）。"
                : warn > 0
                    ? $"致命的な問題はありません。確認したい警告が {warn} 件あります（情報 {info}）。"
                    : "投影配置・画面構成に問題は見つかりませんでした。";
            EditorGUILayout.HelpBox(summary,
                fail > 0 ? MessageType.Error : warn > 0 ? MessageType.Warning : MessageType.Info);
        }

        private void DrawFinding(SpatialKitPreflight.Finding finding)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.color = SeverityColor(finding.Severity);
                    GUILayout.Label(SeverityLabel(finding.Severity), EditorStyles.boldLabel, GUILayout.Width(48f));
                    GUI.color = Color.white;

                    GUILayout.Label(finding.Target, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(finding.Context == null))
                    {
                        if (GUILayout.Button("選択", EditorStyles.miniButton, GUILayout.Width(40f)))
                        {
                            Selection.activeObject = finding.Context;
                            EditorGUIUtility.PingObject(finding.Context);
                        }
                    }
                }
                EditorGUILayout.LabelField(finding.Summary, EditorStyles.wordWrappedLabel);
                if (!string.IsNullOrEmpty(finding.Fix))
                {
                    EditorGUILayout.LabelField("→ " + finding.Fix, EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private static string SeverityLabel(SpatialKitPreflight.Severity severity)
        {
            switch (severity)
            {
                case SpatialKitPreflight.Severity.Fail: return "要対応";
                case SpatialKitPreflight.Severity.Warn: return "警告";
                case SpatialKitPreflight.Severity.Info: return "情報";
                default: return "OK";
            }
        }

        private static Color SeverityColor(SpatialKitPreflight.Severity severity)
        {
            switch (severity)
            {
                case SpatialKitPreflight.Severity.Fail: return new Color(1f, 0.45f, 0.4f);
                case SpatialKitPreflight.Severity.Warn: return new Color(1f, 0.8f, 0.35f);
                case SpatialKitPreflight.Severity.Info: return new Color(0.6f, 0.8f, 1f);
                default: return Color.white;
            }
        }

        private string BuildMarkdown()
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Projection Spatial Kit — Preflight Report");
            builder.AppendLine();
            builder.AppendLine($"- Scene: `{EditorSceneManager.GetActiveScene().name}`");
            builder.AppendLine($"- Date: {System.DateTime.Now:yyyy-MM-dd HH:mm}");
            builder.AppendLine($"- Unity: {Application.unityVersion}");
            builder.AppendLine();
            builder.AppendLine("投影配置と画面構成を計算で検査した結果です。実機の輝度・色・センサ特性は対象外で、");
            builder.AppendLine("現地検証の代替ではありません。");
            builder.AppendLine();

            string category = null;
            foreach (SpatialKitPreflight.Finding finding in findings)
            {
                if (finding.Category != category)
                {
                    category = finding.Category;
                    builder.AppendLine();
                    builder.AppendLine($"## {category}");
                    builder.AppendLine();
                }
                builder.AppendLine($"### {SeverityLabel(finding.Severity)} — {finding.Target}");
                builder.AppendLine();
                builder.AppendLine(finding.Summary);
                if (!string.IsNullOrEmpty(finding.Fix))
                {
                    builder.AppendLine();
                    builder.AppendLine($"**対処**: {finding.Fix}");
                }
                builder.AppendLine();
            }
            return builder.ToString();
        }

        private void SaveMarkdown()
        {
            string path = EditorUtility.SaveFilePanel("Preflight レポートを保存",
                "", $"preflight_{EditorSceneManager.GetActiveScene().name}.md", "md");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            System.IO.File.WriteAllText(path, BuildMarkdown(), Encoding.UTF8);
            EditorUtility.RevealInFinder(path);
        }
    }
}
