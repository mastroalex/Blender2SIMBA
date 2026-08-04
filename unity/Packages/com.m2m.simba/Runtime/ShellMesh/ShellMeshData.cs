using System;
using UnityEngine;

namespace M2M.SIMBA
{
    public sealed class ShellMeshData
    {
        public int Version;
        public ShellTopologyMode TopologyMode = ShellTopologyMode.Static;
        public int FrameCount;
        public int VertexCount;
        public int TriangleCount;
        public float SourceFps;
        public int FrameStep = 1;
        public int[] Triangles = Array.Empty<int>();
        public int[][] FrameTriangles = Array.Empty<int[]>();
        public Vector3[][] Vertices = Array.Empty<Vector3[]>();
        public AnimatedField[] Fields = Array.Empty<AnimatedField>();

        public float FramesPerSecond =>
            SourceFps / Mathf.Max(1, FrameStep);

        public bool HasDynamicTopology =>
            TopologyMode == ShellTopologyMode.Dynamic;

        public int GetVertexCount(int frame) =>
            Vertices[frame].Length;

        public int[] GetTriangles(int frame) =>
            HasDynamicTopology ? FrameTriangles[frame] : Triangles;

        public int GetTriangleCount(int frame) =>
            GetTriangles(frame).Length / 3;
    }
}
