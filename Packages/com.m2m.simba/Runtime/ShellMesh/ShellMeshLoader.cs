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
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SHMSH003");
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
                using UnityWebRequest req = UnityWebRequest.Get(path);
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) throw new IOException(req.error);
                bytes = req.downloadHandler.data;
            }
            else bytes = File.ReadAllBytes(path);
            Data = Parse(bytes); BuildMesh(); IsLoaded = true; Loaded?.Invoke();
        }

        private static ShellMeshData Parse(byte[] bytes)
        {
            using BinaryReader r = new BinaryReader(new MemoryStream(bytes, false), Encoding.UTF8);
            if (!Equal(r.ReadBytes(8), Magic)) throw new InvalidDataException("Magic SHMSH003 non valido.");
            int version = r.ReadInt32(); if (version != 3) throw new InvalidDataException($"Versione {version} non supportata.");
            GeometryType geometryType = (GeometryType)r.ReadInt32();
            if (geometryType != GeometryType.ShellMesh) throw new InvalidDataException($"Il file contiene {geometryType}, non ShellMesh.");
            ShellMeshData d = new ShellMeshData { FrameCount = r.ReadInt32(), VertexCount = r.ReadInt32(), TriangleCount = r.ReadInt32(), FramesPerSecond = r.ReadSingle() };
            int fieldCount = r.ReadInt32();
            d.Fields = new AnimatedField[fieldCount];
            for (int k = 0; k < fieldCount; k++) d.Fields[k] = new AnimatedField { Name = ReadString(r), Units = ReadString(r), GlobalMin = r.ReadSingle(), GlobalMax = r.ReadSingle(), FrameMin = new float[d.FrameCount], FrameMax = new float[d.FrameCount], Values = new float[d.FrameCount][] };
            d.Triangles = new int[d.TriangleCount * 3]; for (int i = 0; i < d.Triangles.Length; i++) d.Triangles[i] = r.ReadInt32();
            foreach (AnimatedField f in d.Fields) { for (int i = 0; i < d.FrameCount; i++) f.FrameMin[i] = r.ReadSingle(); for (int i = 0; i < d.FrameCount; i++) f.FrameMax[i] = r.ReadSingle(); }
            d.Vertices = new Vector3[d.FrameCount][];
            for (int frame = 0; frame < d.FrameCount; frame++)
            {
                Vector3[] v = new Vector3[d.VertexCount]; for (int i = 0; i < v.Length; i++) v[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); d.Vertices[frame] = v;
                foreach (AnimatedField f in d.Fields) { float[] values = new float[d.VertexCount]; for (int i = 0; i < values.Length; i++) values[i] = r.ReadSingle(); f.Values[frame] = values; }
            }
            return d;
        }

        private void BuildMesh()
        {
            RuntimeMesh = new Mesh { name = "shell_mesh_runtime" }; if (Data.VertexCount > 65535) RuntimeMesh.indexFormat = IndexFormat.UInt32; if (markDynamic) RuntimeMesh.MarkDynamic();
            RuntimeMesh.vertices = Data.Vertices[0]; RuntimeMesh.triangles = Data.Triangles; RuntimeMesh.RecalculateBounds(); if (recalculateNormals) RuntimeMesh.RecalculateNormals(); GetComponent<MeshFilter>().sharedMesh = RuntimeMesh;
        }
        private static string ReadString(BinaryReader r) { int n = r.ReadInt32(); return Encoding.UTF8.GetString(r.ReadBytes(n)); }
        private static bool Equal(byte[] a, byte[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    }
}
