#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace M2M.SIMBA.Editor
{
    [CustomEditor(typeof(FieldColorController))]
    public sealed class FieldColorControllerEditor : UnityEditor.Editor
    {
        private SerializedProperty selectedFieldIndex;
        private SerializedProperty preferredFieldName;

        private void OnEnable()
        {
            selectedFieldIndex = serializedObject.FindProperty("selectedFieldIndex");
            preferredFieldName = serializedObject.FindProperty("preferredFieldName");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // These two properties are represented by the data-driven popup below.
            DrawPropertiesExcluding(serializedObject, "m_Script", "selectedFieldIndex", "preferredFieldName");

            FieldColorController controller = (FieldColorController)target;
            string[] names = GetAvailableFieldNames(controller, out string sourceMessage);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Simulation field", EditorStyles.boldLabel);

            if (names.Length > 0)
            {
                int current = ResolveCurrentIndex(names, controller);
                EditorGUI.BeginChangeCheck();
                int next = EditorGUILayout.Popup("Active field", current, names);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(controller, "Change SIMBA field");
                    selectedFieldIndex.intValue = next;
                    preferredFieldName.stringValue = names[next];
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(controller);

                    if (Application.isPlaying)
                        controller.SetField(next);
                }

                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField("Field count", names.Length.ToString());
            }
            else
            {
                EditorGUILayout.HelpBox(sourceMessage, MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static int ResolveCurrentIndex(string[] names, FieldColorController controller)
        {
            if (Application.isPlaying && controller.SelectedFieldIndex >= 0 && controller.SelectedFieldIndex < names.Length)
                return controller.SelectedFieldIndex;

            string preferred = controller.PreferredFieldName;
            for (int i = 0; i < names.Length; i++)
                if (string.Equals(names[i], preferred, StringComparison.OrdinalIgnoreCase))
                    return i;

            return Mathf.Clamp(controller.ConfiguredFieldIndex, 0, names.Length - 1);
        }

        private static string[] GetAvailableFieldNames(FieldColorController controller, out string message)
        {
            if (Application.isPlaying)
            {
                string[] runtimeNames = controller.AvailableFieldNames;
                if (runtimeNames.Length > 0)
                {
                    message = string.Empty;
                    return runtimeNames;
                }
            }

            string fileName = null;
            ShellMeshLoader shell = controller.GetComponent<ShellMeshLoader>();
            if (shell != null) fileName = shell.fileName;

            LineMeshPlayer line = controller.GetComponent<LineMeshPlayer>();
            if (line != null) fileName = line.fileName;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                message = "No ShellMeshLoader or LineMeshPlayer with a configured binary file was found on this GameObject.";
                return Array.Empty<string>();
            }

            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            if (!File.Exists(path))
            {
                message = $"The configured binary was not found in StreamingAssets:\n{path}";
                return Array.Empty<string>();
            }

            try
            {
                SIMBABinaryHeader header = SIMBABinaryHeaderReader.Read(path);
                message = string.Empty;
                return header.FieldNames.ToArray();
            }
            catch (Exception exception)
            {
                message = "Could not read fields from the configured binary:\n" + exception.Message;
                return Array.Empty<string>();
            }
        }
    }
}
#endif
