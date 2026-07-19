using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public sealed class CinematicPostEffect : MonoBehaviour
{
    [SerializeField] private Shader shader;
    private Material material;

    public void Configure(Shader postShader) => shader = postShader;

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (shader == null || !shader.isSupported)
        {
            Graphics.Blit(source, destination);
            return;
        }

        if (material == null || material.shader != shader)
        {
            if (material != null)
                DestroyImmediate(material);
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        Graphics.Blit(source, destination, material);
    }

    private void OnDestroy()
    {
        if (material != null)
            DestroyImmediate(material);
    }
}
