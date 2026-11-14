// Луч солнечного света сквозь воду: аддитивная полупрозрачная плоскость с мягкими краями и мерцанием.
Shader "OctoEvo/GodRay"
{
    Properties
    {
        _Color ("Color", Color) = (0.55, 0.85, 1, 1)
        _Intensity ("Intensity", Range(0, 2)) = 0.35
        _Seed ("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "GodRay"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "OctoCommon.hlsl"

            float _UW_SurfaceY;

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                float _Seed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                // мягкие края по ширине, затухание к низу луча
                half across = sin(saturate(i.uv.x) * PI);
                across *= across;
                half along = pow(saturate(i.uv.y), 1.3);
                // медленное мерцание и «волокна»
                half flicker = OctoValueNoise(float3(i.uv.x * 3.0 + _Seed, t * 0.25, _Seed)) * 0.6 + 0.4;
                half fibers = OctoValueNoise(float3(i.uv.x * 14.0 + _Seed * 3.0, i.uv.y * 1.5 - t * 0.1, 0)) * 0.5 + 0.5;
                // не светить прямо в камеру и гаснуть вдали
                float d = distance(i.positionWS, _WorldSpaceCameraPos.xyz);
                half nearFade = saturate((d - 1.5) / 5.0);
                half farFade = saturate(1.0 - d / 70.0);
                half depthFade = exp(-max(0.0, _UW_SurfaceY - i.positionWS.y) * 0.05);
                half a = across * along * flicker * fibers * nearFade * farFade * depthFade * _Intensity;
                return half4(_Color.rgb * a, 1);
            }
            ENDHLSL
        }
    }
}
