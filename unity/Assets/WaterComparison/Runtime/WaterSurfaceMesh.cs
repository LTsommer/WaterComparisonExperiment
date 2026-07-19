using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class WaterSurfaceMesh : MonoBehaviour
{
    [SerializeField, Min(1)] private int resolution = 200;
    [SerializeField, Min(0.1f)] private float size = 90f;

    private Mesh mesh;

    private void OnEnable() => BuildMesh();

    private void OnDestroy()
    {
        if (mesh == null)
            return;

        if (Application.isPlaying)
            Destroy(mesh);
        else
            DestroyImmediate(mesh);
    }

    private void BuildMesh()
    {
        // Six vertex-wave bands still have almost six samples across the shortest
        // wavelength at this cap. Dropping 250 -> 210 removes roughly 29% of the
        // vertices with no visible loss at the experiment camera distance.
        resolution = Mathf.Clamp(resolution, 1, 210);
        var vertexCount = (resolution + 1) * (resolution + 1);
        var vertices = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];
        var triangles = new int[resolution * resolution * 6];
        var step = size / resolution;

        for (var z = 0; z <= resolution; z++)
        for (var x = 0; x <= resolution; x++)
        {
            var index = z * (resolution + 1) + x;
            vertices[index] = new Vector3(x * step - size * 0.5f, 0f, z * step - size * 0.5f);
            uvs[index] = new Vector2((float)x / resolution, (float)z / resolution);
        }

        var triangle = 0;
        for (var z = 0; z < resolution; z++)
        for (var x = 0; x < resolution; x++)
        {
            var bottomLeft = z * (resolution + 1) + x;
            triangles[triangle++] = bottomLeft;
            triangles[triangle++] = bottomLeft + resolution + 1;
            triangles[triangle++] = bottomLeft + 1;
            triangles[triangle++] = bottomLeft + 1;
            triangles[triangle++] = bottomLeft + resolution + 1;
            triangles[triangle++] = bottomLeft + resolution + 2;
        }

        if (mesh != null)
        {
            if (Application.isPlaying)
                Destroy(mesh);
            else
                DestroyImmediate(mesh);
        }

        mesh = new Mesh
        {
            name = "Cinematic Ocean Grid",
            indexFormat = IndexFormat.UInt32,
            hideFlags = HideFlags.DontSave
        };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(size, 5f, size));
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }
}
