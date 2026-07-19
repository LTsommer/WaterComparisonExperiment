using UnityEngine;

[DisallowMultipleComponent]
public sealed class ExtremeFloatingBuoy : MonoBehaviour
{
    private static readonly int WaveStrengthId = Shader.PropertyToID("_WaveStrength");
    private static readonly int ChoppinessId = Shader.PropertyToID("_Choppiness");
    private static readonly int WaveTimeScaleId = Shader.PropertyToID("_WaveTimeScale");

    private struct Wave
    {
        public readonly Vector2 Direction;
        public readonly float Amplitude;
        public readonly float WaveNumber;
        public readonly float AngularFrequency;
        public readonly float Chop;
        public readonly float Phase;

        public Wave(float directionX, float directionZ, float amplitude, float waveNumber, float angularFrequency, float chop, float phase)
        {
            Direction = new Vector2(directionX, directionZ);
            Amplitude = amplitude;
            WaveNumber = waveNumber;
            AngularFrequency = angularFrequency;
            Chop = chop;
            Phase = phase;
        }
    }

    // Keep this table byte-for-byte aligned with ExtremeSpectralOcean.shader. The shader
    // deliberately uses explicit constants rather than an uploaded array on Metal.
    private static readonly Wave[] Spectrum =
    {
        new Wave(0.984808f,  0.173648f, 0.460f,  0.142f,  1.1804f, 0.85f, 0.31f),
        new Wave(0.913545f,  0.406737f, 0.280f,  0.219f,  1.4656f, 0.82f, 2.17f),
        new Wave(0.998630f, -0.052336f, 0.180f,  0.337f,  1.8183f, 0.78f, 4.83f),
        new Wave(0.819152f,  0.573576f, 0.115f,  0.514f,  2.2454f, 0.72f, 1.21f),
        new Wave(0.945519f, -0.325568f, 0.075f,  0.793f,  2.7890f, 0.67f, 5.44f),
        new Wave(0.669131f,  0.743145f, 0.049f,  1.217f,  3.4554f, 0.62f, 3.02f),
        new Wave(0.990268f,  0.139173f, 0.032f,  1.873f,  4.2867f, 0.56f, 0.77f),
        new Wave(0.559193f, -0.829038f, 0.021f,  2.879f,  5.3144f, 0.50f, 4.11f),
        new Wave(0.874620f,  0.484810f, 0.014f,  4.423f,  6.5870f, 0.44f, 2.69f),
        new Wave(0.743145f, -0.669131f, 0.009f,  6.791f,  8.1620f, 0.38f, 5.91f),
        new Wave(0.406737f,  0.913545f, 0.006f, 10.430f, 10.1150f, 0.32f, 1.72f),
        new Wave(0.965926f, -0.258819f, 0.004f, 16.020f, 12.5360f, 0.28f, 3.63f)
    };

    [SerializeField] private Transform oceanTransform;
    [SerializeField] private Material oceanMaterial;
    [SerializeField] private float waterlineOffset = 0.34f;
    [SerializeField, Min(0.1f)] private float rotationResponse = 8f;
    [SerializeField] private bool readWaveParametersFromMaterial = true;
    [SerializeField, Range(0f, 2f)] private float waveStrength = 1f;
    [SerializeField, Range(0f, 2f)] private float choppiness = 1f;
    [SerializeField, Range(0f, 3f)] private float waveTimeScale = 1f;

    private Vector2 restPositionLocal;
    private Vector3 restForwardLocal;
    private bool initialized;

    private void Awake()
    {
        InitializeRestPose();
    }

    private void OnEnable()
    {
        if (!initialized)
            InitializeRestPose();
    }

    private void LateUpdate()
    {
        if (oceanTransform == null)
            return;

        float currentWaveStrength = waveStrength;
        float currentChoppiness = choppiness;
        float currentTimeScale = waveTimeScale;
        if (readWaveParametersFromMaterial && oceanMaterial != null)
        {
            if (oceanMaterial.HasProperty(WaveStrengthId))
                currentWaveStrength = oceanMaterial.GetFloat(WaveStrengthId);
            if (oceanMaterial.HasProperty(ChoppinessId))
                currentChoppiness = oceanMaterial.GetFloat(ChoppinessId);
            if (oceanMaterial.HasProperty(WaveTimeScaleId))
                currentTimeScale = oceanMaterial.GetFloat(WaveTimeScaleId);
        }

        Vector3 displaced = new Vector3(restPositionLocal.x, 0f, restPositionLocal.y);
        Vector3 derivativeX = Vector3.right;
        Vector3 derivativeZ = Vector3.forward;
        float spectrumTime = Time.time * currentTimeScale;
        for (int index = 0; index < Spectrum.Length; index++)
            AccumulateWave(Spectrum[index], restPositionLocal, spectrumTime, currentWaveStrength, currentChoppiness, ref displaced, ref derivativeX, ref derivativeZ);

        Vector3 normalLocal = Vector3.Cross(derivativeZ, derivativeX).normalized;
        Vector3 normalWorld = oceanTransform.TransformDirection(normalLocal).normalized;
        Vector3 forwardWorld = oceanTransform.TransformDirection(restForwardLocal);
        forwardWorld = Vector3.ProjectOnPlane(forwardWorld, normalWorld).normalized;
        if (forwardWorld.sqrMagnitude < 0.001f)
            forwardWorld = Vector3.ProjectOnPlane(oceanTransform.forward, normalWorld).normalized;

        transform.position = oceanTransform.TransformPoint(displaced) + normalWorld * waterlineOffset;
        Quaternion targetRotation = Quaternion.LookRotation(forwardWorld, normalWorld);
        float interpolation = 1f - Mathf.Exp(-rotationResponse * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, interpolation);
    }

    public void Configure(Transform waterTransform, Material waterMaterial, float offset = 0.34f)
    {
        oceanTransform = waterTransform;
        oceanMaterial = waterMaterial;
        waterlineOffset = offset;
        initialized = false;
        InitializeRestPose();
    }

    private void InitializeRestPose()
    {
        if (oceanTransform == null)
            return;

        Vector3 localPosition = oceanTransform.InverseTransformPoint(transform.position);
        restPositionLocal = new Vector2(localPosition.x, localPosition.z);
        restForwardLocal = oceanTransform.InverseTransformDirection(transform.forward).normalized;
        initialized = true;
    }

    private static void AccumulateWave(
        Wave wave,
        Vector2 surfacePosition,
        float spectrumTime,
        float strength,
        float globalChoppiness,
        ref Vector3 displaced,
        ref Vector3 derivativeX,
        ref Vector3 derivativeZ)
    {
        float phase = wave.WaveNumber * Vector2.Dot(wave.Direction, surfacePosition)
            + wave.AngularFrequency * spectrumTime + wave.Phase;
        float sine = Mathf.Sin(phase);
        float cosine = Mathf.Cos(phase);
        float amplitude = wave.Amplitude * strength;
        float horizontalAmplitude = amplitude * wave.Chop * globalChoppiness;
        displaced.x += wave.Direction.x * horizontalAmplitude * cosine;
        displaced.z += wave.Direction.y * horizontalAmplitude * cosine;
        displaced.y += amplitude * sine;

        float horizontalSlope = horizontalAmplitude * wave.WaveNumber;
        float verticalSlope = amplitude * wave.WaveNumber;
        derivativeX += new Vector3(
            -horizontalSlope * wave.Direction.x * wave.Direction.x * sine,
            verticalSlope * wave.Direction.x * cosine,
            -horizontalSlope * wave.Direction.x * wave.Direction.y * sine);
        derivativeZ += new Vector3(
            -horizontalSlope * wave.Direction.x * wave.Direction.y * sine,
            verticalSlope * wave.Direction.y * cosine,
            -horizontalSlope * wave.Direction.y * wave.Direction.y * sine);
    }
}
