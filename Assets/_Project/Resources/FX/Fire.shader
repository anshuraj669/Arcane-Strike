// Living blue fire (URP, unlit): worked out every frame, not a picture. The fire is a row of soft pointed tongues, each
// with its own height that keeps licking up and falling back; all of them bend together on slow noise that climbs the
// flame, more the higher they reach. Heat falls smoothly from the base to each tip, so no edge is ever hard and nothing
// is blotchy. _Shape 0: tongues under a dome, on a sheet (the cauldron). _Shape 1: tongues all round a cylinder, which
// can open a window towards the viewer (_WindowOn). In Resources so device builds keep it. Premultiplied. Four texture
// reads: fine for a Quest, on the few surfaces that use it.
Shader "ArcaneStrike/Fire"
{
    Properties
    {
        _NoiseTex ("Noise (R filaments, G billows)", 2D) = "gray" {}
        _Shape ("Shape (0 column, 1 wall)", Float) = 0
        _TileX ("Tile Across", Float) = 1
        _Flow ("Flow Speed", Float) = 1
        _Seed ("Seed", Float) = 0.13
        _Power ("Power", Range(0, 1)) = 1
        _Centre ("Centre (wall)", Vector) = (0, 0, 0, 0)
        _WindowDir ("Window Direction (x, z)", Vector) = (0, 1, 0, 0)
        _WindowOn ("Window", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Pass
        {
            Name "Fire"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST, _Centre, _WindowDir;
                float _Shape, _TileX, _Flow, _Seed, _Power, _WindowOn;
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
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = input.uv;
                return o;
            }

            // (Level 0 always: the tongue heights jump between cells, which would otherwise pick a blurry mip along a line.)
            float Noise(float2 uv)
            {
                return SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, uv, 0).g;
            }

            // Round to whole repeats on the wall, so the fire meets itself going round the cylinder.
            float Across(float count) { return _Shape > 0.5 ? round(count) : count; }

            // A row of `count` tongues: a soft peak in each cell, as high as that cell's own noise says just now.
            float Tongues(float across, float count, float seed, float rate, float t)
            {
                float x = across * count;
                float cell = floor(x);
                float peak = 1.0 - abs(2.0 * (x - cell) - 1.0);
                peak = peak * peak * (3.0 - 2.0 * peak);
                float height = smoothstep(0.3, 0.72, Noise(float2(cell * 0.37 + seed, cell * 0.13 - t * rate)));
                return peak * (0.35 + 0.65 * height);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float t = _Time.y * _Flow;
                float u = i.uv.x, v = i.uv.y;
                float w = Noise(float2(u * Across(_TileX * 0.35) + _Seed, v * 0.35 - t * 0.55)) - 0.5;
                float w2 = Noise(float2(u * Across(_TileX * 0.9) + _Seed * 2.0, v * 0.8 - t * 1.05)) - 0.5;
                float bend = (w * 1.6 + w2 * 0.9) * (0.06 + v * 1.1);

                float a = Tongues(u + bend / Across(_TileX * 2.5), Across(_TileX * 2.5), _Seed, 0.55, t);
                float b = Tongues(u + (bend * 1.5 + 0.3) / Across(_TileX * 4.0), Across(_TileX * 4.0), _Seed + 0.4, 0.8, t);
                float peak = max(a, b * 0.72) + 0.3 * min(a, b);

                float height;
                if (_Shape > 0.5) height = 0.1 + 0.62 * peak;
                else
                {
                    float x = 2.0 * u - 1.0;
                    height = (0.14 + 0.86 * peak) * pow(saturate(1.0 - x * x), 0.6); // (nothing at the sheet's sides)
                }
                float core = saturate(1.0 - v / max(height * _Power, 0.02));
                float heat = saturate(pow(core, 0.7) * 1.05 * smoothstep(0.0, 0.08, v) + w2 * 0.25 * core);

                if (_WindowOn > 0.001)
                {
                    float2 d = normalize(i.positionWS.xz - _Centre.xz + 1e-4);
                    heat *= 1.0 - _WindowOn * smoothstep(0.5, 0.74, abs(dot(d, _WindowDir.xy)));
                }

                // Blue at the tips, through azure and cyan, to a white heart at the base.
                float3 c = lerp(float3(0.02, 0.08, 0.5), float3(0.03, 0.2, 0.85), saturate(heat / 0.25));
                c = lerp(c, float3(0.08, 0.48, 1.0), saturate((heat - 0.25) / 0.25));
                c = lerp(c, float3(0.4, 0.85, 1.0), saturate((heat - 0.5) / 0.25));
                c = lerp(c, float3(0.92, 0.99, 1.0), saturate((heat - 0.75) / 0.2));
                float alpha = smoothstep(0.02, 0.42, heat);
                c = pow(c, 2.2) * pow(alpha, 0.7); // (written as seen on screen; the project renders in linear)
                return half4(c, alpha * 0.9);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
