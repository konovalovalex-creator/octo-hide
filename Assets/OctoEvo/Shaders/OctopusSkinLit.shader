// Кожа осьминога (PBR, подводная оптика).
// Два состояния маскировки (A — текущее, B — новое) и волна хроматофоров между ними.
// Узор каждого состояния «прибит» к телу в момент копирования: матрица _CapX переводит
// текущую мировую позицию в мировую позицию на момент копирования. Пока осьминог стоит
// на месте — узор совпадает с поверхностью; стоит сдвинуться — узор едет вместе с телом.
Shader "OctoEvo/OctopusSkinLit"
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

        [NoScaleOffset] _Tex2A ("Pattern A layer 2", 2D) = "white" {}
        [NoScaleOffset] _Tex2B ("Pattern B layer 2", 2D) = "white" {}
        _LayerA ("Layer A (patch size, bias, sharpness, scale2)", Vector) = (0,0,6,0)
        _LayerB ("Layer B (patch size, bias, sharpness, scale2)", Vector) = (0,0,6,0)

        _Blend ("Transition A→B", Range(0,1)) = 1
        _WaveScale ("Chromatophore Wave Scale", Float) = 6
        _EdgeColor ("Wave Edge Color", Color) = (0.15,0.05,0.05,1)

        _Papillae ("Papillae (bumps)", Range(0,1)) = 0.2
        _PapillaeHeight ("Papillae Height (m)", Float) = 0.035
        _PapillaeFreq ("Papillae Frequency", Float) = 14
        _Smear ("Movement Smear", Range(0,1)) = 0
        _RippleCenter ("Ripple Center (world)", Vector) = (0,0,0,0)
        _RippleTime ("Ripple Age (s)", Float) = 100
        _RippleStrength ("Ripple Strength", Range(0,1)) = 0

        _Smoothness ("Wet Smoothness", Range(0,1)) = 0.55
        _SSSColor ("Subsurface Color", Color) = (0.9,0.35,0.3,1)
        _SSSStrength ("Subsurface Strength", Range(0,1)) = 0.25

        [Header(Natural skin (model texture))]
        _UseNatural ("Use Natural Skin", Range(0,1)) = 0
        _NaturalMap ("Natural Albedo", 2D) = "white" {}
        [Normal] _NaturalNormal ("Natural Normal", 2D) = "bump" {}
        _NaturalA ("State A is natural", Range(0,1)) = 1
        _NaturalB ("State B is natural", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull Back

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "OctoCommon.hlsl"
        #include "OctoUnderwater.hlsl"

        TEXTURE2D(_TexA); SAMPLER(sampler_TexA);
        TEXTURE2D(_TexB); SAMPLER(sampler_TexB);
        TEXTURE2D(_Tex2A); TEXTURE2D(_Tex2B);
        TEXTURE2D(_NaturalMap); SAMPLER(sampler_NaturalMap);
        TEXTURE2D(_NaturalNormal); SAMPLER(sampler_NaturalNormal);

        CBUFFER_START(UnityPerMaterial)
            float4x4 _CapA;
            float4x4 _CapB;
            half4 _TintA; half4 _AvgA; float _ScaleA;
            half4 _TintB; half4 _AvgB; float _ScaleB;
            float4 _LayerA;
            float4 _LayerB;
            half _Blend;
            float _WaveScale;
            half4 _EdgeColor;
            half _UseNatural;
            half _NaturalA;
            half _NaturalB;
            half _Papillae;
            float _PapillaeHeight;
            float _PapillaeFreq;
            half _Smear;
            float4 _RippleCenter;
            float _RippleTime;
            half _RippleStrength;
            half _Smoothness;
            half4 _SSSColor;
            half _SSSStrength;
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

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 tangentWS  : TEXCOORD2;
                float2 uv         : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                posWS = Displace(posWS, nWS);
                o.positionWS = posWS;
                o.normalWS = nWS;
                o.tangentWS = float4(TransformObjectToWorldDir(v.tangentOS.xyz), v.tangentOS.w * GetOddNegativeScale());
                o.uv = v.uv;
                o.positionCS = TransformWorldToHClip(posWS);
                return o;
            }

            half3 SampleState(TEXTURE2D_PARAM(tex, smp), TEXTURE2D(tex2), float4 layer, float4x4 cap, float3 posWS, float3 nWS, float scale, half4 tint, half4 avg)
            {
                float3 p = ApplyCap(cap, posWS);
                float3 n = normalize(ApplyCapDir(cap, nWS));
                half3 c = OctoTriplanar(TEXTURE2D_ARGS(tex, smp), p, n, scale).rgb;
                // Второй слой поверхности (мох, гравий): те же пятна, что на камне под осьминогом
                if (layer.w > 0.0)
                {
                    float m = OctoFbm(p * layer.x) + layer.y;
                    m = saturate((m - 0.5) * layer.z + 0.5);
                    half3 c2 = OctoTriplanar(TEXTURE2D_ARGS(tex2, smp), p, n, layer.w).rgb;
                    c = lerp(c, c2, m);
                }
                c *= tint.rgb;
                // В движении узор «плывёт» и усредняется
                return lerp(c, avg.rgb, _Smear);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                half3 natural = half3(1, 1, 1);
                if (_UseNatural > 0.001)
                {
                    natural = SAMPLE_TEXTURE2D(_NaturalMap, sampler_NaturalMap, i.uv).rgb;
                    half3 nTS = UnpackNormal(SAMPLE_TEXTURE2D(_NaturalNormal, sampler_NaturalNormal, i.uv));
                    float3 t = normalize(i.tangentWS.xyz - n * dot(i.tangentWS.xyz, n));
                    float3 b = cross(n, t) * i.tangentWS.w;
                    n = normalize(lerp(n, mul(nTS, float3x3(t, b, n)), _UseNatural));
                }

                half3 colA = SampleState(TEXTURE2D_ARGS(_TexA, sampler_TexA), _Tex2A, _LayerA, _CapA, i.positionWS, n, _ScaleA, _TintA, _AvgA);
                half3 colB = SampleState(TEXTURE2D_ARGS(_TexB, sampler_TexB), _Tex2B, _LayerB, _CapB, i.positionWS, n, _ScaleB, _TintB, _AvgB);
                // Естественный окрас модели (из Tripo) — когда маскировка не включена
                colA = lerp(colA, natural, _NaturalA * _UseNatural);
                colB = lerp(colB, natural, _NaturalB * _UseNatural);

                // Волна хроматофоров: пятна переключаются по шуму, с тёмной кромкой фронта
                float3 bodyPos = BodySpace(i.positionWS);
                float wn = OctoFbm(bodyPos * _WaveScale);
                const float w = 0.12;
                float t = _Blend * (1.0 + 2.0 * w) - w;
                float mask = smoothstep(wn - w, wn + w, t);
                float edge = 1.0 - abs(mask * 2.0 - 1.0);
                half3 albedo = lerp(colA, colB, mask);
                albedo = lerp(albedo, _EdgeColor.rgb, edge * 0.6 * step(0.001, 1.0 - _Blend));

                // Рябь от попадания: кольца расходятся от точки удара, узор мерцает
                if (_RippleStrength > 0.001)
                {
                    float rd = distance(i.positionWS, _RippleCenter.xyz);
                    float age = _RippleTime;
                    float fade = saturate(1.0 - age / 2.5) * _RippleStrength;
                    float rings = sin(rd * 45.0 - age * 16.0) * exp(-rd * 2.0) * fade;
                    float front = exp(-pow((rd - age * 1.4) * 7.0, 2.0)) * fade;
                    albedo *= 1.0 + rings * 0.8;
                    albedo = lerp(albedo, _EdgeColor.rgb * 0.5 + 0.5, saturate(front * 0.6));
                }

                // Папиллы: рельеф кожи через градиент шума (нормаль) + затенение впадин
                float h0 = pow(OctoValueNoise(bodyPos * _PapillaeFreq), 3.0);
                float e = 0.3 / max(1.0, _PapillaeFreq);
                float hx = pow(OctoValueNoise((bodyPos + float3(e, 0, 0)) * _PapillaeFreq), 3.0);
                float hy = pow(OctoValueNoise((bodyPos + float3(0, e, 0)) * _PapillaeFreq), 3.0);
                float hz = pow(OctoValueNoise((bodyPos + float3(0, 0, e)) * _PapillaeFreq), 3.0);
                float3 grad = float3(hx - h0, hy - h0, hz - h0) / e;
                grad -= n * dot(grad, n);
                n = normalize(n - grad * _Papillae * 0.06);
                albedo *= 1.0 - _Papillae * 0.3 * (1.0 - h0);

                half smooth = lerp(_Smoothness, _Smoothness * 0.55, _Papillae);
                half3 col = UW_Shade(albedo, i.positionWS, n, i.positionCS, smooth, 1.0 - _Papillae * 0.3 * (1.0 - h0));

                // Мягкое подповерхностное рассеяние: свет «просвечивает» тонкую кожу
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half wrap = saturate((dot(n, mainLight.direction) + 0.6) / 1.6);
                half3 sss = albedo * _SSSColor.rgb * wrap * _SSSStrength * mainLight.color * UW_SunlightAtDepth(i.positionWS);
                col += UW_ApplyWater(sss, i.positionWS) - UW_ApplyWater(half3(0, 0, 0), i.positionWS);
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
