// 잔디 속도 필드 시뮬레이션. 픽셀마다 스프링 하나가 들어 있다고 보고 한 프레임씩 계산한다.
// R,G = 변위 / B,A = 속도
// pixelgraphics: Copyright (c) 2021 Jacob, MIT License (https://github.com/aarthificial/pixelgraphics)
// 스프링 계산은 aarthificial/pixelgraphics(MIT)의 VelocitySimulation.hlsl 을 기반으로 했다.
Shader "Hidden/RigTest/GrassVelocitySim"
{
    Properties
    {
        _MainTex ("Previous", 2D) = "black" {}
    }

    SubShader
    {
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment Fragment
            #include "UnityCG.cginc"

            #define MAX_EMITTERS 8
            #define MAX_DISPLACEMENT 1.5

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            float4 _SimParams;   // x: 강성, y: 감쇠, z: 번짐, w: 이번 프레임 dt
            float4 _FieldRect;   // xy: 월드 원점, zw: 월드 크기
            float2 _ScrollUV;    // 필드가 카메라를 따라 움직인 만큼 (UV)

            float _EmitterCount;
            float4 _EmitterRects[MAX_EMITTERS];    // xy: 왼쪽 아래, zw: 오른쪽 위 (월드)
            float4 _EmitterVelocities[MAX_EMITTERS]; // xy: 속도

            float4 Previous(float2 uv)
            {
                return tex2D(_MainTex, uv + _ScrollUV);
            }

            float4 Fragment(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float2 texel = _MainTex_TexelSize.xy;

                float4 data = Previous(uv);
                float4 neighbours = Previous(uv + float2(texel.x, 0))
                                  + Previous(uv - float2(texel.x, 0))
                                  + Previous(uv + float2(0, texel.y))
                                  + Previous(uv - float2(0, texel.y));
                data = lerp(data, neighbours * 0.25, _SimParams.z);

                float2 displacement = data.xy;
                float2 velocity = data.zw;

                // 필드 가장자리는 항상 0으로 고정
                if (uv.x < texel.x || uv.x > 1 - texel.x || uv.y < texel.y || uv.y > 1 - texel.y)
                {
                    displacement = 0;
                    velocity = 0;
                }

                float dt = _SimParams.w;
                float2 acceleration = -displacement * _SimParams.x - velocity * _SimParams.y;
                velocity += acceleration * dt;
                displacement += velocity * dt;
                displacement = clamp(displacement, -MAX_DISPLACEMENT, MAX_DISPLACEMENT);

                // 움직이는 물체와 겹친 픽셀의 속도를 덮어쓴다
                float2 world = _FieldRect.xy + uv * _FieldRect.zw;
                for (int e = 0; e < _EmitterCount; e++)
                {
                    float4 rect = _EmitterRects[e];
                    if (world.x < rect.x || world.y < rect.y || world.x > rect.z || world.y > rect.w)
                        continue;

                    float2 emitted = _EmitterVelocities[e].xy;
                    if (dot(emitted, emitted) <= 0)
                        continue;

                    // 잔디는 좌우로만 휘므로, 위아래로 지나갈 때는 중심에서 바깥쪽으로 밀어 헤치게 한다
                    float side = world.x < (rect.x + rect.z) * 0.5 ? -1 : 1;
                    velocity.x = emitted.x + abs(emitted.y) * side;
                    velocity.y = emitted.y;
                }

                return float4(displacement, velocity);
            }
            ENDHLSL
        }
    }
}
