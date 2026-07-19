Shader "WaterComparisonExtreme/Weathered Shore Rock"
{
    Properties
    {
        _BaseColor ("Dry basalt", Color) = (0.125, 0.115, 0.105, 1)
        _SecondaryColor ("Mineral variation", Color) = (0.255, 0.225, 0.180, 1)
        _StrataColor ("Strata and fissures", Color) = (0.040, 0.036, 0.032, 1)
        _WetTint ("Wet rock tint", Color) = (0.018, 0.031, 0.032, 1)
        _DryRoughness ("Dry roughness", Range(0.35, 1)) = 0.82
        _WetRoughness ("Wet roughness", Range(0.06, 0.55)) = 0.19
        _WetDarkening ("Wet darkening", Range(0.15, 0.8)) = 0.42
        _WaterLevel ("World water level", Float) = 0
        _WetReach ("Splash reach", Range(0.05, 1.5)) = 0.48
        _WetTransition ("Submerged transition", Range(0.02, 0.5)) = 0.12
        [Enum(Object,0,World,1)] _TextureSpace ("Procedural texture space", Float) = 0
        _TextureScale ("Rock texture scale", Range(0.2, 8)) = 1.65
        _StrataFrequency ("Strata frequency", Range(2, 32)) = 13.5
        _CrackScale ("Crack scale", Range(0.5, 12)) = 3.2
        _CrackWidth ("Crack width", Range(0.008, 0.12)) = 0.038
        _MineralStrength ("Mineral variation", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 350
        Cull Back
        ZWrite On

        CGPROGRAM
        #pragma target 3.5
        #pragma surface SurfaceRock Standard fullforwardshadows addshadow
        #pragma multi_compile_instancing

        #include "UnityCG.cginc"

        fixed4 _BaseColor;
        fixed4 _SecondaryColor;
        fixed4 _StrataColor;
        fixed4 _WetTint;
        half _DryRoughness;
        half _WetRoughness;
        half _WetDarkening;
        float _WaterLevel;
        half _WetReach;
        half _WetTransition;
        half _TextureSpace;
        half _TextureScale;
        half _StrataFrequency;
        half _CrackScale;
        half _CrackWidth;
        half _MineralStrength;
        float _ExtremeQualityLevel;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        float Hash31(float3 coordinates)
        {
            coordinates = frac(coordinates * 0.1031);
            coordinates += dot(coordinates, coordinates.yzx + 33.33);
            return frac((coordinates.x + coordinates.y) * coordinates.z);
        }

        float ValueNoise3D(float3 coordinates)
        {
            float3 cell = floor(coordinates);
            float3 local = frac(coordinates);
            local = local * local * local * (local * (local * 6.0 - 15.0) + 10.0);

            float lowerNorth = lerp(Hash31(cell), Hash31(cell + float3(1, 0, 0)), local.x);
            float lowerSouth = lerp(Hash31(cell + float3(0, 1, 0)), Hash31(cell + float3(1, 1, 0)), local.x);
            float upperNorth = lerp(Hash31(cell + float3(0, 0, 1)), Hash31(cell + float3(1, 0, 1)), local.x);
            float upperSouth = lerp(Hash31(cell + float3(0, 1, 1)), Hash31(cell + float3(1, 1, 1)), local.x);
            float lower = lerp(lowerNorth, lowerSouth, local.y);
            float upper = lerp(upperNorth, upperSouth, local.y);
            return lerp(lower, upper, local.z);
        }

        float RockFbm(float3 coordinates)
        {
            float value = ValueNoise3D(coordinates) * 0.5333;
            coordinates = float3(
                coordinates.z * 1.91 + coordinates.x * 0.23,
                coordinates.x * -1.73 + coordinates.y * 0.31,
                coordinates.y * 1.87 - coordinates.z * 0.19);
            value += ValueNoise3D(coordinates) * 0.2667;
            coordinates = coordinates * 2.07 + float3(1.7, -2.1, 0.8);
            value += ValueNoise3D(coordinates) * 0.1333;
            coordinates = coordinates * 2.11 + float3(-0.6, 1.3, 2.4);
            value += ValueNoise3D(coordinates) * 0.0667;
            return value;
        }

        float ContourCrack(float fieldValue, float width)
        {
            // An iso-contour through coherent noise creates long, branching fissures.
            float contourDistance = abs(fieldValue - 0.5);
            return 1.0 - smoothstep(width, width * 2.8, contourDistance);
        }

        void SurfaceRock(Input rockInput, inout SurfaceOutputStandard surface)
        {
            float3 objectPosition = mul(unity_WorldToObject, float4(rockInput.worldPos, 1.0)).xyz;
            float3 objectCoordinates = objectPosition * _TextureScale;
            float3 worldCoordinates = rockInput.worldPos * _TextureScale;
            float3 materialCoordinates = lerp(objectCoordinates, worldCoordinates, saturate(_TextureSpace));

            float broadStone = RockFbm(materialCoordinates * 0.58 + float3(2.1, -0.7, 3.4));
            float mediumStone = ValueNoise3D(materialCoordinates * 1.83 + float3(-4.7, 1.9, 0.6));
            [branch]
            if (_ExtremeQualityLevel > 0.5)
                mediumStone = RockFbm(materialCoordinates * 1.83 + float3(-4.7, 1.9, 0.6));
            float mineralGrain = ValueNoise3D(materialCoordinates * 8.7 + float3(7.3, 2.8, -5.1));

            // Layers bend with the low frequency rock field; they are not perfect rings.
            float strataPhase = objectPosition.y * _StrataFrequency
                + (broadStone - 0.5) * 3.4
                + (mediumStone - 0.5) * 0.72;
            float strataDistance = abs(sin(strataPhase));
            float strataCrease = 1.0 - smoothstep(0.055, 0.26, strataDistance);
            float sedimentBand = saturate(sin(strataPhase * 0.47 + 1.2) * 0.5 + 0.5);

            float crackFieldA = RockFbm(materialCoordinates * _CrackScale + float3(11.7, -3.1, 5.8));
            float crackFieldB = 0.5;
            [branch]
            if (_ExtremeQualityLevel > 1.5)
                crackFieldB = RockFbm(materialCoordinates.yzx * (_CrackScale * 1.67) + float3(-6.2, 9.4, 1.7));
            float crackA = ContourCrack(crackFieldA, _CrackWidth);
            float crackB = ContourCrack(crackFieldB, _CrackWidth * 0.62);
            float fissures = saturate(max(crackA, crackB * 0.72) + strataCrease * 0.38);

            float colorVariation = saturate(
                (broadStone * 0.62 + mediumStone * 0.28 + mineralGrain * 0.10 - 0.36)
                * 1.65);
            colorVariation = saturate(colorVariation * _MineralStrength + sedimentBand * 0.17);
            float3 dryColor = lerp(_BaseColor.rgb, _SecondaryColor.rgb, colorVariation);
            dryColor *= lerp(0.84, 1.08, broadStone);
            dryColor = lerp(dryColor, _StrataColor.rgb, saturate(fissures * 0.78));

            // Low frequency variation breaks the mathematically straight waterline.
            float waterlineNoise = (RockFbm(rockInput.worldPos * 0.72 + float3(3.7, 8.1, -2.4)) - 0.5)
                * _WetReach * 0.32;
            float signedWaterDistance = rockInput.worldPos.y - (_WaterLevel + waterlineNoise);
            float wetness = 1.0 - smoothstep(-_WetTransition, _WetReach, signedWaterDistance);
            float splashBand = 1.0 - smoothstep(0.025, _WetReach * 0.72 + 0.025, abs(signedWaterDistance));
            wetness = saturate(max(wetness, splashBand * (0.32 + mediumStone * 0.30)));

            float3 darkenedWetRock = dryColor * _WetDarkening + _WetTint.rgb * 0.32;
            float3 finalAlbedo = lerp(dryColor, darkenedWetRock, wetness);
            finalAlbedo *= lerp(1.0, 0.72, fissures * (1.0 - wetness * 0.45));

            float grainRoughness = (mineralGrain - 0.5) * 0.10;
            float roughness = lerp(_DryRoughness, _WetRoughness, wetness) + grainRoughness;
            roughness = lerp(roughness, lerp(0.94, 0.25, wetness), fissures * 0.62);

            // Standard's metallic workflow uses the physically appropriate dielectric
            // Fresnel response when Metallic is exactly zero. Wetness changes roughness,
            // not metalness or an artificial specular colour.
            surface.Albedo = max(finalAlbedo, 0.0);
            surface.Metallic = 0.0;
            surface.Smoothness = 1.0 - saturate(roughness);
            surface.Occlusion = lerp(1.0, 0.58, saturate(fissures * 0.82 + strataCrease * 0.12));
            surface.Emission = 0.0;
            surface.Alpha = 1.0;
        }
        ENDCG
    }

    FallBack "Standard"
}
