// UI Image 의 그림 자체를 흐리게 그리는 쉐이더. 머티리얼만 넣으면 되고 다른 설정은 필요 없다.
// Image 의 Color, Mask, RectMask2D 를 기본 UI 쉐이더처럼 반영한다.
// 이미지 사각형 밖으로는 번지지 않으므로, 그림이 가장자리까지 꽉 찬 스프라이트는 테두리가 잘려 보일 수 있다.
Shader "Custom/UI Image Blur"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _BlurSize ("Blur Size (texels)", Range(0, 16)) = 1

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 canvasPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            SamplerState blur_linear_clamp_sampler;

            float4 _ClipRect;
            half4 _TextureSampleAdd; // 폰트 텍스처(Alpha8)용, 일반 스프라이트는 0

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _BlurSize;
            CBUFFER_END

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.canvasPosition = input.positionOS;
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }

            half4 SampleBlurred(float2 uv)
            {
                float width, height;
                _MainTex.GetDimensions(width, height);
                float2 texelStep = max(_BlurSize, 0.0) * 0.5 / float2(width, height); // 5x5 탭의 가장자리가 _BlurSize 텍셀

                half4 sum = 0;
                half totalWeight = 0;
                [unroll] for (int y = -2; y <= 2; y++)
                {
                    [unroll] for (int x = -2; x <= 2; x++)
                    {
                        half weight = exp(-(x * x + y * y) / 3.0);
                        float2 sampleUV = uv + float2(x, y) * texelStep;
                        half4 texel = SAMPLE_TEXTURE2D(_MainTex, blur_linear_clamp_sampler, sampleUV);
                        // 텍스처 밖은 가장자리 픽셀을 늘리지 않고 투명하게 본다 (그림이 가장자리에 닿아도 부드럽게 빠지도록)
                        float2 inside = step(0.0, sampleUV) * step(sampleUV, 1.0);
                        texel *= inside.x * inside.y;
                        // 투명한 픽셀의 색이 섞여 테두리가 검게 번지지 않도록 알파를 곱해서 합친다
                        sum += half4(texel.rgb * texel.a, texel.a) * weight;
                        totalWeight += weight;
                    }
                }
                sum /= totalWeight;
                return half4(sum.a > 0.0001 ? sum.rgb / sum.a : 0, sum.a);
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                // 0 이면 텍스처 원래 필터(도트는 Point)로 그대로 그린다
                half4 source = _BlurSize > 0.0 ? SampleBlurred(input.uv) : SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 color = (source + _TextureSampleAdd) * input.color;

            #ifdef UNITY_UI_CLIP_RECT
                float2 inside = step(_ClipRect.xy, input.canvasPosition.xy) * step(input.canvasPosition.xy, _ClipRect.zw);
                color.a *= inside.x * inside.y;
            #endif

            #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
            #endif

                return color;
            }
            ENDHLSL
        }
    }
}
