using System;
using UnityEngine;

namespace M2M.SIMBA
{
    public sealed class ShellMeshData
    {
        public int FrameCount;
        public int VertexCount;
        public int TriangleCount;
        public float FramesPerSecond;
        public int[] Triangles = Array.Empty<int>();
        public Vector3[][] Vertices = Array.Empty<Vector3[]>();
        public AnimatedField[] Fields = Array.Empty<AnimatedField>();
    }
}
