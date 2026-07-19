Shader "WaterComparison/Spectral Ocean"
{
    Properties
    {
        _DeepColor ("Deep water", Color) = (0.002, 0.025, 0.05, 1)
        _MidColor ("Subsurface scattering", Color) = (0.008, 0.20, 0.28, 1)
        _ShallowColor ("Sunlit scattering", Color) = (0.05, 0.48, 0.52, 1)
        _FoamColor ("Foam", Color) = (0.76, 0.92, 0.92, 1)
        _FogColor ("Horizon", Color) = (0.44, 0.62, 0.68, 1)
        _Absorption ("Absorption RGB", Vector) = (0.42, 0.12, 0.045, 0)
        _SunDirection ("Sun direction", Vector) = (-0.48, 0.54, -0.69, 0)
        [HDR] _SunColor ("Sun radiance", Color) = (4.8, 3.5, 2.4, 1)
        _WaveStrength ("Wave strength", Range(0, 1.5)) = 1
        _MicroStrength ("Micro wave strength", Range(0, 2)) = 1
        _Roughness ("Water roughness", Range(0.035, 0.3)) = 0.09
        _FoamThreshold ("Breaking threshold", Range(0.05, 0.3)) = 0.13
        [NoScaleOffset] _ReflectionTex ("Optional planar reflection", 2D) = "black" {}
        _ReflectionStrength ("Planar reflection strength", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Geometry+20" "RenderType"="Opaque" }
        Cull Back
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            float4 _DeepColor;
            float4 _MidColor;
            float4 _ShallowColor;
            float4 _FoamColor;
            float4 _FogColor;
            float4 _Absorption;
            float4 _SunDirection;
            float4 _SunColor;
            float _WaveStrength;
            float _MicroStrength;
            float _Roughness;
            float _FoamThreshold;
            sampler2D _ReflectionTex;
            float _ReflectionStrength;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 surfaceData : TEXCOORD2;
                float4 projectedPosition : TEXCOORD3;
                UNITY_FOG_COORDS(4)
            };

            // A compact directional spectrum. Wave numbers and angular frequencies are
            // precomputed on the CPU side of the design, avoiding sqrt/divide per vertex.
            void AccumulateSpectralWave(
                float2 surfacePosition,
                float2 waveDirection,
                float waveAmplitude,
                float waveNumber,
                float angularFrequency,
                float waveChop,
                float phaseOffset,
                inout float3 displaced,
                inout float3 derivativeX,
                inout float3 derivativeZ)
            {
                float phaseValue = waveNumber * dot(waveDirection, surfacePosition)
                    + angularFrequency * _Time.y + phaseOffset;
                float sineValue;
                float cosineValue;
                sincos(phaseValue, sineValue, cosineValue);

                float amplitudeValue = waveAmplitude * _WaveStrength;
                float horizontalAmplitude = amplitudeValue * waveChop;
                displaced.xz += horizontalAmplitude * waveDirection * cosineValue;
                displaced.y += amplitudeValue * sineValue;

                float horizontalSlope = horizontalAmplitude * waveNumber;
                float verticalSlope = amplitudeValue * waveNumber;
                derivativeX += float3(
                    -horizontalSlope * waveDirection.x * waveDirection.x * sineValue,
                    verticalSlope * waveDirection.x * cosineValue,
                    -horizontalSlope * waveDirection.x * waveDirection.y * sineValue);
                derivativeZ += float3(
                    -horizontalSlope * waveDirection.x * waveDirection.y * sineValue,
                    verticalSlope * waveDirection.y * cosineValue,
                    -horizontalSlope * waveDirection.y * waveDirection.y * sineValue);
            }

            v2f vert(appdata sourceVertex)
            {
                v2f output;
                float3 localPosition = sourceVertex.vertex.xyz;
                float3 displaced = localPosition;
                float3 derivativeX = float3(1.0, 0.0, 0.0);
                float3 derivativeZ = float3(0.0, 0.0, 1.0);

                // Two long swells carry most of the energy.
                AccumulateSpectralWave(localPosition.xz, float2(0.9563, 0.2924), 0.420, 0.25438, 1.579, 0.72, 0.37, displaced, derivativeX, derivativeZ);
                AccumulateSpectralWave(localPosition.xz, float2(0.8462, 0.5329), 0.260, 0.38547, 1.944, 0.62, 2.13, displaced, derivativeX, derivativeZ);

                // The swell's horizontal motion bends the four shorter bands. This cheap
                // domain warp breaks the obvious tiled beat pattern without adding waves.
                float2 lowBandOffset = displaced.xz - localPosition.xz;
                float2 warpedPosition = localPosition.xz + lowBandOffset * 1.35;
                AccumulateSpectralWave(warpedPosition, float2(0.9959, -0.0905), 0.160, 0.57644, 2.378, 0.56, 4.71, displaced, derivativeX, derivativeZ);
                AccumulateSpectralWave(warpedPosition, float2(0.6718, 0.7407), 0.095, 0.88496, 2.947, 0.50, 1.29, displaced, derivativeX, derivativeZ);
                AccumulateSpectralWave(warpedPosition, float2(0.9231, -0.3846), 0.052, 1.46121, 3.785, 0.42, 5.53, displaced, derivativeX, derivativeZ);
                AccumulateSpectralWave(warpedPosition, float2(0.4307, 0.9025), 0.025, 2.37000, 4.821, 0.32, 3.34, displaced, derivativeX, derivativeZ);

                float3 localNormal = normalize(cross(derivativeZ, derivativeX));
                float horizontalJacobian = derivativeX.x * derivativeZ.z - derivativeX.z * derivativeZ.x;
                float compression = max(1.0 - horizontalJacobian, 0.0);
                float breaking = smoothstep(_FoamThreshold + 0.02, _FoamThreshold + 0.10, compression);
                breaking *= smoothstep(0.14, 0.52, displaced.y);

                float3 worldPosition = mul(unity_ObjectToWorld, float4(displaced, 1.0)).xyz;
                float4 clipPosition = UnityWorldToClipPos(worldPosition);
                output.positionCS = clipPosition;
                output.positionWS = worldPosition;
                output.normalWS = UnityObjectToWorldNormal(localNormal);
                output.surfaceData = float2(breaking, displaced.y);
                output.projectedPosition = ComputeScreenPos(clipPosition);
                UNITY_TRANSFER_FOG(output, clipPosition);
                return output;
            }

            float Hash21(float2 coordinates)
            {
                coordinates = frac(coordinates * float2(123.34, 456.21));
                coordinates += dot(coordinates, coordinates + 45.32);
                return frac(coordinates.x * coordinates.y);
            }

            float ValueNoise(float2 coordinates)
            {
                float2 cellCoordinates = floor(coordinates);
                float2 localCoordinates = frac(coordinates);
                localCoordinates = localCoordinates * localCoordinates * (3.0 - 2.0 * localCoordinates);
                float lowerValue = lerp(Hash21(cellCoordinates), Hash21(cellCoordinates + float2(1.0, 0.0)), localCoordinates.x);
                float upperValue = lerp(Hash21(cellCoordinates + float2(0.0, 1.0)), Hash21(cellCoordinates + float2(1.0, 1.0)), localCoordinates.x);
                return lerp(lowerValue, upperValue, localCoordinates.y);
            }

            float Pow5(float value)
            {
                float valueSquared = value * value;
                return valueSquared * valueSquared * value;
            }

            float3 SampleAtmosphere(float3 reflectionDirection, float3 sunDirection)
            {
                float verticalValue = saturate(reflectionDirection.y);
                float horizonBlend = pow(verticalValue, 0.42);
                float3 upperSky = lerp(_FogColor.rgb * 0.72, float3(0.015, 0.065, 0.135), horizonBlend);
                float lowerBlend = saturate(-reflectionDirection.y * 2.5);
                float3 atmosphereColor = lerp(upperSky, float3(0.035, 0.055, 0.052), lowerBlend);

                float2 horizontalReflection = normalize(reflectionDirection.xz + float2(0.0001, 0.0001));
                float2 horizontalSun = normalize(sunDirection.xz + float2(0.0001, 0.0001));
                float sunHorizon = Pow5(saturate(dot(horizontalReflection, horizontalSun)));
                float horizonBand = Pow5(saturate(1.0 - abs(reflectionDirection.y)));
                return atmosphereColor + sunHorizon * horizonBand * float3(0.13, 0.062, 0.022);
            }

            float3 SampleReflection(float3 reflectionDirection, float roughnessValue, float3 sunDirection)
            {
                float perceptualRoughness = roughnessValue * (1.7 - 0.7 * roughnessValue);
                float mipLevel = perceptualRoughness * 6.0;
                float4 encodedProbe = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, reflectionDirection, mipLevel);
                float3 probeColor = DecodeHDR(encodedProbe, unity_SpecCube0_HDR);
                float3 atmosphereColor = SampleAtmosphere(reflectionDirection, sunDirection);
                return lerp(atmosphereColor, probeColor, 0.62);
            }

            float EvaluateSunSpecular(
                float3 surfaceNormal,
                float3 viewDirection,
                float3 lightDirection,
                float roughnessValue,
                out float lightFacing)
            {
                float viewFacing = max(dot(surfaceNormal, viewDirection), 0.001);
                lightFacing = saturate(dot(surfaceNormal, lightDirection));
                float3 halfDirection = normalize(viewDirection + lightDirection);
                float normalHalf = saturate(dot(surfaceNormal, halfDirection));
                float viewHalf = saturate(dot(viewDirection, halfDirection));

                float alphaValue = max(roughnessValue * roughnessValue, 0.0025);
                float alphaSquared = alphaValue * alphaValue;
                float denominatorValue = normalHalf * normalHalf * (alphaSquared - 1.0) + 1.0;
                float distribution = alphaSquared / max(UNITY_PI * denominatorValue * denominatorValue, 0.0001);

                float geometryK = roughnessValue + 1.0;
                geometryK = geometryK * geometryK * 0.125;
                float geometryView = viewFacing / (viewFacing * (1.0 - geometryK) + geometryK);
                float geometryLight = lightFacing / (lightFacing * (1.0 - geometryK) + geometryK);
                float fresnelValue = 0.02037 + (1.0 - 0.02037) * Pow5(1.0 - viewHalf);
                float specularValue = distribution * geometryView * geometryLight * fresnelValue
                    / max(4.0 * viewFacing * lightFacing, 0.001);
                return min(specularValue * lightFacing, 18.0);
            }

            float4 frag(v2f input) : SV_Target
            {
                float3 sunDirection = normalize(_SunDirection.xyz);
                float3 viewDirection = normalize(_WorldSpaceCameraPos - input.positionWS);

                // Four packed, incommensurate micro bands. Macro slope warps their domain,
                // hiding straight sinusoidal tracks while keeping the cost at four cosines.
                float2 detailPosition = input.positionWS.xz + input.normalWS.xz * 1.65;
                float4 detailPhase = float4(
                    dot(detailPosition, float2(3.73, 1.51)) + _Time.y * 2.15,
                    dot(detailPosition, float2(-2.17, 4.93)) + _Time.y * 2.83 + 1.70,
                    dot(detailPosition, float2(6.11, -5.07)) + _Time.y * 4.27 + 4.20,
                    dot(detailPosition, float2(2.27, 9.61)) + _Time.y * 5.63 + 2.60);
                float4 microSignal = cos(detailPhase);
                float2 microSlope = float2(
                    dot(microSignal, float4(0.0649, -0.0210, 0.0254, 0.0041)),
                    dot(microSignal, float4(0.0263, 0.0476, -0.0211, 0.0175)));
                microSlope *= _MicroStrength;

                float3 surfaceNormal = normalize(normalize(input.normalWS) - float3(microSlope.x, 0.0, microSlope.y));
                float viewFacing = saturate(dot(surfaceNormal, viewDirection));
                float fresnelValue = 0.02037 + (1.0 - 0.02037) * Pow5(1.0 - viewFacing);

                float microVariation = saturate(abs(microSignal.x + microSignal.z) * 0.42);
                float roughnessValue = saturate(_Roughness + microVariation * 0.025 + input.surfaceData.x * 0.15);
                float3 reflectionDirection = reflect(-viewDirection, surfaceNormal);
                float3 reflectionColor = SampleReflection(reflectionDirection, roughnessValue, sunDirection);

                if (_ReflectionStrength > 0.001)
                {
                    float2 reflectionUV = input.projectedPosition.xy / max(input.projectedPosition.w, 0.0001);
                    float3 planarColor = tex2D(_ReflectionTex, saturate(reflectionUV)).rgb;
                    reflectionColor = lerp(reflectionColor, planarColor, saturate(_ReflectionStrength));
                }

                // Beer-Lambert attenuation is evaluated over an angle-dependent virtual
                // water column. This removes the flat blue diffuse/plastic appearance.
                float opticalDistance = 1.45 / max(viewFacing, 0.18);
                float3 transmittance = exp2(-max(_Absorption.rgb, 0.001) * opticalDistance * 1.442695);
                float3 bodyColor = _DeepColor.rgb * transmittance + _MidColor.rgb * (1.0 - transmittance);
                float lightFacing;
                float sunSpecular = EvaluateSunSpecular(surfaceNormal, viewDirection, sunDirection, roughnessValue, lightFacing);
                bodyColor += _ShallowColor.rgb * (1.0 - transmittance) * lightFacing * 0.18;

                float3 finalColor = lerp(bodyColor, reflectionColor, fresnelValue);
                finalColor += sunSpecular * _SunColor.rgb;

                // Anisotropic two-scale breakup follows the dominant wind direction.
                // Blending the scales before thresholding produces torn ribbons instead
                // of the large round holes created by isotropic single-octave noise.
                const float2 foamWind = float2(0.9563, 0.2924);
                const float2 foamCrossWind = float2(-0.2924, 0.9563);
                float windDistance = dot(input.positionWS.xz, foamWind);
                float crossWindDistance = dot(input.positionWS.xz, foamCrossWind);
                float2 coarseFoamCoordinates = float2(windDistance * 0.34, crossWindDistance * 1.62)
                    + float2(_Time.y * 0.052, -_Time.y * 0.021);
                float2 fineFoamCoordinates = float2(windDistance * 1.18, crossWindDistance * 4.75)
                    + float2(_Time.y * 0.13, _Time.y * 0.037);
                float coarseFoamNoise = ValueNoise(coarseFoamCoordinates + input.normalWS.xz * 1.7);
                float fineFoamNoise = ValueNoise(fineFoamCoordinates + input.normalWS.xz * 3.6);
                float foamBreakup = coarseFoamNoise * 0.61 + fineFoamNoise * 0.31
                    + microSignal.y * 0.055 + 0.035;
                // Compression already restricts foam to a thin crest. Keep a faint
                // continuous ribbon and use noise for density, avoiding black holes.
                float foamDensity = 0.22 + smoothstep(0.30, 0.76, foamBreakup) * 0.78;
                float foamMask = input.surfaceData.x * foamDensity;
                float3 foamColor = _FoamColor.rgb * (0.62 + lightFacing * 0.38);
                finalColor = lerp(finalColor, foamColor, foamMask * 0.52);

                float4 result = float4(finalColor, 1.0);
                UNITY_APPLY_FOG(input.fogCoord, result);
                return result;
            }
            ENDCG
        }
    }
    Fallback Off
}
