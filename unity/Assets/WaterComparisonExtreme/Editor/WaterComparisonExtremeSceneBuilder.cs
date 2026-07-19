using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using WaterComparisonExtreme.Rocks;

public static class WaterComparisonExtremeSceneBuilder
{
    private const string Root = "Assets/WaterComparisonExtreme";
    private const string Materials = Root + "/Materials";
    private const string Generated = Root + "/Generated";
    private const string ScenePath = "Assets/Scenes/WaterComparisonExtreme.unity";

    [MenuItem("Water Comparison/Create EXTREME ocean scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        EnsureFolder("Assets", "Scenes");
        EnsureFolder("Assets", "WaterComparisonExtreme");
        EnsureFolder(Root, "Materials");
        EnsureFolder(Root, "Generated");

        Shader oceanShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/ExtremeOcean.shader");
        Shader foamShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/FoamHistory.shader");
        Shader rockShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Rocks/ExtremeRock.shader");
        Shader seabedShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Environment/ExtremeSeabed.shader");
        Shader postShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/WaterComparison/Shaders/CinematicTonemap.shader");
        Shader standardShader = Shader.Find("Standard");
        Shader skyboxShader = Shader.Find("Skybox/Procedural");
        if (!IsShaderReady(oceanShader, "Extreme ocean") ||
            !IsShaderReady(foamShader, "Foam history") ||
            !IsShaderReady(rockShader, "Weathered rock") ||
            !IsShaderReady(seabedShader, "Seabed caustics") ||
            !IsShaderReady(postShader, "Cinematic post effect") ||
            !IsShaderReady(standardShader, "Standard") ||
            !IsShaderReady(skyboxShader, "Procedural skybox"))
        {
            Debug.LogError("Extreme ocean scene was not created because at least one shader is missing, unsupported, or has compiler errors.");
            return;
        }

        Material oceanMaterial = GetMaterial(Materials + "/ExtremeOcean.mat", oceanShader);
        ConfigureOcean(oceanMaterial);
        Material rockMaterial = GetMaterial(Materials + "/ExtremeWetRock.mat", rockShader);
        ConfigureRocks(rockMaterial);
        Material seabedMaterial = GetMaterial(Materials + "/ExtremeSeabed.mat", seabedShader);
        ConfigureSeabed(seabedMaterial);
        Material buoyRed = GetMaterial(Materials + "/ExtremeBuoyRed.mat", standardShader);
        ConfigureStandard(buoyRed, new Color(0.84f, 0.055f, 0.024f), 0.03f, 0.46f);
        Material buoyWhite = GetMaterial(Materials + "/ExtremeBuoyWhite.mat", standardShader);
        ConfigureStandard(buoyWhite, new Color(0.90f, 0.88f, 0.76f), 0f, 0.42f);
        Material buoyMetal = GetMaterial(Materials + "/ExtremeBuoyMetal.mat", standardShader);
        ConfigureStandard(buoyMetal, new Color(0.045f, 0.055f, 0.058f), 0.78f, 0.64f);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var environment = new GameObject("UNITY EXTREME OCEAN ENVIRONMENT");

        GameObject water = new GameObject(
            "Extreme spectral ocean · 12 bands",
            typeof(MeshFilter),
            typeof(MeshRenderer),
            typeof(ExtremeOceanMesh),
            typeof(ExtremeOceanRenderer),
            typeof(ExtremeFoamSimulation));
        water.transform.SetParent(environment.transform);
        int waterLayer = LayerMask.NameToLayer("Water");
        water.layer = waterLayer >= 0 ? waterLayer : 4;
        var waterMesh = water.GetComponent<ExtremeOceanMesh>();
        waterMesh.Configure(160f, 160, 240, 320, 2);
        var waterRenderer = water.GetComponent<MeshRenderer>();
        waterRenderer.sharedMaterial = oceanMaterial;
        waterRenderer.shadowCastingMode = ShadowCastingMode.Off;
        waterRenderer.receiveShadows = true;
        var oceanRenderer = water.GetComponent<ExtremeOceanRenderer>();
        int reflectionMask = ~(1 << water.layer);
        oceanRenderer.Configure(reflectionMask, 1280, true);
        var foamSimulation = water.GetComponent<ExtremeFoamSimulation>();
        foamSimulation.Configure(water.transform, 160f, foamShader, oceanMaterial);

        GameObject seabed = CreatePrimitive("Caustic seabed", PrimitiveType.Plane, environment.transform, seabedMaterial);
        seabed.transform.position = new Vector3(0f, -3.1f, 0f);
        seabed.transform.localScale = new Vector3(18f, 1f, 18f);
        seabed.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        Transform rockRoot = new GameObject("Seeded fractured shoreline rocks").transform;
        rockRoot.SetParent(environment.transform);
        AddRock(rockRoot, rockMaterial, 1907, new Vector3(-9.2f, -0.72f, -2.4f), new Vector3(3.2f, 2.45f, 2.5f), new Vector3(4f, 22f, -7f), new Vector3(1.04f, 0.81f, 0.91f), 1.08f);
        AddRock(rockRoot, rockMaterial, 7319, new Vector3(-11.8f, -1.18f, 1.3f), new Vector3(2.15f, 2.1f, 1.85f), new Vector3(-9f, 67f, 11f), new Vector3(0.91f, 1.05f, 0.86f), 1.17f);
        AddRock(rockRoot, rockMaterial, 3713, new Vector3(9.1f, -1.08f, -4.5f), new Vector3(2.75f, 1.9f, 2.25f), new Vector3(7f, 34f, -9f), new Vector3(1.08f, 0.74f, 0.96f), 1.12f);
        AddRock(rockRoot, rockMaterial, 9143, new Vector3(11.5f, -1.42f, -1.45f), new Vector3(1.75f, 1.45f, 1.7f), new Vector3(-13f, 81f, 6f), new Vector3(0.93f, 0.86f, 1.12f), 1.28f);
        AddRock(rockRoot, rockMaterial, 5309, new Vector3(-5.1f, -1.62f, 8.8f), new Vector3(2.0f, 1.55f, 2.15f), new Vector3(11f, 42f, 14f), new Vector3(1.08f, 0.78f, 0.93f), 1.05f);
        AddRock(rockRoot, rockMaterial, 1103, new Vector3(4.6f, -2.05f, 8.1f), new Vector3(1.45f, 1.0f, 1.65f), new Vector3(3f, 103f, -5f), new Vector3(0.88f, 0.71f, 1.16f), 1.22f);
        AddRock(rockRoot, rockMaterial, 8429, new Vector3(-17f, -2.3f, -14f), new Vector3(5.2f, 3.1f, 4.4f), new Vector3(-4f, 15f, 3f), new Vector3(1.10f, 0.66f, 0.92f), 0.92f);

        GameObject buoy = BuildBuoy(environment.transform, buoyRed, buoyWhite, buoyMetal);
        buoy.transform.position = new Vector3(-2.25f, 0.34f, 1.55f);
        buoy.GetComponent<ExtremeFloatingBuoy>().Configure(water.transform, oceanMaterial, 0.34f);

        Vector3 sunDirection = new Vector3(-0.42f, 0.74f, -0.52f).normalized;
        var sunObject = new GameObject("Low Atlantic sun", typeof(Light));
        sunObject.transform.SetParent(environment.transform);
        sunObject.transform.rotation = Quaternion.LookRotation(-sunDirection);
        Light sun = sunObject.GetComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.76f, 0.51f);
        sun.intensity = 1.72f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.86f;
        sun.shadowBias = 0.035f;
        sun.shadowNormalBias = 0.32f;
        RenderSettings.sun = sun;

        GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(12.8f, 5.8f, 16.6f);
        cameraObject.transform.LookAt(new Vector3(-0.3f, 0.05f, -0.8f));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.fieldOfView = 47f;
        camera.nearClipPlane = 0.08f;
        camera.farClipPlane = 420f;
        camera.allowHDR = true;
        camera.allowMSAA = true;
        camera.depthTextureMode = DepthTextureMode.Depth;
        cameraObject.AddComponent<OrbitCamera>();
        cameraObject.AddComponent<CinematicPostEffect>().Configure(postShader);

        var qualityController = environment.AddComponent<ExtremeQualityController>();
        qualityController.Configure(waterMesh, oceanRenderer, foamSimulation, waterRenderer, 2);

        Material skybox = GetMaterial(Materials + "/ExtremeOceanSkybox.mat", skyboxShader);
        skybox.SetColor("_SkyTint", new Color(0.065f, 0.205f, 0.31f));
        skybox.SetColor("_GroundColor", new Color(0.10f, 0.14f, 0.135f));
        skybox.SetFloat("_SunSize", 0.023f);
        skybox.SetFloat("_SunSizeConvergence", 8.4f);
        skybox.SetFloat("_AtmosphereThickness", 0.83f);
        skybox.SetFloat("_Exposure", 1.12f);
        RenderSettings.skybox = skybox;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.defaultReflectionResolution = 256;
        RenderSettings.reflectionIntensity = 1.0f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.0058f;
        RenderSettings.fogColor = new Color(0.30f, 0.50f, 0.58f);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.19f, 0.39f, 0.48f);
        RenderSettings.ambientEquatorColor = new Color(0.105f, 0.225f, 0.24f);
        RenderSettings.ambientGroundColor = new Color(0.032f, 0.045f, 0.043f);
        RenderSettings.ambientIntensity = 1f;

        QualitySettings.antiAliasing = 2;
        QualitySettings.shadowDistance = 95f;
        QualitySettings.shadowResolution = ShadowResolution.High;
        QualitySettings.vSyncCount = 1;

        EditorSceneManager.SaveScene(scene, ScenePath);
        UpdateBuildSettings();
        AssetDatabase.SaveAssets();
        DynamicGI.UpdateEnvironment();
        SceneView.RepaintAll();
        Debug.Log("Unity Extreme ocean scene created: " + ScenePath + " · press 1/2/3 in Play mode for quality.");
    }

    private static void ConfigureOcean(Material material)
    {
        material.SetColor("_DeepColor", new Color(0.0015f, 0.018f, 0.036f));
        material.SetColor("_ScatteringColor", new Color(0.012f, 0.26f, 0.29f));
        material.SetColor("_ShallowTint", new Color(0.055f, 0.46f, 0.43f));
        material.SetColor("_FoamColor", new Color(0.79f, 0.91f, 0.91f));
        material.SetColor("_HorizonColor", new Color(0.30f, 0.50f, 0.58f));
        material.SetVector("_Absorption", new Vector4(0.34f, 0.105f, 0.034f, 0f));
        material.SetFloat("_WaveStrength", 1f);
        material.SetFloat("_Choppiness", 1f);
        material.SetFloat("_WaveTimeScale", 1f);
        material.SetFloat("_MicroStrength", 1f);
        material.SetFloat("_Roughness", 0.075f);
        material.SetFloat("_RefractionStrength", 0.042f);
        material.SetFloat("_RefractionDepthBias", 0.08f);
        material.SetFloat("_MaxOpticalDepth", 32f);
        material.SetFloat("_PlanarReflectionStrength", 0.92f);
        material.SetFloat("_ReflectionDistortion", 0.038f);
        material.SetColor("_SunColor", new Color(5.2f, 4.25f, 3.15f, 1f));
        material.SetVector("_SunDirection", new Vector4(-0.42f, 0.74f, -0.52f, 0f));
        material.SetFloat("_FoamThreshold", 0.145f);
        material.SetFloat("_FoamSoftness", 0.105f);
        material.SetFloat("_ShoreFoamDistance", 0.10f);
        material.SetFloat("_ShoreFoamWidth", 0.72f);
        material.SetFloat("_ShoreFoamStrength", 0.72f);
        EditorUtility.SetDirty(material);
    }

    private static void ConfigureRocks(Material material)
    {
        material.SetColor("_BaseColor", new Color(0.075f, 0.082f, 0.079f));
        material.SetColor("_SecondaryColor", new Color(0.165f, 0.155f, 0.132f));
        material.SetColor("_StrataColor", new Color(0.026f, 0.029f, 0.028f));
        material.SetColor("_WetTint", new Color(0.018f, 0.031f, 0.032f));
        material.SetFloat("_DryRoughness", 0.82f);
        material.SetFloat("_WetRoughness", 0.19f);
        material.SetFloat("_WetDarkening", 0.42f);
        material.SetFloat("_WaterLevel", 0f);
        material.SetFloat("_WetReach", 0.48f);
        material.SetFloat("_TextureScale", 1.65f);
        material.SetFloat("_StrataFrequency", 13.5f);
        material.SetFloat("_CrackScale", 3.2f);
        EditorUtility.SetDirty(material);
    }

    private static void ConfigureSeabed(Material material)
    {
        material.SetColor("_SandColor", new Color(0.27f, 0.245f, 0.18f));
        material.SetColor("_RockColor", new Color(0.08f, 0.095f, 0.085f));
        material.SetColor("_CausticColor", new Color(0.34f, 0.58f, 0.48f));
        material.SetColor("_DepthTint", new Color(0.035f, 0.20f, 0.22f));
        material.SetFloat("_CausticStrength", 0.7f);
        EditorUtility.SetDirty(material);
    }

    private static GameObject BuildBuoy(Transform parent, Material red, Material white, Material metal)
    {
        var buoy = new GameObject("Extreme navigation buoy", typeof(ExtremeFloatingBuoy));
        buoy.transform.SetParent(parent);
        CreatePart("Tapered red hull", PrimitiveType.Cylinder, buoy.transform, red, new Vector3(0f, 0f, 0f), new Vector3(0.55f, 0.52f, 0.55f));
        CreatePart("Reflective white band", PrimitiveType.Cylinder, buoy.transform, white, new Vector3(0f, 0.12f, 0f), new Vector3(0.565f, 0.09f, 0.565f));
        CreatePart("Lower flotation ring", PrimitiveType.Cylinder, buoy.transform, red, new Vector3(0f, -0.43f, 0f), new Vector3(0.67f, 0.075f, 0.67f));
        CreatePart("Signal mast", PrimitiveType.Cylinder, buoy.transform, metal, new Vector3(0f, 1.05f, 0f), new Vector3(0.052f, 0.72f, 0.052f));
        CreatePart("Radar crossbar", PrimitiveType.Cube, buoy.transform, white, new Vector3(0f, 1.58f, 0f), new Vector3(0.48f, 0.065f, 0.08f));
        GameObject beacon = CreatePart("Beacon", PrimitiveType.Sphere, buoy.transform, red, new Vector3(0f, 1.82f, 0f), Vector3.one * 0.13f);
        var beaconLight = beacon.AddComponent<Light>();
        beaconLight.type = LightType.Point;
        beaconLight.range = 3.5f;
        beaconLight.intensity = 0.7f;
        beaconLight.color = new Color(1f, 0.12f, 0.035f);
        return buoy;
    }

    private static void AddRock(
        Transform parent,
        Material material,
        int seed,
        Vector3 position,
        Vector3 scale,
        Vector3 rotation,
        Vector3 proportions,
        float ruggedness)
    {
        string path = Generated + $"/ExtremeRock_{seed}.asset";
        Mesh mesh = ExtremeRockGenerator.GenerateMesh(seed, 4, proportions, ruggedness);
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            mesh.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(mesh, path);
            existing = mesh;
        }
        else
        {
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = Path.GetFileNameWithoutExtension(path);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
        }

        var rock = new GameObject($"Fractured rock {seed}", typeof(MeshFilter), typeof(MeshRenderer));
        rock.transform.SetParent(parent);
        rock.transform.position = position;
        rock.transform.localScale = scale;
        rock.transform.rotation = Quaternion.Euler(rotation);
        rock.GetComponent<MeshFilter>().sharedMesh = existing;
        MeshRenderer renderer = rock.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
    }

    private static GameObject CreatePart(string name, PrimitiveType type, Transform parent, Material material, Vector3 localPosition, Vector3 localScale)
    {
        GameObject part = CreatePrimitive(name, type, parent, material);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        return part;
    }

    private static GameObject CreatePrimitive(string name, PrimitiveType type, Transform parent, Material material)
    {
        GameObject result = GameObject.CreatePrimitive(type);
        result.name = name;
        result.transform.SetParent(parent);
        result.GetComponent<MeshRenderer>().sharedMaterial = material;
        Collider collider = result.GetComponent<Collider>();
        if (collider != null)
            Object.DestroyImmediate(collider);
        return result;
    }

    private static Material GetMaterial(string path, Shader shader)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
            material.name = Path.GetFileNameWithoutExtension(path);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void ConfigureStandard(Material material, Color color, float metallic, float smoothness)
    {
        material.color = color;
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Glossiness", smoothness);
        EditorUtility.SetDirty(material);
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }

    private static bool IsShaderReady(Shader shader, string label)
    {
        bool ready = shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader);
        if (!ready)
            Debug.LogError($"{label} shader is not ready for the active graphics API.");
        return ready;
    }

    private static void UpdateBuildSettings()
    {
        const string StandardScenePath = "Assets/Scenes/WaterComparison.unity";
        var scenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };

        bool hasStandardScene = false;
        foreach (EditorBuildSettingsScene existingScene in EditorBuildSettings.scenes)
        {
            if (existingScene.path == ScenePath)
                continue;

            if (existingScene.path == StandardScenePath)
                hasStandardScene = true;
            scenes.Add(existingScene);
        }

        if (!hasStandardScene && File.Exists(StandardScenePath))
            scenes.Add(new EditorBuildSettingsScene(StandardScenePath, true));

        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
