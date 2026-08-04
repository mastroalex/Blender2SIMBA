#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace M2M.SIMBA.Editor
{
    public sealed class SIMBAImportSimulationWindow : EditorWindow
    {
        private enum SourceMode { HDF5, SIMBABinary }

        private SourceMode sourceMode = SourceMode.HDF5;
        private string sourcePath = string.Empty;
        private string errorMessage = string.Empty;
        private string conversionLog = string.Empty;

        private SIMBAH5Inspection inspection;
        private bool[] selectedFields = Array.Empty<bool>();
        private SIMBABinaryHeader header;
        private GeometryType geometryType = GeometryType.ShellMesh;

        private float fps = 30f;
        private int frameStep = 1;
        private float positionScale = 1f;
        private bool swapYZ = true;
        private bool includeRadius = true;
        private string outputFileName = "simulation_simba.bin";

        private int selectedInitialField;
        private SIMBAColorMap colorMap = SIMBAColorMap.Turbo;
        private Transform parent;
        private float lineRadius = 0.00015f;
        private int lineSides = 6;
        private Vector2 scroll;

        [MenuItem("Tools/SIMBA/Import Simulation...")]
        public static void Open()
        {
            var window = GetWindow<SIMBAImportSimulationWindow>(true, "SIMBA Import Simulation");
            window.minSize = new Vector2(540f, 620f);
            window.Show();
        }

        private void OnEnable()
        {
            fps = SIMBAEditorSettings.DefaultFps;
            frameStep = SIMBAEditorSettings.DefaultFrameStep;
            colorMap = SIMBAEditorSettings.DefaultColorMap;
        }

        private void OnGUI()
        {
            using (var scrollView = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = scrollView.scrollPosition;

                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("SIMBA", new GUIStyle(EditorStyles.boldLabel) { fontSize = 20 });
                EditorGUILayout.LabelField("SIMulation Buffered Animation", EditorStyles.miniLabel);
                EditorGUILayout.Space(10);

                DrawPythonStatus();
                sourceMode = (SourceMode)EditorGUILayout.EnumPopup("Source", sourceMode);
                DrawSourcePicker();

                if (!string.IsNullOrEmpty(errorMessage))
                    EditorGUILayout.HelpBox(errorMessage, MessageType.Error);
                if (!string.IsNullOrEmpty(conversionLog))
                    EditorGUILayout.HelpBox(conversionLog, MessageType.Info);

                try
                {
                    if (sourceMode == SourceMode.HDF5)
                        DrawH5Section();
                    else
                        DrawBinarySection();
                }
                catch (Exception exception)
                {
                    errorMessage = exception.Message;
                    EditorGUILayout.HelpBox(
                        "The importer encountered an error while drawing its preview. " +
                        "See the Console for details.",
                        MessageType.Error);
                    Debug.LogException(exception);
                }
            }
        }

        private void DrawPythonStatus()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string path = SIMBAEditorSettings.PythonPath;
                EditorGUILayout.LabelField("Python", string.IsNullOrWhiteSpace(path) ? "Not configured" : path);
                if (GUILayout.Button("Settings...", GUILayout.Width(90))) SIMBASettingsWindow.Open();
            }
            if (sourceMode == SourceMode.HDF5 && string.IsNullOrWhiteSpace(SIMBAEditorSettings.PythonPath))
                EditorGUILayout.HelpBox("Configure a Python or Conda interpreter in Tools > SIMBA > Settings.", MessageType.Warning);
            EditorGUILayout.Space(5);
        }

        private void DrawSourcePicker()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(sourceMode == SourceMode.HDF5 ? "HDF5 file" : "Binary file");
                EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(sourcePath) ? "No file selected" : sourcePath, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("Browse...", GUILayout.Width(85))) Browse();
            }
        }

        private void DrawH5Section()
        {
            if (inspection == null)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.HelpBox("Select an .h5/.hdf5 file. SIMBA will inspect it with the configured Python environment before conversion.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("HDF5 preview", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Vertices dataset", inspection.verticesDataset);
                EditorGUILayout.TextField("Connectivity", inspection.connectivityDataset);
                EditorGUILayout.IntField("Frames", inspection.frameCount);
                EditorGUILayout.IntField("Values", inspection.valueCount);
                EditorGUILayout.IntField("Elements", inspection.elementCount);
            }
            geometryType = (GeometryType)EditorGUILayout.EnumPopup(new GUIContent("Geometry", "Auto-detected; override when necessary."), geometryType);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Fields to export", EditorStyles.boldLabel);
            for (int i = 0; i < inspection.fields.Length; i++)
            {
                bool isRadius = string.Equals(inspection.fields[i], "Radius", StringComparison.OrdinalIgnoreCase);
                if (isRadius) includeRadius = EditorGUILayout.ToggleLeft("Radius (synthetic fallback)", includeRadius);
                else selectedFields[i] = EditorGUILayout.ToggleLeft(inspection.fields[i], selectedFields[i]);
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Conversion", EditorStyles.boldLabel);
            fps = Mathf.Max(0.001f, EditorGUILayout.FloatField("Source FPS", fps));
            frameStep = Mathf.Max(1, EditorGUILayout.IntField("Frame step", frameStep));
            positionScale = EditorGUILayout.FloatField("Position scale", positionScale);
            swapYZ = EditorGUILayout.Toggle("Convert Z-up to Y-up", swapYZ);
            outputFileName = EditorGUILayout.TextField("Output file", outputFileName);
            if (string.IsNullOrWhiteSpace(Path.GetExtension(outputFileName))) outputFileName += ".bin";

            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledScope(!CanConvert()))
            {
                if (GUILayout.Button("Convert to StreamingAssets", GUILayout.Height(32))) ConvertH5();
            }

            if (header != null) DrawPlayerOptions();
        }

        private void DrawBinarySection()
        {
            if (header == null)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.HelpBox("Select a SIMBA v3 binary.", MessageType.Info);
                return;
            }
            DrawBinaryPreview();
            DrawPlayerOptions();
        }

        private void DrawBinaryPreview()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Binary preview", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Version", header.Version);
                EditorGUILayout.IntField("Frames", header.FrameCount);
                EditorGUILayout.IntField(header.GeometryType == GeometryType.ShellMesh ? "Vertices" : "Nodes", header.ValueCount);
                EditorGUILayout.IntField(header.GeometryType == GeometryType.ShellMesh ? "Triangles" : "Edges", header.ElementCount);
                EditorGUILayout.FloatField("FPS", header.FramesPerSecond);
            }
            geometryType = (GeometryType)EditorGUILayout.EnumPopup("Geometry", geometryType);
        }

        private void DrawPlayerOptions()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Create visualization", EditorStyles.boldLabel);
            selectedInitialField = EditorGUILayout.Popup("Initial field", Mathf.Clamp(selectedInitialField, 0, header.FieldNames.Count - 1), header.FieldNames.ToArray());
            colorMap = (SIMBAColorMap)EditorGUILayout.EnumPopup("Colormap", colorMap);
            parent = (Transform)EditorGUILayout.ObjectField("Parent", parent, typeof(Transform), true);
            if (geometryType == GeometryType.LineMesh)
            {
                lineRadius = EditorGUILayout.FloatField("Tube radius", lineRadius);
                lineSides = EditorGUILayout.IntSlider("Tube sides", lineSides, 3, 16);
            }
            using (new EditorGUI.DisabledScope(colorMap == SIMBAColorMap.Custom))
            {
                if (GUILayout.Button("Create Player", GUILayout.Height(34))) CreatePlayer();
            }
        }

        private bool CanConvert()
        {
            if (inspection == null || string.IsNullOrWhiteSpace(SIMBAEditorSettings.PythonPath) || string.IsNullOrWhiteSpace(outputFileName)) return false;
            if (includeRadius) return true;
            for (int i = 0; i < selectedFields.Length; i++) if (selectedFields[i] && !string.Equals(inspection.fields[i], "Radius", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void Browse()
        {
            string directory = string.IsNullOrEmpty(sourcePath) ? Application.dataPath : Path.GetDirectoryName(sourcePath);
            string selected = sourceMode == SourceMode.HDF5
                ? EditorUtility.OpenFilePanelWithFilters("Select HDF5 simulation", directory, new[] { "HDF5", "h5,hdf5", "All files", "*" })
                : EditorUtility.OpenFilePanelWithFilters("Select SIMBA binary", directory, new[] { "SIMBA binary", "bin,bytes", "All files", "*" });
            if (string.IsNullOrEmpty(selected)) return;
            sourcePath = selected; errorMessage = string.Empty; conversionLog = string.Empty; header = null;
            if (sourceMode == SourceMode.HDF5) InspectH5(); else ReadBinary(selected);
        }

        private void InspectH5()
        {
            try
            {
                var result = SIMBAPythonProcess.Run(SIMBAEditorSettings.PythonPath, SIMBAPythonProcess.PythonTool("simba_h5_inspect.py"), "--input", sourcePath);
                if (!result.Success) throw new InvalidOperationException(result.StandardError);
                inspection = JsonUtility.FromJson<SIMBAH5Inspection>(result.StandardOutput);
                if (inspection == null || inspection.fields == null) throw new InvalidDataException("Python returned invalid inspection JSON.");
                geometryType = string.Equals(inspection.suggestedGeometry, "LineMesh", StringComparison.OrdinalIgnoreCase) ? GeometryType.LineMesh : GeometryType.ShellMesh;
                selectedFields = new bool[inspection.fields.Length];
                for (int i = 0; i < inspection.fields.Length; i++) selectedFields[i] = !string.Equals(inspection.fields[i], "Radius", StringComparison.OrdinalIgnoreCase);
                includeRadius = true;
                string baseName = Path.GetFileNameWithoutExtension(sourcePath);
                outputFileName = baseName + (geometryType == GeometryType.LineMesh ? "_line_simba.bin" : "_shell_simba.bin");
            }
            catch (Exception e) { inspection = null; errorMessage = e.Message; }
        }

        private void ConvertH5()
        {
            string streaming = Path.Combine(Application.dataPath, "StreamingAssets");
            Directory.CreateDirectory(streaming);
            string destination = Path.Combine(streaming, Path.GetFileName(outputFileName));
            string script = SIMBAPythonProcess.PythonTool(geometryType == GeometryType.LineMesh ? "line_mesh_h5_to_fields.py" : "shell_mesh_h5_to_fields.py");
            var arguments = new List<string> { script, "--input", sourcePath, "--output", destination, "--fps", fps.ToString(System.Globalization.CultureInfo.InvariantCulture), "--frame-step", frameStep.ToString(), "--scale", positionScale.ToString(System.Globalization.CultureInfo.InvariantCulture), "--fields" };
            for (int i = 0; i < inspection.fields.Length; i++)
                if (selectedFields[i] && !string.Equals(inspection.fields[i], "Radius", StringComparison.OrdinalIgnoreCase)) arguments.Add(inspection.fields[i]);
            if (includeRadius) arguments.Add("--add-radius");
            if (!swapYZ) arguments.Add("--no-swap-yz");

            try
            {
                EditorUtility.DisplayProgressBar("SIMBA", "Converting HDF5 simulation...", 0.5f);
                var result = SIMBAPythonProcess.Run(SIMBAEditorSettings.PythonPath, arguments.ToArray());
                if (!result.Success) throw new InvalidOperationException(result.StandardError);
                conversionLog = result.StandardOutput;
                AssetDatabase.Refresh();
                ReadBinary(destination);
            }
            catch (Exception e) { errorMessage = e.Message; }
            finally { EditorUtility.ClearProgressBar(); }
        }

        private void ReadBinary(string path)
        {
            try
            {
                header = SIMBABinaryHeaderReader.Read(path);
                geometryType = header.GeometryType;
                selectedInitialField = Mathf.Clamp(selectedInitialField, 0, header.FieldNames.Count - 1);
                errorMessage = string.Empty;
            }
            catch (Exception e) { header = null; errorMessage = e.Message; }
        }

        private void CreatePlayer()
        {
            try
            {
                string binaryPath = header.SourcePath;
                string streamingFolder = Path.Combine(Application.dataPath, "StreamingAssets");
                Directory.CreateDirectory(streamingFolder);
                string destination = Path.Combine(streamingFolder, Path.GetFileName(binaryPath));
                if (!string.Equals(Path.GetFullPath(binaryPath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) File.Copy(binaryPath, destination, true);
                AssetDatabase.Refresh();
                SIMBAPlayerFactory.Create(header, geometryType, Path.GetFileName(destination), header.FieldNames[selectedInitialField], colorMap, parent, lineRadius, lineSides);
                Close();
            }
            catch (Exception e) { Debug.LogException(e); EditorUtility.DisplayDialog("SIMBA Import Failed", e.Message, "OK"); }
        }
    }
}
#endif
