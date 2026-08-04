#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace M2M.SIMBA.Editor
{
    public static class SIMBAEditorMenu
    {
        [MenuItem("Tools/SIMBA/Create Default Material")]
        public static void CreateDefaultMaterial()
        {
            Shader shader = Shader.Find("SIMBA/FieldGradientURP");
            if (shader == null) throw new InvalidOperationException("SIMBA shader not found. Ensure URP is installed.");
            const string root = "Assets/SIMBA Generated";
            const string materials = root + "/Materials";
            if (!AssetDatabase.IsValidFolder(root)) AssetDatabase.CreateFolder("Assets", "SIMBA Generated");
            if (!AssetDatabase.IsValidFolder(materials)) AssetDatabase.CreateFolder(root, "Materials");
            string path = AssetDatabase.GenerateUniqueAssetPath(materials + "/SIMBA_Default.mat");
            Material material = new Material(shader);
            material.SetTexture("_ColorMap", SIMBAColorMaps.Load(SIMBAColorMap.Turbo));
            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = material;
        }

        [MenuItem("Tools/SIMBA/Take Screenshot")]
        public static void TakeScreenshot()
        {
            string directory = Path.Combine(Application.dataPath, "SIMBA Generated", "Screenshots");
            string path = SIMBAUtilities.CaptureScreenshot(directory);
            Debug.Log($"[SIMBA] Screenshot requested: {path}");
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/SIMBA/Documentation")]
        public static void OpenDocumentation()
        {
            PackageManagerPackageInfo info = PackageManagerPackageInfo.FindForAssembly(typeof(SIMBAPlayer).Assembly);
            if (info == null) return;
            string path = Path.Combine(info.resolvedPath, "Documentation~", "GettingStarted.md");
            Application.OpenURL(new Uri(path).AbsoluteUri);
        }

        [MenuItem("Tools/SIMBA/About")]
        public static void OpenAbout() => SIMBAAboutWindow.ShowWindow();
    }
}
#endif
