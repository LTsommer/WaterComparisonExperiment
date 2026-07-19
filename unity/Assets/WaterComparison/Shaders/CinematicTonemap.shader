Shader "Hidden/WaterComparison/Cinematic Tonemap"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            float3 AcesToneMap(float3 color)
            {
                return saturate((color * (2.51 * color + 0.03)) / (color * (2.43 * color + 0.59) + 0.14));
            }

            fixed4 frag(v2f_img input) : SV_Target
            {
                float3 color = tex2D(_MainTex, input.uv).rgb;
                color = AcesToneMap(color * 1.08);
                color = pow(max(color, 0.0), 0.96);
                float2 centered = input.uv * 2.0 - 1.0;
                float vignette = 1.0 - dot(centered, centered) * 0.14;
                color *= saturate(vignette);
                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
