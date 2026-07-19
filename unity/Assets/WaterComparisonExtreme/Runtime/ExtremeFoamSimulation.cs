using UnityEngine;

[DisallowMultipleComponent]
public sealed class ExtremeFoamSimulation : MonoBehaviour
{
    private const string DefaultSimulationShaderName = "Hidden/WaterComparisonExtreme/FoamSimulation";

    private static readonly int PreviousTextureId = Shader.PropertyToID("_PreviousTex");
    private static readonly int FoamHistoryTextureId = Shader.PropertyToID("_ExtremeFoamHistoryTex");
    private static readonly int FoamWorldToUvId = Shader.PropertyToID("_ExtremeFoamWorldToUV");
    private static readonly int FoamUvToWorldId = Shader.PropertyToID("_ExtremeFoamUVToWorld");
    private static readonly int FoamWorldParamsId = Shader.PropertyToID("_ExtremeFoamWorldParams");
    private static readonly int FoamDeltaTimeId = Shader.PropertyToID("_ExtremeFoamDeltaTime");
    private static readonly int FoamTimeId = Shader.PropertyToID("_ExtremeFoamTime");
    private static readonly int FoamTexelSizeId = Shader.PropertyToID("_ExtremeFoamTexelSize");
    private static readonly int FoamAdvectionVelocityId = Shader.PropertyToID("_ExtremeFoamAdvectionVelocity");
    private static readonly int FoamHalfLifeId = Shader.PropertyToID("_ExtremeFoamHalfLife");
    private static readonly int FoamInjectionId = Shader.PropertyToID("_ExtremeFoamInjection");
    private static readonly int FoamHistoryStrengthId = Shader.PropertyToID("_ExtremeFoamHistoryStrength");
    private static readonly int WaveStrengthId = Shader.PropertyToID("_WaveStrength");
    private static readonly int ChoppinessId = Shader.PropertyToID("_Choppiness");
    private static readonly int WaveTimeScaleId = Shader.PropertyToID("_WaveTimeScale");
    private static readonly int FoamThresholdId = Shader.PropertyToID("_FoamThreshold");
    private static readonly int FoamSoftnessId = Shader.PropertyToID("_FoamSoftness");

    [Header("World coverage")]
    [SerializeField] private Transform oceanTransform;
    [SerializeField, Min(10f)] private float worldSize = 160f;

    [Header("Temporal simulation")]
    [SerializeField, Range(1f, 60f)] private float updatesPerSecond = 30f;
    [SerializeField] private Shader simulationShader;
    [SerializeField] private Material oceanMaterial;
    [SerializeField] private bool updateInEditMode = false;
    [SerializeField] private Vector2 advectionVelocity = new Vector2(0.42f, 0.1f);
    [SerializeField, Range(0.25f, 20f)] private float foamHalfLife = 5.5f;
    [SerializeField, Range(0f, 4f)] private float foamInjection = 1.15f;
    [SerializeField, Range(0f, 2f)] private float historyStrength = 1f;

    private Material simulationMaterial;
    private RenderTexture[] historyTextures;
    private int currentTextureIndex;
    private int requestedResolution = 256;
    private float timeAccumulator;
    private float simulationTime;
    private bool suspended;
    private bool simulationEnabledForQuality = true;

    public RenderTexture CurrentTexture => historyTextures == null ? null : historyTextures[currentTextureIndex];

    private void OnEnable()
    {
        if (oceanTransform == null)
            oceanTransform = transform;
        simulationTime = Application.isPlaying ? Time.time : 0f;
        if ((!Application.isPlaying && !updateInEditMode) || !simulationEnabledForQuality)
            return;
        EnsureMaterial();
        EnsureTextures();
        PublishGlobals();
    }

    private void Update()
    {
        if (suspended || !simulationEnabledForQuality || (!Application.isPlaying && !updateInEditMode))
            return;

        EnsureMaterial();
        EnsureTextures();
        if (simulationMaterial == null || historyTextures == null)
            return;

        // Follow the same scaled clock as the wave phase. Pausing timeScale must freeze
        // advection, decay and injection together instead of evolving foam over a frozen sea.
        float deltaTime = Application.isPlaying ? Time.deltaTime : 1f / updatesPerSecond;
        timeAccumulator = Mathf.Min(timeAccumulator + deltaTime, 2f / updatesPerSecond);
        float fixedDeltaTime = 1f / Mathf.Max(updatesPerSecond, 1f);
        if (timeAccumulator < fixedDeltaTime)
            return;

        // At most two steps are accumulated. A hidden or stalled window therefore never
        // tries to replay hundreds of GPU blits when it becomes active again.
        int stepCount = Mathf.Min(Mathf.FloorToInt(timeAccumulator / fixedDeltaTime), 2);
        for (int step = 0; step < stepCount; step++)
            StepSimulation(fixedDeltaTime);
        timeAccumulator -= fixedDeltaTime * stepCount;
        PublishGlobals();
    }

    private void OnDisable()
    {
        ReleaseResources();
    }

    private void OnDestroy()
    {
        ReleaseResources();
    }

    public void ApplyQuality(int level)
    {
        level = Mathf.Clamp(level, 1, 3);
        bool wasEnabled = simulationEnabledForQuality;
        simulationEnabledForQuality = level >= 2;
        requestedResolution = level == 3 ? 512 : 256;
        timeAccumulator = 0f;
        if (simulationEnabledForQuality && (Application.isPlaying || updateInEditMode))
        {
            EnsureTextures();
            if (!wasEnabled)
            {
                simulationTime = Application.isPlaying ? Time.time : simulationTime;
                ClearHistory();
            }
            PublishGlobals();
        }
    }

    public void Configure(
        Transform waterTransform,
        float coverageSize,
        Shader updateShader = null,
        Material sourceOceanMaterial = null)
    {
        oceanTransform = waterTransform != null ? waterTransform : transform;
        worldSize = Mathf.Max(coverageSize, 10f);
        if (sourceOceanMaterial != null)
            oceanMaterial = sourceOceanMaterial;
        if (updateShader != null && updateShader != simulationShader)
        {
            simulationShader = updateShader;
            ReleaseResources();
        }

        if (simulationEnabledForQuality && (Application.isPlaying || updateInEditMode))
        {
            EnsureMaterial();
            EnsureTextures();
            PublishGlobals();
        }
    }

    public void SetSuspended(bool value)
    {
        suspended = value;
        timeAccumulator = 0f;
        if (!value && Application.isPlaying)
            simulationTime = Time.time;
    }

    public void ClearHistory()
    {
        if (historyTextures == null)
            return;

        RenderTexture previousActive = RenderTexture.active;
        for (int index = 0; index < historyTextures.Length; index++)
        {
            RenderTexture.active = historyTextures[index];
            GL.Clear(false, true, Color.clear);
        }
        RenderTexture.active = previousActive;
        currentTextureIndex = 0;
        PublishGlobals();
    }

    private void EnsureMaterial()
    {
        if (simulationMaterial != null)
            return;

        if (simulationShader == null)
            simulationShader = Shader.Find(DefaultSimulationShaderName);
        if (simulationShader == null || !simulationShader.isSupported)
            return;

        simulationMaterial = new Material(simulationShader)
        {
            name = "Extreme Foam Simulation (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };
    }

    private void EnsureTextures()
    {
        requestedResolution = requestedResolution >= 512 ? 512 : 256;
        if (historyTextures != null &&
            historyTextures.Length == 2 &&
            historyTextures[0] != null &&
            historyTextures[1] != null &&
            historyTextures[0].IsCreated() &&
            historyTextures[1].IsCreated() &&
            historyTextures[0].width == requestedResolution)
            return;

        ReleaseTextures();
        RenderTextureFormat format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf)
            ? RenderTextureFormat.RHalf
            : RenderTextureFormat.ARGB32;
        historyTextures = new RenderTexture[2];
        for (int index = 0; index < historyTextures.Length; index++)
        {
            historyTextures[index] = new RenderTexture(requestedResolution, requestedResolution, 0, format, RenderTextureReadWrite.Linear)
            {
                name = $"Extreme Foam History {index}",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = 1
            };
            if (!historyTextures[index].Create())
            {
                Debug.LogError($"Could not allocate Extreme Foam History at {requestedResolution} x {requestedResolution}.");
                ReleaseTextures();
                return;
            }
        }

        currentTextureIndex = 0;
        ClearHistory();
    }

    private void StepSimulation(float fixedDeltaTime)
    {
        int nextTextureIndex = 1 - currentTextureIndex;
        RenderTexture previous = historyTextures[currentTextureIndex];
        RenderTexture next = historyTextures[nextTextureIndex];
        Vector4 worldToUv = BuildWorldToUv();

        simulationTime += fixedDeltaTime;
        simulationMaterial.SetTexture(PreviousTextureId, previous);
        simulationMaterial.SetFloat(FoamDeltaTimeId, fixedDeltaTime);
        simulationMaterial.SetFloat(FoamTimeId, simulationTime);
        simulationMaterial.SetVector(FoamWorldToUvId, worldToUv);
        simulationMaterial.SetVector(FoamUvToWorldId, BuildUvToWorld());
        simulationMaterial.SetVector(FoamWorldParamsId, BuildWorldParams());
        Vector4 advectionVector = new Vector4(advectionVelocity.x, advectionVelocity.y, 0f, 0f);
        simulationMaterial.SetVector(FoamAdvectionVelocityId, advectionVector);
        simulationMaterial.SetFloat(FoamHalfLifeId, foamHalfLife);
        simulationMaterial.SetFloat(FoamInjectionId, foamInjection);
        CopyWaveParameter(WaveStrengthId);
        CopyWaveParameter(ChoppinessId);
        CopyWaveParameter(WaveTimeScaleId);
        CopyWaveParameter(FoamThresholdId);
        CopyWaveParameter(FoamSoftnessId);
        simulationMaterial.SetVector(
            FoamTexelSizeId,
            new Vector4(1f / requestedResolution, 1f / requestedResolution, requestedResolution, requestedResolution));
        Graphics.Blit(previous, next, simulationMaterial, 0);
        currentTextureIndex = nextTextureIndex;
    }

    private void CopyWaveParameter(int propertyId)
    {
        if (oceanMaterial != null && oceanMaterial.HasProperty(propertyId) && simulationMaterial.HasProperty(propertyId))
            simulationMaterial.SetFloat(propertyId, oceanMaterial.GetFloat(propertyId));
    }

    private void PublishGlobals()
    {
        RenderTexture current = CurrentTexture;
        if (current != null)
            Shader.SetGlobalTexture(FoamHistoryTextureId, current);
        Shader.SetGlobalVector(FoamWorldToUvId, BuildWorldToUv());
        Shader.SetGlobalVector(FoamUvToWorldId, BuildUvToWorld());
        Shader.SetGlobalVector(FoamWorldParamsId, BuildWorldParams());
        Shader.SetGlobalFloat(FoamDeltaTimeId, 1f / Mathf.Max(updatesPerSecond, 1f));
        Shader.SetGlobalFloat(FoamTimeId, simulationTime);
        Shader.SetGlobalVector(
            FoamAdvectionVelocityId,
            new Vector4(advectionVelocity.x, advectionVelocity.y, 0f, 0f));
        Shader.SetGlobalFloat(FoamHalfLifeId, foamHalfLife);
        Shader.SetGlobalFloat(FoamInjectionId, foamInjection);
        Shader.SetGlobalFloat(FoamHistoryStrengthId, historyStrength);
        Shader.SetGlobalVector(
            FoamTexelSizeId,
            new Vector4(1f / requestedResolution, 1f / requestedResolution, requestedResolution, requestedResolution));
    }

    private Vector4 BuildWorldToUv()
    {
        Vector3 center = oceanTransform != null ? oceanTransform.position : transform.position;
        float inverseSize = 1f / Mathf.Max(worldSize, 0.001f);
        return new Vector4(
            inverseSize,
            inverseSize,
            0.5f - center.x * inverseSize,
            0.5f - center.z * inverseSize);
    }

    private Vector4 BuildUvToWorld()
    {
        Vector3 center = oceanTransform != null ? oceanTransform.position : transform.position;
        return new Vector4(worldSize, worldSize, center.x - worldSize * 0.5f, center.z - worldSize * 0.5f);
    }

    private Vector4 BuildWorldParams()
    {
        Vector3 center = oceanTransform != null ? oceanTransform.position : transform.position;
        return new Vector4(center.x, center.z, worldSize, 1f / Mathf.Max(worldSize, 0.001f));
    }

    private void ReleaseResources()
    {
        ReleaseTextures();
        Shader.SetGlobalTexture(FoamHistoryTextureId, Texture2D.blackTexture);
        if (simulationMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(simulationMaterial);
            else
                DestroyImmediate(simulationMaterial);
            simulationMaterial = null;
        }
    }

    private void ReleaseTextures()
    {
        if (historyTextures == null)
            return;

        for (int index = 0; index < historyTextures.Length; index++)
        {
            RenderTexture texture = historyTextures[index];
            if (texture == null)
                continue;
            texture.Release();
            if (Application.isPlaying)
                Destroy(texture);
            else
                DestroyImmediate(texture);
        }

        historyTextures = null;
        currentTextureIndex = 0;
    }
}
