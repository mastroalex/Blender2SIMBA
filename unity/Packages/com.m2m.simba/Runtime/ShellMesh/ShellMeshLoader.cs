using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace M2M.SIMBA
{
    [DisallowMultipleComponent, RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ShellMeshLoader : MonoBehaviour
    {
        private static readonly byte[] MagicV3 = Encoding.ASCII.GetBytes("SHMSH003");
        private static readonly byte[] MagicV4 = Encoding.ASCII.GetBytes("SHMSH004");
        public string fileName = "shell_mesh_fields.bin";
        public bool loadOnStart = true;
        public bool recalculateNormals = true;
        public bool markDynamic = true;
        public bool IsLoaded { get; private set; }
        public ShellMeshData Data { get; private set; }
        public Mesh RuntimeMesh { get; private set; }
        public event Action Loaded;

        private IEnumerator Start() { if (loadOnStart) yield return Load(); }

        public IEnumerator Load()
        {
            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            byte[] bytes;
            if (path.Contains("://"))
            {
                using UnityWebRequest request = UnityWebRequest.Get(path);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) throw new IOException(request.error);
                bytes = request.downloadHandler.data;
            }
            else bytes = File.ReadAllBytes(path);
            Data = Parse(bytes);
            BuildMesh();
            IsLoaded = true;
            Loaded?.Invoke();
        }

        private static ShellMeshData Parse(byte[] bytes)
        {
            using BinaryReader r = new BinaryReader(new MemoryStream(bytes, false), Encoding.UTF8);
            byte[] magic = r.ReadBytes(8);
            if (Equal(magic, MagicV3)) return ParseV3(r);
            if (Equal(magic, MagicV4)) return ParseV4(r);
            throw new InvalidDataException("Magic SIMBA ShellMesh non valido.");
        }

        private static ShellMeshData ParseV3(BinaryReader r)
        {
            int version = r.ReadInt32();
            if (version != 3) throw new InvalidDataException($"Versione {version} non supportata.");
            ValidateGeometry(r);
            var d = NewData(version, ShellTopologyMode.Static, r, false);
            int fieldCount = r.ReadInt32();
            ReadFieldHeaders(r, d, fieldCount);
            d.Triangles = ReadInts(r, d.TriangleCount * 3);
            ReadFieldRanges(r, d);
            d.Vertices = new Vector3[d.FrameCount][];
            for (int frame = 0; frame < d.FrameCount; frame++)
            {
                d.Vertices[frame] = ReadVertices(r, d.VertexCount);
                ReadFieldValues(r, d, frame, d.VertexCount);
            }
            EnsureEnd(r);
            return d;
        }

        private static ShellMeshData ParseV4(BinaryReader r)
        {
            int version = r.ReadInt32();
            if (version != 4) throw new InvalidDataException($"Versione {version} non supportata.");
            ValidateGeometry(r);
            ShellTopologyMode mode = (ShellTopologyMode)r.ReadInt32();
            if (mode != ShellTopologyMode.Static && mode != ShellTopologyMode.Dynamic) throw new InvalidDataException("TopologyMode non valido.");
            var d = NewData(version, mode, r, true);
            int fieldCount = r.ReadInt32();
            ReadFieldHeaders(r, d, fieldCount);
            ReadFieldRanges(r, d);
            d.Vertices = new Vector3[d.FrameCount][];
            if (!d.HasDynamicTopology)
            {
                d.Triangles = ReadInts(r, d.TriangleCount * 3);
                for (int frame = 0; frame < d.FrameCount; frame++)
                {
                    d.Vertices[frame] = ReadVertices(r, d.VertexCount);
                    ReadFieldValues(r, d, frame, d.VertexCount);
                }
            }
            else
            {
                d.FrameTriangles = new int[d.FrameCount][];
                for (int frame = 0; frame < d.FrameCount; frame++)
                {
                    int nv = r.ReadInt32();
                    int nt = r.ReadInt32();
                    if (nv <= 0 || nt <= 0) throw new InvalidDataException($"Frame {frame}: conteggi non validi.");
                    d.Vertices[frame] = ReadVertices(r, nv);
                    d.FrameTriangles[frame] = ReadInts(r, nt * 3);
                    ReadFieldValues(r, d, frame, nv);
                }
            }
            EnsureEnd(r);
            return d;
        }

        private static ShellMeshData NewData(
            int version,
            ShellTopologyMode mode,
            BinaryReader reader,
            bool hasFrameStep)
        {
            ShellMeshData data = new ShellMeshData
            {
                Version = version,
                TopologyMode = mode,
                FrameCount = reader.ReadInt32(),
                VertexCount = reader.ReadInt32(),
                TriangleCount = reader.ReadInt32(),
                SourceFps = reader.ReadSingle(),
                FrameStep = hasFrameStep ? reader.ReadInt32() : 1
            };

            return data;
        }

        private static void ValidateGeometry(BinaryReader r)
        {
            GeometryType type = (GeometryType)r.ReadInt32();
            if (type != GeometryType.ShellMesh) throw new InvalidDataException($"Il file contiene {type}, non ShellMesh.");
        }

        private static void ReadFieldHeaders(BinaryReader r, ShellMeshData d, int count)
        {
            if (d.FrameCount <= 0 || d.VertexCount <= 0 || d.TriangleCount <= 0 || d.FramesPerSecond <= 0 || count <= 0)
                throw new InvalidDataException("Header ShellMesh non valido.");
            d.Fields = new AnimatedField[count];
            for (int i = 0; i < count; i++) d.Fields[i] = new AnimatedField
            {
                Name = ReadString(r), Units = ReadString(r), GlobalMin = r.ReadSingle(), GlobalMax = r.ReadSingle(),
                FrameMin = new float[d.FrameCount], FrameMax = new float[d.FrameCount], Values = new float[d.FrameCount][]
            };
        }

        private static void ReadFieldRanges(BinaryReader r, ShellMeshData d)
        {
            foreach (AnimatedField f in d.Fields)
            {
                for (int i = 0; i < d.FrameCount; i++) f.FrameMin[i] = r.ReadSingle();
                for (int i = 0; i < d.FrameCount; i++) f.FrameMax[i] = r.ReadSingle();
            }
        }

        private static void ReadFieldValues(BinaryReader r, ShellMeshData d, int frame, int count)
        {
            foreach (AnimatedField f in d.Fields)
            {
                float[] values = new float[count];
                for (int i = 0; i < count; i++) values[i] = r.ReadSingle();
                f.Values[frame] = values;
            }
        }

        private static Vector3[] ReadVertices(BinaryReader r, int count)
        {
            Vector3[] v = new Vector3[count];
            for (int i = 0; i < count; i++) v[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            return v;
        }
        private static int[] ReadInts(BinaryReader r, int count) { int[] a = new int[count]; for (int i = 0; i < count; i++) a[i] = r.ReadInt32(); return a; }
        private static string ReadString(BinaryReader r) { int n = r.ReadInt32(); if (n < 0 || n > 1024 * 1024) throw new InvalidDataException("Stringa non valida."); return Encoding.UTF8.GetString(r.ReadBytes(n)); }
        private static void EnsureEnd(BinaryReader r) { if (r.BaseStream.Position != r.BaseStream.Length) Debug.LogWarning($"[SIMBA] {r.BaseStream.Length - r.BaseStream.Position} byte non letti nel file ShellMesh."); }

        private void BuildMesh()
        {
            Vector3[] vertices = Data.Vertices[0];
            int[] triangles = Data.GetTriangles(0);
            RuntimeMesh = new Mesh { name = "shell_mesh_runtime", indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            if (markDynamic) RuntimeMesh.MarkDynamic();
            RuntimeMesh.vertices = vertices;
            RuntimeMesh.triangles = triangles;
            RuntimeMesh.RecalculateBounds();
            if (recalculateNormals) RuntimeMesh.RecalculateNormals();
            GetComponent<MeshFilter>().sharedMesh = RuntimeMesh;
        }

        private static bool Equal(byte[] a, byte[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    }
}
