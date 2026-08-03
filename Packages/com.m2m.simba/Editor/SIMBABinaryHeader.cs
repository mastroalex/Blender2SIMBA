#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace M2M.SIMBA.Editor
{
    [Serializable]
    public sealed class SIMBABinaryHeader
    {
        public string SourcePath;
        public GeometryType GeometryType;
        public int Version;
        public int FrameCount;
        public int ValueCount;
        public int ElementCount;
        public float FramesPerSecond;
        public int FrameStep = 1;
        public readonly List<string> FieldNames = new List<string>();
        public readonly List<string> FieldUnits = new List<string>();
    }
}
#endif
