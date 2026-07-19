Shader "WaterComparisonExtreme/Extreme Ocean"
{
    Properties
    {
        _DeepColor ("Deep water", Color) = (0.0015, 0.018, 0.036, 1)
        _ScatteringColor ("Underwater scattering", Color) = (0.012, 0.26, 0.29, 1)
        _ShallowTint ("Shallow tint", Color) = (0.055, 0.46, 0.43, 1)
        _FoamColor ("Foam", Color) = (0.79, 0.91, 0.91, 1)
        _HorizonColor ("Atmospheric horizon", Color) = (0.30, 0.50, 0.58, 1)
        _Absorption ("Absorption RGB", Vector) = (0.34, 0.105, 0.034, 0)

        _WaveStrength ("Wave height", Range(0, 1.5)) = 1
        _Choppiness ("Choppiness", Range(0, 1.4)) = 1
        _WaveTimeScale ("Wave time scale", Range(0, 2)) = 1
        _MicroStrength ("Capillary detail", Range(0, 2)) = 1

        _Roughness ("Surface roughness", Range(0.035, 0.3)) = 0.075
        _RefractionStrength ("Refraction distortion", Range(0, 0.12)) = 0.042
        _RefractionDepthBias ("Refraction depth guard", Range(0.001, 0.5)) = 0.08
        _MaxOpticalDepth ("Maximum optical depth", Range(1, 80)) = 32
        _PlanarReflectionStrength ("Planar reflection", Range(0, 1)) = 0.92
        _ReflectionDistortion ("Reflection distortion", Range(0, 0.12)) = 0.038

        [HDR] _SunColor ("Sun radiance", Color) = (5.2, 4.25, 3.15, 1)
        _SunDirection ("Direction toward sun", Vector) = (-0.42, 0.74, -0.52, 0)

        _FoamThreshold ("Breaking compression", Range(0.03, 0.5)) = 0.145
        _FoamSoftness ("Breaking softness", Range(0.01, 0.4)) = 0.105
        _ShoreFoamDistance ("Shore foam start", Range(0.02, 3)) = 0.42
        _ShoreFoamWidth ("Shore foam width", Range(0.05, 5)) = 1.15
        _ShoreFoamStrength ("Shore foam", Range(0, 2)) = 1
    }

    SubShader
    {
        // This pass is delayed until after the opaque-scene copy, but it is itself
        // fully opaque: no alpha blending and a real depth write. The Transparent
        // RenderType also keeps the ocean out of _CameraDepthTexture, allowing that
        // texture to describe the floor and rocks behind the water.
        Tags { "Queue"="Transparent-100" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Back
        ZWrite On
        ZTest LEqual
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            float4 _DeepColor;
            float4 _ScatteringColor;
            float4 _ShallowTint;
            float4 _FoamColor;
            float4 _HorizonColor;
            float4 _Absorption;

            float _WaveStrength;
            float _Choppiness;
            float _WaveTimeScale;
            float _MicroStrength;
            float _Roughness;
            float _RefractionStrength;
            float _RefractionDepthBias;
            float _MaxOpticalDepth;
            float _PlanarReflectionStrength;
            float _ReflectionDistortion;
            float4 _SunColor;
            float4 _SunDirection;
            float _FoamThreshold;
            float _FoamSoftness;
            float _ShoreFoamDistance;
            float _ShoreFoamWidth;
            float _ShoreFoamStrength;

            // Runtime-owned globals. Keeping them out of Properties allows
            // Shader.SetGlobal* to control every water material consistently.
            sampler2D _ExtremePlanarReflectionTex;
            float4x4 _ExtremeReflectionVP;
            float4 _ExtremeReflectionUVScaleOffset;
            float4 _ExtremeReflectionResolution; // xy = pixels, zw = reciprocal pixels
            sampler2D _ExtremeOpaqueSceneTex;
            float4 _ExtremeOpaqueSceneResolution; // xy = pixels, zw = reciprocal pixels
            sampler2D _ExtremeFoamHistoryTex;
            float4 _ExtremeFoamWorldParams;        // xy = centre XZ, z = size, w = 1 / size
            float _ExtremeFoamHistoryStrength;
            float _ExtremeQualityLevel;            // runtime publishes UI quality minus one: 0/1/2
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct VertexInput
            {
                float4 vertex : POSITION;
            };

            struct FragmentInput
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 surfaceData : TEXCOORD2; // compression, local height, determinant
                float4 screenPosition : TEXCOORD3;
                float eyeDepth : TEXCOORD4;
                UNITY_FOG_COORDS(5)
                float3 basePositionWS : TEXCOORD6;
            };

            void AccumulateWave(
                float2 basePosition,
                float2 directionValue,
                float heightValue,
                float waveNumber,
                float angularFrequency,
                float chopValue,
                float phaseOffset,
                float timeValue,
                inout float3 displacedPosition,
                inout float3 tangentX,
                inout float3 tangentZ)
            {
                float phaseValue = waveNumber * dot(directionValue, basePosition)
                    + angularFrequency * timeValue + phaseOffset;
                float sineValue;
                float cosineValue;
                sincos(phaseValue, sineValue, cosineValue);

                float verticalHeight = heightValue * _WaveStrength;
                float horizontalHeight = verticalHeight * chopValue * _Choppiness;
                displacedPosition.xz += directionValue * horizontalHeight * cosineValue;
                displacedPosition.y += verticalHeight * sineValue;

                float horizontalSlope = horizontalHeight * waveNumber;
                float verticalSlope = verticalHeight * waveNumber;
                tangentX += float3(
                    -horizontalSlope * directionValue.x * directionValue.x * sineValue,
                     verticalSlope * directionValue.x * cosineValue,
                    -horizontalSlope * directionValue.x * directionValue.y * sineValue);
                tangentZ += float3(
                    -horizontalSlope * directionValue.x * directionValue.y * sineValue,
                     verticalSlope * directionValue.y * cosineValue,
                    -horizontalSlope * directionValue.y * directionValue.y * sineValue);
            }

            FragmentInput Vert(VertexInput source)
            {
                FragmentInput output;
                float3 localPosition = source.vertex.xyz;
                float3 displacedPosition = localPosition;
                float3 tangentX = float3(1.0, 0.0, 0.0);
                float3 tangentZ = float3(0.0, 0.0, 1.0);
                float timeValue = _Time.y * _WaveTimeScale;

                // A broad directional spectrum with irrationally-related wave numbers,
                // phases and headings. Its beat period is far longer than a play session,
                // so no visible loop or regular wave lattice emerges.
                AccumulateWave(localPosition.xz, float2(0.984808,  0.173648), 0.460,  0.142,  1.1804, 0.85, 0.31, timeValue, displacedPosition, tangentX, tangentZ);
                AccumulateWave(localPosition.xz, float2(0.913545,  0.406737), 0.280,  0.219,  1.4656, 0.82, 2.17, timeValue, displacedPosition, tangentX, tangentZ);
                AccumulateWave(localPosition.xz, float2(0.998630, -0.052336), 0.180,  0.337,  1.8183, 0.78, 4.83, timeValue, displacedPosition, tangentX, tangentZ);
                AccumulateWave(localPosition.xz, float2(0.819152,  0.573576), 0.115,  0.514,  2.2454, 0.72, 1.21, timeValue, displacedPosition, tangentX, tangentZ);
                AccumulateWave(localPosition.xz, float2(0.945519, -0.325568), 0.075,  0.793,  2.7890, 0.67, 5.44, timeValue, displacedPosition, tangentX, tangentZ);
                AccumulateWave(localPosition.xz, float2(0.669131,  0.743145), 0.049,  1.217,  3.4554, 0.62, 3.02, timeValue, displacedPosition, tangentX, tangentZ);
                AccumulateWave(localPosition.xz, float2(0.990268,  0.139173), 0.032,  1.873,  4.2867, 0.56, 0.77, timeValue, displacedPosition, tangentX, tangentZ);
                AccumulateWave(localPosition.xz, float2(0.559193, -0.829038), 0.021,  2.879,  5.3144, 0.50, 4.11, timeValue, displacedPosition, tangentX, tangentZ);
                AccumulateWave(localPosition.xz, float2(0.874620,  0.484810), 0.014,  4.423,  6.5870, 0.44, 2.69, timeValue, displacedPosition, tangentX, tangentZ);
                AccumulateWave(localPosition.xz, float2(0.743145, -0.669131), 0.009,  6.791,  8.1620, 0.38, 5.91, timeValue, displacedPosition, tangentX, tangentZ);
                AccumulateWave(localPosition.xz, float2(0.406737,  0.913545), 0.006, 10.430, 10.1150, 0.32, 1.72, timeValue, displacedPosition, tangentX, tangentZ);
                AccumulateWave(localPosition.xz, float2(0.965926, -0.258819), 0.004, 16.020, 12.5360, 0.28, 3.63, timeValue, displacedPosition, tangentX, tangentZ);

                // The determinant is the exact horizontal Jacobian of the complete
                // Gerstner mapping. Compression, rather than wave height alone, drives
                // breaking foam and remains stable when wave strength is retuned.
                float determinantValue = tangentX.x * tangentZ.z - tangentX.z * tangentZ.x;
                float compressionValue = max(1.0 - determinantValue, 0.0);
                float3 localNormal = normalize(cross(tangentZ, tangentX));

                float3 worldPosition = mul(unity_ObjectToWorld, float4(displacedPosition, 1.0)).xyz;
                float4 clipPosition = UnityWorldToClipPos(worldPosition);
                output.positionCS = clipPosition;
                output.positionWS = worldPosition;
                output.normalWS = UnityObjectToWorldNormal(localNormal);
                output.surfaceData = float3(compressionValue, displacedPosition.y, determinantValue);
                output.screenPosition = ComputeScreenPos(clipPosition);
                output.eyeDepth = -mul(UNITY_MATRIX_V, float4(worldPosition, 1.0)).z;
                output.basePositionWS = mul(unity_ObjectToWorld, source.vertex).xyz;
                UNITY_TRANSFER_FOG(output, clipPosition);
                return output;
            }

            float Hash21(float2 coordinates)
            {
                float3 hashed = frac(float3(coordinates.xyx) * float3(0.1031, 0.1030, 0.0973));
                hashed += dot(hashed, hashed.yzx + 33.33);
                return frac((hashed.x + hashed.y) * hashed.z);
            }

            float ValueNoise(float2 coordinates)
            {
                float2 cellValue = floor(coordinates);
                float2 localValue = frac(coordinates);
                localValue = localValue * localValue * (3.0 - 2.0 * localValue);
                float lowerValue = lerp(Hash21(cellValue), Hash21(cellValue + float2(1.0, 0.0)), localValue.x);
                float upperValue = lerp(Hash21(cellValue + float2(0.0, 1.0)), Hash21(cellValue + 1.0), localValue.x);
                return lerp(lowerValue, upperValue, localValue.y);
            }

            float Pow5(float value)
            {
                float squaredValue = value * value;
                return squaredValue * squaredValue * value;
            }

            float2 EvaluateMicroSlope(float2 worldPosition, float3 macroNormal, float qualityValue)
            {
                float2 warpedPosition = worldPosition + macroNormal.xz * 1.7;
                float timeValue = _Time.y * _WaveTimeScale;
                float footprint = max(length(ddx(worldPosition)), length(ddy(worldPosition)));

                float4 phaseA = float4(
                    dot(warpedPosition, float2( 2.61,  1.03)) + timeValue * 2.03,
                    dot(warpedPosition, float2(-1.91,  3.71)) + timeValue * 2.77 + 1.31,
                    dot(warpedPosition, float2( 4.79, -3.13)) + timeValue * 3.61 + 4.19,
                    dot(warpedPosition, float2( 1.43,  7.97)) + timeValue * 4.83 + 2.37);
                float4 signalA = cos(phaseA);
                float fadeA = saturate(1.0 - footprint * 4.5);
                float2 slopeValue = float2(
                    dot(signalA, float4(0.035, -0.018,  0.022, 0.006)),
                    dot(signalA, float4(0.014,  0.032, -0.014, 0.025))) * fadeA;

                [branch]
                if (qualityValue > 1.5)
                {
                    float4 phaseB = float4(
                        dot(warpedPosition, float2( 8.37,   2.53)) + timeValue * 6.11 + 0.73,
                        dot(warpedPosition, float2(-5.17,  10.11)) + timeValue * 7.43 + 3.81,
                        dot(warpedPosition, float2(13.70,  -8.30)) + timeValue * 9.17 + 5.27,
                        dot(warpedPosition, float2( 3.90,  17.30)) + timeValue * 11.31 + 1.83);
                    float4 signalB = cos(phaseB);
                    float fadeB = saturate(1.0 - footprint * 11.0);
                    slopeValue += float2(
                        dot(signalB, float4(0.012, -0.007,  0.009, 0.003)),
                        dot(signalB, float4(0.004,  0.009, -0.006, 0.008))) * fadeB;
                }

                return slopeValue * _MicroStrength;
            }

            float3 SampleAtmosphere(float3 reflectionDirection, float3 sunDirection)
            {
                float skyHeight = saturate(reflectionDirection.y * 0.5 + 0.5);
                float horizonBlend = pow(saturate(reflectionDirection.y), 0.36);
                float3 upperSky = lerp(_HorizonColor.rgb, float3(0.012, 0.055, 0.125), horizonBlend);
                float3 lowerSky = lerp(float3(0.018, 0.030, 0.028), _HorizonColor.rgb * 0.72, skyHeight);
                float3 skyValue = lerp(lowerSky, upperSky, smoothstep(0.42, 0.58, skyHeight));
                float sunDisc = pow(saturate(dot(reflectionDirection, sunDirection)), 720.0);
                float sunGlow = pow(saturate(dot(reflectionDirection, sunDirection)), 18.0);
                return skyValue + _SunColor.rgb * (sunDisc * 0.85 + sunGlow * 0.016);
            }

            float3 SampleEnvironment(float3 reflectionDirection, float roughnessValue, float3 sunDirection)
            {
                float mipLevel = roughnessValue * (1.7 - 0.7 * roughnessValue) * 6.0;
                float4 encodedProbe = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, reflectionDirection, mipLevel);
                float3 probeValue = DecodeHDR(encodedProbe, unity_SpecCube0_HDR);
                return lerp(SampleAtmosphere(reflectionDirection, sunDirection), probeValue, 0.58);
            }

            float3 EvaluateGGX(
                float3 surfaceNormal,
                float3 viewDirection,
                float3 lightDirection,
                float roughnessValue,
                float3 radianceValue)
            {
                float normalView = max(dot(surfaceNormal, viewDirection), 0.001);
                float normalLight = saturate(dot(surfaceNormal, lightDirection));
                float3 halfDirection = normalize(viewDirection + lightDirection);
                float normalHalf = saturate(dot(surfaceNormal, halfDirection));
                float viewHalf = saturate(dot(viewDirection, halfDirection));

                float alphaValue = max(roughnessValue * roughnessValue, 0.0025);
                float alphaSquared = alphaValue * alphaValue;
                float denominatorValue = normalHalf * normalHalf * (alphaSquared - 1.0) + 1.0;
                float distributionValue = alphaSquared / max(UNITY_PI * denominatorValue * denominatorValue, 0.0001);

                float geometryK = roughnessValue + 1.0;
                geometryK = geometryK * geometryK * 0.125;
                float geometryView = normalView / (normalView * (1.0 - geometryK) + geometryK);
                float geometryLight = normalLight / (normalLight * (1.0 - geometryK) + geometryK);
                float3 fresnelValue = 0.02037 + (1.0 - 0.02037) * Pow5(1.0 - viewHalf);
                float3 specularValue = distributionValue * geometryView * geometryLight * fresnelValue
                    / max(4.0 * normalView * max(normalLight, 0.001), 0.001);
                return min(specularValue * normalLight * radianceValue, 32.0);
            }

            float SampleSceneEyeDepth(float2 screenUV)
            {
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, saturate(screenUV));
                return LinearEyeDepth(rawDepth);
            }

            float ScreenBoundsMask(float2 screenUV, float2 reciprocalResolution)
            {
                float2 minimumUV = max(reciprocalResolution * 1.5, 0.0001);
                float2 insideLow = step(minimumUV, screenUV);
                float2 insideHigh = step(screenUV, 1.0 - minimumUV);
                return insideLow.x * insideLow.y * insideHigh.x * insideHigh.y;
            }

            float3 SamplePlanarReflection(
                FragmentInput input,
                float3 surfaceNormal,
                float3 environmentValue,
                float roughnessValue,
                float qualityValue)
            {
                // Metal's data-flow validator can reject an early-return branch in a
                // helper containing a texture LOD sample as "potentially uninitialized".
                // Start from the guaranteed environment fallback and assign exactly once
                // inside the reflection branch instead.
                float3 resultValue = environmentValue;
                [branch]
                if (qualityValue >= 0.5 && _PlanarReflectionStrength > 0.001)
                {
                    float4 reflectionClip = mul(_ExtremeReflectionVP, float4(input.positionWS, 1.0));
                    float inverseW = rcp(max(abs(reflectionClip.w), 0.0001));
                    float2 reflectionUV = reflectionClip.xy * inverseW;
                    reflectionUV = reflectionUV * _ExtremeReflectionUVScaleOffset.xy
                        + _ExtremeReflectionUVScaleOffset.zw;

                    float3 normalVS = mul((float3x3)UNITY_MATRIX_V, surfaceNormal);
                    float distanceFade = rcp(max(input.eyeDepth * 0.055, 1.0));
                    reflectionUV += normalVS.xy * (_ReflectionDistortion * distanceFade);
                    float validValue = step(0.0001, reflectionClip.w)
                        * ScreenBoundsMask(reflectionUV, max(_ExtremeReflectionResolution.zw, 0.0001));
                    float4 reflectionSample = tex2Dlod(
                        _ExtremePlanarReflectionTex,
                        float4(saturate(reflectionUV), 0.0, roughnessValue * 4.0));
                    float blendValue = validValue * _PlanarReflectionStrength;
                    resultValue = lerp(environmentValue, reflectionSample.rgb, blendValue);
                }
                return resultValue;
            }

            float SampleFoamHistory(float2 worldPosition)
            {
                float2 historyUV = (worldPosition - _ExtremeFoamWorldParams.xy)
                    * _ExtremeFoamWorldParams.w + 0.5;
                float2 edgeLow = smoothstep(0.0, 0.025, historyUV);
                float2 edgeHigh = smoothstep(0.0, 0.025, 1.0 - historyUV);
                float edgeMask = edgeLow.x * edgeLow.y * edgeHigh.x * edgeHigh.y;
                return tex2D(_ExtremeFoamHistoryTex, saturate(historyUV)).r * edgeMask;
            }

            float4 Frag(FragmentInput input) : SV_Target
            {
                float qualityValue = _ExtremeQualityLevel;
                float3 macroNormal = normalize(input.normalWS);
                float2 microSlope = EvaluateMicroSlope(input.positionWS.xz, macroNormal, qualityValue);
                float3 surfaceNormal = normalize(macroNormal - float3(microSlope.x, 0.0, microSlope.y));
                float3 viewDirection = normalize(_WorldSpaceCameraPos - input.positionWS);
                float3 sunDirection = normalize(_SunDirection.xyz);
                float normalView = saturate(dot(surfaceNormal, viewDirection));
                float fresnelScalar = 0.02037 + (1.0 - 0.02037) * Pow5(1.0 - normalView);

                float2 screenUV = input.screenPosition.xy / max(input.screenPosition.w, 0.0001);
                float sceneEyeDepth = SampleSceneEyeDepth(screenUV);
                float undistortedThickness = max(sceneEyeDepth - input.eyeDepth, 0.0);

                float3 normalVS = mul((float3x3)UNITY_MATRIX_V, surfaceNormal);
                float depthDistortionFade = saturate(undistortedThickness * 0.7);
                float distanceAttenuation = rcp(max(input.eyeDepth * 0.045, 1.0));
                float2 refractionOffset = normalVS.xy * _RefractionStrength
                    * depthDistortionFade * distanceAttenuation;
                float2 candidateUV = screenUV + refractionOffset;
                float candidateEyeDepth = SampleSceneEyeDepth(candidateUV);
                float candidateValid = step(input.eyeDepth + _RefractionDepthBias, candidateEyeDepth)
                    * ScreenBoundsMask(candidateUV, max(_ExtremeOpaqueSceneResolution.zw, 0.0001));
                [branch]
                if (qualityValue < 0.5)
                {
                    candidateValid = 0.0;
                }
                float2 refractionUV = lerp(screenUV, candidateUV, candidateValid);
                float opticalDepth = min(lerp(sceneEyeDepth, candidateEyeDepth, candidateValid) - input.eyeDepth, _MaxOpticalDepth);
                opticalDepth = max(opticalDepth, 0.0);

                float3 sceneColor = tex2D(_ExtremeOpaqueSceneTex, saturate(refractionUV)).rgb;
                float3 transmissionValue = exp2(-max(_Absorption.rgb, 0.0001) * opticalDepth * 1.442695);
                float lightFacing = saturate(dot(surfaceNormal, sunDirection));
                float3 scatteredValue = lerp(_DeepColor.rgb, _ScatteringColor.rgb, lightFacing * 0.34);
                scatteredValue += _ShallowTint.rgb * exp2(-opticalDepth * 0.72) * lightFacing * 0.12;
                float3 refractedValue = sceneColor * transmissionValue + scatteredValue * (1.0 - transmissionValue);

                float roughnessValue = saturate(_Roughness + length(microSlope) * 0.16);
                float3 reflectionDirection = reflect(-viewDirection, surfaceNormal);
                float3 environmentValue = SampleEnvironment(reflectionDirection, roughnessValue, sunDirection);
                float3 reflectionValue = SamplePlanarReflection(
                    input, surfaceNormal, environmentValue, roughnessValue, qualityValue);
                float3 finalColor = lerp(refractedValue, reflectionValue, fresnelScalar);
                finalColor += EvaluateGGX(surfaceNormal, viewDirection, sunDirection, roughnessValue, _SunColor.rgb);

                // A restrained forward-scattering rim gives thin, back-lit wave crests
                // volume without turning the whole surface into translucent plastic.
                float forwardScatter = Pow5(saturate(dot(viewDirection, -sunDirection) * 0.5 + 0.5));
                float crestValue = smoothstep(0.10, 0.75, input.surfaceData.y);
                finalColor += _ShallowTint.rgb * forwardScatter * crestValue * (1.0 - normalView) * 0.18;

                float freshFoam = smoothstep(
                    _FoamThreshold,
                    _FoamThreshold + max(_FoamSoftness, 0.001),
                    input.surfaceData.x);
                freshFoam *= smoothstep(0.04, 0.62, input.surfaceData.y);

                const float2 dominantWind = float2(0.984808, 0.173648);
                const float2 crossWind = float2(-0.173648, 0.984808);
                float windCoordinate = dot(input.positionWS.xz, dominantWind);
                float crossCoordinate = dot(input.positionWS.xz, crossWind);
                float coarseNoise = ValueNoise(float2(windCoordinate * 0.31, crossCoordinate * 1.37)
                    + float2(_Time.y * 0.055, -_Time.y * 0.018));
                float fineNoise = ValueNoise(float2(windCoordinate * 1.13, crossCoordinate * 4.73)
                    + float2(_Time.y * 0.13, _Time.y * 0.031));
                float breakupValue = saturate(0.23 + coarseNoise * 0.55 + fineNoise * 0.31);
                freshFoam *= breakupValue;

                float historyFoam = 0.0;
                [branch]
                if (qualityValue > 0.5)
                {
                    historyFoam = SampleFoamHistory(input.basePositionWS.xz) * _ExtremeFoamHistoryStrength;
                }

                // Camera-depth thickness is zero at rock/water and beach/water contacts.
                // Two moving noise scales split the band into natural tongues rather
                // than a perfectly uniform procedural outline.
                float shoreNoise = ValueNoise(input.positionWS.xz * 0.72
                    + float2(_Time.y * 0.08, -_Time.y * 0.05));
                float shoreDetail = ValueNoise(input.positionWS.xz * 2.43
                    + float2(-_Time.y * 0.13, _Time.y * 0.07));
                float irregularDistance = max(
                    _ShoreFoamDistance + (shoreNoise - 0.5) * _ShoreFoamWidth * 0.42,
                    0.015);
                float shoreBand = 1.0 - smoothstep(
                    irregularDistance,
                    irregularDistance + max(_ShoreFoamWidth, 0.001),
                    undistortedThickness);
                // Keep the exact contact line translucent, then break the outer edge
                // into moving lace. This avoids the solid white "icing" ring that a
                // simple depth threshold creates around rocks.
                shoreBand *= smoothstep(0.025, 0.16, undistortedThickness);
                shoreBand *= smoothstep(0.34, 0.72, shoreNoise * 0.64 + shoreDetail * 0.36 + shoreBand * 0.20);
                shoreBand *= _ShoreFoamStrength;

                float crestCoverage = saturate(max(freshFoam, historyFoam * 0.82));
                float foamCoverage = saturate(crestCoverage + shoreBand * 0.72);
                float foamLight = 0.54 + lightFacing * 0.38;
                float3 foamValue = _FoamColor.rgb * foamLight + reflectionValue * 0.10;
                foamValue += EvaluateGGX(
                    surfaceNormal, viewDirection, sunDirection, 0.31, _SunColor.rgb * 0.06);
                float foamOpacity = saturate(crestCoverage * 0.82 + shoreBand * 0.56);
                finalColor = lerp(finalColor, foamValue, foamOpacity);

                UNITY_APPLY_FOG(input.fogCoord, finalColor);
                return float4(finalColor, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
