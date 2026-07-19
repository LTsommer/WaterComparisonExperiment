using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class ExtremeOceanMesh : MonoBehaviour
{
    [Header("Ocean footprint")]
    [SerializeField, Min(10f)] private float size = 160f;
    [SerializeField, Min(1f)] private float maximumDisplacement = 8f;

    [Header("Grid quality")]
    [SerializeField, Range(32, 512)] private int performanceResolution = 160;
    [SerializeField, Range(32, 512)] private int highResolution = 240;
    [SerializeField, Range(32, 512)] private int extremeResolution = 320;
    [SerializeField, Range(1, 3)] private int initialQuality = 2;

    private Mesh generatedMesh;
    private int builtResolution = -1;
    private float builtSize = -1f;
    private float builtMaximumDisplacement = -1f;

    public int CurrentResolution => builtResolution;
    public float Size => size;

    private void OnEnable()
    {
        ApplyQuality(initialQuality);
    }

    private void OnValidate()
    {
        performanceResolution = Mathf.Clamp(performanceResolution, 32, 512);
        highResolution = Mathf.Max(performanceResolution, Mathf.Clamp(highResolution, 32, 512));
        extremeResolution = Mathf.Max(highResolution, Mathf.Clamp(extremeResolution, 32, 512));
        initialQuality = Mathf.Clamp(initialQuality, 1, 3);
        size = Mathf.Max(size, 10f);
        maximumDisplacement = Mathf.Max(maximumDisplacement, 1f);

        if (isActiveAndEnabled)
        {
#if UNITY_EDITOR
            // Unity 6 forbids DestroyImmediate during OnValidate. Defer the mesh
            // replacement until the validation callback has fully returned.
            UnityEditor.EditorApplication.delayCall -= RebuildAfterValidation;
            UnityEditor.EditorApplication.delayCall += RebuildAfterValidation;
#else
            ApplyQuality(initialQuality);
#endif
        }
    }

#if UNITY_EDITOR
    private void RebuildAfterValidation()
    {
        UnityEditor.EditorApplication.delayCall -= RebuildAfterValidation;
        if (this != null && isActiveAndEnabled)
            ApplyQuality(initialQuality);
    }
#endif

    private void OnDestroy()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= RebuildAfterValidation;
#endif
        ReleaseMesh();
    }

    public void ApplyQuality(int level)
    {
        initialQuality = Mathf.Clamp(level, 1, 3);
        int resolution = initialQuality == 1
            ? performanceResolution
            : initialQuality == 2 ? highResolution : extremeResolution;
        BuildMesh(resolution);
    }

    public void Configure(float oceanSize, int performanceGrid, int highGrid, int extremeGrid, int defaultQuality = 2)
    {
        size = Mathf.Max(oceanSize, 10f);
        performanceResolution = Mathf.Clamp(performanceGrid, 32, 512);
        highResolution = Mathf.Clamp(Mathf.Max(highGrid, performanceResolution), 32, 512);
        extremeResolution = Mathf.Clamp(Mathf.Max(extremeGrid, highResolution), 32, 512);
        ApplyQuality(defaultQuality);
    }

    private void BuildMesh(int resolution)
    {
        resolution = Mathf.Clamp(resolution, 32, 512);
        if (generatedMesh != null &&
            builtResolution == resolution &&
            Mathf.Approximately(builtSize, size) &&
            Mathf.Approximately(builtMaximumDisplacement, maximumDisplacement))
            return;

        int sideVertexCount = resolution + 1;
        int vertexCount = sideVertexCount * sideVertexCount;
        var vertices = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];
        var triangles = new int[resolution * resolution * 6];
        float step = size / resolution;
        float halfSize = size * 0.5f;

        for (int z = 0; z <= resolution; z++)
        {
            int rowStart = z * sideVertexCount;
            float zPosition = z * step - halfSize;
            float v = (float)z / resolution;
            for (int x = 0; x <= resolution; x++)
            {
                int index = rowStart + x;
                vertices[index] = new Vector3(x * step - halfSize, 0f, zPosition);
                uvs[index] = new Vector2((float)x / resolution, v);
            }
        }

        int triangleIndex = 0;
        for (int z = 0; z < resolution; z++)
        {
            int rowStart = z * sideVertexCount;
            int nextRowStart = rowStart + sideVertexCount;
            for (int x = 0; x < resolution; x++)
            {
                int lowerLeft = rowStart + x;
                int upperLeft = nextRowStart + x;
                triangles[triangleIndex++] = lowerLeft;
                triangles[triangleIndex++] = upperLeft;
                triangles[triangleIndex++] = lowerLeft + 1;
                triangles[triangleIndex++] = lowerLeft + 1;
                triangles[triangleIndex++] = upperLeft;
                triangles[triangleIndex++] = upperLeft + 1;
            }
        }

        ReleaseMesh();
        generatedMesh = new Mesh
        {
            name = $"Extreme Ocean Grid {resolution}x{resolution}",
            hideFlags = HideFlags.DontSave,
            indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        generatedMesh.SetVertices(vertices);
        generatedMesh.SetUVs(0, uvs);
        generatedMesh.SetTriangles(triangles, 0, false);
        generatedMesh.bounds = new Bounds(
            Vector3.zero,
            new Vector3(size + maximumDisplacement * 2f, maximumDisplacement * 2f, size + maximumDisplacement * 2f));
        generatedMesh.UploadMeshData(false);

        GetComponent<MeshFilter>().sharedMesh = generatedMesh;
        builtResolution = resolution;
        builtSize = size;
        builtMaximumDisplacement = maximumDisplacement;
    }

    private void ReleaseMesh()
    {
        if (generatedMesh == null)
            return;

        MeshFilter filter = GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh == generatedMesh)
            filter.sharedMesh = null;

        if (Application.isPlaying)
            Destroy(generatedMesh);
        else
            DestroyImmediate(generatedMesh);

        generatedMesh = null;
        builtResolution = -1;
        builtSize = -1f;
        builtMaximumDisplacement = -1f;
    }
}
