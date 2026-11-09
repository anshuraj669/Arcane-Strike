// Unnatural shadow rippling across the floorboards (URP, unlit, alpha-blended black): rings of darkness that flow
// in towards _Target and break up on noise, out to _Reach metres from it; _Strength fades it in. A flat quad lying on
// the floor, worked out in world space so it doesn't stretch. In Resources so device builds keep it.
Shader "ArcaneStrike/CreepingShadow"
{
    Properties
    {
        _NoiseTex ("Noise (R filaments, G billows)", 2D) = "gray" {}
        _Target ("Target (x, z)", Vector) = (0, 0, 0, 0)
        _Reach ("Reach (m)", Float) = 2
        _Strength ("Strength", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-50" }
        Pass
        {
            Name "CreepingShadow"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST, _Target;
                float _Reach, _Strength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p = i.positionWS.xz;
                float2 to = p - _Target.xy;
                float r = length(to);
                float t = _Time.y;
                float n = SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, p * 0.35 + float2(t * 0.02, -t * 0.03), 0).g;
                float n2 = SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, p * 0.9 - float2(t * 0.05, t * 0.01), 0).r;
                // Rings flowing in towards the target, wavering on the noise.
                float rings = 0.5 + 0.5 * sin(r * 9.0 + t * 2.6 + (n - 0.5) * 6.0);
                float body = smoothstep(_Reach, _Reach * 0.35, r);           // thicker towards the target
                float edge = smoothstep(0.25, 0.75, n + 0.25 * n2);         // ragged, like spilled ink
                float a = body * lerp(0.45, 1.0, rings) * edge * _Strength;
                return half4(0.0, 0.0, 0.01, saturate(a) * 0.85);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
