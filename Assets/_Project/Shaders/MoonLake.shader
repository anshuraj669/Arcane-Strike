// Water that mirrors a sky panorama (URP, unlit): the Ascension temple's jade pool. A mirror of the night sky: each pixel reflects the view off a
// barely rippling surface and looks the direction up in the sky panorama, so the moon and stars lie on the water. The
// peak can't really be reflected in a panorama, so a dark red ridge band and the peak's triangle are drawn in over the
// low sky. Cheap enough for Quest: three texture reads and a little arithmetic.
Shader "ArcaneStrike/MoonLake"
{
    Properties
    {
        _SkyTex ("Sky (latitude-longitude)", 2D) = "black" {}
        _NoiseTex ("Noise (G, B: soft)", 2D) = "gray" {}
        _Deep ("Deep Water", Color) = (0.012, 0.004, 0.014, 1)
        _Ridges ("Reflected Mountains", Color) = (0.11, 0.015, 0.03, 1)
        _PeakShape ("Peak (x: height, y: half width, as tangents)", Vector) = (0.46, 0.39, 0, 0)
        _Mirror ("Mirror (least reflection)", Range(0, 1)) = 0.45
        _Ripple ("Ripple", Float) = 0.035
        _Tiling ("Ripple Tiling (per metre)", Float) = 0.06
        _Foam ("Foam Streaks", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "MoonLake"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SkyTex);
            SAMPLER(sampler_SkyTex);
            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _SkyTex_ST, _NoiseTex_ST;
                half4 _Deep, _Ridges;
                float4 _PeakShape;
                float _Mirror, _Ripple, _Tiling, _Foam;
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
                float fog : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // The same mapping as Skybox/Panoramic, so the reflection lines up with the sky. Level 0: no seam at the wrap.
            half3 Sky(float3 d)
            {
                float2 uv = float2(0.5 - atan2(d.z, d.x) / (2.0 * PI), 1.0 - acos(clamp(d.y, -1.0, 1.0)) / PI);
                return SAMPLE_TEXTURE2D_LOD(_SkyTex, sampler_SkyTex, uv, 0).rgb;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float t = _Time.y;
                float3 toPixel = i.positionWS - GetCameraPositionWS();
                float dist = length(toPixel);
                float3 view = toPixel / dist;

                // Two slow drifts of soft noise tilt the surface; far water calms down so it doesn't shimmer.
                float2 uv = i.positionWS.xz * _Tiling;
                float2 a = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv + float2(0.011, 0.007) * t).gb - 0.5;
                float2 b = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv * 2.3 - float2(0.009, 0.013) * t).gb - 0.5;
                float3 n = normalize(float3((a + b) * _Ripple / (1.0 + dist * 0.012), 1.0).xzy);

                float3 r = reflect(view, n);
                r.y = abs(r.y);
                half3 sky = Sky(r);

                // The mountains stand in front of the low sky: a band along the horizon and the peak's triangle ahead.
                sky = lerp(_Ridges.rgb, sky, smoothstep(0.02, 0.26, r.y));
                if (r.z > 0.05)
                {
                    float peak = _PeakShape.x * (1.0 - abs(r.x / r.z) / _PeakShape.y) - r.y / r.z;
                    sky = lerp(sky, _Ridges.rgb, smoothstep(-0.015, 0.015, peak));
                }

                float fresnel = pow(saturate(1.0 + dot(view, n)), 5.0);
                half3 col = lerp(_Deep.rgb, sky, max(fresnel, _Mirror));
                // Pale streaks where the water slides: long stretched noise, drifting.
                float streak = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv * float2(0.45, 1.8) + float2(0.018, 0.0) * t).g;
                col = lerp(col, half3(0.9, 0.97, 0.92), smoothstep(0.56, 0.74, streak) * _Foam);
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
