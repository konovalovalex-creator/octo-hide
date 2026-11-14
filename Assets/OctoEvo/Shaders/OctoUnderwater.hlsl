#ifndef OCTO_UNDERWATER_INCLUDED
#define OCTO_UNDERWATER_INCLUDED

// Подводная оптика, общая для всех наших шейдеров.
// Глобальные параметры задаёт компонент UnderwaterEnvironment (Shader.SetGlobal*).
//
//  - UW_SunlightAtDepth: солнечный свет, дошедший сверху до точки (красный гаснет первым)
//  - UW_Caustics:        световая сетка от волн, сверху, слабеет с глубиной
//  - UW_ApplyWater:      поглощение и рассеяние по пути от точки до камеры

float4 _UW_WaterColor;     // цвет рассеянного света в воде у поверхности
float4 _UW_Absorption;     // поглощение на метр, по каналам RGB
float  _UW_Scatter;        // рассеяние на метр (мутность), одинаково для всех каналов
float  _UW_SurfaceY;       // высота поверхности воды
float  _UW_CausticsStrength;
float  _UW_CausticsScale;
TEXTURE2D(_UW_CausticsTex); SAMPLER(sampler_UW_CausticsTex);

float UW_DepthBelowSurface(float3 positionWS) { return max(0.0, _UW_SurfaceY - positionWS.y); }

half3 UW_SunlightAtDepth(float3 positionWS)
{
    return exp(-_UW_Absorption.rgb * UW_DepthBelowSurface(positionWS));
}

half3 UW_WaterColorAt(float3 positionWS)
{
    // чем глубже, тем темнее и синее сама вода
    return _UW_WaterColor.rgb * exp(-_UW_Absorption.rgb * 0.6 * UW_DepthBelowSurface(positionWS));
}

half UW_Caustics(float3 positionWS, float3 normalWS)
{
    float2 uv = positionWS.xz * _UW_CausticsScale;
    float t = _Time.y;
    half c1 = SAMPLE_TEXTURE2D(_UW_CausticsTex, sampler_UW_CausticsTex, uv + float2(0.031, 0.017) * t).r;
    half c2 = SAMPLE_TEXTURE2D(_UW_CausticsTex, sampler_UW_CausticsTex, uv * 1.37 + float2(-0.023, 0.029) * t).r;
    half c = min(c1, c2);
    half facingUp = saturate(normalWS.y * 0.8 + 0.2);
    half depthFade = exp(-UW_DepthBelowSurface(positionWS) * 0.07);
    return c * facingUp * depthFade * _UW_CausticsStrength;
}

half3 UW_ApplyWater(half3 color, float3 positionWS)
{
    float d = distance(_WorldSpaceCameraPos.xyz, positionWS);
    half3 transmittance = exp(-(_UW_Absorption.rgb + _UW_Scatter) * d);
    half3 water = UW_WaterColorAt(lerp(positionWS, _WorldSpaceCameraPos.xyz, 0.5));
    return color * transmittance + water * (1.0 - transmittance);
}

// Нормаль из триплэнарной карты нормалей (whiteout blend, Ben Golus)
float3 UW_TriplanarNormal(TEXTURE2D_PARAM(tex, smp), float3 pos, float3 n, float scale, float strength)
{
    float3 w = pow(abs(n), 4.0);
    w /= max(dot(w, 1.0), 1e-4);
    float3 p = pos * scale;
    half3 tx = UnpackNormalScale(SAMPLE_TEXTURE2D(tex, smp, p.zy), strength);
    half3 ty = UnpackNormalScale(SAMPLE_TEXTURE2D(tex, smp, p.xz), strength);
    half3 tz = UnpackNormalScale(SAMPLE_TEXTURE2D(tex, smp, p.xy), strength);
    tx = half3(tx.xy + n.zy, abs(tx.z) * n.x);
    ty = half3(ty.xy + n.xz, abs(ty.z) * n.y);
    tz = half3(tz.xy + n.xy, abs(tz.z) * n.z);
    return normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
}

// Полное подводное освещение поверхности: PBR от главного источника + амбиент + каустика + вода
half3 UW_Shade(half3 albedo, float3 positionWS, float3 normalWS, float4 positionCS, half smoothness, half occlusion)
{
    half3 sun = UW_SunlightAtDepth(positionWS);
    half3 litAlbedo = albedo * sun;

    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalWS = normalWS;
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
    inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
    inputData.fogCoord = 0;
    inputData.vertexLighting = half3(0, 0, 0);
    inputData.bakedGI = SampleSH(normalWS) * sun;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    inputData.shadowMask = half4(1, 1, 1, 1);

    SurfaceData surface = (SurfaceData)0;
    surface.albedo = litAlbedo;
    surface.metallic = 0;
    surface.specular = half3(0, 0, 0);
    surface.smoothness = smoothness;
    surface.normalTS = half3(0, 0, 1);
    surface.occlusion = occlusion;
    surface.alpha = 1;

    half3 color = UniversalFragmentPBR(inputData, surface).rgb;

    Light mainLight = GetMainLight(inputData.shadowCoord);
    color += litAlbedo * mainLight.color * mainLight.shadowAttenuation * UW_Caustics(positionWS, normalWS);

    return UW_ApplyWater(color, positionWS);
}

#endif
