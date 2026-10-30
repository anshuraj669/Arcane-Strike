// Molten rock for the monster world (URP, unlit: it is its own light). Glowing veins creep along under a dark crust
// whose plates slowly drift, break up and close again; the hottest seams burn yellow-white, the whole surface
// breathes with a slow pulse, and pools and rivers swell gently. All from one tiling noise texture (LavaFlow makes it:
// R = ridged veins, G = soft noise for the crust, B = soft noise for the warp), mapped in world space so neighbouring
// pieces of lava join up. Cheap enough for Quest: four texture reads and a little arithmetic.
Shader "ArcaneStrike/Lava"
{
    Properties
    {
        _NoiseTex ("Noise (R veins, G crust, B warp)", 2D) = "gray" {}
        _Hot ("Hot", Color) = (1, 0.42, 0.08, 1)
        _Core ("Core", Color) = (1, 0.85, 0.45, 1)
        _Crust ("Crust", Color) = (0.07, 0.02, 0.015, 1)
        _Glow ("Glow", Float) = 2.6
        _Tiling ("Tiling (per metre)", Float) = 0.18
        _Flow ("Flow (xy: world direction, metres/s)", Vector) = (0.04, 0.09, 0, 0)
        _CrustAmount ("Crust Amount", Range(0, 1)) = 0.5
        _Swell ("Swell (m)", Float) = 0.03
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Lava"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST;
                half4 _Hot, _Core, _Crust;
                float _Glow, _Tiling, _CrustAmount, _Swell;
                float4 _Flow;
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
                float3 ws = TransformObjectToWorld(input.positionOS.xyz);
                ws.y += (sin(_Time.y * 0.8 + ws.x * 0.7 + ws.z * 0.45) + sin(_Time.y * 1.3 - ws.z * 1.1)) * 0.5 * _Swell;
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Noise(float2 uv) { return SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv); }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float t = _Time.y;
                float2 flow = _Flow.xy * _Tiling * t;
                float2 uv = i.positionWS.xz * _Tiling;

                // The melt churns: a slow warp bends the veins as they creep downstream.
                float2 warp = (Noise(uv * 0.31 + flow * 0.4).bb - 0.5) * 0.5;
                half4 a = Noise(uv + warp + flow);
                half4 b = Noise(uv * 2.1 - warp * 0.6 + flow * 1.7 + 0.37);
                float veins = a.r * 0.65 + b.r * 0.35;

                // Crust plates: darker, slower, cooling over the hot flow and cracking open again.
                float crustNoise = Noise(uv * 0.55 + flow * 0.3 + float2(0.71, 0.13) + sin(t * 0.07) * 0.05).g;
                float crust = smoothstep(_CrustAmount - 0.1, _CrustAmount + 0.1, crustNoise);

                float heat = saturate(veins * (1.0 - crust * 0.8) + (1.0 - crust) * 0.3);
                heat *= 0.88 + 0.12 * sin(t * 1.1 + a.g * 9.0); // the surface breathes
                half3 col = lerp(_Crust.rgb, _Hot.rgb * _Glow, smoothstep(0.3, 0.75, heat));
                col = lerp(col, _Core.rgb * _Glow * 1.25, smoothstep(0.78, 0.97, heat));
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
