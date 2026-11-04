#ifndef RIGTEST_GRASS_COMMON_INCLUDED
#define RIGTEST_GRASS_COMMON_INCLUDED

// 잔디 쉐이더 공통 계산. 수식은 aarthificial/pixelgraphics(MIT)의 grass 쉐이더 그래프를 옮긴 것이다.
// pixelgraphics: Copyright (c) 2021 Jacob, MIT License (https://github.com/aarthificial/pixelgraphics)
//
// Simplex noise: Copyright (C) 2011 Ashima Arts, MIT License (https://github.com/ashima/webgl-noise)
// Keijiro Takahashi 의 포팅을 줄여서 사용했다.

TEXTURE2D(_GrassVelocityTexture);
SAMPLER(sampler_GrassVelocityTexture);
float4 _GrassFieldRect; // xy: 월드 원점, zw: 월드 크기. 필드가 없으면 0

float3 GrassMod289(float3 x) { return x - floor(x / 289.0) * 289.0; }
float4 GrassMod289(float4 x) { return x - floor(x / 289.0) * 289.0; }
float4 GrassPermute(float4 x) { return GrassMod289((x * 34.0 + 1.0) * x); }
float4 GrassInvSqrt(float4 r) { return 1.79284291400159 - r * 0.85373472095314; }

float GrassNoise(float3 v)
{
    const float2 C = float2(1.0 / 6.0, 1.0 / 3.0);

    float3 i = floor(v + dot(v, C.yyy));
    float3 x0 = v - i + dot(i, C.xxx);

    float3 g = step(x0.yzx, x0.xyz);
    float3 l = 1.0 - g;
    float3 i1 = min(g.xyz, l.zxy);
    float3 i2 = max(g.xyz, l.zxy);

    float3 x1 = x0 - i1 + C.xxx;
    float3 x2 = x0 - i2 + C.yyy;
    float3 x3 = x0 - 0.5;

    i = GrassMod289(i);
    float4 p = GrassPermute(GrassPermute(GrassPermute(i.z + float4(0.0, i1.z, i2.z, 1.0))
        + i.y + float4(0.0, i1.y, i2.y, 1.0))
        + i.x + float4(0.0, i1.x, i2.x, 1.0));

    float4 j = p - 49.0 * floor(p / 49.0);
    float4 xs = floor(j / 7.0);
    float4 ys = floor(j - 7.0 * xs);
    float4 x = (xs * 2.0 + 0.5) / 7.0 - 1.0;
    float4 y = (ys * 2.0 + 0.5) / 7.0 - 1.0;
    float4 h = 1.0 - abs(x) - abs(y);

    float4 b0 = float4(x.xy, y.xy);
    float4 b1 = float4(x.zw, y.zw);
    float4 s0 = floor(b0) * 2.0 + 1.0;
    float4 s1 = floor(b1) * 2.0 + 1.0;
    float4 sh = -step(h, 0.0);

    float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
    float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;

    float3 g0 = float3(a0.xy, h.x);
    float3 g1 = float3(a0.zw, h.y);
    float3 g2 = float3(a1.xy, h.z);
    float3 g3 = float3(a1.zw, h.w);

    float4 norm = GrassInvSqrt(float4(dot(g0, g0), dot(g1, g1), dot(g2, g2), dot(g3, g3)));
    g0 *= norm.x;
    g1 *= norm.y;
    g2 *= norm.z;
    g3 *= norm.w;

    float4 m = max(0.6 - float4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
    float4 m2 = m * m;
    float4 m4 = m2 * m2;
    float4 px = float4(dot(x0, g0), dot(x1, g1), dot(x2, g2), dot(x3, g3));
    return 42.0 * dot(m4, px);
}

// 좌우로 d 만큼 휠 때의 변위. 많이 휠수록 끝이 아래로 처진다 (x: 좌우, y: 처짐)
float2 GrassBend(float d)
{
    float clamped = clamp(d, -1.0, 1.0);
    return float2(clamped, (1.0 - cos(clamped * 2.0)) * 0.5);
}

// 바람 + 속도 필드를 합친 휨 (단위는 strength 를 따른다)
float2 GrassDisplacement(float2 worldPos, float windWorldScale, float2 windVelocity, float windStrength, float velocityStrength)
{
    float noise = GrassNoise(float3(worldPos + _Time.y * windVelocity, 0.0) * windWorldScale);
    float2 displacement = GrassBend(noise) * windStrength;

    if (_GrassFieldRect.z > 0.0)
    {
        float2 uv = (worldPos - _GrassFieldRect.xy) / _GrassFieldRect.zw;
        float d = SAMPLE_TEXTURE2D_LOD(_GrassVelocityTexture, sampler_GrassVelocityTexture, uv, 0).r;
        displacement += GrassBend(d) * velocityStrength;
    }

    return displacement;
}

#endif
