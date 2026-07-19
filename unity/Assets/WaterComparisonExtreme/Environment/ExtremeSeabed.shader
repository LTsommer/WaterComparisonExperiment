Shader "WaterComparisonExtreme/Seabed Caustics"
{
    Properties
    {
        _SandColor ("Sand", Color) = (0.27, 0.245, 0.18, 1)
        _RockColor ("Dark grains", Color) = (0.08, 0.095, 0.085, 1)
        _CausticColor ("Caustic light", Color) = (0.34, 0.58, 0.48, 1)
        _CausticStrength ("Caustic strength", Range(0, 1.5)) = 0.7
        _DepthTint ("Underwater tint", Color) = (0.035, 0.20, 0.22, 1)
    }

    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        LOD 220

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            float4 _SandColor;
            float4 _RockColor;
            float4 _CausticColor;
            float4 _DepthTint;
            float _CausticStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                SHADOW_COORDS(3)
            };

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            float ValueNoise(float2 value)
            {
                float2 cell = floor(value);
                float2 localValue = frac(value);
                localValue = localValue * localValue * (3.0 - 2.0 * localValue);
                float lower = lerp(Hash21(cell), Hash21(cell + float2(1.0, 0.0)), localValue.x);
                float upper = lerp(Hash21(cell + float2(0.0, 1.0)), Hash21(cell + 1.0), localValue.x);
                return lerp(lower, upper, localValue.y);
            }

            v2f vert(appdata input)
            {
                v2f output;
                output.pos = UnityObjectToClipPos(input.vertex);
                output.positionWS = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.normalWS = UnityObjectToWorldNormal(input.normal);
                UNITY_TRANSFER_FOG(output, output.pos);
                TRANSFER_SHADOW(output);
                return output;
            }

            float CausticBand(float2 position, float timeValue)
            {
                float first = sin(position.x * 1.47 + sin(position.y * 1.13 - timeValue * 0.73));
                float second = sin(position.y * 1.61 - sin(position.x * 1.29 + timeValue * 0.57));
                float crossing = 1.0 - abs(first - second);
                return pow(saturate(crossing), 10.0);
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 surface = input.positionWS.xz;
                float broadGrain = ValueNoise(surface * 0.42 + 17.0);
                float fineGrain = ValueNoise(surface * 3.7 - 4.0);
                float pebbleMask = smoothstep(0.74, 0.93, ValueNoise(surface * 1.23 + 9.7));
                float3 baseColor = _SandColor.rgb * (0.82 + broadGrain * 0.23 + fineGrain * 0.055);
                baseColor = lerp(baseColor, _RockColor.rgb, pebbleMask * 0.38);

                float timeValue = _Time.y;
                float causticA = CausticBand(surface, timeValue);
                float causticB = CausticBand(surface * 1.71 + float2(4.2, -7.3), timeValue * 1.19);
                float caustic = causticA * 0.68 + causticB * 0.32;
                caustic *= 0.55 + 0.45 * broadGrain;

                float3 normalWS = normalize(input.normalWS);
                float3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                float diffuse = saturate(dot(normalWS, lightDirection));
                float shadow = SHADOW_ATTENUATION(input);
                float3 ambient = ShadeSH9(float4(normalWS, 1.0));
                float3 lighting = ambient + _LightColor0.rgb * diffuse * shadow;
                float3 color = baseColor * lighting;
                color += _CausticColor.rgb * caustic * _CausticStrength * shadow;
                color = lerp(color, color * _DepthTint.rgb * 2.25, 0.34);

                fixed4 result = fixed4(color, 1.0);
                UNITY_APPLY_FOG(input.fogCoord, result);
                return result;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
