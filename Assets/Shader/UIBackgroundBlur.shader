// UI 이미지 뒤에 보이는 화면(게임 화면 + 그 아래 UI)을 흐리게 보여 주는 쉐이더. 반투명 유리판 같은 팝업 배경용 (URP 2D 렌더러 전용)
// 렌더러의 Camera Sorting Layer Texture 가 켜져 있어야 하고(Foremost Sorting Layer = Default),
// 이 머티리얼을 쓰는 Image 는 Default 보다 뒤에 그려지는 정렬 레이어(UI)에 있어야 한다.
// 다른 UI 와 같은 Canvas 안에 있으면 그 오브젝트에 Canvas(Override Sorting, Sorting Layer = UI)를 붙여 분리한다.
// Canvas 는 Screen Space - Camera 여야 한다. Overlay 에서는 화면 텍스처를 읽을 수 없다.
// Image 의 Color 는 흐린 화면에 곱해지는 색, Color 의 알파는 투명도로 쓰인다. 모양은 Image 스프라이트의 알파를 따른다.
Shader "Custom/UI Background Blur"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _BlurSize ("Blur Size (pixels)", Range(0, 32)) = 8

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
                float4 screenPosition : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_CameraSortingLayerTexture);
            SamplerState blur_linear_clamp_sampler;

            float4 _ClipRect;

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
                output.screenPosition = ComputeScreenPos(output.positionCS);
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }

            // 5x5 가우시안. 가장자리 탭이 _BlurSize 픽셀 떨어진 곳
            half3 SampleBlurredScreen(float2 screenUV)
            {
                float2 pixelStep = max(_BlurSize, 0.0) * 0.5 / _ScaledScreenParams.xy;

                half3 sum = 0;
                half totalWeight = 0;
                [unroll] for (int y = -2; y <= 2; y++)
                {
                    [unroll] for (int x = -2; x <= 2; x++)
                    {
                        half weight = exp(-(x * x + y * y) / 3.0);
                        sum += SAMPLE_TEXTURE2D(_CameraSortingLayerTexture, blur_linear_clamp_sampler, screenUV + float2(x, y) * pixelStep).rgb * weight;
                        totalWeight += weight;
                    }
                }
                return sum / totalWeight;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                half shape = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                half alpha = shape * input.color.a;

            #ifdef UNITY_UI_CLIP_RECT
                float2 inside = step(_ClipRect.xy, input.canvasPosition.xy) * step(input.canvasPosition.xy, _ClipRect.zw);
                alpha *= inside.x * inside.y;
            #endif

            #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
            #endif

                float2 screenUV = input.screenPosition.xy / input.screenPosition.w;
                return half4(SampleBlurredScreen(screenUV) * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
