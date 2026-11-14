// Окружение: PBR + триплэнарные текстуры в мировых координатах + подводная оптика.
// Свойства _BaseMap / _BaseColor / _Scale читает CamoSurface, когда осьминог копирует окрас.
Shader "OctoEvo/EnvironmentLit"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        [Normal] _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 2)) = 1
        _Scale ("Texture Scale (1/м)", Float) = 0.5
        _Smoothness ("Smoothness", Range(0, 1)) = 0.25
        _Occlusion ("Cavity Occlusion", Range(0, 1)) = 0.5

        [Header(Second layer (algae, gravel, moss))]
        [Toggle(_LAYER2)] _Layer2 ("Enable Layer 2", Float) = 0
        _Base2Map ("Albedo 2", 2D) = "white" {}
        [Normal] _Normal2Map ("Normal Map 2", 2D) = "bump" {}
        _Scale2 ("Texture Scale 2 (1/м)", Float) = 0.5
        _BlendNoiseScale ("Patch Size (1/м)", Float) = 0.15
        _BlendAmount ("Coverage", Range(0, 1)) = 0.4
        _BlendUp ("Prefer Top-Facing", Range(0, 1)) = 0
        _BlendSharpness ("Edge Sharpness", Range(1, 20)) = 6
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "OctoCommon.hlsl"
        #include "OctoUnderwater.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
        TEXTURE2D(_Base2Map); SAMPLER(sampler_Base2Map);
        TEXTURE2D(_Normal2Map); SAMPLER(sampler_Normal2Map);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _NormalStrength;
            float _Scale;
            half _Smoothness;
            half _Occlusion;
            float _Scale2;
            float _BlendNoiseScale;
            half _BlendAmount;
            half _BlendUp;
            half _BlendSharpness;
        CBUFFER_END
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
            #pragma shader_feature_local _LAYER2

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                half4 albedoTex = OctoTriplanar(TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap), i.positionWS, n, _Scale);
                half3 albedo = albedoTex.rgb * _BaseColor.rgb;
                float3 nWS = UW_TriplanarNormal(TEXTURE2D_ARGS(_NormalMap, sampler_NormalMap), i.positionWS, n, _Scale, _NormalStrength);
            #if defined(_LAYER2)
                // Пятна второго слоя: шум в мире + (по желанию) только сверху — мох и водоросли растут к свету
                float patch = OctoFbm(i.positionWS * _BlendNoiseScale);
                float m = patch + (_BlendAmount - 0.5) + (n.y - 0.5) * _BlendUp;
                m = saturate((m - 0.5) * _BlendSharpness + 0.5);
                if (m > 0.001)
                {
                    half4 tex2 = OctoTriplanar(TEXTURE2D_ARGS(_Base2Map, sampler_Base2Map), i.positionWS, n, _Scale2);
                    float3 n2 = UW_TriplanarNormal(TEXTURE2D_ARGS(_Normal2Map, sampler_Normal2Map), i.positionWS, n, _Scale2, _NormalStrength);
                    albedoTex = lerp(albedoTex, tex2, m);
                    albedo = albedoTex.rgb * _BaseColor.rgb;
                    nWS = normalize(lerp(nWS, n2, m));
                }
            #endif
                // тёмные участки текстуры — впадины: чуть приглушаем амбиент
                half occlusion = lerp(1.0, saturate(Luminance(albedoTex.rgb) * 1.6), _Occlusion);
                half3 col = UW_Shade(albedo, i.positionWS, nWS, i.positionCS, _Smoothness, occlusion);
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
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
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
            Varyings vert(Attributes v) { Varyings o; UNITY_SETUP_INSTANCE_ID(v); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
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
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0.0); }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
