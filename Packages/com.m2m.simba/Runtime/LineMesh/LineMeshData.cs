using System;
using UnityEngine;
namespace M2M.SIMBA
{
    public sealed class LineMeshData
    {
        public int FrameCount, NodeCount, EdgeCount, FrameStep; public float SourceFps;
        public int[] SourceFrameIndices = Array.Empty<int>(); public Vector2Int[] Edges = Array.Empty<Vector2Int>(); public Vector3[][] Nodes = Array.Empty<Vector3[]>(); public AnimatedField[] Fields = Array.Empty<AnimatedField>();
    }
}
