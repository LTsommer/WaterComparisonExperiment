Shader "Hidden/WaterComparisonExtreme/FoamSimulation"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Previous foam history", 2D) = "black" {}
        _WaveStrength ("Wave height", Range(0, 1.5)) = 1
        _Choppiness ("Choppiness", Range(0, 1.4)) = 1
        _WaveTimeScale ("Wave time scale", Range(0, 2)) = 1
        _FoamThreshold ("Breaking compression", Range(0.03, 0.5)) = 0.145
        _FoamSoftness ("Breaking softness", Range(0.01, 0.4)) = 0.105
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _WaveStrength;
            float _Choppiness;
            float _WaveTimeScale;
            float _FoamThreshold;
            float _FoamSoftness;

            // These values are globals shared with the runtime and ocean shader.
            float4 _ExtremeFoamWorldParams;       // xy centre, z size, w reciprocal size
            float _ExtremeFoamDeltaTime;
            float _ExtremeFoamTime;
            float4 _ExtremeFoamAdvectionVelocity; // xy world-space metres per second
            float _ExtremeFoamHalfLife;
            float _ExtremeFoamInjection;
            float _ExtremeQualityLevel;

            void AccumulateJacobian(
                float2 worldPosition,
                float2 directionValue,
                float heightValue,
                float waveNumber,
                float angularFrequency,
                float chopValue,
                float phaseOffset,
                float timeValue,
                inout float surfaceHeight,
                inout float jacobianXX,
                inout float jacobianXZ,
                inout float jacobianZZ)
            {
                float phaseValue = waveNumber * dot(directionValue, worldPosition)
                    + angularFrequency * timeValue + phaseOffset;
                float sineValue;
                float cosineValue;
                sincos(phaseValue, sineValue, cosineValue);
                float verticalHeight = heightValue * _WaveStrength;
                float horizontalSlope = verticalHeight * chopValue * _Choppiness * waveNumber;
                surfaceHeight += verticalHeight * sineValue;
                jacobianXX -= horizontalSlope * directionValue.x * directionValue.x * sineValue;
                jacobianXZ -= horizontalSlope * directionValue.x * directionValue.y * sineValue;
                jacobianZZ -= horizontalSlope * directionValue.y * directionValue.y * sineValue;
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

            float4 Frag(v2f_img input) : SV_Target
            {
                float deltaTime = min(max(_ExtremeFoamDeltaTime, 0.0), 0.1);
                float worldSize = max(_ExtremeFoamWorldParams.z, 0.001);
                float inverseWorldSize = max(_ExtremeFoamWorldParams.w, rcp(worldSize));
                float2 worldPosition = _ExtremeFoamWorldParams.xy
                    + (input.uv - 0.5) * worldSize;
                float2 wavePosition = worldPosition - _ExtremeFoamWorldParams.xy;

                // Advect back into the previous frame. A tiny divergence-free-looking
                // perturbation keeps old foam from becoming ruler-straight streaks.
                float curlNoiseX = ValueNoise(worldPosition * 0.071 + float2(_ExtremeFoamTime * 0.017, 4.13));
                float curlNoiseY = ValueNoise(worldPosition.yx * 0.083 + float2(7.91, -_ExtremeFoamTime * 0.014));
                float2 curlVelocity = (float2(curlNoiseY, -curlNoiseX) - 0.5) * 0.22;
                float2 previousWorldPosition = worldPosition
                    - (_ExtremeFoamAdvectionVelocity.xy + curlVelocity) * deltaTime;
                float2 previousUV = (previousWorldPosition - _ExtremeFoamWorldParams.xy)
                    * inverseWorldSize + 0.5;
                float2 previousLow = step(0.0, previousUV);
                float2 previousHigh = step(previousUV, 1.0);
                float previousValid = previousLow.x * previousLow.y * previousHigh.x * previousHigh.y;
                float previousDensity = tex2D(_MainTex, saturate(previousUV)).r * previousValid;

                float surfaceHeight = 0.0;
                float jacobianXX = 1.0;
                float jacobianXZ = 0.0;
                float jacobianZZ = 1.0;
                float timeValue = _ExtremeFoamTime * _WaveTimeScale;

                AccumulateJacobian(wavePosition, float2(0.984808,  0.173648), 0.460,  0.142,  1.1804, 0.85, 0.31, timeValue, surfaceHeight, jacobianXX, jacobianXZ, jacobianZZ);
                AccumulateJacobian(wavePosition, float2(0.913545,  0.406737), 0.280,  0.219,  1.4656, 0.82, 2.17, timeValue, surfaceHeight, jacobianXX, jacobianXZ, jacobianZZ);
                AccumulateJacobian(wavePosition, float2(0.998630, -0.052336), 0.180,  0.337,  1.8183, 0.78, 4.83, timeValue, surfaceHeight, jacobianXX, jacobianXZ, jacobianZZ);
                AccumulateJacobian(wavePosition, float2(0.819152,  0.573576), 0.115,  0.514,  2.2454, 0.72, 1.21, timeValue, surfaceHeight, jacobianXX, jacobianXZ, jacobianZZ);
                AccumulateJacobian(wavePosition, float2(0.945519, -0.325568), 0.075,  0.793, 2.7890, 0.67, 5.44, timeValue, surfaceHeight, jacobianXX, jacobianXZ, jacobianZZ);
                AccumulateJacobian(wavePosition, float2(0.669131,  0.743145), 0.049, 1.217, 3.4554, 0.62, 3.02, timeValue, surfaceHeight, jacobianXX, jacobianXZ, jacobianZZ);

                [branch]
                if (_ExtremeQualityLevel > 0.5)
                {
                    AccumulateJacobian(wavePosition, float2(0.990268,  0.139173), 0.032, 1.873, 4.2867, 0.56, 0.77, timeValue, surfaceHeight, jacobianXX, jacobianXZ, jacobianZZ);
                    AccumulateJacobian(wavePosition, float2(0.559193, -0.829038), 0.021, 2.879, 5.3144, 0.50, 4.11, timeValue, surfaceHeight, jacobianXX, jacobianXZ, jacobianZZ);
                    AccumulateJacobian(wavePosition, float2(0.874620,  0.484810), 0.014, 4.423, 6.5870, 0.44, 2.69, timeValue, surfaceHeight, jacobianXX, jacobianXZ, jacobianZZ);
                }

                [branch]
                if (_ExtremeQualityLevel > 1.5)
                {
                    AccumulateJacobian(wavePosition, float2(0.743145, -0.669131), 0.009, 6.791, 8.1620, 0.38, 5.91, timeValue, surfaceHeight, jacobianXX, jacobianXZ, jacobianZZ);
                    AccumulateJacobian(wavePosition, float2(0.406737,  0.913545), 0.006, 10.430, 10.1150, 0.32, 1.72, timeValue, surfaceHeight, jacobianXX, jacobianXZ, jacobianZZ);
                    AccumulateJacobian(wavePosition, float2(0.965926, -0.258819), 0.004, 16.020, 12.5360, 0.28, 3.63, timeValue, surfaceHeight, jacobianXX, jacobianXZ, jacobianZZ);
                }

                float determinantValue = jacobianXX * jacobianZZ - jacobianXZ * jacobianXZ;
                float compressionValue = max(1.0 - determinantValue, 0.0);
                float sourceDensity = smoothstep(
                    _FoamThreshold,
                    _FoamThreshold + max(_FoamSoftness, 0.001),
                    compressionValue);
                sourceDensity *= smoothstep(0.04, 0.62, surfaceHeight);

                const float2 dominantWind = float2(0.984808, 0.173648);
                const float2 crossWind = float2(-0.173648, 0.984808);
                float windCoordinate = dot(worldPosition, dominantWind);
                float crossCoordinate = dot(worldPosition, crossWind);
                float coarseNoise = ValueNoise(float2(windCoordinate * 0.31, crossCoordinate * 1.37)
                    + float2(_ExtremeFoamTime * 0.055, -_ExtremeFoamTime * 0.018));
                float fineNoise = ValueNoise(float2(windCoordinate * 1.13, crossCoordinate * 4.73)
                    + float2(_ExtremeFoamTime * 0.13, _ExtremeFoamTime * 0.031));
                sourceDensity *= saturate(0.21 + coarseNoise * 0.57 + fineNoise * 0.32);

                float decayValue = exp2(-deltaTime / max(_ExtremeFoamHalfLife, 0.1));
                float retainedDensity = previousDensity * decayValue;
                float injectedDensity = sourceDensity * max(_ExtremeFoamInjection, 0.0) * deltaTime;
                float nextDensity = saturate(retainedDensity + injectedDensity);

                // G is useful for visual diagnostics; the ocean intentionally samples R.
                return float4(nextDensity, sourceDensity, saturate(compressionValue), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
