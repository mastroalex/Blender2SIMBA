#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace M2M.SIMBA.Editor
{
    public static class SIMBAPlayerFactory
    {
        private const string GeneratedRoot = "Assets/SIMBA Generated";
        private const string MaterialFolder = GeneratedRoot + "/Materials";

        public static GameObject Create(
            SIMBABinaryHeader header,
            GeometryType geometryType,
            string streamingFileName,
            string initialField,
            SIMBAColorMap colorMap,
            Transform parent,
            float lineRadius,
            int lineSides)
        {
            EnsureFolder("Assets", "SIMBA Generated");
            EnsureFolder(GeneratedRoot, "Materials");

            string simulationName = Path.GetFileNameWithoutExtension(streamingFileName);

            GameObject gameObject = new GameObject($"[SIMBA] {simulationName}");
            Undo.RegisterCreatedObjectUndo(gameObject, "Create SIMBA Player");
            if (parent != null) GameObjectUtility.SetParentAndAlign(gameObject, parent.gameObject);

            MeshFilter filter = Undo.AddComponent<MeshFilter>(gameObject);
            MeshRenderer renderer = Undo.AddComponent<MeshRenderer>(gameObject);
            renderer.sharedMaterial = CreateMaterial(gameObject.name, colorMap);

            if (geometryType == GeometryType.ShellMesh)
            {
                ShellMeshLoader loader = Undo.AddComponent<ShellMeshLoader>(gameObject);
                loader.fileName = streamingFileName;
                Undo.AddComponent<ShellMeshAnimator>(gameObject);
            }
            else
            {
                LineMeshPlayer player = Undo.AddComponent<LineMeshPlayer>(gameObject);
                player.fileName = streamingFileName;
                player.tubeRadius = Mathf.Max(1e-7f, lineRadius);
                player.tubeSides = Mathf.Clamp(lineSides, 3, 16);
            }

            FieldColorController colors = Undo.AddComponent<FieldColorController>(gameObject);
            colors.colorMapPreset = colorMap;
            colors.ConfigureInitialField(initialField);
            Undo.AddComponent<SIMBAPlayer>(gameObject);

            EditorUtility.SetDirty(gameObject);
            Selection.activeGameObject = gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            return gameObject;
        }

        private static Material CreateMaterial(string playerName, SIMBAColorMap colorMap)
        {
            Shader shader = Shader.Find("SIMBA/FieldGradientURP");
            if (shader == null)
                throw new InvalidOperationException("Shader SIMBA/FieldGradientURP not found. Check that URP and the SIMBA package are installed.");

            string safeName = string.Join("_", playerName.Split(Path.GetInvalidFileNameChars()));
            string path = AssetDatabase.GenerateUniqueAssetPath($"{MaterialFolder}/[SIMBA]{safeName}.mat");
            Material material = new Material(shader) { name = "[SIMBA]" + safeName };
            Texture2D texture = SIMBAColorMaps.Load(colorMap);
            if (texture != null) material.SetTexture("_ColorMap", texture);
            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static void EnsureFolder(string parent, string child)
        {
            string full = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(full)) AssetDatabase.CreateFolder(parent, child);
        }
    }
}
#endif
