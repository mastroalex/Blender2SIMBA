#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace M2M.SIMBA.Editor
{
    public sealed class SIMBAAboutWindow : EditorWindow
    {
        private Texture2D icon;

        public static void ShowWindow()
        {
            SIMBAAboutWindow window = GetWindow<SIMBAAboutWindow>(true, "About SIMBA", true);
            window.minSize = new Vector2(420, 300);
            window.Show();
        }

        private void OnEnable()
        {
            PackageManagerPackageInfo info = PackageManagerPackageInfo.FindForAssembly(typeof(SIMBAPlayer).Assembly);
            if (info != null)
            {
                string iconPath = Path.Combine(info.assetPath, "Editor", "Icons", "SIMBA_128.png").Replace('\\', '/');
                icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            }
        }

        private void OnGUI()
        {
            GUILayout.Space(12);
            if (icon != null)
            {
                Rect rect = GUILayoutUtility.GetRect(96, 96, GUILayout.ExpandWidth(true));
                GUI.DrawTexture(new Rect((rect.width - 96) * 0.5f, rect.y, 96, 96), icon, ScaleMode.ScaleToFit, true);
            }
            GUILayout.Label("SIMBA", new GUIStyle(EditorStyles.boldLabel) { fontSize = 24, alignment = TextAnchor.MiddleCenter });
            GUILayout.Label("SIMulation Buffered Animation", new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter });
            GUILayout.Label("A Unity framework for animated scientific simulations.", new GUIStyle(EditorStyles.wordWrappedLabel) { alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(12);
            GUILayout.Label("Version 1.0.0", new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter });
            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Documentation")) SIMBAEditorMenu.OpenDocumentation();
                if (GUILayout.Button("MIT License")) OpenLicense();
            }
            GUILayout.Space(8);
        }

        private static void OpenLicense()
        {
            PackageManagerPackageInfo info = PackageManagerPackageInfo.FindForAssembly(typeof(SIMBAPlayer).Assembly);
            if (info != null) EditorUtility.RevealInFinder(Path.Combine(info.resolvedPath, "LICENSE.md"));
        }
    }
}
#endif
