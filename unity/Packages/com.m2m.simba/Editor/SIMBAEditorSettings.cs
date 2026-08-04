#if UNITY_EDITOR
using UnityEditor;

namespace M2M.SIMBA.Editor
{
    internal static class SIMBAEditorSettings
    {
        private const string Prefix = "M2M.SIMBA.";
        public static string PythonPath { get => EditorPrefs.GetString(Prefix + "PythonPath", string.Empty); set => EditorPrefs.SetString(Prefix + "PythonPath", value ?? string.Empty); }
        public static float DefaultFps { get => EditorPrefs.GetFloat(Prefix + "DefaultFps", 30f); set => EditorPrefs.SetFloat(Prefix + "DefaultFps", value); }
        public static int DefaultFrameStep { get => EditorPrefs.GetInt(Prefix + "DefaultFrameStep", 1); set => EditorPrefs.SetInt(Prefix + "DefaultFrameStep", value); }
        public static SIMBAColorMap DefaultColorMap { get => (SIMBAColorMap)EditorPrefs.GetInt(Prefix + "DefaultColorMap", (int)SIMBAColorMap.Turbo); set => EditorPrefs.SetInt(Prefix + "DefaultColorMap", (int)value); }
    }
}
#endif
