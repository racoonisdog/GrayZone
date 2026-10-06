// 바라본 대상의 외곽선을 그리기 전에 대상이 화면에서 차지하는 영역을 스텐실에 표시합니다.
// 색은 쓰지 않습니다. 외곽선 패스(FocusOutline)가 이 영역 밖에만 그려서, 반투명 청사진 안쪽으로
// 외곽선 색이 비쳐 보이지 않게 합니다.
Shader "GrayZone/FocusOutlineMask"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "FocusOutlineMask"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            ColorMask 0

            Stencil
            {
                Ref 64
                ReadMask 64
                WriteMask 64
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
