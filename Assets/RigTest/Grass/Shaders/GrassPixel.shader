// 메시는 그대로 두고 UV를 텍셀 단위로 밀어서 도트가 깨지지 않게 흔드는 잔디.
// 휘는 만큼 스프라이트 둘레에 투명한 여백이 있어야 하고, Full Rect 메시를 써야 한다.
// _VelocityStrength, _WindStrength 단위는 텍셀.
Shader "RigTest/Grass Pixel"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _VelocityStrength ("Velocity Strength", Float) = 7
        _WindVelocity ("Wind Velocity", Vector) = (0.25, 0, 0, 0)
        _WindStrength ("Wind Strength", Float) = 2
        _WindScale ("Wind Scale", Float) = 1.5
        _PixelsPerUnit ("Pixels Per Unit", Float) = 64

        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
                float3 worldAndFlip : TEXCOORD1; // xy: 월드 위치, z: 좌우 반전
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"
            #include "GrassCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _VelocityStrength;
                float2 _WindVelocity;
                float _WindStrength;
                float _WindScale;
                float _PixelsPerUnit;
            CBUFFER_END

            Varyings Vertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings output = CommonUnlitVertex(input);
                output.color = input.color * _Color * unity_SpriteColor;
                output.worldAndFlip = float3(TransformObjectToWorld(input.positionOS).xy, unity_SpriteProps.x);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float width, height;
                _MainTex.GetDimensions(width, height);
                float2 textureSize = float2(width, height);
                float2 texel = floor(input.uv * textureSize);

                // 같은 도트 안에서는 같은 값이 나오도록 위치를 격자에 맞춘다
                float2 snappedWorld = floor(input.worldAndFlip.xy * _PixelsPerUnit) / _PixelsPerUnit;
                float weight = pow(saturate((texel.y + 0.5) / height), 1.5);

                float2 displacement = GrassDisplacement(snappedWorld, _WindScale, _WindVelocity, _WindStrength, _VelocityStrength) * weight;
                displacement = round(displacement);

                float2 sampleTexel = texel + 0.5 - float2(displacement.x * input.worldAndFlip.z, -displacement.y);
                input.uv = sampleTexel / textureSize;

                return CommonUnlitFragment(input, input.color);
            }
            ENDHLSL
        }
    }
}
