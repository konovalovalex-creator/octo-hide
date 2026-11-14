#ifndef OCTO_COMMON_INCLUDED
#define OCTO_COMMON_INCLUDED

// Общие функции: шум, триплэнарная проекция.
// ВАЖНО: окружение и кожа используют ОДИНАКОВУЮ триплэнарную проекцию в мировых координатах —
// поэтому неподвижный осьминог, скопировавший узор поверхности, совпадает с ней пиксель в пиксель.

float OctoHash31(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float OctoValueNoise(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(OctoHash31(i + float3(0,0,0)), OctoHash31(i + float3(1,0,0)), f.x),
                     lerp(OctoHash31(i + float3(0,1,0)), OctoHash31(i + float3(1,1,0)), f.x), f.y),
                lerp(lerp(OctoHash31(i + float3(0,0,1)), OctoHash31(i + float3(1,0,1)), f.x),
                     lerp(OctoHash31(i + float3(0,1,1)), OctoHash31(i + float3(1,1,1)), f.x), f.y), f.z);
}

float OctoFbm(float3 p)
{
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 3; i++)
    {
        v += a * OctoValueNoise(p);
        p *= 2.03;
        a *= 0.5;
    }
    return v;
}

float3 OctoTriplanarWeights(float3 n)
{
    float3 w = pow(abs(n), 4.0);
    return w / max(dot(w, 1.0), 1e-4);
}

half4 OctoTriplanar(TEXTURE2D_PARAM(tex, smp), float3 pos, float3 n, float scale)
{
    float3 w = OctoTriplanarWeights(n);
    float3 p = pos * scale;
    half4 cx = SAMPLE_TEXTURE2D(tex, smp, p.zy);
    half4 cy = SAMPLE_TEXTURE2D(tex, smp, p.xz);
    half4 cz = SAMPLE_TEXTURE2D(tex, smp, p.xy);
    return cx * w.x + cy * w.y + cz * w.z;
}

#endif
