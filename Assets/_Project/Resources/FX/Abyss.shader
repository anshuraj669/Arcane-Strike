// The abyss beyond the shattered bedroom (URP, unlit): a sphere round the player, seen from inside, of slow swirling
// violet with darker rivers of ash, brightest towards the breach (_VortexDir), where the swirl winds tighter. Made from
// the fire noise texture, sampled by direction (whole repeats round the circle, so there is no seam). _Opacity fades it
// in over the room. In Resources so device builds keep it.
Shader "ArcaneStrike/Abyss"
{
    Properties
    {
        _NoiseTex ("Noise (R filaments, G billows)", 2D) = "gray" {}
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Centre ("Centre", Vector) = (0, 0, 0, 0)
        _VortexDir ("Vortex Direction", Vector) = (0, 0, 1, 0)
        _Deep ("Deep", Color) = (0.02, 0.0, 0.05, 1)
        _Violet ("Violet", Color) = (0.42, 0.1, 0.8, 1)
        _Hot ("Hot", Color) = (1.0, 0.6, 1.0, 1)
        _Speed ("Swirl Speed", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-200" }
        Pass
        {
            Name "Abyss"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST, _Centre, _VortexDir, _Deep, _Violet, _Hot;
                float _Opacity, _Speed;
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

            float N(float2 uv, int channel)
            {
                float4 s = SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, uv, 0);
                return channel == 0 ? s.r : s.g;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 d = normalize(i.positionWS - _Centre.xyz);
                float3 vortex = normalize(_VortexDir.xyz);
                float toward = saturate(dot(d, vortex));
                float t = _Time.y * _Speed;

                // Round the vortex: the angle about its axis, and how far from it.
                float3 side = normalize(cross(vortex, abs(vortex.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0)));
                float3 up = cross(side, vortex);
                float angle = atan2(dot(d, up), dot(d, side)) / 6.2831853; // -0.5 .. 0.5
                float away = acos(clamp(dot(d, vortex), -1.0, 1.0)) / 3.14159265; // 0 at the vortex .. 1 behind

                // The swirl winds tighter near the vortex and turns faster there.
                float wind = angle * 3.0 + (1.0 - away) * 1.6 + t * (0.02 + 0.08 * toward * toward);
                float n1 = N(float2(wind, away * 2.2 - t * 0.03), 1);
                float n2 = N(float2(wind * 2.0 + n1 * 0.4, away * 4.5 - t * 0.07), 0);
                float ash = N(float2(angle * 5.0 - t * 0.01, away * 7.0 + t * 0.02), 1);

                float glow = saturate(n1 * 1.3 - 0.35) * (0.45 + 0.55 * n2);
                float3 c = lerp(_Deep.rgb, _Violet.rgb, glow);
                c += _Hot.rgb * pow(saturate(n2 * glow * 1.6), 3.0) * 0.9;
                c += _Violet.rgb * pow(toward, 10.0) * 1.4 + _Hot.rgb * pow(toward, 40.0) * 1.5; // the breach
                c *= 1.0 - 0.75 * smoothstep(0.55, 0.8, ash);                                     // rivers of ash
                return half4(c, _Opacity);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
