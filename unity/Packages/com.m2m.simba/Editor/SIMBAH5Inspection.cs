#if UNITY_EDITOR
using System;

namespace M2M.SIMBA.Editor
{
    [Serializable]
    internal sealed class SIMBAH5Inspection
    {
        public string suggestedGeometry;
        public int frameCount;
        public int valueCount;
        public int elementCount;
        public string verticesDataset;
        public string connectivityDataset;
        public string[] fields = Array.Empty<string>();
        public string[] fieldPaths = Array.Empty<string>();
    }
}
#endif
