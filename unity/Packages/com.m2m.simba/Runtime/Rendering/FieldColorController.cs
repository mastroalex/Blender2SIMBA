using System;
using UnityEngine;

namespace M2M.SIMBA
{
    [DisallowMultipleComponent]
    public sealed class FieldColorController : MonoBehaviour
    {
        public enum RangeMode { Global, PerFrame, Manual }
        [Header("Field selection")]
        [SerializeField, Min(0)] private int selectedFieldIndex;
        [SerializeField] private string preferredFieldName = "Stress";
        [Header("Colormap")]
        public SIMBAColorMap colorMapPreset = SIMBAColorMap.Turbo;
        [Tooltip("Used only when Color Map Preset is Custom.")] public Texture2D customColorMap;
        public RangeMode rangeMode = RangeMode.Global;
        public float manualMin;
        public float manualMax = 1f;
        public bool interpolateField = true;
        [Header("Appearance")]
        [Range(0f, 1f)] public float metallic;
        [Range(0f, 1f)] public float smoothness = 0.35f;

        public int SelectedFieldIndex => selectedFieldIndex;
        public SIMBAColorMap ColorMapPreset => colorMapPreset;
        public RangeMode CurrentRangeMode => rangeMode;
        public float ManualMinimum => manualMin;
        public float ManualMaximum => manualMax;
        public int ConfiguredFieldIndex => selectedFieldIndex;
        public string PreferredFieldName => preferredFieldName;
        public string SelectedFieldName => source != null && source.IsLoaded && source.FieldCount > 0 ? source.GetField(selectedFieldIndex).Name : string.Empty;
        public string[] AvailableFieldNames { get { if (source == null || !source.IsLoaded) return Array.Empty<string>(); string[] n = new string[source.FieldCount]; for (int i = 0; i < n.Length; i++) n[i] = source.GetField(i).Name; return n; } }

        public event Action<int, string> FieldChanged;
        public event Action<SIMBAColorMap> ColorMapChanged;
        public event Action<RangeMode, float, float> RangeChanged;

        private static readonly int FieldBufferId = Shader.PropertyToID("_FieldBuffer");
        private static readonly int ColorMapId = Shader.PropertyToID("_ColorMap");
        private static readonly int FieldMinId = Shader.PropertyToID("_FieldMin");
        private static readonly int FieldMaxId = Shader.PropertyToID("_FieldMax");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private const string VertexColorKeyword = "SIMBA_VERTEX_COLORS";

        private IFieldAnimationSource source;
        private Renderer targetRenderer;
        private Material runtimeMaterial;
        private float[] interpolatedValues = Array.Empty<float>();
#if UNITY_WEBGL && !UNITY_EDITOR
        private Mesh targetMesh;
        private Color32[] encodedVertexColors = Array.Empty<Color32>();
#else
        private GraphicsBuffer fieldBuffer;
        private int bufferCount;
#endif
        private bool backendReady;

        private void Awake()
        {
            source = FindSource();
            if (source == null) throw new MissingComponentException("Serve un componente IFieldAnimationSource sullo stesso GameObject.");
            source.DataLoaded += HandleLoaded;
            source.FrameChanged += HandleFrameChanged;
        }
        private void Start() { if (source.IsLoaded) HandleLoaded(); }
        private void OnDestroy()
        {
            if (source != null) { source.DataLoaded -= HandleLoaded; source.FrameChanged -= HandleFrameChanged; }
#if !UNITY_WEBGL || UNITY_EDITOR
            fieldBuffer?.Dispose();
#endif
            if (runtimeMaterial != null) Destroy(runtimeMaterial);
        }
        private IFieldAnimationSource FindSource() { foreach (MonoBehaviour b in GetComponents<MonoBehaviour>()) if (b is IFieldAnimationSource s) return s; return null; }

        private void HandleLoaded()
        {
            if (!isActiveAndEnabled) return;
            if (source.FieldCount <= 0) throw new InvalidOperationException("Il file non contiene campi.");
            int preferred = string.IsNullOrWhiteSpace(preferredFieldName) ? -1 : source.FindField(preferredFieldName);
            if (preferred >= 0) selectedFieldIndex = preferred;
            selectedFieldIndex = Mathf.Clamp(selectedFieldIndex, 0, source.FieldCount - 1);
            targetRenderer = source.TargetRenderer;
            if (targetRenderer == null || targetRenderer.sharedMaterial == null) throw new MissingReferenceException("Assegna un materiale SIMBA/FieldGradientURP.");
            CreateRuntimeMaterial();
#if UNITY_WEBGL && !UNITY_EDITOR
            MeshFilter filter = targetRenderer.GetComponent<MeshFilter>();
            targetMesh = filter != null ? filter.sharedMesh : (targetRenderer as SkinnedMeshRenderer)?.sharedMesh;
            if (targetMesh == null) throw new MissingComponentException("Il backend WebGL richiede MeshFilter o SkinnedMeshRenderer.");
#endif
            backendReady = true;
            UpdateField(source.CurrentFrame, source.NextFrame, source.FrameInterpolation);
            FieldChanged?.Invoke(selectedFieldIndex, SelectedFieldName);
        }

        private void CreateRuntimeMaterial()
        {
            if (runtimeMaterial != null) Destroy(runtimeMaterial);
            runtimeMaterial = new Material(targetRenderer.sharedMaterial) { name = targetRenderer.sharedMaterial.name + " (Runtime)" };
            targetRenderer.sharedMaterial = runtimeMaterial;
#if UNITY_WEBGL && !UNITY_EDITOR
            runtimeMaterial.EnableKeyword(VertexColorKeyword);
#else
            runtimeMaterial.DisableKeyword(VertexColorKeyword);
#endif
        }

        private void EnsureBackendCapacity(int count)
        {
            if (count <= 0) throw new InvalidOperationException("Il frame non contiene valori di campo.");
            if (interpolatedValues.Length != count) interpolatedValues = new float[count];
#if UNITY_WEBGL && !UNITY_EDITOR
            if (encodedVertexColors.Length != count) encodedVertexColors = new Color32[count];
#else
            if (fieldBuffer == null || bufferCount != count)
            {
                fieldBuffer?.Dispose();
                fieldBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, sizeof(float));
                bufferCount = count;
                runtimeMaterial.SetBuffer(FieldBufferId, fieldBuffer);
            }
#endif
        }

        public bool SetField(string name) { if (source == null || !source.IsLoaded) { preferredFieldName = name; return false; } int i = source.FindField(name); if (i < 0) return false; SetField(i); return true; }
        public void SetField(int index) { if (source == null || !source.IsLoaded) { selectedFieldIndex = Mathf.Max(0, index); return; } selectedFieldIndex = Mathf.Clamp(index, 0, source.FieldCount - 1); preferredFieldName = source.GetField(selectedFieldIndex).Name; RefreshCurrentFrame(); FieldChanged?.Invoke(selectedFieldIndex, SelectedFieldName); }
        public void SetColorMap(SIMBAColorMap preset) { colorMapPreset = preset; ApplyMaterialProperties(); ColorMapChanged?.Invoke(preset); }
        public void SetColorMap(Texture2D texture) { customColorMap = texture; colorMapPreset = SIMBAColorMap.Custom; ApplyMaterialProperties(); ColorMapChanged?.Invoke(colorMapPreset); }
        public void UseGlobalRange() { rangeMode = RangeMode.Global; RefreshCurrentFrame(); RangeChanged?.Invoke(rangeMode, manualMin, manualMax); }
        public void UsePerFrameRange() { rangeMode = RangeMode.PerFrame; RefreshCurrentFrame(); RangeChanged?.Invoke(rangeMode, manualMin, manualMax); }
        public void SetManualRange(float min, float max) { if (max < min) (min, max) = (max, min); manualMin = min; manualMax = max; rangeMode = RangeMode.Manual; RefreshCurrentFrame(); RangeChanged?.Invoke(rangeMode, min, max); }
        public void SetMetallic(float v) { metallic = Mathf.Clamp01(v); ApplyMaterialProperties(); }
        public void SetSmoothness(float v) { smoothness = Mathf.Clamp01(v); ApplyMaterialProperties(); }
        public void RefreshCurrentFrame() { if (source != null && source.IsLoaded && backendReady) UpdateField(source.CurrentFrame, source.NextFrame, source.FrameInterpolation); }
        public void ConfigureInitialField(string name) { preferredFieldName = name ?? string.Empty; if (source != null && source.IsLoaded) SetField(preferredFieldName); }
        private void HandleFrameChanged(int frame, int next, float t) { if (backendReady) UpdateField(frame, next, t); }

        private void UpdateField(int frame, int nextFrame, float interpolation)
        {
            if (runtimeMaterial == null || source == null || !source.IsLoaded) return;
            AnimatedField field = source.GetField(selectedFieldIndex);
            float[] a = field.Values[frame];
            float t = interpolateField && nextFrame != frame && field.Values[nextFrame].Length == a.Length ? interpolation : 0f;
            EnsureBackendCapacity(a.Length);
            float[] values = a;
            if (t != 0f)
            {
                float[] b = field.Values[nextFrame];
                for (int i = 0; i < a.Length; i++) interpolatedValues[i] = Mathf.LerpUnclamped(a[i], b[i], t);
                values = interpolatedValues;
            }
            ResolveRange(field, frame, nextFrame, t, out float min, out float max);
#if UNITY_WEBGL && !UNITY_EDITOR
            if (targetMesh == null) return;
            if (targetMesh.vertexCount != values.Length) throw new InvalidOperationException($"SIMBA WebGL: valori={values.Length}, vertici={targetMesh.vertexCount}.");
            float inv = 1f / Mathf.Max(max - min, 1e-20f);
            for (int i = 0; i < values.Length; i++) { byte c = (byte)Mathf.RoundToInt(Mathf.Clamp01((values[i] - min) * inv) * 255f); encodedVertexColors[i] = new Color32(c, 0, 0, 255); }
            targetMesh.colors32 = encodedVertexColors;
#else
            fieldBuffer.SetData(values);
            runtimeMaterial.SetBuffer(FieldBufferId, fieldBuffer);
            runtimeMaterial.SetFloat(FieldMinId, min);
            runtimeMaterial.SetFloat(FieldMaxId, max);
#endif
            ApplyMaterialProperties();
        }

        private void ResolveRange(AnimatedField f, int frame, int next, float t, out float min, out float max)
        {
            if (rangeMode == RangeMode.Manual) { min = manualMin; max = manualMax; }
            else if (rangeMode == RangeMode.PerFrame) { min = Mathf.Lerp(f.FrameMin[frame], f.FrameMin[next], t); max = Mathf.Lerp(f.FrameMax[frame], f.FrameMax[next], t); }
            else { min = f.GlobalMin; max = f.GlobalMax; }
            if (max < min) (min, max) = (max, min);
            if (Mathf.Abs(max - min) < 1e-20f) max = min + 1e-20f;
        }

        private void ApplyMaterialProperties()
        {
            if (runtimeMaterial == null) return;
            Texture2D map = colorMapPreset == SIMBAColorMap.Custom ? customColorMap : SIMBAColorMaps.Load(colorMapPreset);
            if (map != null) { map.wrapMode = TextureWrapMode.Clamp; map.filterMode = FilterMode.Bilinear; runtimeMaterial.SetTexture(ColorMapId, map); }
            runtimeMaterial.SetFloat(MetallicId, metallic);
            runtimeMaterial.SetFloat(SmoothnessId, smoothness);
        }
    }
}
