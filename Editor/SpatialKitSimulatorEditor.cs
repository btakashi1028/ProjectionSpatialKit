using UnityEditor;
using UnityEngine;

namespace ProjectionSpatialKit.Editor
{
    /// <summary>
    /// Inspector for the simulator façade: the default fields plus explicit preset
    /// save/load buttons. The preset asset is a FILE the user writes/reads on demand —
    /// it is never applied automatically, so the component's fields are always the
    /// single source of truth.
    /// </summary>
    [CustomEditor(typeof(SpatialKitSimulator))]
    public sealed class SpatialKitSimulatorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Content scene as a drag-and-drop SceneAsset instead of a hand-typed project path
            // (the path field was error-prone: exact spelling and the .unity extension mattered).
            SerializedProperty pathProp = serializedObject.FindProperty("contentScenePath");
            DrawContentSceneField(pathProp);

            // Everything else via the default inspector, minus the path we drew ourselves.
            DrawPropertiesExcluding(serializedObject, "m_Script", "contentScenePath");
            serializedObject.ApplyModifiedProperties();

            SpatialKitSimulator simulator = (SpatialKitSimulator)target;

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("新規保存..."))
                {
                    SaveAsNew(simulator);
                }
                using (new EditorGUI.DisabledScope(simulator.Preset == null))
                {
                    if (GUILayout.Button("上書き保存"))
                    {
                        Overwrite(simulator);
                    }
                    if (GUILayout.Button("読み込み"))
                    {
                        Load(simulator);
                    }
                }
            }
            EditorGUILayout.HelpBox(
                "設定はこのコンポーネントが本体です。Preset File は保存/読込ボタンで明示的に" +
                "コピーする書類で、割り当てただけでは何も変わりません。設定は Play 開始時に適用されます。",
                MessageType.Info);
        }

        private static void DrawContentSceneField(SerializedProperty pathProp)
        {
            string path = pathProp.stringValue;
            SceneAsset current = string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<SceneAsset>(path);

            EditorGUI.BeginChangeCheck();
            SceneAsset picked = (SceneAsset)EditorGUILayout.ObjectField(
                new GUIContent("Content Scene",
                    "投影するコンテンツシーン。Project からシーンアセットをドラッグしてください。"),
                current, typeof(SceneAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                pathProp.stringValue = picked != null ? AssetDatabase.GetAssetPath(picked) : string.Empty;
            }

            // A path pointing at a missing/renamed asset would silently fail at Play; surface it.
            if (!string.IsNullOrEmpty(path) && current == null)
            {
                EditorGUILayout.HelpBox(
                    $"設定されたシーンが見つかりません:\n{path}\n上の欄にシーンをドラッグし直してください。",
                    MessageType.Warning);
            }
        }

        private static void SaveAsNew(SpatialKitSimulator simulator)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Simulation Preset", "SpatialKitSimulationProfile", "asset",
                "現在のシミュレータ設定をプリセットとして保存します");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            SpatialKitSimulationProfile asset = ScriptableObject.CreateInstance<SpatialKitSimulationProfile>();
            simulator.SaveTo(asset);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            Undo.RecordObject(simulator, "Link simulation preset");
            simulator.Preset = asset;
            EditorUtility.SetDirty(simulator);
        }

        private static void Overwrite(SpatialKitSimulator simulator)
        {
            Undo.RecordObject(simulator.Preset, "Overwrite simulation preset");
            simulator.SaveTo(simulator.Preset);
            EditorUtility.SetDirty(simulator.Preset);
            AssetDatabase.SaveAssets();
        }

        private static void Load(SpatialKitSimulator simulator)
        {
            Undo.RecordObject(simulator, "Load simulation preset");
            simulator.LoadFrom(simulator.Preset);
            EditorUtility.SetDirty(simulator);
        }
    }
}
