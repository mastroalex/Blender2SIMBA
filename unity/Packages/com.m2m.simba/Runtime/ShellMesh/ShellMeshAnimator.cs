using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace M2M.SIMBA
{
    [DisallowMultipleComponent, RequireComponent(typeof(ShellMeshLoader))]
    public sealed class ShellMeshAnimator : MonoBehaviour, IFieldAnimationSource
    {
        public bool playOnLoad = true, loop = true, interpolateFrames = true, recalculateNormalsEveryFrame = true, recalculateBoundsEveryFrame = true;
        [Min(0f)] public float speed = 1f;
        public bool IsPlaying { get; private set; }
        public int CurrentFrame { get; private set; }
        public int NextFrame { get; private set; }
        public float FrameInterpolation { get; private set; }
        public float CurrentTime { get; private set; }
        public bool IsLoaded => loader != null && loader.IsLoaded;
        public int FrameCount => IsLoaded ? loader.Data.FrameCount : 0;
        public int ValueCount => IsLoaded ? loader.Data.GetVertexCount(CurrentFrame) : 0;
        public int FieldCount => IsLoaded ? loader.Data.Fields.Length : 0;
        public Renderer TargetRenderer => GetComponent<MeshRenderer>();
        public event Action DataLoaded;
        public event Action<int, int, float> FrameChanged;
        private ShellMeshLoader loader;
        private Vector3[] work = Array.Empty<Vector3>();

        private void Awake() { loader = GetComponent<ShellMeshLoader>(); loader.Loaded += OnLoaded; }
        private void OnDestroy() { if (loader != null) loader.Loaded -= OnLoaded; }
        private void Start() { if (loader.IsLoaded) OnLoaded(); }
        private void OnLoaded() { work = new Vector3[loader.Data.GetVertexCount(0)]; CurrentTime = 0; IsPlaying = playOnLoad; Apply(); DataLoaded?.Invoke(); }
        private void Update() { if (!IsLoaded || !IsPlaying) return; CurrentTime += Time.deltaTime * speed; float duration = FrameCount / loader.Data.FramesPerSecond; if (loop) CurrentTime = Mathf.Repeat(CurrentTime, duration); else if (CurrentTime >= duration) { CurrentTime = Mathf.Max(0, duration - 1f / loader.Data.FramesPerSecond); IsPlaying = false; } Apply(); }
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Stop() { IsPlaying = false; CurrentTime = 0; Apply(); }
        public void SetFrame(int frame) { if (!IsLoaded) return; CurrentTime = Mathf.Clamp(frame, 0, FrameCount - 1) / loader.Data.FramesPerSecond; Apply(); }
        public void SetNormalizedTime(float t) { if (!IsLoaded) return; float duration = FrameCount / loader.Data.FramesPerSecond; CurrentTime = Mathf.Clamp01(t) * Mathf.Max(0f, duration - 1f / loader.Data.FramesPerSecond); Apply(); }

        private void Apply()
        {
            float exact = CurrentTime * loader.Data.FramesPerSecond;
            CurrentFrame = loop ? Mod(Mathf.FloorToInt(exact), FrameCount) : Mathf.Clamp(Mathf.FloorToInt(exact), 0, FrameCount - 1);
            NextFrame = loop ? (CurrentFrame + 1) % FrameCount : Mathf.Min(CurrentFrame + 1, FrameCount - 1);
            bool dynamic = loader.Data.HasDynamicTopology;
            FrameInterpolation = !dynamic && interpolateFrames ? exact - Mathf.Floor(exact) : 0f;
            if (dynamic) ApplyDynamicFrame(CurrentFrame); else ApplyStaticFrame();
            FrameChanged?.Invoke(CurrentFrame, NextFrame, FrameInterpolation);
        }

        private void ApplyDynamicFrame(int frame)
        {
            Vector3[] vertices = loader.Data.Vertices[frame];
            int[] triangles = loader.Data.GetTriangles(frame);
            Mesh mesh = loader.RuntimeMesh;
            mesh.Clear(false);
            mesh.indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            if (recalculateBoundsEveryFrame) mesh.RecalculateBounds();
            if (recalculateNormalsEveryFrame) mesh.RecalculateNormals();
        }

        private void ApplyStaticFrame()
        {
            Vector3[] a = loader.Data.Vertices[CurrentFrame];
            Mesh mesh = loader.RuntimeMesh;
            if (interpolateFrames && NextFrame != CurrentFrame)
            {
                Vector3[] b = loader.Data.Vertices[NextFrame];
                if (work.Length != a.Length) work = new Vector3[a.Length];
                for (int i = 0; i < work.Length; i++) work[i] = Vector3.LerpUnclamped(a[i], b[i], FrameInterpolation);
                mesh.vertices = work;
            }
            else mesh.vertices = a;
            if (recalculateBoundsEveryFrame) mesh.RecalculateBounds();
            if (recalculateNormalsEveryFrame) mesh.RecalculateNormals();
        }

        public AnimatedField GetField(int index) => loader.Data.Fields[index];
        public int FindField(string name) { for (int i = 0; i < FieldCount; i++) if (string.Equals(GetField(i).Name, name, StringComparison.OrdinalIgnoreCase)) return i; return -1; }
        private static int Mod(int x, int m) { int r = x % m; return r < 0 ? r + m : r; }
    }
}
