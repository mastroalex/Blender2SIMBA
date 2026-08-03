#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace M2M.SIMBA.Editor
{
    public sealed class SIMBASettingsWindow : EditorWindow
    {
        private string pythonPath = string.Empty;
        private string status = "Not validated";
        private MessageType statusType = MessageType.Info;
        private float fps;
        private int frameStep;
        private SIMBAColorMap colorMap;

        [MenuItem("Tools/SIMBA/Settings...")]
        public static void Open()
        {
            var window = GetWindow<SIMBASettingsWindow>(true, "SIMBA Settings");
            window.minSize = new Vector2(520, 270);
            window.Show();
        }

        private void OnEnable()
        {
            pythonPath = SIMBAEditorSettings.PythonPath;
            fps = SIMBAEditorSettings.DefaultFps;
            frameStep = SIMBAEditorSettings.DefaultFrameStep;
            colorMap = SIMBAEditorSettings.DefaultColorMap;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("SIMBA Settings", new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 });
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Python environment", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                pythonPath = EditorGUILayout.TextField("Interpreter", pythonPath);
                if (GUILayout.Button("Browse...", GUILayout.Width(85))) BrowsePython();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Auto Detect")) AutoDetect();
                if (GUILayout.Button("Validate")) ValidatePython();
                if (GUILayout.Button("Install numpy + h5py")) InstallDependencies();
            }
            EditorGUILayout.HelpBox(status, statusType);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Conversion defaults", EditorStyles.boldLabel);
            fps = EditorGUILayout.FloatField("FPS", Mathf.Max(0.001f, fps));
            frameStep = EditorGUILayout.IntField("Frame step", Mathf.Max(1, frameStep));
            colorMap = (SIMBAColorMap)EditorGUILayout.EnumPopup("Colormap", colorMap);

            EditorGUILayout.Space(10);
            if (GUILayout.Button("Save Settings", GUILayout.Height(30))) Save();
        }

        private void BrowsePython()
        {
            string start = string.IsNullOrWhiteSpace(pythonPath) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : Path.GetDirectoryName(pythonPath);
#if UNITY_EDITOR_WIN
            string selected = EditorUtility.OpenFilePanel("Select Python interpreter", start, "exe");
#else
            string selected = EditorUtility.OpenFilePanel("Select Python interpreter", start, string.Empty);
#endif
            if (!string.IsNullOrEmpty(selected)) { pythonPath = selected; Save(); ValidatePython(); }
        }

        private void AutoDetect()
        {
            string[] candidates =
#if UNITY_EDITOR_WIN
                { "python.exe", "python3.exe", "py.exe" };
#else
                { "python3", "python" };
#endif
            foreach (string candidate in candidates)
            {
                try { var result = SIMBAPythonProcess.Validate(candidate); if (result.Success) { pythonPath = candidate; Save(); SetStatus(result); return; } }
                catch { }
            }
            status = "Python was not found automatically. Browse to your Conda/venv interpreter.";
            statusType = MessageType.Warning;
        }

        private void ValidatePython()
        {
            try { SetStatus(SIMBAPythonProcess.Validate(pythonPath)); }
            catch (Exception e) { status = e.Message; statusType = MessageType.Error; }
        }

        private void InstallDependencies()
        {
            try
            {
                var result = SIMBAPythonProcess.Run(pythonPath, "-m", "pip", "install", "numpy", "h5py");
                status = result.Success ? "Dependencies installed successfully.\n" + result.StandardOutput : result.StandardError;
                statusType = result.Success ? MessageType.Info : MessageType.Error;
            }
            catch (Exception e) { status = e.Message; statusType = MessageType.Error; }
        }

        private void SetStatus(SIMBAProcessResult result)
        {
            status = result.Success ? "Python environment valid:\n" + result.StandardOutput : "Validation failed:\n" + result.StandardError;
            statusType = result.Success ? MessageType.Info : MessageType.Error;
        }

        private void Save()
        {
            SIMBAEditorSettings.PythonPath = pythonPath.Trim();
            SIMBAEditorSettings.DefaultFps = Mathf.Max(0.001f, fps);
            SIMBAEditorSettings.DefaultFrameStep = Mathf.Max(1, frameStep);
            SIMBAEditorSettings.DefaultColorMap = colorMap;
            status = "Settings saved for this Unity editor user.";
            statusType = MessageType.Info;
        }
    }
}
#endif
