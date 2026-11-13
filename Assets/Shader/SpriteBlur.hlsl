#ifndef TACTICALARENA_SPRITE_BLUR_INCLUDED
#define TACTICALARENA_SPRITE_BLUR_INCLUDED

// _MainTex 를 가우시안으로 흐리게 샘플링한다. _MainTex 선언(2D 공통 include) 뒤에 include 해야 한다.
// 도트 텍스처(Point 필터)도 부드럽게 번지도록 선형 샘플러를 따로 쓴다.
SamplerState blur_linear_clamp_sampler;

half4 SampleSpriteBlur(float2 uv, float blurSize)
{
    if (blurSize <= 0.0)
        return SAMPLE_TEXTURE2D(_MainTex, blur_linear_clamp_sampler, uv);

    float width, height;
    _MainTex.GetDimensions(width, height);
    float2 step = blurSize * 0.5 / float2(width, height); // 5x5 탭의 가장자리가 blurSize 텍셀

    half4 sum = 0;
    half totalWeight = 0;
    [unroll] for (int y = -2; y <= 2; y++)
    {
        [unroll] for (int x = -2; x <= 2; x++)
        {
            half weight = exp(-(x * x + y * y) / 3.0);
            sum += SAMPLE_TEXTURE2D(_MainTex, blur_linear_clamp_sampler, uv + float2(x, y) * step) * weight;
            totalWeight += weight;
        }
    }
    return sum / totalWeight;
}

#endif
