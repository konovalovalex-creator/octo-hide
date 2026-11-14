// Поверхность воды, видимая снизу: «окно Снеллиуса» (светлый круг неба над головой),
// полное внутреннее отражение по краям и бегущая рябь.
Shader "OctoEvo/WaterSurfaceUnder"
{
    Properties
    {
        _SkyColor ("Sky Through Water", Color) = (0.75, 0.95, 1, 1)
        _ReflectColor ("Internal Reflection", Color) = (0.05, 0.3, 0.38, 1)
        _RippleScale ("Ripple Scale", Float) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" "RenderPipeline"="UniversalPipeline" }
        Cull Off

        Pass
        {
            Name "WaterSurface"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "OctoCommon.hlsl"
            #include "OctoUnderwater.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SkyColor;
                half4 _ReflectColor;
                float _RippleScale;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float3 p = float3(i.positionWS.x, 0, i.positionWS.z) * _RippleScale;
                float r1 = OctoValueNoise(p + float3(t * 0.4, 0, t * 0.3));
                float r2 = OctoValueNoise(p * 2.3 + float3(-t * 0.5, 0, t * 0.6));
                float ripple = (r1 + r2) * 0.5;

                float3 viewDir = normalize(i.positionWS - _WorldSpaceCameraPos.xyz);
                // окно Снеллиуса: небо видно только в конусе ~48° от вертикали
                float up = viewDir.y + (ripple - 0.5) * 0.12;
                half window = smoothstep(0.62, 0.72, up);
                half3 col = lerp(_ReflectColor.rgb, _SkyColor.rgb * (0.8 + ripple * 0.5), window);
                col += smoothstep(0.55, 0.75, ripple) * window * 0.3;
                col = UW_ApplyWater(col, i.positionWS);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
