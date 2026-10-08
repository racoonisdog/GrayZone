// 바라본 대상의 외곽선입니다. 메시를 법선 방향으로 화면 픽셀 단위만큼 부풀려 그리고,
// FocusOutlineMask가 표시한 대상 영역은 건너뛰어 바깥 테두리만 남깁니다.
// 굵기는 거리와 관계없이 화면에서 같은 픽셀 수로 보입니다.
Shader "GrayZone/FocusOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 0.85, 0.2, 1)
        _OutlineWidth ("Outline Width (px)", Range(0, 10)) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+51" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "FocusOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            Stencil
            {
                Ref 64
                ReadMask 64
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float2 normalCS = mul((float3x3)GetWorldToHClipMatrix(), normalWS).xy;

                // 클립 공간에서 밀면 w로 나뉜 뒤 화면 크기가 거리와 무관해집니다. 2/화면 크기는 NDC 한 픽셀입니다.
                float lengthCS = length(normalCS);
                if (lengthCS > 1e-5)
                {
                    float2 offset = normalCS / lengthCS * _OutlineWidth * 2.0 / _ScreenParams.xy;
                    positionCS.xy += offset * positionCS.w;
                }

                output.positionCS = positionCS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
