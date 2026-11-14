// Предметы рифа из Tripo (кораллы, водоросли, губки, раковины): обычная UV-развёртка,
// карты цвета/нормалей/шероховатости + общая подводная оптика.
// Водоросли и анемоны качаются в течении: смещение растёт с высотой (модель нормализована, низ y = 0, высота ~1).
Shader "OctoEvo/PropLit"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        [Normal] _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 2)) = 1
        _RoughnessMap ("Roughness (R)", 2D) = "gray" {}
        _SmoothnessScale ("Smoothness Scale", Range(0, 1.5)) = 0.8
        _Translucency ("Back-light (thin leaves)", Range(0, 1)) = 0
        [Header(Sway)]
        _SwayAmount ("Sway Amount (доли высоты)", Range(0, 0.5)) = 0
        _SwaySpeed ("Sway Speed", Float) = 1
        _SwayFlex ("Flex Exponent", Range(1, 4)) = 2
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

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
        TEXTURE2D(_RoughnessMap); SAMPLER(sampler_RoughnessMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _NormalStrength;
            half _SmoothnessScale;
            half _Translucency;
            float _SwayAmount;
            float _SwaySpeed;
            float _SwayFlex;
        CBUFFER_END

        // Качание в мировых координатах: фаза зависит от положения, чтобы соседние кусты не шли в ногу
        float3 SwayWS(float3 posOS, float3 posWS)
        {
            if (_SwayAmount <= 0.0) return posWS;
            float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
            float3 topWS = TransformObjectToWorld(float3(0, 1, 0));
            float height = max(1e-3, distance(originWS, topWS));
            float h = pow(saturate(posOS.y), _SwayFlex);
            float t = _Time.y * _SwaySpeed;
            float ph = dot(originWS.xz, float2(0.73, 0.41)) + posOS.y * 1.7;
            float2 dir = float2(sin(t + ph), cos(t * 0.71 + ph * 1.3)) * 0.7
                       + float2(sin(t * 2.3 + ph * 2.0), sin(t * 1.9 + ph)) * 0.3;
            posWS.xz += dir * _SwayAmount * height * h;
            return posWS;
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
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 tangentWS  : TEXCOORD2;
                float2 uv         : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 posWS = SwayWS(v.positionOS.xyz, TransformObjectToWorld(v.positionOS.xyz));
                o.positionWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                VertexNormalInputs ni = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.normalWS = ni.normalWS;
                o.tangentWS = float4(ni.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float sgn = IS_FRONT_VFACE(face, 1, -1);
                float3 n = normalize(i.normalWS) * sgn;
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv), _NormalStrength);
                float3 t = normalize(i.tangentWS.xyz);
                float3 b = cross(n, t) * i.tangentWS.w;
                float3 nWS = normalize(mul(nTS, float3x3(t, b, n)));
                half rough = SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, i.uv).r;
                half smooth = saturate((1.0 - rough) * _SmoothnessScale);

                half3 col = UW_Shade(albedo, i.positionWS, nWS, i.positionCS, smooth, 1.0);
                // тонкие листья просвечивают солнцем сзади
                if (_Translucency > 0)
                {
                    Light sun = GetMainLight();
                    half back = saturate(dot(-nWS, sun.direction));
                    half3 through = albedo * UW_SunlightAtDepth(i.positionWS) * sun.color * back * _Translucency * 0.35;
                    col += UW_ApplyWater(through, i.positionWS) - UW_ApplyWater(0, i.positionWS);
                }
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
                float3 posWS = SwayWS(v.positionOS.xyz, TransformObjectToWorld(v.positionOS.xyz));
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
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformWorldToHClip(SwayWS(v.positionOS.xyz, TransformObjectToWorld(v.positionOS.xyz)));
                return o;
            }
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
                o.positionCS = TransformWorldToHClip(SwayWS(v.positionOS.xyz, TransformObjectToWorld(v.positionOS.xyz)));
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
