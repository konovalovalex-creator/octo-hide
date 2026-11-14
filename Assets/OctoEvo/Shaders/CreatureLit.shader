// Морские существа (рыбы, креветки, крабы): окрас считается в координатах объекта,
// поэтому узор едет вместе с рыбой. Плавательная волна по телу — в вершинном шейдере.
// Меш: голова по +Z, длина 1 (z от -0.5 до 0.5), цвет вершины R = 1 тело / 0 плавник.
// Индивидуальные цвета и фаза плавания задаются через MaterialPropertyBlock.
Shader "OctoEvo/CreatureLit"
{
    Properties
    {
        _BaseColor ("Back Color", Color) = (0.35,0.42,0.45,1)
        _BellyColor ("Belly Color", Color) = (0.85,0.85,0.8,1)
        _PatternColor ("Pattern Color", Color) = (0.1,0.1,0.12,1)
        _PatternType ("Pattern (0 none,1 bars,2 spots,3 stripe,4 speckle)", Float) = 1
        _PatternScale ("Pattern Scale", Float) = 6
        _HalfHeight ("Body Half Height (OS)", Float) = 0.12
        _EyePos ("Eye Pos (OS, x mirrored)", Vector) = (0.05,0.03,0.38,0)
        _EyeRadius ("Eye Radius (OS)", Float) = 0.025
        _Smoothness ("Smoothness", Range(0,1)) = 0.55
        _Translucency ("Translucency", Range(0,1)) = 0
        _SwimPhase ("Swim Phase", Float) = 0
        _SwimAmp ("Swim Amplitude (OS)", Float) = 0.06
        _WaveFreq ("Wave Frequency", Float) = 1.1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "OctoCommon.hlsl"
        #include "OctoUnderwater.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _BellyColor;
            half4 _PatternColor;
            float _PatternType;
            float _PatternScale;
            float _HalfHeight;
            float4 _EyePos;
            float _EyeRadius;
            half _Smoothness;
            half _Translucency;
            float _SwimPhase;
            float _SwimAmp;
            float _WaveFreq;
        CBUFFER_END

        // Волна бежит от головы к хвосту, амплитуда растёт к хвосту
        float3 SwimDisplace(float3 p)
        {
            float t = saturate((0.3 - p.z) / 0.8);
            p.x += sin(p.z * _WaveFreq * 6.2832 - _SwimPhase) * _SwimAmp * t * t;
            // голова слегка «рыскает» в противофазе — так рыба выглядит живой
            p.x += sin(_SwimPhase) * _SwimAmp * 0.12 * saturate(p.z * 2.0);
            return p;
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
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                half body         : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 p = SwimDisplace(v.positionOS.xyz);
                VertexPositionInputs pi = GetVertexPositionInputs(p);
                o.positionCS = pi.positionCS;
                o.positionWS = pi.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS.xyz;
                o.body = v.color.r;
                return o;
            }

            half Pattern(float3 p)
            {
                float s = _PatternScale;
                float wob = OctoValueNoise(p * s * 1.7) * 0.6;
                if (_PatternType < 0.5) return 0;
                if (_PatternType < 1.5) // поперечные полосы
                    return smoothstep(0.25, 0.45, sin((p.z + wob * 0.08) * s * 6.2832) * 0.5 + 0.5) * smoothstep(-0.6, 0.2, p.y / _HalfHeight);
                if (_PatternType < 2.5) // пятна
                    return smoothstep(0.62, 0.7, OctoValueNoise(p * s * 2.0 + 13.1));
                if (_PatternType < 3.5) // продольная полоса по боку
                    return 1.0 - smoothstep(0.08, 0.2, abs(p.y / _HalfHeight - 0.05 + wob * 0.15));
                // мелкий крап
                return smoothstep(0.55, 0.75, OctoValueNoise(p * s * 6.0)) * 0.8;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS) * (IS_FRONT_VFACE(face, 1, -1));
                float3 p = i.positionOS;

                // противотень: тёмная спина, светлое брюхо
                half belly = 1.0 - smoothstep(-0.55, 0.35, p.y / max(1e-4, _HalfHeight));
                half3 albedo = lerp(_BaseColor.rgb, _BellyColor.rgb, belly);
                albedo *= 0.9 + 0.2 * OctoValueNoise(p * 40.0); // мелкая неоднородность чешуи
                albedo = lerp(albedo, _PatternColor.rgb, Pattern(p) * i.body);

                // плавники светлее и чуть прозрачнее на вид
                albedo = lerp(lerp(_BaseColor.rgb, _BellyColor.rgb, 0.45) * 0.95, albedo, i.body);

                // глаз: тёмный зрачок со светлым ободком
                float3 e = float3(abs(p.x), p.y, p.z) - _EyePos.xyz;
                float ed = length(e) / max(1e-4, _EyeRadius);
                half smooth = _Smoothness;
                if (ed < 1.0 && i.body > 0.5)
                {
                    albedo = lerp(half3(0.02, 0.02, 0.02), half3(0.75, 0.68, 0.45), smoothstep(0.55, 0.75, ed));
                    smooth = 0.9;
                }

                half3 col = UW_Shade(albedo, i.positionWS, n, i.positionCS, smooth, 1.0);
                // креветки и мальки просвечивают: подмешиваем цвет воды
                col = lerp(col, UW_WaterColorAt(i.positionWS) * 0.9 + albedo * 0.15, _Translucency * 0.5);
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
            #pragma multi_compile_instancing
            float3 _LightDirection;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 posWS = TransformObjectToWorld(SwimDisplace(v.positionOS.xyz));
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
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
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v) { Varyings o; UNITY_SETUP_INSTANCE_ID(v); o.positionCS = TransformObjectToHClip(SwimDisplace(v.positionOS.xyz)); return o; }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(SwimDisplace(v.positionOS.xyz));
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
