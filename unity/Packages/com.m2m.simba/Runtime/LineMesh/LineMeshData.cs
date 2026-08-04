using System;
using UnityEngine;

namespace M2M.SIMBA
{
    public sealed class LineMeshData
    {
        public int Version;
        public ShellTopologyMode TopologyMode = ShellTopologyMode.Static;
        public int FrameCount;
        public int NodeCount;
        public int EdgeCount;
        public int FrameStep = 1;
        public float SourceFps;

        public int[] SourceFrameIndices = Array.Empty<int>();
        public Vector2Int[] Edges = Array.Empty<Vector2Int>();
        public Vector2Int[][] FrameEdges = Array.Empty<Vector2Int[]>();
        public Vector3[][] Nodes = Array.Empty<Vector3[]>();
        public AnimatedField[] Fields = Array.Empty<AnimatedField>();

        public bool HasDynamicTopology =>
            TopologyMode == ShellTopologyMode.Dynamic;

        public int GetNodeCount(int frame) => Nodes[frame].Length;

        public Vector2Int[] GetEdges(int frame) =>
            HasDynamicTopology ? FrameEdges[frame] : Edges;

        public int GetEdgeCount(int frame) => GetEdges(frame).Length;
    }
}
