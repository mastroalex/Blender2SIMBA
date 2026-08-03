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
    public sealed class LineMeshPlayer : MonoBehaviour, IFieldAnimationSource
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("LINEM003");
        [Header("Data")] public string fileName = "line_mesh_fields.bytes"; public bool loadOnStart = true;
        [Header("Playback")] public bool playOnLoad = true, loop = true, interpolateFrames = true; [Min(0f)] public float speed = 1f;
        [Header("Tube")] [Min(1e-7f)] public float tubeRadius = 0.00015f; [Range(3, 16)] public int tubeSides = 6;
        [Header("Updates")] public bool recalculateNormals = true, recalculateBounds = true;
        public bool IsLoaded { get; private set; } public bool IsPlaying { get; private set; } public int CurrentFrame { get; private set; } public int NextFrame { get; private set; } public float FrameInterpolation { get; private set; }
        public int FrameCount => IsLoaded ? data.FrameCount : 0; public int ValueCount => meshVertexCount; public int FieldCount => IsLoaded ? data.Fields.Length : 0; public Renderer TargetRenderer => GetComponent<MeshRenderer>();
        public event Action DataLoaded; public event Action<int, int, float> FrameChanged;
        private LineMeshData data; private Mesh mesh; private Vector3[] vertices; private int[] triangles; private int meshVertexCount; private float time; private AnimatedField[] expandedFields;
        public float CurrentTime => time;
        public float FramesPerSecond => IsLoaded ? data.SourceFps / Mathf.Max(1, data.FrameStep) : 0f;

        private IEnumerator Start() { if (loadOnStart) yield return Load(); }
        public IEnumerator Load()
        {
            string path = Path.Combine(Application.streamingAssetsPath, fileName); byte[] bytes;
            if (path.Contains("://")) { using UnityWebRequest req = UnityWebRequest.Get(path); yield return req.SendWebRequest(); if (req.result != UnityWebRequest.Result.Success) throw new IOException(req.error); bytes = req.downloadHandler.data; } else bytes = File.ReadAllBytes(path);
            data = Parse(bytes); BuildMesh(); BuildExpandedFields(); IsLoaded = true; IsPlaying = playOnLoad; time = 0; Apply(); DataLoaded?.Invoke();
        }
        private void Update() { if (!IsLoaded || !IsPlaying) return; time += Time.deltaTime * speed; float duration = data.SourceFrameIndices[data.FrameCount - 1] / data.SourceFps; if (duration <= 0) return; if (loop) time = Mathf.Repeat(time, duration); else if (time >= duration) { time = duration; IsPlaying = false; } Apply(); }
        public void Play() => IsPlaying = true; public void Pause() => IsPlaying = false; public void Stop() { IsPlaying = false; time = 0; Apply(); }
        public void SetFrame(int frame) { if (!IsLoaded) return; frame = Mathf.Clamp(frame, 0, data.FrameCount - 1); time = data.SourceFrameIndices[frame] / data.SourceFps; Apply(); }
        public void SetNormalizedTime(float normalizedTime) { if (!IsLoaded) return; float duration = data.SourceFrameIndices[data.FrameCount - 1] / data.SourceFps; time = Mathf.Clamp01(normalizedTime) * duration; Apply(); }

        private static LineMeshData Parse(byte[] bytes)
        {
            using BinaryReader r = new BinaryReader(new MemoryStream(bytes, false), Encoding.UTF8);
            if (!Equal(r.ReadBytes(8), Magic)) throw new InvalidDataException("Magic LINEM003 non valido."); int version = r.ReadInt32(); if (version != 3) throw new InvalidDataException($"Versione {version} non supportata.");
            GeometryType geometryType = (GeometryType)r.ReadInt32();
            if (geometryType != GeometryType.LineMesh) throw new InvalidDataException($"Il file contiene {geometryType}, non LineMesh.");
            LineMeshData d = new LineMeshData { FrameCount = r.ReadInt32(), NodeCount = r.ReadInt32(), EdgeCount = r.ReadInt32(), SourceFps = r.ReadSingle(), FrameStep = r.ReadInt32() }; int fieldCount = r.ReadInt32();
            d.Fields = new AnimatedField[fieldCount]; for (int k = 0; k < fieldCount; k++) d.Fields[k] = new AnimatedField { Name = ReadString(r), Units = ReadString(r), GlobalMin = r.ReadSingle(), GlobalMax = r.ReadSingle(), FrameMin = new float[d.FrameCount], FrameMax = new float[d.FrameCount], Values = new float[d.FrameCount][] };
            d.SourceFrameIndices = new int[d.FrameCount]; for (int i = 0; i < d.FrameCount; i++) d.SourceFrameIndices[i] = r.ReadInt32();
            d.Edges = new Vector2Int[d.EdgeCount]; for (int i = 0; i < d.EdgeCount; i++) d.Edges[i] = new Vector2Int(r.ReadInt32(), r.ReadInt32());
            foreach (AnimatedField f in d.Fields) { for (int i = 0; i < d.FrameCount; i++) f.FrameMin[i] = r.ReadSingle(); for (int i = 0; i < d.FrameCount; i++) f.FrameMax[i] = r.ReadSingle(); }
            d.Nodes = new Vector3[d.FrameCount][];
            for (int frame = 0; frame < d.FrameCount; frame++) { Vector3[] n = new Vector3[d.NodeCount]; for (int i = 0; i < n.Length; i++) n[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); d.Nodes[frame] = n; foreach (AnimatedField f in d.Fields) { float[] values = new float[d.NodeCount]; for (int i = 0; i < values.Length; i++) values[i] = r.ReadSingle(); f.Values[frame] = values; } }
            return d;
        }

        private void BuildMesh()
        {
            int perEdge = tubeSides * 2; meshVertexCount = data.EdgeCount * perEdge; vertices = new Vector3[meshVertexCount]; triangles = new int[data.EdgeCount * tubeSides * 6];
            for (int e = 0; e < data.EdgeCount; e++) for (int s = 0; s < tubeSides; s++) { int v = e * perEdge, a = v + s, b = v + (s + 1) % tubeSides, c = v + tubeSides + s, d = v + tubeSides + (s + 1) % tubeSides, t = (e * tubeSides + s) * 6; triangles[t] = a; triangles[t+1] = c; triangles[t+2] = b; triangles[t+3] = b; triangles[t+4] = c; triangles[t+5] = d; }
            mesh = new Mesh { name = "line_mesh_runtime" }; if (meshVertexCount > 65535) mesh.indexFormat = IndexFormat.UInt32; mesh.MarkDynamic(); mesh.vertices = vertices; mesh.triangles = triangles; GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        private void Apply()
        {
            float sourceFrame = time * data.SourceFps; int upper = 0; while (upper < data.FrameCount && data.SourceFrameIndices[upper] < sourceFrame) upper++; NextFrame = Mathf.Clamp(upper, 0, data.FrameCount - 1); CurrentFrame = Mathf.Max(0, NextFrame - 1); if (NextFrame == 0) CurrentFrame = 0;
            float aTime = data.SourceFrameIndices[CurrentFrame], bTime = data.SourceFrameIndices[NextFrame]; FrameInterpolation = interpolateFrames && bTime > aTime ? Mathf.InverseLerp(aTime, bTime, sourceFrame) : 0f;
            Vector3[] a = data.Nodes[CurrentFrame], b = data.Nodes[NextFrame]; int perEdge = tubeSides * 2;
            for (int e = 0; e < data.EdgeCount; e++)
            {
                Vector2Int edge = data.Edges[e]; Vector3 p0 = Vector3.LerpUnclamped(a[edge.x], b[edge.x], FrameInterpolation), p1 = Vector3.LerpUnclamped(a[edge.y], b[edge.y], FrameInterpolation); Vector3 axis = (p1 - p0).normalized; Vector3 refAxis = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up; Vector3 u = Vector3.Cross(axis, refAxis).normalized; Vector3 v = Vector3.Cross(axis, u).normalized;
                int start = e * perEdge; for (int s = 0; s < tubeSides; s++) { float angle = 2f * Mathf.PI * s / tubeSides; Vector3 offset = tubeRadius * (Mathf.Cos(angle) * u + Mathf.Sin(angle) * v); vertices[start+s] = p0 + offset; vertices[start+tubeSides+s] = p1 + offset; }
            }
            mesh.vertices = vertices; if (recalculateNormals) mesh.RecalculateNormals(); if (recalculateBounds) mesh.RecalculateBounds(); FrameChanged?.Invoke(CurrentFrame, NextFrame, FrameInterpolation);
        }

        private void BuildExpandedFields()
        {
            expandedFields = new AnimatedField[data.Fields.Length];
            int perEdge = tubeSides * 2;
            for (int index = 0; index < data.Fields.Length; index++)
            {
                AnimatedField nodeField = data.Fields[index];
                AnimatedField expanded = new AnimatedField
                {
                    Name = nodeField.Name, Units = nodeField.Units,
                    GlobalMin = nodeField.GlobalMin, GlobalMax = nodeField.GlobalMax,
                    FrameMin = nodeField.FrameMin, FrameMax = nodeField.FrameMax,
                    Values = new float[data.FrameCount][]
                };
                for (int frame = 0; frame < data.FrameCount; frame++)
                {
                    float[] values = new float[meshVertexCount];
                    for (int e = 0; e < data.EdgeCount; e++)
                    {
                        Vector2Int edge = data.Edges[e]; int start = e * perEdge;
                        for (int side = 0; side < tubeSides; side++)
                        {
                            values[start + side] = nodeField.Values[frame][edge.x];
                            values[start + tubeSides + side] = nodeField.Values[frame][edge.y];
                        }
                    }
                    expanded.Values[frame] = values;
                }
                expandedFields[index] = expanded;
            }
        }

        public AnimatedField GetField(int index) => expandedFields[index];
        public int FindField(string name) { for (int i = 0; i < FieldCount; i++) if (string.Equals(data.Fields[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i; return -1; }
        private static string ReadString(BinaryReader r) { int n = r.ReadInt32(); return Encoding.UTF8.GetString(r.ReadBytes(n)); }
        private static bool Equal(byte[] a, byte[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    }
}
