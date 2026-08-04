#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace M2M.SIMBA.Editor
{
    /// <summary>
    /// Describes how shell-mesh connectivity is stored in a SIMBA binary.
    /// </summary>
    public enum SIMBATopologyMode
    {
        /// <summary>
        /// A single connectivity array is shared by every animation frame.
        /// </summary>
        Static = 0,

        /// <summary>
        /// Every frame stores its own vertex and connectivity arrays.
        /// </summary>
        Dynamic = 1
    }

    [Serializable]
    public sealed class SIMBABinaryHeader
    {
        public string SourcePath;
        public GeometryType GeometryType;
        public SIMBATopologyMode TopologyMode = SIMBATopologyMode.Static;

        public int Version;
        public int FrameCount;

        /// <summary>
        /// For static topology this is the exact number of vertices/nodes.
        /// For dynamic topology this is the maximum value count across frames.
        /// </summary>
        public int ValueCount;

        /// <summary>
        /// For static topology this is the exact number of triangles/edges.
        /// For dynamic topology this is the maximum element count across frames.
        /// </summary>
        public int ElementCount;

        public float FramesPerSecond;
        public int FrameStep = 1;

        public readonly List<string> FieldNames = new List<string>();
        public readonly List<string> FieldUnits = new List<string>();

        public bool HasVariableTopology =>
            TopologyMode == SIMBATopologyMode.Dynamic;
    }
}
#endif
