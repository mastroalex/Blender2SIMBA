using System;
using UnityEngine;

namespace M2M.SIMBA
{
    [DisallowMultipleComponent]
    public sealed class FieldColorController : MonoBehaviour
    {
        public enum RangeMode
        {
            Global,
            PerFrame,
            Manual
        }

        [Header("Field selection")]
        [SerializeField, Min(0)]
        private int selectedFieldIndex;

        [SerializeField]
        private string preferredFieldName = "Stress";

        [Header("Colormap")]
        public SIMBAColorMap colorMapPreset = SIMBAColorMap.Turbo;

        [Tooltip("Used only when Color Map Preset is Custom.")]
        public Texture2D customColorMap;

        public RangeMode rangeMode = RangeMode.Global;
        public float manualMin;
        public float manualMax = 1f;
        public bool interpolateField = true;

        [Header("Appearance")]
        [Range(0f, 1f)]
        public float metallic;

        [Range(0f, 1f)]
        public float smoothness = 0.35f;

        public int SelectedFieldIndex => selectedFieldIndex;
        public SIMBAColorMap ColorMapPreset => colorMapPreset;
        public RangeMode CurrentRangeMode => rangeMode;
        public float ManualMinimum => manualMin;
        public float ManualMaximum => manualMax;
        public int ConfiguredFieldIndex => selectedFieldIndex;
        public string PreferredFieldName => preferredFieldName;

        public string SelectedFieldName =>
            source != null &&
            source.IsLoaded &&
            source.FieldCount > 0
                ? source.GetField(selectedFieldIndex).Name
                : string.Empty;

        public string[] AvailableFieldNames
        {
            get
            {
                if (source == null || !source.IsLoaded)
                    return Array.Empty<string>();

                string[] names = new string[source.FieldCount];

                for (int i = 0; i < names.Length; i++)
                    names[i] = source.GetField(i).Name;

                return names;
            }
        }

        public event Action<int, string> FieldChanged;
        public event Action<SIMBAColorMap> ColorMapChanged;
        public event Action<RangeMode, float, float> RangeChanged;

        private static readonly int FieldBufferId =
            Shader.PropertyToID("_FieldBuffer");

        private static readonly int ColorMapId =
            Shader.PropertyToID("_ColorMap");

        private static readonly int FieldMinId =
            Shader.PropertyToID("_FieldMin");

        private static readonly int FieldMaxId =
            Shader.PropertyToID("_FieldMax");

        private static readonly int MetallicId =
            Shader.PropertyToID("_Metallic");

        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");

        private const string VertexColorKeyword =
            "SIMBA_VERTEX_COLORS";

        private IFieldAnimationSource source;
        private Renderer targetRenderer;
        private Material runtimeMaterial;

        private float[] interpolatedValues;

#if UNITY_WEBGL && !UNITY_EDITOR
        private Mesh targetMesh;
        private Color32[] encodedVertexColors;
#else
        private GraphicsBuffer fieldBuffer;
#endif

        private bool backendReady;

        private void Awake()
        {
            source = FindSource();

            if (source == null)
            {
                throw new MissingComponentException(
                    "Serve un componente che implementi " +
                    "IFieldAnimationSource sullo stesso GameObject.");
            }

            source.DataLoaded += HandleLoaded;
            source.FrameChanged += HandleFrameChanged;
        }

        private void Start()
        {
            if (source.IsLoaded)
                HandleLoaded();
        }

        private void OnDestroy()
        {
            if (source != null)
            {
                source.DataLoaded -= HandleLoaded;
                source.FrameChanged -= HandleFrameChanged;
            }

#if !UNITY_WEBGL || UNITY_EDITOR
            fieldBuffer?.Dispose();
            fieldBuffer = null;
#endif

            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        private IFieldAnimationSource FindSource()
        {
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();

            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IFieldAnimationSource candidate)
                    return candidate;
            }

            return null;
        }

        private void HandleLoaded()
        {
            if (source.FieldCount <= 0)
            {
                throw new InvalidOperationException(
                    "Il file non contiene campi.");
            }

            int preferred =
                string.IsNullOrWhiteSpace(preferredFieldName)
                    ? -1
                    : source.FindField(preferredFieldName);

            if (preferred >= 0)
                selectedFieldIndex = preferred;

            selectedFieldIndex = Mathf.Clamp(
                selectedFieldIndex,
                0,
                source.FieldCount - 1);

            targetRenderer = source.TargetRenderer;

            if (targetRenderer == null ||
                targetRenderer.sharedMaterial == null)
            {
                throw new MissingReferenceException(
                    "Assegna al Renderer un materiale " +
                    "SIMBA/FieldGradientURP.");
            }

            CreateRuntimeMaterial();
            interpolatedValues = new float[source.ValueCount];

#if UNITY_WEBGL && !UNITY_EDITOR
            InitializeVertexColorBackend();
#else
            InitializeGraphicsBufferBackend();
#endif

            backendReady = true;

            UpdateField(
                source.CurrentFrame,
                source.NextFrame,
                source.FrameInterpolation);

            FieldChanged?.Invoke(
                selectedFieldIndex,
                SelectedFieldName);
        }

        private void CreateRuntimeMaterial()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);

            runtimeMaterial = new Material(
                targetRenderer.sharedMaterial)
            {
                name =
                    targetRenderer.sharedMaterial.name +
                    " (Runtime)"
            };

            targetRenderer.sharedMaterial = runtimeMaterial;

#if UNITY_WEBGL && !UNITY_EDITOR
            runtimeMaterial.EnableKeyword(VertexColorKeyword);
#else
            runtimeMaterial.DisableKeyword(VertexColorKeyword);
#endif
        }


#if UNITY_WEBGL && !UNITY_EDITOR
        private void InitializeVertexColorBackend()
        {
            MeshFilter meshFilter =
                targetRenderer.GetComponent<MeshFilter>();

            if (meshFilter != null)
            {
                // mesh crea una copia runtime modificabile.
                targetMesh = meshFilter.mesh;
            }
            else if (targetRenderer is SkinnedMeshRenderer skinned)
            {
                targetMesh = skinned.sharedMesh;
            }

            if (targetMesh == null)
            {
                throw new MissingComponentException(
                    "Il backend WebGL richiede un MeshFilter " +
                    "o uno SkinnedMeshRenderer.");
            }

            if (targetMesh.vertexCount != source.ValueCount)
            {
                throw new InvalidOperationException(
                    "Il numero di valori del campo non coincide " +
                    "con il numero di vertici della mesh. " +
                    $"Values={source.ValueCount}, " +
                    $"Vertices={targetMesh.vertexCount}.");
            }

            encodedVertexColors =
                new Color32[source.ValueCount];
        }
#else
        private void InitializeGraphicsBufferBackend()
        {
            fieldBuffer?.Dispose();

            fieldBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                source.ValueCount,
                sizeof(float));

            runtimeMaterial.SetBuffer(
                FieldBufferId,
                fieldBuffer);
        }
#endif

        public bool SetField(string fieldName)
        {
            if (source == null || !source.IsLoaded)
            {
                preferredFieldName = fieldName;
                return false;
            }

            int index = source.FindField(fieldName);

            if (index < 0)
                return false;

            SetField(index);
            return true;
        }

        public void SetField(int index)
        {
            if (source == null || !source.IsLoaded)
            {
                selectedFieldIndex = Mathf.Max(0, index);
                return;
            }

            int clamped = Mathf.Clamp(
                index,
                0,
                source.FieldCount - 1);

            if (selectedFieldIndex == clamped &&
                backendReady)
            {
                return;
            }

            selectedFieldIndex = clamped;
            preferredFieldName =
                source.GetField(clamped).Name;

            UpdateField(
                source.CurrentFrame,
                source.NextFrame,
                source.FrameInterpolation);

            FieldChanged?.Invoke(
                selectedFieldIndex,
                SelectedFieldName);
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

            RangeChanged?.Invoke(
                rangeMode,
                manualMin,
                manualMax);
        }

        public void UsePerFrameRange()
        {
            rangeMode = RangeMode.PerFrame;
            RefreshCurrentFrame();

            RangeChanged?.Invoke(
                rangeMode,
                manualMin,
                manualMax);
        }

        public void SetManualRange(
            float minimum,
            float maximum)
        {
            if (maximum < minimum)
            {
                float temporary = minimum;
                minimum = maximum;
                maximum = temporary;
            }

            manualMin = minimum;
            manualMax = maximum;
            rangeMode = RangeMode.Manual;

            RefreshCurrentFrame();

            RangeChanged?.Invoke(
                rangeMode,
                manualMin,
                manualMax);
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
            if (source == null ||
                !source.IsLoaded ||
                !backendReady)
            {
                return;
            }

            UpdateField(
                source.CurrentFrame,
                source.NextFrame,
                source.FrameInterpolation);
        }

        public void ConfigureInitialField(string fieldName)
        {
            preferredFieldName = fieldName ?? string.Empty;

            if (source != null && source.IsLoaded)
                SetField(preferredFieldName);
        }

        private void HandleFrameChanged(
            int frame,
            int nextFrame,
            float interpolation)
        {
            if (backendReady)
                UpdateField(frame, nextFrame, interpolation);
        }

        private void UpdateField(
            int frame,
            int nextFrame,
            float interpolation)
        {
            if (runtimeMaterial == null ||
                source == null ||
                !source.IsLoaded)
            {
                return;
            }

            AnimatedField field =
                source.GetField(selectedFieldIndex);

            float t =
                interpolateField ? interpolation : 0f;

            float[] values;

            if (interpolateField &&
                nextFrame != frame)
            {
                float[] a = field.Values[frame];
                float[] b = field.Values[nextFrame];

                for (int i = 0;
                     i < interpolatedValues.Length;
                     i++)
                {
                    interpolatedValues[i] =
                        Mathf.LerpUnclamped(
                            a[i],
                            b[i],
                            t);
                }

                values = interpolatedValues;
            }
            else
            {
                values = field.Values[frame];
            }

            ResolveRange(
                field,
                frame,
                nextFrame,
                t,
                out float minimum,
                out float maximum);

#if UNITY_WEBGL && !UNITY_EDITOR
            UploadVertexColors(
                values,
                minimum,
                maximum);
#else
            fieldBuffer.SetData(values);

            runtimeMaterial.SetBuffer(
                FieldBufferId,
                fieldBuffer);

            runtimeMaterial.SetFloat(
                FieldMinId,
                minimum);

            runtimeMaterial.SetFloat(
                FieldMaxId,
                maximum);
#endif

            ApplyMaterialProperties();
        }

        private void ResolveRange(
            AnimatedField field,
            int frame,
            int nextFrame,
            float interpolation,
            out float minimum,
            out float maximum)
        {
            if (rangeMode == RangeMode.Manual)
            {
                minimum = manualMin;
                maximum = manualMax;
            }
            else if (rangeMode == RangeMode.PerFrame)
            {
                minimum = Mathf.Lerp(
                    field.FrameMin[frame],
                    field.FrameMin[nextFrame],
                    interpolation);

                maximum = Mathf.Lerp(
                    field.FrameMax[frame],
                    field.FrameMax[nextFrame],
                    interpolation);
            }
            else
            {
                minimum = field.GlobalMin;
                maximum = field.GlobalMax;
            }

            if (maximum < minimum)
            {
                float temporary = minimum;
                minimum = maximum;
                maximum = temporary;
            }

            if (Mathf.Abs(maximum - minimum) < 1e-20f)
                maximum = minimum + 1e-20f;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private void UploadVertexColors(
            float[] values,
            float minimum,
            float maximum)
        {
            float inverseSpan =
                1f / Mathf.Max(
                    maximum - minimum,
                    1e-20f);

            for (int i = 0;
                 i < encodedVertexColors.Length;
                 i++)
            {
                float normalized =
                    Mathf.Clamp01(
                        (values[i] - minimum) *
                        inverseSpan);

                byte encoded =
                    (byte)Mathf.RoundToInt(
                        normalized * 255f);

                encodedVertexColors[i] =
                    new Color32(
                        encoded,
                        0,
                        0,
                        255);
            }

            targetMesh.colors32 =
                encodedVertexColors;
        }
#endif

        private void ApplyMaterialProperties()
        {
            if (runtimeMaterial == null)
                return;

            Texture2D colorMap =
                colorMapPreset == SIMBAColorMap.Custom
                    ? customColorMap
                    : SIMBAColorMaps.Load(colorMapPreset);

            if (colorMap != null)
            {
                colorMap.wrapMode =
                    TextureWrapMode.Clamp;

                colorMap.filterMode =
                    FilterMode.Bilinear;

                runtimeMaterial.SetTexture(
                    ColorMapId,
                    colorMap);
            }

            runtimeMaterial.SetFloat(
                MetallicId,
                metallic);

            runtimeMaterial.SetFloat(
                SmoothnessId,
                smoothness);
        }
    }
}