// Кожа осьминога.
// Два состояния маскировки (A — текущее, B — новое) и волна хроматофоров между ними.
// Узор каждого состояния «прибит» к телу в момент копирования: матрица _CapX переводит
// текущую мировую позицию в мировую позицию на момент копирования. Пока осьминог стоит
// на месте — узор совпадает с поверхностью; стоит сдвинуться — узор едет вместе с телом.
Shader "OctoHide/OctopusSkin"
{
    Properties
    {
        [NoScaleOffset] _TexA ("Pattern A", 2D) = "white" {}
        _TintA ("Tint A", Color) = (0.9,0.35,0.3,1)
        _ScaleA ("Scale A", Float) = 2
        _AvgA ("Average A", Color) = (0.7,0.27,0.23,1)

        [NoScaleOffset] _TexB ("Pattern B", 2D) = "white" {}
        _TintB ("Tint B", Color) = (0.9,0.35,0.3,1)
        _ScaleB ("Scale B", Float) = 2
        _AvgB ("Average B", Color) = (0.7,0.27,0.23,1)

        _Blend ("Transition A→B", Range(0,1)) = 1
        _WaveScale ("Chromatophore Wave Scale", Float) = 6
        _EdgeColor ("Wave Edge Color", Color) = (0.15,0.05,0.05,1)

        _Papillae ("Papillae (bumps)", Range(0,1)) = 0.2
        _PapillaeHeight ("Papillae Height (m)", Float) = 0.035
        _PapillaeFreq ("Papillae Frequency", Float) = 14
        _Smear ("Movement Smear", Range(0,1)) = 0

        _RampThreshold ("Ramp Threshold", Range(0,1)) = 0.25
        _RampSmooth ("Ramp Smooth", Range(0.001,0.5)) = 0.08
        _ShadowStrength ("Shadow Brightness", Range(0,1)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull Back

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "OctoCommon.hlsl"

        TEXTURE2D(_TexA); SAMPLER(sampler_TexA);
        TEXTURE2D(_TexB); SAMPLER(sampler_TexB);

        CBUFFER_START(UnityPerMaterial)
            float4x4 _CapA;
            float4x4 _CapB;
            half4 _TintA; half4 _AvgA; float _ScaleA;
            half4 _TintB; half4 _AvgB; float _ScaleB;
            half _Blend;
            float _WaveScale;
            half4 _EdgeColor;
            half _Papillae;
            float _PapillaeHeight;
            float _PapillaeFreq;
            half _Smear;
            half _RampThreshold;
            half _RampSmooth;
            half _ShadowStrength;
        CBUFFER_END

        // Матрица ещё не задана скриптом (например, в режиме редактора) — m33 == 0, считаем её единичной
        float3 ApplyCap(float4x4 cap, float3 p) { return cap[3][3] < 0.5 ? p : mul(cap, float4(p, 1)).xyz; }
        float3 ApplyCapDir(float4x4 cap, float3 n) { return cap[3][3] < 0.5 ? n : mul((float3x3)cap, n); }

        // Позиция «в теле» — стабильная при движении осьминога (через матрицу последнего копирования)
        float3 BodySpace(float3 positionWS) { return ApplyCap(_CapB, positionWS); }

        float3 Displace(float3 positionWS, float3 normalWS)
        {
            float bumps = OctoValueNoise(BodySpace(positionWS) * _PapillaeFreq);
            bumps = pow(bumps, 3.0);
            return positionWS + normalWS * bumps * _Papillae * _PapillaeHeight;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                posWS = Displace(posWS, nWS);
                o.positionWS = posWS;
                o.normalWS = nWS;
                o.positionCS = TransformWorldToHClip(posWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half3 SampleState(TEXTURE2D_PARAM(tex, smp), float4x4 cap, float3 posWS, float3 nWS, float scale, half4 tint, half4 avg)
            {
                float3 p = ApplyCap(cap, posWS);
                float3 n = normalize(ApplyCapDir(cap, nWS));
                half3 c = OctoTriplanar(TEXTURE2D_ARGS(tex, smp), p, n, scale).rgb * tint.rgb;
                // В движении узор «плывёт» и усредняется
                return lerp(c, avg.rgb, _Smear);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);

                half3 colA = SampleState(TEXTURE2D_ARGS(_TexA, sampler_TexA), _CapA, i.positionWS, n, _ScaleA, _TintA, _AvgA);
                half3 colB = SampleState(TEXTURE2D_ARGS(_TexB, sampler_TexB), _CapB, i.positionWS, n, _ScaleB, _TintB, _AvgB);

                // Волна хроматофоров: пятна переключаются по шуму, с тёмной кромкой фронта
                float3 bodyPos = BodySpace(i.positionWS);
                float wn = OctoFbm(bodyPos * _WaveScale);
                const float w = 0.12;
                float t = _Blend * (1.0 + 2.0 * w) - w;
                float mask = smoothstep(wn - w, wn + w, t);
                float edge = 1.0 - abs(mask * 2.0 - 1.0);
                half3 albedo = lerp(colA, colB, mask);
                albedo = lerp(albedo, _EdgeColor.rgb, edge * 0.6 * step(0.001, 1.0 - _Blend));

                // Папиллы: фейковое затенение впадин + огранка нормали
                float bumps = pow(OctoValueNoise(bodyPos * _PapillaeFreq), 3.0);
                albedo *= 1.0 - _Papillae * 0.35 * (1.0 - bumps);
                float3 facet = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                facet *= sign(dot(facet, n) + 1e-4);
                n = normalize(lerp(n, facet, _Papillae * 0.5));

                half3 col = OctoToonLighting(albedo, i.positionWS, n, _RampThreshold, _RampSmooth, _ShadowStrength);
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float3 _LightDirection;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                posWS = Displace(posWS, nWS);
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(Displace(posWS, nWS));
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
