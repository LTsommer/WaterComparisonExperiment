using UnityEngine;

[DisallowMultipleComponent]
public sealed class ExtremeQualityController : MonoBehaviour
{
    private static readonly int QualityLevelId = Shader.PropertyToID("_ExtremeQualityLevel");

    [Header("Controlled systems")]
    [SerializeField] private ExtremeOceanMesh oceanMesh;
    [SerializeField] private ExtremeOceanRenderer oceanRenderer;
    [SerializeField] private ExtremeFoamSimulation foamSimulation;
    [SerializeField] private Renderer visibilityRenderer;

    [Header("Startup")]
    [SerializeField, Range(1, 3)] private int qualityLevel = 2;
    [SerializeField] private bool allowNumberKeyShortcuts = true;

    [Header("Thermal safeguards")]
    [SerializeField] private bool throttleWhenUnfocused = true;
    [SerializeField] private bool throttleWhenOceanIsHidden = true;
    [SerializeField, Range(5, 30)] private int backgroundFrameRate = 15;
    [SerializeField, Range(0.25f, 3f)] private float visibilityGracePeriod = 1f;

    private int previousTargetFrameRate;
    private int previousVSyncCount;
    private float enabledAtTime;
    private bool hasFocus = true;
    private bool applicationPaused;
    private bool throttled;
    private bool capturedGlobalSettings;

    public int QualityLevel => qualityLevel;
    public bool IsThrottled => throttled;

    private void Awake()
    {
        ResolveReferences();
        CaptureGlobalSettings();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CaptureGlobalSettings();
        enabledAtTime = Time.realtimeSinceStartup;
        hasFocus = Application.isFocused;
        ApplyQuality(qualityLevel);
        UpdateThrottleState(true);
    }

    private void Update()
    {
        if (allowNumberKeyShortcuts && hasFocus && !applicationPaused)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
                ApplyQuality(1);
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
                ApplyQuality(2);
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
                ApplyQuality(3);
        }

        UpdateThrottleState(false);
    }

    private void OnApplicationFocus(bool focus)
    {
        hasFocus = focus;
        UpdateThrottleState(true);
    }

    private void OnApplicationPause(bool pause)
    {
        applicationPaused = pause;
        UpdateThrottleState(true);
    }

    private void OnDisable()
    {
        // A disabled controller has nobody left to supervise GPU work, so fail closed.
        SetSubsystemsSuspended(true);
        RestoreGlobalSettings();
    }

    public void ApplyQuality(int level)
    {
        qualityLevel = Mathf.Clamp(level, 1, 3);
        ResolveReferences();

        if (oceanMesh != null)
            oceanMesh.ApplyQuality(qualityLevel);
        if (oceanRenderer != null)
            oceanRenderer.ApplyQuality(qualityLevel);
        if (foamSimulation != null)
            foamSimulation.ApplyQuality(qualityLevel);

        // The public/UI levels are 1/2/3; shaders use compact 0/1/2 branches.
        Shader.SetGlobalFloat(QualityLevelId, qualityLevel - 1);
        if (!throttled)
            ApplyForegroundFramePacing();
    }

    public void Configure(
        ExtremeOceanMesh mesh,
        ExtremeOceanRenderer renderer,
        ExtremeFoamSimulation foam,
        Renderer oceanVisibilityRenderer,
        int defaultQuality = 2)
    {
        oceanMesh = mesh;
        oceanRenderer = renderer;
        foamSimulation = foam;
        visibilityRenderer = oceanVisibilityRenderer;
        ApplyQuality(defaultQuality);
    }

    private void ResolveReferences()
    {
        if (oceanMesh == null)
            oceanMesh = GetComponentInChildren<ExtremeOceanMesh>(true);
        if (oceanRenderer == null)
            oceanRenderer = GetComponentInChildren<ExtremeOceanRenderer>(true);
        if (foamSimulation == null)
            foamSimulation = GetComponentInChildren<ExtremeFoamSimulation>(true);
        if (visibilityRenderer == null && oceanRenderer != null)
            visibilityRenderer = oceanRenderer.GetComponent<Renderer>();
    }

    private void UpdateThrottleState(bool force)
    {
        bool focusThrottle = throttleWhenUnfocused && (!hasFocus || applicationPaused);
        bool visibilityThrottle = false;
        if (throttleWhenOceanIsHidden && Time.realtimeSinceStartup - enabledAtTime >= visibilityGracePeriod)
            visibilityThrottle = visibilityRenderer != null && !visibilityRenderer.isVisible;

        bool shouldThrottle = focusThrottle || visibilityThrottle;
        if (!force && shouldThrottle == throttled)
            return;

        throttled = shouldThrottle;
        SetSubsystemsSuspended(throttled);
        if (throttled)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = backgroundFrameRate;
        }
        else
        {
            ApplyForegroundFramePacing();
        }
    }

    private void SetSubsystemsSuspended(bool value)
    {
        if (oceanRenderer != null)
            oceanRenderer.SetSuspended(value);
        if (foamSimulation != null)
            foamSimulation.SetSuspended(value);
    }

    private void ApplyForegroundFramePacing()
    {
        int targetRate = qualityLevel == 1 ? 45 : 60;
        if (qualityLevel == 1)
        {
            // A 45 Hz target has no clean divisor on a 60/120 Hz display.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetRate;
            return;
        }

        // vSyncCount=1 would run at 120 FPS on a ProMotion Mac and ignore
        // targetFrameRate. Pick a refresh divisor that never exceeds 60 rendered FPS.
        double refreshRate = Screen.currentResolution.refreshRateRatio.value;
        int divisor = refreshRate > 1.0
            ? Mathf.Max(1, Mathf.CeilToInt((float)(refreshRate / targetRate)))
            : 1;
        QualitySettings.vSyncCount = divisor;
        Application.targetFrameRate = targetRate;
    }

    private void CaptureGlobalSettings()
    {
        if (capturedGlobalSettings)
            return;

        previousTargetFrameRate = Application.targetFrameRate;
        previousVSyncCount = QualitySettings.vSyncCount;
        capturedGlobalSettings = true;
    }

    private void RestoreGlobalSettings()
    {
        if (!capturedGlobalSettings)
            return;

        Application.targetFrameRate = previousTargetFrameRate;
        QualitySettings.vSyncCount = previousVSyncCount;
        capturedGlobalSettings = false;
    }
}
