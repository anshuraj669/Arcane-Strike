// The Jyoti Seal being burned into the skin (URP, unlit, premultiplied). The texture is the crest: R = where the lines
// are, G = the order they are drawn in (0 first .. 1 last). Lines appear where their order is below _Reveal; right at
// the burning front they are white-hot molten gold, then they cool to gold with blood-red edges, and the whole crest
// throbs with _Pulse. In Resources so device builds keep it.
Shader "ArcaneStrike/SealBrand"
{
    Properties
    {
        _MainTex ("Crest (R lines, G order)", 2D) = "black" {}
        _Reveal ("Reveal", Range(0, 1.2)) = 0
        _Pulse ("Pulse", Range(0, 2)) = 1
        _Fade ("Fade", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" }
        Pass
        {
            Name "SealBrand"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -2, -2
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Reveal, _Pulse, _Fade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float4 crest = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float stroke = crest.r;
                float behind = _Reveal - crest.g;              // how long ago the fire passed here (in order units)
                float shown = smoothstep(-0.005, 0.01, behind);
                float front = saturate(1.0 - behind / 0.06);   // 1 right at the burning front
                float flicker = 0.85 + 0.15 * sin(_Time.y * 31.0 + crest.g * 60.0);

                float3 blood = float3(0.55, 0.02, 0.01);
                float3 gold = float3(1.0, 0.62, 0.12);
                float3 hot = float3(1.0, 0.95, 0.8);
                // The middle of a line is gold, its edges blood red; at the front it is white hot.
                float3 c = lerp(blood, gold, smoothstep(0.35, 0.9, stroke));
                c = lerp(c, hot, front * flicker);
                float glow = (0.9 + 1.4 * front) * _Pulse;
                float alpha = saturate(stroke * 1.4) * shown * _Fade;
                c = pow(c, 2.2) * glow * alpha;
                return half4(c, alpha * 0.95);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
