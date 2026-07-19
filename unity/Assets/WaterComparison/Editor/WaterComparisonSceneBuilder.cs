using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class WaterComparisonSceneBuilder
{
    private const string Root = "Assets/WaterComparison";
    private const string Materials = Root + "/Materials";

    [MenuItem("Water Comparison/Create cinematic ocean scene")]
    public static void Build()
    {
        EnsureFolder("Assets", "Scenes");
        EnsureFolder(Root, "Materials");

        var oceanShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/ThreeWaveWater.shader");
        var postShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/CinematicTonemap.shader");
        if (oceanShader == null || postShader == null)
        {
            Debug.LogError("Water Comparison shaders have not finished importing.");
            return;
        }

        var standardShader = Shader.Find("Standard");
        var oceanMaterial = GetMaterial(Materials + "/CinematicOcean.mat", oceanShader, "Cinematic Ocean");
        oceanMaterial.SetColor("_DeepColor", new Color(0.002f, 0.025f, 0.05f));
        oceanMaterial.SetColor("_MidColor", new Color(0.008f, 0.20f, 0.28f));
        oceanMaterial.SetColor("_ShallowColor", new Color(0.05f, 0.48f, 0.52f));
        oceanMaterial.SetColor("_FoamColor", new Color(0.76f, 0.92f, 0.92f));
        oceanMaterial.SetColor("_FogColor", new Color(0.44f, 0.62f, 0.68f));
        oceanMaterial.SetVector("_Absorption", new Vector4(0.42f, 0.12f, 0.045f, 0f));
        oceanMaterial.SetVector("_SunDirection", new Vector4(-0.48f, 0.54f, -0.69f, 0f));
        oceanMaterial.SetColor("_SunColor", new Color(4.8f, 3.5f, 2.4f, 1f));
        oceanMaterial.SetFloat("_WaveStrength", 1f);
        oceanMaterial.SetFloat("_MicroStrength", 1f);
        oceanMaterial.SetFloat("_Roughness", 0.09f);
        oceanMaterial.SetFloat("_FoamThreshold", 0.13f);
        oceanMaterial.SetFloat("_ReflectionStrength", 0f);

        var sandMaterial = GetMaterial(Materials + "/Seabed.mat", standardShader, "Seabed");
        ConfigureStandard(sandMaterial, new Color(0.30f, 0.26f, 0.20f), 0f, 0.08f);
        var rockMaterial = GetMaterial(Materials + "/WetRock.mat", standardShader, "Wet Rock");
        ConfigureStandard(rockMaterial, new Color(0.028f, 0.045f, 0.048f), 0.08f, 0.18f);
        var rockMesh = GetRockMesh(Materials + "/WeatheredRock.asset");
        var buoyRed = GetMaterial(Materials + "/BuoyRed.mat", standardShader, "Buoy Red");
        ConfigureStandard(buoyRed, new Color(0.86f, 0.11f, 0.045f), 0.08f, 0.48f);
        var buoyWhite = GetMaterial(Materials + "/BuoyWhite.mat", standardShader, "Buoy White");
        ConfigureStandard(buoyWhite, new Color(0.86f, 0.84f, 0.73f), 0f, 0.38f);
        var buoyMetal = GetMaterial(Materials + "/BuoyMetal.mat", standardShader, "Buoy Metal");
        ConfigureStandard(buoyMetal, new Color(0.06f, 0.075f, 0.08f), 0.72f, 0.52f);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var environment = new GameObject("Cinematic Ocean Environment");
        var water = new GameObject("Non-repeating Spectral Ocean");
        water.transform.SetParent(environment.transform);
        water.AddComponent<WaterSurfaceMesh>();
        water.GetComponent<MeshRenderer>().sharedMaterial = oceanMaterial;

        var seabed = CreatePrimitive("Seabed", PrimitiveType.Plane, environment.transform, sandMaterial);
        seabed.transform.position = new Vector3(0f, -2.2f, 0f);
        seabed.transform.localScale = new Vector3(10f, 1f, 10f);
        seabed.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        var rocks = new GameObject("Rock formations");
        rocks.transform.SetParent(environment.transform);
        AddRock(rocks.transform, rockMesh, rockMaterial, new Vector3(-8.4f, -0.15f, -2f), new Vector3(3.2f, 1.8f, 2.4f), new Vector3(8f, 28f, -5f));
        AddRock(rocks.transform, rockMesh, rockMaterial, new Vector3(-10.4f, 0.05f, 1.7f), new Vector3(2.4f, 2.55f, 2f), new Vector3(-12f, 58f, 9f));
        AddRock(rocks.transform, rockMesh, rockMaterial, new Vector3(9.2f, -0.45f, -4.1f), new Vector3(2.6f, 1.55f, 2.1f), new Vector3(5f, 18f, -8f));
        AddRock(rocks.transform, rockMesh, rockMaterial, new Vector3(10.8f, -0.62f, -2.1f), new Vector3(1.7f, 1.1f, 1.55f), new Vector3(18f, 66f, 4f));
        AddRock(rocks.transform, rockMesh, rockMaterial, new Vector3(-4.9f, -1f, 8.5f), new Vector3(1.8f, 1.25f, 1.9f), new Vector3(0f, 34f, 13f));

        var buoy = new GameObject("Animated navigation buoy");
        buoy.transform.SetParent(environment.transform);
        buoy.transform.position = new Vector3(-2.1f, 0.38f, 1.6f);
        var body = CreatePrimitive("Red hull", PrimitiveType.Cylinder, buoy.transform, buoyRed);
        body.transform.localPosition = Vector3.zero;
        body.transform.localScale = new Vector3(0.52f, 0.52f, 0.52f);
        var stripe = CreatePrimitive("Reflective stripe", PrimitiveType.Cylinder, buoy.transform, buoyWhite);
        stripe.transform.localPosition = new Vector3(0f, 0.12f, 0f);
        stripe.transform.localScale = new Vector3(0.525f, 0.09f, 0.525f);
        var pole = CreatePrimitive("Signal pole", PrimitiveType.Cylinder, buoy.transform, buoyMetal);
        pole.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        pole.transform.localScale = new Vector3(0.065f, 0.68f, 0.065f);
        var signal = CreatePrimitive("Signal cap", PrimitiveType.Sphere, buoy.transform, buoyRed);
        signal.transform.localPosition = new Vector3(0f, 1.76f, 0f);
        signal.transform.localScale = Vector3.one * 0.18f;
        buoy.AddComponent<FloatingBuoy>();

        var sunDirection = new Vector3(-0.48f, 0.54f, -0.69f).normalized;
        var sunObject = new GameObject("Low warm sun", typeof(Light));
        sunObject.transform.SetParent(environment.transform);
        sunObject.transform.rotation = Quaternion.LookRotation(-sunDirection);
        var sun = sunObject.GetComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.78f, 0.53f);
        sun.intensity = 1.55f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.82f;
        sun.shadowBias = 0.04f;
        sun.shadowNormalBias = 0.35f;
        RenderSettings.sun = sun;

        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = Quaternion.Euler(20f, 38f, 0f) * new Vector3(0f, 0f, -19.5f);
        cameraObject.transform.LookAt(Vector3.zero);
        var camera = cameraObject.GetComponent<Camera>();
        camera.fieldOfView = 48f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 350f;
        camera.allowHDR = true;
        camera.allowMSAA = true;
        camera.depthTextureMode = DepthTextureMode.None;
        cameraObject.AddComponent<OrbitCamera>();
        cameraObject.AddComponent<CinematicPostEffect>().Configure(postShader);

        var skyboxShader = Shader.Find("Skybox/Procedural");
        var skybox = GetMaterial(Materials + "/OceanSkybox.mat", skyboxShader, "Ocean Skybox");
        skybox.SetColor("_SkyTint", new Color(0.10f, 0.27f, 0.39f));
        skybox.SetColor("_GroundColor", new Color(0.18f, 0.23f, 0.22f));
        skybox.SetFloat("_SunSize", 0.028f);
        skybox.SetFloat("_SunSizeConvergence", 7.5f);
        skybox.SetFloat("_AtmosphereThickness", 0.78f);
        skybox.SetFloat("_Exposure", 1.08f);
        RenderSettings.skybox = skybox;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.0095f;
        RenderSettings.fogColor = new Color(0.44f, 0.62f, 0.68f);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.23f, 0.44f, 0.52f);
        RenderSettings.ambientEquatorColor = new Color(0.15f, 0.28f, 0.29f);
        RenderSettings.ambientGroundColor = new Color(0.055f, 0.07f, 0.065f);
        RenderSettings.ambientIntensity = 1.0f;

        QualitySettings.antiAliasing = 2;
        QualitySettings.shadowDistance = 80f;
        QualitySettings.shadowResolution = ShadowResolution.High;

        const string scenePath = "Assets/Scenes/WaterComparison.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
        AssetDatabase.SaveAssets();
        SceneView.RepaintAll();
        Debug.Log("Cinematic ocean scene created and saved: " + scenePath);
    }

    private static void EnsureFolder(string parent, string child)
    {
        var path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }

    private static Material GetMaterial(string path, Shader shader, string displayName)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
            material.name = System.IO.Path.GetFileNameWithoutExtension(path);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void ConfigureStandard(Material material, Color color, float metallic, float smoothness)
    {
        material.color = color;
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Glossiness", smoothness);
    }

    private static GameObject CreatePrimitive(string name, PrimitiveType type, Transform parent, Material material)
    {
        var result = GameObject.CreatePrimitive(type);
        result.name = name;
        result.transform.SetParent(parent);
        result.GetComponent<MeshRenderer>().sharedMaterial = material;
        var collider = result.GetComponent<Collider>();
        if (collider != null)
            Object.DestroyImmediate(collider);
        return result;
    }

    private static Mesh GetRockMesh(string path)
    {
        var source = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
        var vertices = source.vertices;
        for (var index = 0; index < vertices.Length; index++)
        {
            var direction = vertices[index].normalized;
            var largeForm = Mathf.Sin(direction.x * 4.7f + direction.y * 2.9f + direction.z * 3.8f) * 0.13f;
            var chippedForm = Mathf.Sin(direction.x * 11.3f - direction.y * 7.1f + direction.z * 8.6f) * 0.055f;
            var verticalStrata = Mathf.Sin(direction.y * 16f + direction.x * 2.4f) * 0.035f;
            vertices[index] *= 1f + largeForm + chippedForm + verticalStrata;
        }

        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            mesh = new Mesh { name = "Weathered Rock" };
            AssetDatabase.CreateAsset(mesh, path);
        }
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = source.triangles;
        mesh.uv = source.uv;
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    private static void AddRock(Transform parent, Mesh mesh, Material material, Vector3 position, Vector3 scale, Vector3 rotation)
    {
        var rock = new GameObject("Weathered rock", typeof(MeshFilter), typeof(MeshRenderer));
        rock.transform.SetParent(parent);
        rock.GetComponent<MeshFilter>().sharedMesh = mesh;
        rock.GetComponent<MeshRenderer>().sharedMaterial = material;
        rock.transform.position = position;
        rock.transform.localScale = scale;
        rock.transform.rotation = Quaternion.Euler(rotation);
        var renderer = rock.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
    }
}
