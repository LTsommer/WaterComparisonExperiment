using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(Renderer))]
public sealed class ExtremeOceanRenderer : MonoBehaviour
{
    private static readonly int PlanarReflectionTextureId = Shader.PropertyToID("_ExtremePlanarReflectionTex");
    private static readonly int ReflectionViewProjectionId = Shader.PropertyToID("_ExtremeReflectionVP");
    private static readonly int ReflectionUvScaleOffsetId = Shader.PropertyToID("_ExtremeReflectionUVScaleOffset");
    private static readonly int ReflectionResolutionId = Shader.PropertyToID("_ExtremeReflectionResolution");
    private static readonly int OpaqueSceneTextureId = Shader.PropertyToID("_ExtremeOpaqueSceneTex");
    private static readonly int OpaqueSceneResolutionId = Shader.PropertyToID("_ExtremeOpaqueSceneResolution");

    [Header("Planar reflection")]
    [SerializeField] private LayerMask reflectionMask = ~0;
    [SerializeField, Range(0.01f, 0.25f)] private float clipPlaneOffset = 0.07f;
    [SerializeField, Range(128, 2048)] private int maximumReflectionDimension = 1280;
    [SerializeField, Range(512, 2560)] private int maximumOpaqueDimension = 1920;
    [SerializeField] private bool reflectSkybox = true;

    [Header("Opaque scene capture")]
    [SerializeField] private bool captureOpaqueScene = true;
    [SerializeField] private bool requestCameraDepth = true;

    private Renderer oceanRenderer;
    private Camera sourceCamera;
    private Camera reflectionCamera;
    private GameObject reflectionCameraObject;
    private RenderTexture reflectionTexture;
    private RenderTexture opaqueSceneTexture;
    private CommandBuffer opaqueCaptureBuffer;
    private bool addedDepthTextureRequest;
    private float reflectionScale = 0.5f;
    private float opaqueSceneScale = 0.85f;
    private int reflectionFrameInterval = 1;
    private bool renderPlanarReflection = true;
    private int lastReflectionFrame = -1000;
    private bool renderingReflection;
    private bool suspended;

    public bool IsOceanVisible => oceanRenderer != null && oceanRenderer.isVisible;

    private void OnEnable()
    {
        oceanRenderer = GetComponent<Renderer>();
        if (Application.isPlaying)
            EnsureSourceCamera(Camera.main);
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying || suspended)
            return;

        if (sourceCamera == null || !sourceCamera.isActiveAndEnabled)
            EnsureSourceCamera(Camera.main);
        else
            EnsureOpaqueCaptureResources(sourceCamera);
    }

    private void OnWillRenderObject()
    {
        if (!Application.isPlaying || suspended || renderingReflection || oceanRenderer == null || !oceanRenderer.enabled)
            return;

        Camera currentCamera = Camera.current;
        if (currentCamera == null || currentCamera == reflectionCamera || currentCamera.cameraType == CameraType.Reflection)
            return;

        // In the Editor, Scene/Preview cameras can render between Game-camera passes.
        // Letting those cameras overwrite the shared reflection matrices and opaque
        // capture produces a visible alternating-frame flash in Game view, especially
        // at the Extreme resolution. The experiment is intentionally driven by the
        // tagged gameplay camera, just like a shipped single-camera scene.
        Camera gameplayCamera = Camera.main;
        if (currentCamera.cameraType != CameraType.Game || gameplayCamera == null || currentCamera != gameplayCamera)
            return;

        EnsureSourceCamera(currentCamera);
        EnsureOpaqueCaptureResources(currentCamera);

        if (renderPlanarReflection && Time.frameCount - lastReflectionFrame >= reflectionFrameInterval)
        {
            RenderPlanarReflection(currentCamera);
            lastReflectionFrame = Time.frameCount;
        }

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
        if (level == 1)
        {
            reflectionScale = 0.35f;
            opaqueSceneScale = 0.65f;
            reflectionFrameInterval = 2;
            renderPlanarReflection = false;
        }
        else if (level == 2)
        {
            reflectionScale = 0.5f;
            opaqueSceneScale = 0.85f;
            reflectionFrameInterval = 1;
            renderPlanarReflection = true;
        }
        else
        {
            reflectionScale = 0.65f;
            opaqueSceneScale = 1f;
            reflectionFrameInterval = 1;
            renderPlanarReflection = true;
        }

        ReleaseRenderTextures();
        if (Application.isPlaying && sourceCamera != null)
            EnsureOpaqueCaptureResources(sourceCamera);
    }

    public void Configure(LayerMask layersToReflect, int reflectionDimensionLimit = 1280, bool enableOpaqueCapture = true)
    {
        reflectionMask = layersToReflect;
        maximumReflectionDimension = Mathf.Clamp(reflectionDimensionLimit, 128, 2048);
        captureOpaqueScene = enableOpaqueCapture;
        ReleaseRenderTextures();
        if (Application.isPlaying && sourceCamera != null)
            EnsureOpaqueCaptureResources(sourceCamera);
    }

    public void SetSuspended(bool value)
    {
        suspended = value;
        if (value)
        {
            RemoveOpaqueCaptureBuffer();
        }
        else if (Application.isPlaying && isActiveAndEnabled)
        {
            EnsureSourceCamera(Camera.main);
        }
    }

    private void EnsureSourceCamera(Camera cameraToUse)
    {
        if (cameraToUse == null || cameraToUse == reflectionCamera)
            return;

        if (sourceCamera != cameraToUse)
        {
            RemoveOpaqueCaptureBuffer();
            RestoreSourceCameraState();
            sourceCamera = cameraToUse;
            addedDepthTextureRequest = requestCameraDepth && (sourceCamera.depthTextureMode & DepthTextureMode.Depth) == 0;
        }

        if (requestCameraDepth)
            sourceCamera.depthTextureMode |= DepthTextureMode.Depth;

        EnsureOpaqueCaptureResources(sourceCamera);
    }

    private void EnsureOpaqueCaptureResources(Camera cameraToUse)
    {
        if (!captureOpaqueScene || suspended || cameraToUse == null)
            return;

        int width = Mathf.Max(1, Mathf.RoundToInt(cameraToUse.pixelWidth * opaqueSceneScale));
        int height = Mathf.Max(1, Mathf.RoundToInt(cameraToUse.pixelHeight * opaqueSceneScale));
        float dimensionScale = Mathf.Min(1f, maximumOpaqueDimension / (float)Mathf.Max(width, height));
        width = Mathf.Max(1, Mathf.RoundToInt(width * dimensionScale));
        height = Mathf.Max(1, Mathf.RoundToInt(height * dimensionScale));
        bool hdr = cameraToUse.allowHDR && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf);
        RenderTextureFormat format = hdr ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
        if (opaqueSceneTexture == null ||
            !opaqueSceneTexture.IsCreated() ||
            opaqueSceneTexture.width != width ||
            opaqueSceneTexture.height != height ||
            opaqueSceneTexture.format != format)
        {
            RemoveOpaqueCaptureBuffer();
            ReleaseRenderTexture(ref opaqueSceneTexture);
            opaqueSceneTexture = CreateRenderTexture("Extreme Opaque Scene", width, height, format, 0, false);
            if (opaqueSceneTexture == null)
                return;
        }

        if (opaqueCaptureBuffer == null)
        {
            opaqueCaptureBuffer = new CommandBuffer { name = "Extreme Ocean - capture opaque scene" };
            // BeforeForwardAlpha inherits the camera's active viewport. On a Retina or
            // fullscreen target that viewport can be larger than this downscaled texture,
            // leaving only a centred rectangle populated by the blit. Bind the target and
            // reset its viewport explicitly before copying the opaque camera colour.
            opaqueCaptureBuffer.SetRenderTarget(new RenderTargetIdentifier(opaqueSceneTexture));
            opaqueCaptureBuffer.SetViewport(new Rect(0f, 0f, width, height));
            opaqueCaptureBuffer.Blit(BuiltinRenderTextureType.CameraTarget, BuiltinRenderTextureType.CurrentActive);
            opaqueCaptureBuffer.SetGlobalTexture(OpaqueSceneTextureId, new RenderTargetIdentifier(opaqueSceneTexture));
            opaqueCaptureBuffer.SetGlobalVector(OpaqueSceneResolutionId, BuildResolutionVector(width, height));
            cameraToUse.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, opaqueCaptureBuffer);
        }
    }

    private void RenderPlanarReflection(Camera cameraToReflect)
    {
        EnsureReflectionCamera(cameraToReflect);
        EnsureReflectionTexture(cameraToReflect);
        if (reflectionCamera == null || reflectionTexture == null)
            return;

        Vector3 planeNormal = transform.up.normalized;
        Vector3 planePosition = transform.position + planeNormal * clipPlaneOffset;
        Vector4 worldPlane = new Vector4(
            planeNormal.x,
            planeNormal.y,
            planeNormal.z,
            -Vector3.Dot(planeNormal, planePosition));
        Matrix4x4 reflectionMatrix = CalculateReflectionMatrix(worldPlane);

        reflectionCamera.CopyFrom(cameraToReflect);
        reflectionCamera.enabled = false;
        reflectionCamera.cameraType = CameraType.Reflection;
        reflectionCamera.targetTexture = reflectionTexture;
        reflectionCamera.allowMSAA = false;
        reflectionCamera.depthTextureMode = DepthTextureMode.None;
        reflectionCamera.useOcclusionCulling = false;
        // Always exclude this water layer from the reflection camera. This makes the
        // anti-recursion rule explicit without mutating the live renderer state.
        reflectionCamera.cullingMask = reflectionMask & ~(1 << gameObject.layer);
        reflectionCamera.clearFlags = reflectSkybox ? cameraToReflect.clearFlags : CameraClearFlags.SolidColor;

        Vector3 reflectedCameraPosition = reflectionMatrix.MultiplyPoint(cameraToReflect.transform.position);
        Vector3 reflectedForward = reflectionMatrix.MultiplyVector(cameraToReflect.transform.forward);
        Vector3 reflectedUp = reflectionMatrix.MultiplyVector(cameraToReflect.transform.up);
        reflectionCamera.transform.SetPositionAndRotation(
            reflectedCameraPosition,
            Quaternion.LookRotation(reflectedForward, reflectedUp));
        reflectionCamera.worldToCameraMatrix = cameraToReflect.worldToCameraMatrix * reflectionMatrix;
        Vector4 cameraSpacePlane = CameraSpacePlane(reflectionCamera, planePosition, planeNormal, 1f);
        reflectionCamera.projectionMatrix = reflectionCamera.CalculateObliqueMatrix(cameraSpacePlane);

        bool previousInvertCulling = GL.invertCulling;
        renderingReflection = true;
        GL.invertCulling = !previousInvertCulling;
        try
        {
            reflectionCamera.Render();
        }
        finally
        {
            GL.invertCulling = previousInvertCulling;
            renderingReflection = false;
        }

        Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(reflectionCamera.projectionMatrix, true);
        Shader.SetGlobalMatrix(ReflectionViewProjectionId, gpuProjection * reflectionCamera.worldToCameraMatrix);
        Shader.SetGlobalVector(ReflectionUvScaleOffsetId, new Vector4(0.5f, 0.5f, 0.5f, 0.5f));
    }

    private void EnsureReflectionCamera(Camera source)
    {
        if (reflectionCamera != null)
            return;

        reflectionCameraObject = new GameObject("Extreme Planar Reflection Camera")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        reflectionCamera = reflectionCameraObject.AddComponent<Camera>();
        reflectionCamera.enabled = false;
        reflectionCamera.CopyFrom(source);
    }

    private void EnsureReflectionTexture(Camera source)
    {
        int width = Mathf.Clamp(Mathf.RoundToInt(source.pixelWidth * reflectionScale), 1, maximumReflectionDimension);
        int height = Mathf.Clamp(Mathf.RoundToInt(source.pixelHeight * reflectionScale), 1, maximumReflectionDimension);
        bool hdr = source.allowHDR && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf);
        RenderTextureFormat format = hdr ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
        if (reflectionTexture != null &&
            reflectionTexture.IsCreated() &&
            reflectionTexture.width == width &&
            reflectionTexture.height == height &&
            reflectionTexture.format == format)
            return;

        ReleaseRenderTexture(ref reflectionTexture);
        reflectionTexture = CreateRenderTexture("Extreme Planar Reflection", width, height, format, 24, true);
        if (reflectionCamera != null)
            reflectionCamera.targetTexture = reflectionTexture;
    }

    private void PublishGlobals()
    {
        if (reflectionTexture != null)
        {
            Shader.SetGlobalTexture(PlanarReflectionTextureId, reflectionTexture);
            Shader.SetGlobalVector(ReflectionResolutionId, BuildResolutionVector(reflectionTexture.width, reflectionTexture.height));
        }

        if (opaqueSceneTexture != null)
        {
            Shader.SetGlobalTexture(OpaqueSceneTextureId, opaqueSceneTexture);
            Shader.SetGlobalVector(OpaqueSceneResolutionId, BuildResolutionVector(opaqueSceneTexture.width, opaqueSceneTexture.height));
        }
    }

    private void RemoveOpaqueCaptureBuffer()
    {
        if (sourceCamera != null && opaqueCaptureBuffer != null)
            sourceCamera.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, opaqueCaptureBuffer);

        if (opaqueCaptureBuffer != null)
        {
            opaqueCaptureBuffer.Release();
            opaqueCaptureBuffer = null;
        }
    }

    private void ReleaseRenderTextures()
    {
        RemoveOpaqueCaptureBuffer();
        if (reflectionCamera != null)
            reflectionCamera.targetTexture = null;
        ReleaseRenderTexture(ref reflectionTexture);
        ReleaseRenderTexture(ref opaqueSceneTexture);
        Shader.SetGlobalTexture(PlanarReflectionTextureId, Texture2D.blackTexture);
        Shader.SetGlobalTexture(OpaqueSceneTextureId, Texture2D.blackTexture);
        Shader.SetGlobalVector(ReflectionResolutionId, Vector4.one);
        Shader.SetGlobalVector(OpaqueSceneResolutionId, Vector4.one);
    }

    private void ReleaseResources()
    {
        ReleaseRenderTextures();
        RestoreSourceCameraState();
        sourceCamera = null;

        if (reflectionCameraObject != null)
        {
            if (Application.isPlaying)
                Destroy(reflectionCameraObject);
            else
                DestroyImmediate(reflectionCameraObject);
        }

        reflectionCameraObject = null;
        reflectionCamera = null;
    }

    private void RestoreSourceCameraState()
    {
        if (sourceCamera != null && addedDepthTextureRequest)
            sourceCamera.depthTextureMode &= ~DepthTextureMode.Depth;
        addedDepthTextureRequest = false;
    }

    private static RenderTexture CreateRenderTexture(
        string textureName,
        int width,
        int height,
        RenderTextureFormat format,
        int depthBits,
        bool useMipMaps)
    {
        var texture = new RenderTexture(width, height, depthBits, format, RenderTextureReadWrite.Default)
        {
            name = textureName,
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = useMipMaps,
            autoGenerateMips = useMipMaps,
            antiAliasing = 1
        };
        if (!texture.Create())
        {
            Debug.LogError($"Could not allocate {textureName} RenderTexture ({width} x {height}, {format}).");
            if (Application.isPlaying)
                Destroy(texture);
            else
                DestroyImmediate(texture);
            return null;
        }
        return texture;
    }

    private static void ReleaseRenderTexture(ref RenderTexture texture)
    {
        if (texture == null)
            return;

        texture.Release();
        if (Application.isPlaying)
            Destroy(texture);
        else
            DestroyImmediate(texture);
        texture = null;
    }

    private static Vector4 BuildResolutionVector(int width, int height)
    {
        return new Vector4(width, height, 1f / Mathf.Max(width, 1), 1f / Mathf.Max(height, 1));
    }

    private static Vector4 CameraSpacePlane(Camera camera, Vector3 position, Vector3 normal, float sideSign)
    {
        Matrix4x4 worldToCamera = camera.worldToCameraMatrix;
        Vector3 cameraPosition = worldToCamera.MultiplyPoint(position);
        Vector3 cameraNormal = worldToCamera.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(cameraNormal.x, cameraNormal.y, cameraNormal.z, -Vector3.Dot(cameraPosition, cameraNormal));
    }

    private static Matrix4x4 CalculateReflectionMatrix(Vector4 plane)
    {
        var matrix = Matrix4x4.identity;
        matrix.m00 = 1f - 2f * plane.x * plane.x;
        matrix.m01 = -2f * plane.x * plane.y;
        matrix.m02 = -2f * plane.x * plane.z;
        matrix.m03 = -2f * plane.w * plane.x;
        matrix.m10 = -2f * plane.y * plane.x;
        matrix.m11 = 1f - 2f * plane.y * plane.y;
        matrix.m12 = -2f * plane.y * plane.z;
        matrix.m13 = -2f * plane.w * plane.y;
        matrix.m20 = -2f * plane.z * plane.x;
        matrix.m21 = -2f * plane.z * plane.y;
        matrix.m22 = 1f - 2f * plane.z * plane.z;
        matrix.m23 = -2f * plane.w * plane.z;
        return matrix;
    }
}
