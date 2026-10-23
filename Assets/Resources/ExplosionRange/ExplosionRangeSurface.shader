// 폭발 범위 구체 안에 들어온 장면 표면(바닥, 벽, 물체)만 칠합니다.
// 구체 메시는 셰이더를 돌릴 화면 영역을 정할 뿐이고, 칠할지는 카메라 깊이 텍스처로 복원한
// 실제 표면 위치와 폭발 중심 사이의 거리로 정합니다. URP 에셋의 Depth Texture가 켜져 있어야 합니다.
//
// 두 번째 패스(ExclusionStencil)는 메시 렌더러가 그리지 않고, ExplosionRangeExclusionPass가
// 제외 Layer의 불투명 렌더러를 덮어 그릴 때만 씁니다. 그 영역의 스텐실 비트를 세우면
// 첫 번째 패스가 해당 픽셀을 건너뜁니다.
Shader "GrayZone/ExplosionRangeSurface"
{
    Properties
    {
        _BaseColor ("Fill Color", Color) = (1, 0.75, 0.1, 0.2)
        _RimColor ("Rim Color", Color) = (1, 0.75, 0.1, 0.9)
        _RimWidth ("Rim Width", Float) = 0.08
        _SphereCenterRadius ("Sphere Center (xyz) Radius (w)", Vector) = (0, 0, 0, 1)
        [IntRange] _ExclusionStencilBit ("Exclusion Stencil Bit", Range(1, 255)) = 8
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _RimColor;
            float _RimWidth;
            float4 _SphereCenterRadius; // xyz = 폭발 중심(월드), w = 반지름
            float _ExclusionStencilBit;
        CBUFFER_END

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
        ENDHLSL

        Pass
        {
            Name "ExplosionRangeSurface"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            // 뒷면만 그리고 깊이 테스트는 하지 않습니다. 카메라가 구체 안에 들어와도 화면 픽셀마다
            // 한 번씩만 실행되고, 판정은 구체 메시의 깊이가 아니라 장면 표면 위치로 하기 때문입니다.
            Cull Front
            ZTest Always
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            // 제외 Layer가 표시한 픽셀은 칠하지 않습니다.
            Stencil
            {
                Ref [_ExclusionStencilBit]
                ReadMask [_ExclusionStencilBit]
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
            #if UNITY_REVERSED_Z
                float depth = SampleSceneDepth(uv);
            #else
                float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
            #endif
                // 이 픽셀에 실제로 보이는 장면 표면의 월드 좌표입니다.
                float3 surfaceWS = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);

                float radius = _SphereCenterRadius.w;
                float dist = distance(surfaceWS, _SphereCenterRadius.xyz);
                clip(radius - dist);

                // 경계에 가까울수록 테두리 색으로 바뀝니다.
                float rim = smoothstep(radius - max(_RimWidth, 0.0001), radius, dist);
                return lerp(_BaseColor, _RimColor, rim);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ExclusionStencil"
            // URP가 메시 렌더러를 그릴 때 찾지 않는 LightMode라서 구체에는 그려지지 않습니다.
            Tags { "LightMode" = "ExplosionRangeExclusion" }

            Cull Back
            ZTest LEqual
            ZWrite Off
            // 같은 메시를 다시 그리므로 깊이가 미세하게 어긋나도 원래 표면을 통과하도록 앞으로 당깁니다.
            Offset -1, -1
            ColorMask 0

            Stencil
            {
                Ref [_ExclusionStencilBit]
                WriteMask [_ExclusionStencilBit]
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
