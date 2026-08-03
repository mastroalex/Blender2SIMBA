#if UNITY_EDITOR
using UnityEditor;

namespace M2M.SIMBA.Editor
{
    public sealed class SIMBAColorMapPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/SIMBA/Colormaps/")) return;
            TextureImporter importer = (TextureImporter)assetImporter;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
        }
    }
}
#endif
