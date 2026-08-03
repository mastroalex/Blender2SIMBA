#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace M2M.SIMBA.Editor
{
    [CustomEditor(typeof(SIMBAPlayer))]
    public sealed class SIMBAPlayerEditor : UnityEditor.Editor
    {
        private int fieldIndex;
        private SIMBAColorMap colorMap = SIMBAColorMap.Turbo;
        private float manualMin;
        private float manualMax = 1f;

        public override void OnInspectorGUI()
        {
            SIMBAPlayer player = (SIMBAPlayer)target;
            DrawHeader(player);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Runtime controls become active in Play Mode. Configure the binary, material and initial field using the SIMBA importer and component inspectors.", MessageType.Info);
                if (GUILayout.Button("Open Import Simulation")) SIMBAImportSimulationWindow.Open();
                return;
            }

            if (!player.IsLoaded)
            {
                EditorGUILayout.HelpBox("Waiting for simulation data...", MessageType.Info);
                if (GUILayout.Button("Reload")) player.Reload();
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Playback", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Play")) player.Play();
                if (GUILayout.Button("Pause")) player.Pause();
                if (GUILayout.Button("Stop")) player.Stop();
                if (GUILayout.Button("Restart")) player.Restart();
            }

            float normalized = player.Duration > 0f ? Mathf.Clamp01(player.CurrentTime / player.Duration) : 0f;
            float changed = EditorGUILayout.Slider("Timeline", normalized, 0f, 1f);
            if (!Mathf.Approximately(changed, normalized)) player.SetNormalizedTime(changed);

            int frame = EditorGUILayout.IntSlider("Frame", player.CurrentFrame, 0, Mathf.Max(0, player.FrameCount - 1));
            if (frame != player.CurrentFrame) player.SetFrame(frame);

            Component playback = player.GetComponent<ShellMeshAnimator>();
            if (playback is ShellMeshAnimator shell)
            {
                float speed = EditorGUILayout.FloatField("Speed", shell.speed);
                if (!Mathf.Approximately(speed, shell.speed)) player.SetSpeed(speed);
                bool loop = EditorGUILayout.Toggle("Loop", shell.loop);
                if (loop != shell.loop) player.SetLoop(loop);
            }
            else if (player.GetComponent<LineMeshPlayer>() is LineMeshPlayer line)
            {
                float speed = EditorGUILayout.FloatField("Speed", line.speed);
                if (!Mathf.Approximately(speed, line.speed)) player.SetSpeed(speed);
                bool loop = EditorGUILayout.Toggle("Loop", line.loop);
                if (loop != line.loop) player.SetLoop(loop);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scientific field", EditorStyles.boldLabel);
            string[] fields = player.AvailableFields;
            fieldIndex = Mathf.Clamp(player.CurrentFieldIndex, 0, Mathf.Max(0, fields.Length - 1));
            if (fields.Length > 0)
            {
                int selected = EditorGUILayout.Popup("Field", fieldIndex, fields);
                if (selected != player.CurrentFieldIndex) player.SetField(selected);
            }

            colorMap = player.CurrentColorMap;
            SIMBAColorMap selectedMap = (SIMBAColorMap)EditorGUILayout.EnumPopup("Colormap", colorMap);
            if (selectedMap != colorMap) player.SetColorMap(selectedMap);

            FieldColorController.RangeMode mode = player.CurrentRangeMode;
            FieldColorController.RangeMode selectedMode = (FieldColorController.RangeMode)EditorGUILayout.EnumPopup("Range", mode);
            if (selectedMode != mode)
            {
                if (selectedMode == FieldColorController.RangeMode.Global) player.UseGlobalRange();
                else if (selectedMode == FieldColorController.RangeMode.PerFrame) player.UsePerFrameRange();
                else player.SetManualRange(manualMin, manualMax);
            }
            if (selectedMode == FieldColorController.RangeMode.Manual)
            {
                manualMin = EditorGUILayout.FloatField("Minimum", manualMin);
                manualMax = EditorGUILayout.FloatField("Maximum", manualMax);
                if (GUILayout.Button("Apply Manual Range")) player.SetManualRange(manualMin, manualMax);
            }

            Repaint();
        }

        private static void DrawHeader(SIMBAPlayer player)
        {
            EditorGUILayout.LabelField("SIMBA Player", EditorStyles.largeLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.EnumPopup("Geometry", player.Geometry);
                EditorGUILayout.Toggle("Loaded", player.IsLoaded);
                EditorGUILayout.IntField("Frames", player.FrameCount);
                EditorGUILayout.FloatField("FPS", player.FramesPerSecond);
                EditorGUILayout.TextField("Current field", player.CurrentField);
            }
        }
    }
}
#endif
