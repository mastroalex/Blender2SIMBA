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
        [Tooltip("Used only when Color Map Preset is Custom.")]
        public Texture2D customColorMap;
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
        public string SelectedFieldName => source != null && source.IsLoaded && source.FieldCount > 0
            ? source.GetField(selectedFieldIndex).Name : string.Empty;
        public string[] AvailableFieldNames
        {
            get
            {
                if (source == null || !source.IsLoaded) return Array.Empty<string>();
                string[] names = new string[source.FieldCount];
                for (int i = 0; i < names.Length; i++) names[i] = source.GetField(i).Name;
                return names;
            }
        }

        public event Action<int, string> FieldChanged;
        public event Action<SIMBAColorMap> ColorMapChanged;
        public event Action<RangeMode, float, float> RangeChanged;

        private static readonly int FieldBufferId = Shader.PropertyToID("_FieldBuffer");
        private static readonly int ColorMapId = Shader.PropertyToID("_ColorMap");
        private static readonly int FieldMinId = Shader.PropertyToID("_FieldMin");
        private static readonly int FieldMaxId = Shader.PropertyToID("_FieldMax");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        private IFieldAnimationSource source;
        private Renderer targetRenderer;
        private Material runtimeMaterial;
        private GraphicsBuffer fieldBuffer;
        private float[] interpolatedValues;

        private void Awake()
        {
            source = FindSource();
            if (source == null)
                throw new MissingComponentException("Serve un componente che implementi IFieldAnimationSource sullo stesso GameObject.");
            source.DataLoaded += HandleLoaded;
            source.FrameChanged += HandleFrameChanged;
        }

        private void Start()
        {
            if (source.IsLoaded) HandleLoaded();
        }

        private void OnDestroy()
        {
            if (source != null)
            {
                source.DataLoaded -= HandleLoaded;
                source.FrameChanged -= HandleFrameChanged;
            }
            fieldBuffer?.Dispose();
            if (runtimeMaterial != null) Destroy(runtimeMaterial);
        }

        private IFieldAnimationSource FindSource()
        {
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in behaviours)
                if (behaviour is IFieldAnimationSource candidate) return candidate;
            return null;
        }

        private void HandleLoaded()
        {
            if (source.FieldCount <= 0) throw new InvalidOperationException("Il file non contiene campi.");
            int preferred = string.IsNullOrWhiteSpace(preferredFieldName) ? -1 : source.FindField(preferredFieldName);
            if (preferred >= 0) selectedFieldIndex = preferred;
            selectedFieldIndex = Mathf.Clamp(selectedFieldIndex, 0, source.FieldCount - 1);

            targetRenderer = source.TargetRenderer;
            if (targetRenderer == null || targetRenderer.sharedMaterial == null)
                throw new MissingReferenceException("Assegna al Renderer un materiale SIMBA/FieldGradientURP.");

            fieldBuffer?.Dispose();
            fieldBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, source.ValueCount, sizeof(float));
            interpolatedValues = new float[source.ValueCount];

            if (runtimeMaterial != null) Destroy(runtimeMaterial);
            runtimeMaterial = new Material(targetRenderer.sharedMaterial) { name = targetRenderer.sharedMaterial.name + " (Runtime)" };
            targetRenderer.sharedMaterial = runtimeMaterial;
            runtimeMaterial.SetBuffer(FieldBufferId, fieldBuffer);
            UpdateField(source.CurrentFrame, source.NextFrame, source.FrameInterpolation);
            FieldChanged?.Invoke(selectedFieldIndex, SelectedFieldName);
        }

        public bool SetField(string fieldName)
        {
            if (source == null || !source.IsLoaded) { preferredFieldName = fieldName; return false; }
            int index = source.FindField(fieldName);
            if (index < 0) return false;
            SetField(index);
            return true;
        }

        public void SetField(int index)
        {
            if (source == null || !source.IsLoaded) { selectedFieldIndex = Mathf.Max(0, index); return; }
            int clamped = Mathf.Clamp(index, 0, source.FieldCount - 1);
            if (selectedFieldIndex == clamped && fieldBuffer != null) return;
            selectedFieldIndex = clamped;
            preferredFieldName = source.GetField(clamped).Name;
            UpdateField(source.CurrentFrame, source.NextFrame, source.FrameInterpolation);
            FieldChanged?.Invoke(selectedFieldIndex, SelectedFieldName);
        }

        public void SetColorMap(SIMBAColorMap preset)
        {
            colorMapPreset = preset;
            ApplyMaterialProperties();
            ColorMapChanged?.Invoke(colorMapPreset);
        }

        public void SetColorMap(Texture2D texture)
        {
            customColorMap = texture;
            colorMapPreset = SIMBAColorMap.Custom;
            ApplyMaterialProperties();
            ColorMapChanged?.Invoke(colorMapPreset);
        }

        public void UseGlobalRange()
        {
            rangeMode = RangeMode.Global;
            RefreshCurrentFrame();
            RangeChanged?.Invoke(rangeMode, manualMin, manualMax);
        }

        public void UsePerFrameRange()
        {
            rangeMode = RangeMode.PerFrame;
            RefreshCurrentFrame();
            RangeChanged?.Invoke(rangeMode, manualMin, manualMax);
        }

        public void SetManualRange(float minimum, float maximum)
        {
            if (maximum < minimum) (minimum, maximum) = (maximum, minimum);
            manualMin = minimum;
            manualMax = maximum;
            rangeMode = RangeMode.Manual;
            RefreshCurrentFrame();
            RangeChanged?.Invoke(rangeMode, manualMin, manualMax);
        }

        public void SetMetallic(float value)
        {
            metallic = Mathf.Clamp01(value);
            ApplyMaterialProperties();
        }

        public void SetSmoothness(float value)
        {
            smoothness = Mathf.Clamp01(value);
            ApplyMaterialProperties();
        }

        public void RefreshCurrentFrame()
        {
            if (source != null && source.IsLoaded && fieldBuffer != null)
                UpdateField(source.CurrentFrame, source.NextFrame, source.FrameInterpolation);
        }

        public void ConfigureInitialField(string fieldName)
        {
            preferredFieldName = fieldName ?? string.Empty;
            if (source != null && source.IsLoaded) SetField(preferredFieldName);
        }

        private void HandleFrameChanged(int frame, int nextFrame, float interpolation)
        {
            if (fieldBuffer != null) UpdateField(frame, nextFrame, interpolation);
        }

        private void UpdateField(int frame, int nextFrame, float interpolation)
        {
            AnimatedField field = source.GetField(selectedFieldIndex);
            float t = interpolateField ? interpolation : 0f;
            if (interpolateField && nextFrame != frame)
            {
                float[] a = field.Values[frame];
                float[] b = field.Values[nextFrame];
                for (int i = 0; i < interpolatedValues.Length; i++)
                    interpolatedValues[i] = Mathf.LerpUnclamped(a[i], b[i], t);
                fieldBuffer.SetData(interpolatedValues);
            }
            else fieldBuffer.SetData(field.Values[frame]);

            float min, max;
            if (rangeMode == RangeMode.Manual) { min = manualMin; max = manualMax; }
            else if (rangeMode == RangeMode.PerFrame)
            {
                min = Mathf.Lerp(field.FrameMin[frame], field.FrameMin[nextFrame], t);
                max = Mathf.Lerp(field.FrameMax[frame], field.FrameMax[nextFrame], t);
            }
            else { min = field.GlobalMin; max = field.GlobalMax; }
            if (max < min)
            {
                float temporary = min;
                min = max;
                max = temporary;
            }
            if (Mathf.Abs(max - min) < 1e-20f) max = min + 1e-20f;

            runtimeMaterial.SetBuffer(FieldBufferId, fieldBuffer);
            runtimeMaterial.SetFloat(FieldMinId, min);
            runtimeMaterial.SetFloat(FieldMaxId, max);
            ApplyMaterialProperties();
        }

        private void ApplyMaterialProperties()
        {
            if (runtimeMaterial == null) return;
            Texture2D colorMap = colorMapPreset == SIMBAColorMap.Custom
                ? customColorMap
                : SIMBAColorMaps.Load(colorMapPreset);
            if (colorMap != null)
            {
                colorMap.wrapMode = TextureWrapMode.Clamp;
                colorMap.filterMode = FilterMode.Bilinear;
                runtimeMaterial.SetTexture(ColorMapId, colorMap);
            }
            runtimeMaterial.SetFloat(MetallicId, metallic);
            runtimeMaterial.SetFloat(SmoothnessId, smoothness);
        }
    }
}
