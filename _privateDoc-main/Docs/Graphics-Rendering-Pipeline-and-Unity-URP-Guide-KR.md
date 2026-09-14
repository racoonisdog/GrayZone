# 그래픽 렌더링 파이프라인과 Unity URP 이해 가이드

> 작성일: 2026-09-11  
> 대상 프로젝트: GrayZone  
> 문서 성격: 렌더링 이론 + 현재 프로젝트 검증 결과 + 이번 세션의 Medical Room 디더링 사례 정리  
> 기준 버전: Unity 6000.3.17f1 / Universal Render Pipeline 17.3.0

---

## 0. 문서 목적과 결론

이 문서는 다음 세 가지를 한 번에 이해할 수 있도록 구성했다.

1. 일반적인 실시간 그래픽 렌더링 파이프라인이 CPU와 GPU에서 어떻게 동작하는가
2. Unity의 Built-in Render Pipeline, URP, HDRP와 Shader Graph가 각각 어떤 역할을 하는가
3. 이번 세션에서 확인한 `Medical_Room_Layout_Set_0`의 거리 컬링과 디더링 문제를 어떤 구조로 해결하는 것이 안전한가

핵심 결론은 다음과 같다.

- **Shader Graph는 렌더 파이프라인이 아니라, 최종 ShaderLab/HLSL 셰이더를 생성하는 제작 도구다.**
- **Material은 셰이더 코드가 아니라, 특정 셰이더에 전달할 텍스처와 수치 값의 묶음이다.**
- 재질의 셰이더를 바꿀 때 프로퍼티 이름과 타입이 다르면 텍스처가 자동 이전되지 않는다.
- Medical Room은 27개 재질을 쓰지만 셰이더 계열은 5개다. 따라서 원칙적으로 필요한 것은 재질별 27개 셰이더가 아니라 **셰이더 계열별 디더 대응본 5개**다.
- 공용 디더 계산은 하나의 `.hlsl` 파일 또는 Shader Graph용 `.shadersubgraph`로 공유할 수 있다. 단, Opaque/Cutout/Transparent, 기존 텍스처 프로퍼티, 패스와 렌더 상태는 각 부모 셰이더가 유지해야 한다.
- 현재 거리 컬링은 `GameObject.SetActive(false)`가 아니라 `Renderer.forceRenderingOff`를 사용한다. 그래서 콜라이더와 스크립트는 남고 렌더러만 즉시 사라진다.
- 팝 현상을 해결하려면 일정 거리 구간에서 디더 값을 보간하고, 완전히 사라진 뒤에만 `forceRenderingOff=true`로 전환하는 혼합 구조가 적합하다.

---

## 1. 그래픽 렌더링 파이프라인이란

렌더링 파이프라인은 3D 장면의 데이터를 화면의 2D 픽셀로 바꾸는 전체 과정이다. 단순히 “셰이더를 실행한다”는 뜻이 아니라 다음 작업을 모두 포함한다.

- 어떤 카메라로 어떤 장면을 그릴지 결정
- 보이지 않는 오브젝트를 제외하는 컬링
- 불투명/투명 등의 렌더 큐 분류와 정렬
- 조명, 그림자, 깊이, 후처리용 패스 구성
- 메시와 재질 정보를 GPU 명령으로 변환
- GPU에서 정점 처리, 래스터화, 픽셀 처리, 깊이 검사, 블렌딩 수행
- 결과 이미지를 화면에 표시

큰 흐름은 다음과 같다.

```text
게임 로직과 Transform 갱신
        ↓
카메라별 가시성 판정과 컬링
        ↓
렌더 큐 분류, 정렬, 배칭, 렌더 패스 구성
        ↓
CPU가 GPU Draw/Dispatch 명령 제출
        ↓
정점 처리 → 프리미티브 조립 → 클리핑 → 래스터화
        ↓
픽셀/프래그먼트 처리 → 깊이·스텐실 → 블렌딩
        ↓
렌더 타깃 → 후처리 → 최종 화면
```

여기서 중요한 구분은 **프레임**, **패스**, **드로우 콜**이다.

- 프레임(Frame): 화면 한 장을 완성하는 전체 작업
- 패스(Pass): 그림자, 깊이, 불투명, 투명, 후처리처럼 목적이 다른 렌더 단계
- 드로우 콜(Draw Call): 특정 메시를 특정 셰이더 상태와 재질 데이터로 그리라는 CPU → GPU 명령

하나의 오브젝트가 항상 프레임당 한 번만 그려지는 것은 아니다. 같은 메시가 ShadowCaster 패스, DepthOnly 패스, Forward 패스에서 각각 다시 그려질 수 있다.

---

## 2. CPU 측 렌더링 준비 단계

GPU가 픽셀을 만들기 전에 CPU와 렌더 파이프라인이 먼저 그릴 대상을 정한다.

### 2.1 장면 상태 갱신

게임 로직, 애니메이션, 물리, Transform 변경이 반영된다. Renderer, Light, Camera, Volume 등의 상태도 이 시점의 장면 데이터에 포함된다.

### 2.2 컬링

컬링은 그릴 필요가 없는 대상을 제외하는 단계다.

- Frustum Culling: 카메라 시야 절두체 밖의 Renderer 제외
- Occlusion Culling: 다른 물체에 완전히 가려진 Renderer 제외
- Layer Culling Distance: 카메라와 레이어별 최대 거리로 제외
- LOD: 거리에 따라 더 단순한 메시로 교체하거나 최종 LOD를 제외
- 사용자 정의 거리 컬링: `CullingGroup`, 거리 계산, `forceRenderingOff` 등으로 직접 제어

이번 Medical Room 사례는 Layer 기반 컬링이 아니라 마지막 항목인 **사용자 정의 거리 컬링**이다.

### 2.3 정렬과 렌더 큐

일반적으로 불투명 물체는 깊이 효율을 높이도록 정렬하고, 투명 물체는 뒤에서 앞으로 정렬한다. 투명 렌더링은 깊이 기록과 블렌딩 때문에 불투명보다 순서 의존성이 크다.

### 2.4 배칭과 상태 변경 최소화

렌더 파이프라인은 동일하거나 호환되는 셰이더 상태를 묶어 CPU의 드로우 준비 비용을 줄이려고 한다.

- SRP Batcher
- GPU Instancing
- Static/Dynamic Batching
- GPU Resident Drawer 및 GPU Occlusion Culling

이 기능들은 서로 적용 조건이 다르다. “재질 수가 적다”만으로 배칭 여부가 결정되지 않으며 셰이더 변형, 프로퍼티 배치, 메시 조건, 렌더 상태도 영향을 준다.

### 2.5 명령 제출

렌더 파이프라인이 패스별로 Draw 명령과 상태 변경을 Command Buffer에 구성하고 GPU에 제출한다. Unity의 SRP에서는 이 프레임 구성 자체를 C#으로 제어할 수 있다.

---

## 3. GPU 그래픽 파이프라인 단계

아래는 현대 GPU 그래픽 파이프라인을 개념적으로 단순화한 것이다. 실제 지원 단계와 최적화 방식은 그래픽 API와 하드웨어에 따라 다르다.

### 3.1 Input Assembler

Vertex Buffer와 Index Buffer에서 정점 데이터를 읽고 점, 선, 삼각형 같은 프리미티브를 구성할 준비를 한다.

대표 입력:

- Position
- Normal
- Tangent
- UV
- Vertex Color
- Bone Weight/Index

### 3.2 Vertex Shader

각 정점을 처리한다. 가장 기본적인 역할은 오브젝트 로컬 좌표를 월드, 뷰, 클립 공간으로 변환하는 것이다.

```text
Object Space
  → World Space
  → View Space
  → Clip Space
```

스키닝, 바람에 의한 정점 변형, 물결, 버텍스 애니메이션도 이 단계에서 수행할 수 있다.

### 3.3 선택적 Tessellation / Geometry 단계

지원되는 파이프라인에서는 표면을 세분화하거나 프리미티브를 추가·변형할 수 있다. 모바일과 일반적인 URP 프로젝트에서는 비용과 호환성 때문에 항상 사용하는 단계는 아니다.

### 3.4 Clipping과 Perspective Divide

카메라에 보이지 않는 프리미티브 영역을 잘라내고 클립 공간 좌표를 정규화된 장치 좌표로 변환한다.

### 3.5 Rasterization

삼각형이 화면의 어떤 픽셀 샘플을 덮는지 계산하고, 정점에서 전달된 UV, Normal 등의 값을 화면 내부에서 보간한다. 이 단계에서 삼각형이 프래그먼트 후보들로 바뀐다.

### 3.6 Fragment/Pixel Shader

각 프래그먼트의 표면색과 출력 데이터를 계산한다.

대표 계산:

- Base Color/Albedo 텍스처 샘플링
- Normal Map 적용
- Metallic, Roughness/Smoothness, Ambient Occlusion 적용
- 직접광과 간접광 계산
- 그림자 샘플링
- Emission 추가
- Alpha Clip 또는 디더 Clip 판정

Shader Graph의 Master Stack에 연결하는 Base Color, Normal, Metallic, Smoothness, Alpha, Alpha Clip Threshold 등이 결국 이 단계의 입력 코드로 생성된다.

### 3.7 Depth/Stencil Test

새 프래그먼트가 기존 픽셀보다 카메라에 가까운지 깊이 버퍼로 검사한다. 스텐실 버퍼는 마스크, 포털, 특정 후처리 영역 구분 등에 사용할 수 있다.

Alpha Clip 또는 `clip()`은 프래그먼트를 폐기하므로 깊이, 그림자, 오버드로와 밀접한 관계가 있다. 하드웨어와 셰이더 조건에 따라 Early-Z 최적화 가능 여부도 달라진다.

### 3.8 Blending과 Output Merger

셰이더 출력과 기존 렌더 타깃 값을 결합한다.

- Opaque: 보통 기존 색을 덮어씀
- Transparent: Source Alpha 등에 따라 기존 색과 혼합
- Additive: 빛이나 이펙트처럼 색을 더함

마지막 결과는 Color Render Target에 기록되고, 필요에 따라 후처리와 UI 합성을 거쳐 화면에 표시된다.

---

## 4. 한 프레임은 여러 렌더 패스로 구성된다

실제 프레임은 하나의 거대한 셰이더 한 번으로 완성되지 않는다. 일반적인 구성 예시는 다음과 같다.

```text
Shadow Map Pass
        ↓
Depth / DepthNormals Prepass (필요 시)
        ↓
Opaque Pass
        ↓
Skybox
        ↓
Transparent Pass
        ↓
Post-processing
        ↓
UI / Final Blit
```

이 순서는 기능과 플랫폼, URP Renderer 설정에 따라 달라진다. 예를 들어 SSAO가 DepthNormals를 요구하거나, 특정 Renderer Feature가 중간에 별도 패스를 삽입할 수 있다.

따라서 디더링을 Forward 색상 패스에만 넣으면 다음 문제가 생길 수 있다.

- 본체는 사라졌는데 그림자는 남음
- Depth Texture에는 남아 후처리 윤곽이나 SSAO가 이상해짐
- DepthNormals에는 남아 노멀 기반 효과가 어긋남
- Scene View, Picking, Meta 패스의 결과가 기대와 다름

디더 대응 셰이더를 만들 때는 필요한 모든 패스에서 동일한 Clip 기준을 사용해야 한다.

---

## 5. Forward, Forward+, Deferred 렌더링 경로

### 5.1 Forward

오브젝트를 그릴 때 표면과 조명을 함께 계산해 최종 색을 출력한다.

장점:

- 구조가 비교적 직관적
- MSAA와 투명 렌더링에 유리
- 메모리 대역폭 부담이 비교적 단순
- 다양한 재질 모델을 한 장면에서 다루기 쉬움

주의점:

- 오브젝트에 영향을 주는 광원이 많을수록 조명 계산 비용이 증가
- 전통적인 Forward에서는 오브젝트당 추가 광원 수 제한의 영향을 받음

### 5.2 Forward+

화면을 타일 또는 클러스터로 나누고 각 영역에 영향을 주는 광원 목록을 미리 계산한 뒤 Forward 방식으로 셰이딩한다.

장점:

- Forward의 재질 유연성과 투명 처리 방식을 유지
- 다수의 광원을 전통적 Forward보다 효율적으로 처리
- 오브젝트당 추가 광원 제한을 완화

주의점:

- 광원 컬링용 추가 계산과 버퍼가 필요
- 플랫폼과 기능에 따라 성능 이득이 달라지므로 프로파일링 필요

### 5.3 Deferred

먼저 Base Color, Normal, Material 특성 등을 G-buffer에 기록하고, 이후 조명 패스에서 화면 공간으로 조명을 계산한다.

장점:

- 많은 동적 광원이 있는 불투명 장면에서 유리할 수 있음
- 오브젝트 수와 광원 수의 조합에 대한 중복 셰이딩을 줄일 수 있음

주의점:

- 여러 G-buffer로 인한 메모리와 대역폭 비용
- MSAA 제약 또는 높은 비용
- 투명 오브젝트는 보통 별도 Forward 패스로 처리
- 재질 표현을 G-buffer 구조에 맞춰야 함

### 5.4 비교 요약

| 항목 | Forward | Forward+ | Deferred |
|---|---|---|---|
| 기본 아이디어 | 오브젝트를 그리며 조명 계산 | 화면 영역별 광원 목록 + Forward 셰이딩 | 재질 정보를 먼저 저장한 뒤 조명 계산 |
| 다수 광원 | 불리할 수 있음 | Forward보다 유리 | 불투명 장면에서 유리할 수 있음 |
| 투명 오브젝트 | 자연스럽게 처리 | 자연스럽게 처리 | 일반적으로 별도 Forward 처리 |
| MSAA | 적합 | 적합 | 제약/비용이 큼 |
| 메모리 대역폭 | 상대적으로 단순 | 추가 광원 목록 필요 | G-buffer 비용 큼 |
| 재질 다양성 | 높음 | 높음 | G-buffer 제약을 받음 |

현재 GrayZone Renderer는 **Forward** 경로다.

---

## 6. PBR과 재질 데이터의 의미

PBR(Physically Based Rendering)은 빛과 표면의 상호작용을 물리적으로 그럴듯한 규칙에 맞춰 계산하는 방식이다. Unity URP/Lit도 PBR 셰이더다.

### 6.1 대표 입력

- Base Color/Albedo: 표면의 기본 색
- Normal: 미세 표면 방향
- Metallic: 금속성
- Smoothness 또는 Roughness: 표면의 매끄러움/거칠기
- Ambient Occlusion: 틈이나 접촉부의 간접광 감쇠
- Emission: 외부 조명과 별개로 방출하는 색
- Alpha: 투명도 또는 Clip 판정 값

Smoothness와 Roughness는 대개 반대 개념으로 사용된다.

```text
Smoothness ≈ 1 - Roughness
```

단, 실제 임포트와 셰이더 구현에서는 감마 처리, 리매핑, 채널 범위 때문에 단순 반전 이상의 처리가 들어갈 수 있다.

### 6.2 채널 패킹

구매 에셋은 여러 흑백 정보를 한 텍스처의 R/G/B/A 채널에 묶는 경우가 많다. 예를 들어 ARM 텍스처는 흔히 AO, Roughness, Metallic을 각각 다른 채널에 저장한다.

중요한 점은 “ARM”이라는 이름만으로 정확한 채널 순서를 단정할 수 없다는 것이다. 셰이더 코드나 제작 문서를 확인해야 한다.

### 6.3 같은 PBR이어도 결과가 다른 이유

두 셰이더가 모두 PBR이라고 해도 다음이 다르면 화면 결과가 달라진다.

- 프로퍼티 이름과 텍스처 연결
- Metallic/Smoothness/Roughness 변환 방식
- Normal 강도와 탄젠트 공간 처리
- AO 적용 위치와 강도
- 채널 패킹 규칙
- Alpha Clip/Transparent 처리
- Double-sided, Cull, ZWrite, Blend 상태
- 환경 반사, Lightmap, Reflection Probe, Fog 지원
- ShadowCaster와 Depth 패스 구현

따라서 “URP/Lit처럼 생긴 Shader Graph”를 만들었다고 해서 구매 에셋의 커스텀 셰이더와 자동으로 같은 결과가 나오지는 않는다.

---

## 7. Unity의 렌더 파이프라인 종류

### 7.1 Built-in Render Pipeline

Unity의 전통적인 내장 렌더 파이프라인이다.

- 오래된 에셋과 셰이더의 호환성이 넓음
- 렌더 루프를 프로젝트가 직접 크게 확장하기는 어려움
- Surface Shader 같은 Built-in 전용 제작 방식이 존재
- 신규 프로젝트에서 URP/HDRP 기능을 그대로 사용할 수 없음

### 7.2 Scriptable Render Pipeline

SRP는 C# 스크립트로 렌더 루프를 구성할 수 있는 Unity의 렌더링 아키텍처다. URP와 HDRP가 대표적인 완성형 SRP다.

```text
SRP Core
 ├─ URP
 ├─ HDRP
 └─ Custom SRP
```

### 7.3 Universal Render Pipeline

URP는 모바일부터 PC/콘솔까지 폭넓은 플랫폼을 목표로 한다.

특징:

- 비교적 확장성이 높고 성능 조절 범위가 넓음
- Forward, Forward+, Deferred 렌더링 경로 제공
- Renderer Feature와 Scriptable Render Pass로 커스텀 패스 추가 가능
- Shader Graph와 VFX Graph 통합
- SRP Batcher, GPU Resident Drawer 등 SRP 최적화 기능 활용 가능
- 한 프로젝트에서 품질 레벨별로 서로 다른 URP Asset을 지정 가능

URP라고 해서 항상 가볍다는 뜻은 아니다. Shadow, SSAO, Opaque/Depth Texture, Render Scale, HDR, Post-processing, Intermediate Texture 설정에 따라 비용이 크게 달라진다.

### 7.4 High Definition Render Pipeline

HDRP는 고성능 PC와 콘솔에서 높은 시각 품질을 목표로 한다.

- 고급 조명과 재질 모델
- 정교한 볼륨과 후처리
- 고급 그림자, 반사, 레이 트레이싱 지원 범위
- 높은 하드웨어 요구와 복잡한 설정

URP용 셰이더와 HDRP용 셰이더는 조명 함수, 패스, 프로퍼티, 포함 파일이 다르므로 단순 교체 호환되지 않는다.

### 7.5 Custom SRP

프로젝트가 직접 렌더 루프와 패스를 구현할 수 있다. 자체 엔진의 렌더러에 가장 가까운 자유도를 얻지만 조명, 그림자, 후처리, 디버깅, 플랫폼 대응까지 직접 책임져야 한다.

---

## 8. Unity URP의 구조

URP는 대략 다음 계층으로 이해하면 된다.

```text
Project Graphics / Quality Settings
        ↓
Universal Render Pipeline Asset
        ↓
Universal Renderer Data
        ├─ Rendering Path
        ├─ Renderer Features
        └─ 각종 패스 설정
        ↓
Camera
        ↓
Culling → Render Pass 구성 → 실행
        ↓
Shader Pass + Material Data → GPU
```

### 8.1 Universal Render Pipeline Asset

프로젝트 또는 품질 레벨의 공통 렌더링 정책을 가진다.

- HDR
- Render Scale
- MSAA
- Main/Additional Light
- Shadow Distance와 해상도
- Depth Texture/Opaque Texture
- SRP Batcher
- LOD Cross Fade

### 8.2 Universal Renderer Data

카메라가 실제로 어떤 렌더링 경로와 Renderer Feature를 사용할지 정의한다.

- Forward/Forward+/Deferred 경로
- Depth Priming
- Intermediate Texture
- Renderer Features
- 렌더 패스 이벤트 지점

### 8.3 Renderer Feature와 Scriptable Render Pass

렌더 파이프라인 중간에 별도 화면 효과나 버퍼 처리를 추가하는 방식이다. 예를 들어 SSAO, 커스텀 블릿, 외곽선, 특정 마스크 패스를 구현할 수 있다.

이 기능은 **개별 오브젝트의 Lit 셰이더 내부에 디더링을 자동 삽입하는 기능과는 다르다.** 전체 화면 패스가 개별 재질의 표면 Clip 규칙을 대신하려면 대상 분리용 마스크, 별도 렌더링, 깊이/그림자 동기화 등이 추가로 필요하다. Medical Room처럼 개별 Renderer를 점진적으로 제거하는 용도는 대개 대상 셰이더의 Alpha Clip 쪽에서 처리하는 것이 단순하다.

### 8.4 Render Graph

Unity 6 URP는 Render Graph를 사용해 렌더 패스와 리소스의 의존성을 선언하고 관리할 수 있다.

Render Graph가 담당하는 핵심은 다음과 같다.

- 어떤 패스가 어떤 텍스처나 버퍼를 읽고 쓰는지 추적
- 사용하지 않는 패스 제거 가능성
- 임시 렌더 리소스의 수명과 재사용 관리
- 패스 실행 순서와 의존성 관리

Render Graph는 Shader Graph와 이름만 비슷할 뿐 역할이 완전히 다르다.

| 항목 | Render Graph | Shader Graph |
|---|---|---|
| 대상 | 프레임의 렌더 패스와 리소스 | 한 셰이더의 정점/표면 계산 |
| 실행 범위 | 카메라 프레임 전체 | 해당 셰이더를 사용하는 Draw |
| 주요 사용자 | 렌더 파이프라인/Renderer Feature 개발자 | 테크니컬 아티스트/셰이더 개발자 |
| 결과 | 패스 실행 그래프 | ShaderLab/HLSL 셰이더 코드 |

---

## 9. ShaderLab, HLSL, Shader Graph, Material의 관계

### 9.1 ShaderLab

Unity 셰이더 에셋의 바깥 구조를 정의한다.

- Properties
- SubShader
- Pass
- Tags
- Render Queue
- Cull, ZWrite, ZTest, Blend
- HLSLPROGRAM/ENDHLSL 코드 블록

### 9.2 HLSL

GPU에서 실행될 실제 계산 함수를 작성하는 언어다. Vertex/Fragment 함수, 공용 조명 함수, 디더 함수 등이 여기에 들어간다.

### 9.3 Shader Graph

Shader Graph는 노드 UI로 셰이더를 제작하고 최종 ShaderLab/HLSL을 **생성**하는 도구다. 런타임에서 Lit 셰이더 앞이나 뒤에 별도 효과 셰이더가 끼어드는 구조가 아니다.

```text
Shader Graph 노드와 Target 설정
        ↓ 코드 생성
ShaderLab + HLSL + 여러 Pass
        ↓ 컴파일
플랫폼별 GPU Shader
```

그래프의 Universal Lit Target은 URP의 조명 라이브러리와 패스 템플릿을 사용해 Lit 셰이더를 생성한다. 개발자가 PBR 전체를 매번 직접 작성하지 않아도 되지만, 최종적으로는 완전한 셰이더가 만들어진다.

### 9.4 Sub Graph

`.shadersubgraph`는 여러 Shader Graph에서 재사용할 수 있는 함수형 그래프 조각이다.

- Blackboard에 입력 정의
- Output 노드에 결과 정의
- 여러 상위 Shader Graph에서 노드 하나처럼 사용
- Master Stack이 없으므로 단독 최종 셰이더가 아님
- 일반 `.shader`의 `#include` 대상으로 직접 쓰는 HLSL 파일은 아님

즉 자체 엔진에서 공용 함수 파일을 참조하던 방식과 개념은 비슷하지만, 대상이 Shader Graph끼리의 공유로 한정된다.

### 9.5 Custom Function Node와 공용 HLSL

Shader Graph의 Custom Function Node는 직접 작성한 HLSL 함수 또는 외부 `.hlsl` 파일을 호출할 수 있다.

```text
MedicalDither.hlsl
       ├─ Shader Graph A의 Custom Function Node
       ├─ Shader Graph B의 Custom Function Node
       └─ 수동 .shader 파일의 #include
```

Shader Graph와 수동 ShaderLab 셰이더가 섞여 있는 현재 Medical 에셋에는 이 방식이 가장 범용적이다.

### 9.6 Material

Material은 선택한 Shader와 해당 Shader의 프로퍼티 값을 저장한다.

```text
Material
 ├─ Shader 참조
 ├─ Texture 값
 ├─ Color/Float/Vector 값
 ├─ Keyword 상태
 └─ Render Queue 오버라이드
```

Material 자체가 PBR 계산을 수행하는 것이 아니다. 어떤 셰이더를 사용하고 그 셰이더의 입력에 어떤 값을 줄지를 저장한다.

### 9.7 MaterialPropertyBlock

Material 에셋을 복제하지 않고 Renderer별 프로퍼티 값을 덮어쓸 수 있다. 현재 지붕 디더 제어도 이 방식을 사용한다.

장점:

- 런타임 재질 인스턴스 남발 방지
- Renderer별 Fade 값 제어 가능
- 공유 Material 에셋 원본을 변경하지 않음

주의점:

- URP/HDRP의 드로우 콜 최적화 관점에서는 SRP Batcher와의 상호작용을 확인해야 한다.
- 공식 Unity 최적화 문서는 URP/HDRP에서 SRP Batcher를 우선하고 MaterialPropertyBlock 사용을 피하라고 안내한다.
- 그러나 Renderer마다 계속 변하는 값이 필요한 경우 MPB가 실용적일 수 있다. 실제 대상 수, 드로우 수, 플랫폼을 기준으로 Profiler와 Frame Debugger에서 측정해야 한다.

---

## 10. 자체 엔진의 통짜 HLSL과 Unity Shader Graph의 차이

자체 엔진에서 하나의 큰 HLSL에 PBR 전체를 넣었던 구조와 Unity는 다음처럼 대응된다.

| 자체 엔진에서의 개념 | Unity URP에서 대응되는 개념 |
|---|---|
| 렌더 루프 C++ 코드 | URP C# 렌더 파이프라인과 Render Graph |
| 통짜 PBR HLSL | URP 조명 라이브러리 + 각 셰이더의 생성 HLSL |
| 공용 함수 include | `.hlsl` include / Shader Graph Custom Function |
| 머티리얼 상수 버퍼 | Material 프로퍼티와 UnityPerMaterial CBUFFER |
| 셰이더 permutation | Shader Keyword와 Variant |
| 직접 작성한 패스 | ShaderLab Pass / Scriptable Render Pass |
| 셰이더 에디터 UI | Shader Graph |

차이는 “Unity는 셰이더를 따로 실행한다”가 아니라 **PBR 공통부와 패스 생성 책임을 URP 패키지와 Shader Graph 코드 생성기가 나눠 가진다**는 점이다.

자체 엔진에서도 실제로는 다음과 같은 변형이 존재했을 가능성이 높다.

- Opaque와 Alpha Test
- Skinned와 Static
- Shadow pass
- Depth pass
- Normal map 사용 여부
- Instancing 여부

Unity에서는 이 차이가 별도 셰이더 에셋, SubShader/Pass, Keyword Variant, Shader Graph Target 옵션 등으로 더 명시적으로 보인다.

---

## 11. 기존 재질을 새 Shader Graph에 넣는다는 의미

Unity에는 “기존 Material을 Shader Graph 안에 넣는다”는 동작이 없다. 정확한 흐름은 다음과 같다.

1. Shader Graph에 기존 셰이더와 동일한 이름/타입의 프로퍼티를 만든다.
2. Graph에서 그 프로퍼티를 Base Color, Normal, Metallic 등에 올바르게 연결한다.
3. Surface Type, Alpha Clip, Cull, ZWrite 등의 렌더 상태를 맞춘다.
4. Material Inspector에서 Material의 Shader를 새 Graph로 변경한다.
5. Unity가 이름과 타입이 같은 직렬화 프로퍼티 값을 새 셰이더에 연결한다.

예를 들어 기존 셰이더가 Albedo에 `Material_Texture2D_1`을 쓰고 새 그래프가 `_BaseMap`을 쓰면 Unity는 둘이 같은 의미인지 알 수 없다. 텍스처 파일은 삭제되지 않지만 새 셰이더의 `_BaseMap` 슬롯은 비어 보인다.

안전한 방법은 다음 두 가지다.

- 새 셰이더가 기존 프로퍼티 이름을 그대로 유지
- 변환 도구가 기존 프로퍼티를 읽어 새 프로퍼티로 명시적으로 복사

첫 번째 방법이 기존 재질 수가 많고 외형 보존이 중요한 경우 더 안전하다.

---

## 12. 디더링은 어디에 구현하는가

### 12.1 일반적인 위치

오브젝트 자체가 점진적으로 사라져야 한다면 보통 대상 셰이더의 Fragment 단계에서 Alpha Clip을 사용한다.

개념적인 코드는 다음과 같다.

```hlsl
float threshold = DitherThreshold(screenPosition);
clip(visibility - threshold);
```

여기서 화면상의 규칙적인 패턴 또는 Blue Noise와 Fade 값을 비교해 일부 픽셀부터 순차적으로 폐기한다.

### 12.2 왜 전용 후처리 하나로 끝내기 어려운가

전체 화면 후처리는 이미 그려진 화면을 다룬다. 특정 Medical 오브젝트만 골라 깊이, 그림자, 표면 단위로 제거하려면 별도 마스크와 패스가 필요하다. 단순 오브젝트 디더는 각 표면 셰이더의 Clip 단계에서 처리하는 것이 자연스럽다.

### 12.3 공용 로직과 최종 셰이더의 구분

공용 디더 파일 하나가 있어도 GPU가 선택하는 최종 Shader 에셋은 각 재질 계열별로 필요하다.

```text
MedicalDither.hlsl  ← 공용 계산 함수 1개
    ├─ URP Lit + Dither
    ├─ M_Standart_Master + Dither
    ├─ M_Standart_Cutout + Dither
    ├─ M_Glass + Dither
    └─ RealBlendVariationURP + Dither
```

따라서 저작 파일 관점에서는 `공용 HLSL 1개 + 최종 셰이더 5개`가 될 수 있다. Material Inspector에서 선택 가능한 실제 최종 셰이더는 5개이며 공용 HLSL은 단독 셰이더가 아니다. 컴파일 시 공용 함수 코드는 각 최종 셰이더에 포함된다.

### 12.4 왜 계열별 최종 셰이더가 필요한가

다음 계약이 서로 다르기 때문이다.

- 프로퍼티 이름과 텍스처 채널
- Opaque/Cutout/Transparent
- Cull/ZWrite/ZTest/Blend
- 조명과 반사 계산
- Shader Keyword
- Forward, ShadowCaster, DepthOnly, DepthNormals 패스
- 에셋 전용 효과와 버텍스 변형

하나의 Uber Shader로 통합하는 것도 기술적으로 가능하지만, 기존 5계열의 모든 계약을 하나로 합치는 복잡성과 Variant 수가 증가한다. 현재 목적이 Medical Room의 거리 페이드라면 기존 계열을 보존한 소규모 파생본이 위험이 낮다.

---

## 13. 거리 컬링, 디더 페이드, 비활성화의 차이

| 방식 | 보이는 상태 | Collider/Script | 렌더 비용 | 전환 모양 |
|---|---|---|---|---|
| `GameObject.SetActive(false)` | 전체 비활성 | 같이 비활성 | 대부분 제거 | 즉시 팝 |
| `Renderer.enabled=false` | Renderer 미제출 | 남음 | Draw 제거 | 즉시 팝 |
| `Renderer.forceRenderingOff=true` | Renderer 강제 미제출 | 남음 | Draw 제거 | 즉시 팝 |
| Shader Dither/Alpha Clip | 일부 픽셀 폐기 | 남음 | 완전 컬링 전까지 Draw와 셰이더 비용 존재 | 점진적 전환 |
| Transparent Alpha Blend | 반투명 혼합 | 남음 | 정렬/오버드로 비용 큼 | 부드러운 반투명 |

Medical Room에서 Collider가 남는 것은 비정상이 아니라 `forceRenderingOff` 방식의 의도된 결과다. 이 API는 GameObject의 활성 상태나 Collider를 건드리지 않고 Renderer의 렌더 제출만 막는다.

---

## 14. 이번 세션에서 확인한 GrayZone 사례

이 장은 일반 이론이 아니라 이번 세션에서 프로젝트와 Unity Editor를 직접 확인한 결과다.

### 14.1 `UnityEditor.Graphs.Edge.WakeUp` NullReferenceException

초기 오류 스택:

```text
NullReferenceException: Object reference not set to an instance of an object
UnityEditor.Graphs.Edge.WakeUp()
UnityEditor.Graphs.Graph.DoWakeUpEdges(...)
UnityEditor.Graphs.Graph.WakeUpEdges(...)
UnityEditor.Graphs.Graph.OnEnable()
```

판단 결과:

- 런타임 렌더링이나 Shader Graph 오류가 아니라 Unity Animator 그래프 에디터의 Edge/UI 캐시 상태 문제였다.
- 대상 `PlayerInputsThirdPerson.controller`의 직렬화 데이터는 유효했다.
- Animator 창을 닫았다 다시 열어 그래프를 재구성한 뒤 stale edge가 남지 않았고 콘솔 오류도 정리되었다.

즉 스택에 `UnityEditor.Graphs`가 나타난다고 해서 반드시 Shader Graph 문제는 아니다. Animator Controller와 일부 Unity Editor 그래프 UI도 이 네임스페이스를 사용한다.

### 14.2 `Medical_Room_Layout_Set_0`의 거리 팝

Main Camera의 `DistanceRendererCullingGroup`이 Medical Room 렌더러를 제어하고 있었다.

확인된 기준:

- Show Distance: 8m
- Hide Distance: 10m
- 8~10m: 이전 상태 유지용 히스테리시스 구간
- `CullingGroup`의 Bounding Sphere로 거리 밴드 판정
- 실제 표시/숨김: `Renderer.forceRenderingOff`

소스:

- `Assets/1.Scripts/Rendering/DistanceRendererCullingGroup.cs`

동작은 다음과 같다.

```text
가까움                  전이 구간                  멀어짐
distance ≤ 8m           8m < distance < 10m       distance ≥ 10m
표시                    이전 상태 유지             forceRenderingOff = true
```

8m와 10m가 다른 이유는 경계에서 카메라가 조금 흔들릴 때 매 프레임 표시/숨김이 왕복하는 현상을 막기 위해서다. 하지만 최종 전환 자체는 bool이므로 여전히 “뿅” 하고 나타나고 사라진다.

### 14.3 기존 지붕 페이드 구조

기존 테스트와 지붕 가림 처리에는 다음 스크립트가 있다.

- `Assets/1.Scripts/Shelter/TPS/CameraOcclusionFader.cs`
- `Assets/1.Scripts/Shelter/TPS/RoofOcclusionTarget.cs`

구조:

1. 카메라와 캐릭터 사이를 SphereCast해 가리는 지붕 탐색
2. 목표 Fade 값 결정
3. `Mathf.MoveTowards`로 현재 값을 목표까지 보간
4. `MaterialPropertyBlock`으로 Renderer별 디더 값 전달

현재 지붕 스크립트의 값 규약은 다음과 같다.

- `0 = 보임`
- `1 = 가려져 숨김`

### 14.4 Medical Room 재질과 셰이더 계열 조사

`Medical_Room_Layout_Set_0`에서 확인된 규모:

- Renderer 119개
- Material Slot 159개
- 서로 다른 Material 27개
- Shader 계열 5개

| 셰이더 계열 | 재질 수 | 슬롯 수 | 비고 |
|---|---:|---:|---|
| `Universal Render Pipeline/Lit` | 14 | 32 | Unity URP 기본 Lit |
| `M_Standart_Master` | 9 | 90 | SurvivorBase 커스텀 셰이더 |
| `M_Standart_Cutout` | 2 | 32 | SurvivorBase 컷아웃 셰이더 |
| `M_Glass` | 1 | 4 | SurvivorBase 투명/유리 셰이더 |
| `Shader Graphs/RealBlendVariationURP` | 1 | 1 | 바닥용 커스텀 Shader Graph |

여기서 중요한 결론은 **27개의 Material이 27종의 셰이더를 의미하지 않는다**는 것이다.

### 14.5 `DF_Test03` 제작 결과

테스트 경로:

- `Assets/3.Resources/TempImage/TestShader/DF_Test03.shadergraph`

현재 `DF_Test03`은 `DF_Test02`를 복사한 것이 아니라 Universal Lit Target을 기준으로 만든 Opaque/Alpha Clip Shader Graph다.

주요 프로퍼티:

- `_BaseMap`
- `_BaseColor`
- `_BumpMap`
- `_BumpScale`
- `_MetallicGlossMap`
- `_Metallic`
- `_Smoothness`
- `_OcclusionMap`
- `_OcclusionStrength`
- `_EmissionMap`
- `_EmissionColor`
- `_DitherValue`

현재 그래프의 Dither 연결은 `_DitherValue`가 Dither 입력으로 직접 들어가고 Alpha Clip Threshold가 0인 형태다. 이 구조에서는 값의 체감 규약이 **1에 가까울수록 보이고 0에 가까울수록 숨는 방향**일 가능성이 높다.

이는 기존 `RoofOcclusionTarget`의 `0=보임, 1=숨김` 규약과 반대다. 둘을 연결하려면 다음 중 하나를 선택해야 한다.

- 그래프에서 `One Minus`를 적용
- 스크립트에서 `1 - fade`를 전달
- 프로퍼티를 `_Visibility`처럼 의미가 명확한 이름으로 통일

이 부분은 현재 확인된 설계 불일치이며 아직 최종 통합 구현으로 확정된 상태가 아니다.

### 14.6 Medical 테스트 Material 복사본

테스트 경로:

- `Assets/3.Resources/TempImage/TestShader/Medical`

27개 Material과 대응 `.meta`가 원본과 독립된 GUID로 복사되었다. 원본 Material과 Scene 할당은 변경하지 않았다.

### 14.7 `MI_Arm_Chair_A1` 이후 텍스처가 사라진 이유

문제가 발생한 `MI_*` 12개는 기본 URP/Lit이 아니라 SurvivorBase 커스텀 셰이더를 사용한다.

원본 주요 프로퍼티 계약:

| 셰이더 | Albedo | Normal | 패킹/기타 |
|---|---|---|---|
| `M_Standart_Master` | `Material_Texture2D_1` | `Material_Texture2D_0` | `Material_Texture2D_5` ARM, `_2` Metallic, `_3` Roughness, `_4` Mask |
| `M_Standart_Cutout` | `_MainTex` | `Material_Texture2D_0` | `Material_Texture2D_5` ARM + Alpha/Opacity 값 |
| `M_Glass` | `_Albedo` | 셰이더 고유 구조 | `_Mask`와 Transparent 전용 값 |
| `DF_Test03` | `_BaseMap` | `_BumpMap` | `_MetallicGlossMap`, `_OcclusionMap` 등 |

Material의 Shader를 `DF_Test03`으로 바꾸면 Unity는 `Material_Texture2D_1`과 `_BaseMap`이 모두 Albedo라는 사실을 추론하지 못한다. 이름이 다르므로 Inspector의 새 슬롯이 비고 텍스처가 날아간 것처럼 보인다.

그러나 복사된 `.mat` 직렬화에는 기존 `Material_Texture2D_0~5` 참조가 남아 있었다. 텍스처 에셋이 삭제된 것은 아니다.

관련 원본 셰이더:

- `Assets/3.Resources/ThirdParty/SurvivorBase/Shaders/M_Standart_Master.shader`
- `Assets/3.Resources/ThirdParty/SurvivorBase/Shaders/M_Standart_Masked.shader`
- `Assets/3.Resources/ThirdParty/SurvivorBase/Shaders/M_Glass.shader`

---

## 15. Medical Room에 권장하는 디더 구조

### 15.1 목표 상태 흐름

팝을 줄이면서 완전히 멀어진 오브젝트의 렌더 비용도 제거하려면 다음 흐름이 적합하다.

```text
[완전히 표시]
forceRenderingOff = false
fade = visible
        ↓ 거리 증가
[Fade Out 구간]
forceRenderingOff = false
디더 값 보간
        ↓ 완전히 숨김
[완전히 컬링]
forceRenderingOff = true
        ↓ 거리 감소
[Fade In 시작 준비]
먼저 forceRenderingOff = false
        ↓
디더 값을 visible 방향으로 보간
        ↓
[완전히 표시]
```

Fade 중에 `forceRenderingOff=true`를 먼저 설정하면 셰이더가 아예 실행되지 않으므로 보간이 보이지 않는다. Fade-in도 렌더러를 먼저 다시 제출한 뒤 디더 값을 변경해야 한다.

### 15.2 거리 구간 예시

현재 8m/10m를 유지한다면 한 가지 해석은 다음과 같다.

```text
≤ 8m           8m ~ 10m                    ≥ 10m
완전 표시       거리 기반 디더 보간          완전 숨김 + 강제 컬링
```

다만 기존 코드는 8~10m를 히스테리시스로 사용한다. 이를 연속 보간 구간으로 바꾸면 경계 떨림 방지 방식이 달라진다. 실전에서는 다음처럼 별도의 진입/이탈 상태를 두는 것이 좋다.

- Fade Out은 8m → 10m
- 완전 숨김은 10m 이상
- Fade In은 반대 방향이되 상태 히스테리시스를 유지
- 카메라가 경계에 정지할 때 Fade 방향이 매 프레임 뒤집히지 않도록 상태 머신 사용

### 15.3 셰이더 계열별 대응본

권장 최소 단위:

| 대상 계열 | 권장 작업 |
|---|---|
| URP/Lit | `DF_Test03`의 Lit 입력과 디더 패스 검증 후 사용 |
| M_Standart_Master | 원본 복제 후 기존 프로퍼티/ARM/PBR 구조를 유지하고 공용 디더 추가 |
| M_Standart_Cutout | 기존 Cutout Alpha와 거리 Dither의 결합 규칙 정의 |
| M_Glass | Blend/ZWrite/정렬을 보존하고 디더 적용 필요성을 별도 검토 |
| RealBlendVariationURP | 원본 Graph 복제 후 Sub Graph 또는 Custom Function으로 디더 추가 |

`M_Glass`는 투명 블렌딩 셰이더이므로 불투명 Alpha Clip과 동일한 결과가 나오지 않을 수 있다. 유리까지 반드시 같은 방식으로 사라져야 하는지 먼저 시각적으로 검증하는 편이 좋다.

### 15.4 공용 디더 함수가 담당할 범위

공용 함수는 가능한 작게 유지한다.

- Screen Position 또는 픽셀 좌표 입력
- Fade/Visibility 입력
- Dither threshold 계산
- 최종 clip mask 또는 threshold 출력

공용 함수가 담당하지 않아야 할 것:

- 각 셰이더의 Albedo/Normal/ARM 해석
- Glass Blend 상태
- Cutout 원본 Alpha 의미
- 셰이더별 Keyword와 렌더 큐
- 조명 모델 전체

예시 개념:

```hlsl
// 실제 함수명과 입력 타입은 프로젝트 규약에 맞춰 확정한다.
void MedicalDither_float(float2 screenUV, float visibility, out float alpha)
{
    float threshold = /* ordered pattern or noise */;
    alpha = visibility - threshold;
}
```

각 최종 셰이더는 이 결과를 자기 Alpha Clip 구조에 맞게 결합한다.

### 15.5 패스 체크리스트

최종 셰이더별로 최소한 다음을 확인한다.

- UniversalForward 또는 UniversalGBuffer
- ShadowCaster
- DepthOnly
- DepthNormals
- SceneSelection/ScenePicking 필요 여부
- Meta/Lightmap Bake 영향

디더 패턴에 화면 좌표를 사용하면 그림자 카메라와 메인 카메라에서 패턴 기준이 달라질 수 있다. 그림자까지 동일한 시각적 점진 전환이 필요한지, 완전 숨김 시점에만 그림자를 끌지 정책을 정해야 한다.

---

## 16. 구매 에셋 셰이더를 수정할 때의 원칙

### 16.1 소스가 있는 경우

현재 SurvivorBase 셰이더 소스는 프로젝트에 존재한다. 따라서 구매 에셋이라는 이유만으로 수정이 불가능한 상태는 아니다.

안전한 절차:

1. ThirdParty 원본은 보존
2. 프로젝트 테스트 경로에 셰이더 복제
3. Shader 이름을 별도로 변경
4. Properties, Tags, Pass, Keywords, 렌더 상태 유지
5. 공용 디더 함수와 Fade 프로퍼티만 추가
6. 복제 Material에서만 새 셰이더로 변경
7. 원본과 테스트본을 같은 조명에서 비교

### 16.2 소스가 없는 경우

컴파일된 셰이더만 있거나 라이선스상 수정이 제한되면 선택지가 줄어든다.

- 제작사에 기능 추가 또는 소스 제공 문의
- 외형을 재현한 새 셰이더 제작
- 렌더러 전환 대신 LOD Cross Fade, 메시 분할, 다른 가림 연출 사용
- Renderer Feature 기반 마스크/후처리 방식 검토

소스가 없을 때 “원본과 픽셀 단위로 동일한 결과”를 보장하는 것은 어렵다.

### 16.3 업그레이드와 라이선스

복사본을 수정하면 에셋 업데이트 시 원본 개선 사항이 자동 반영되지 않는다. 변경점과 원본 버전을 문서화하고 필요 시 Diff로 재적용해야 한다. 또한 외부 배포나 소스 공유 범위는 해당 에셋 라이선스를 따라야 한다.

---

## 17. 성능 관점에서 볼 것

### 17.1 디더는 컬링이 아니다

디더로 모든 픽셀이 폐기되더라도 Draw Call 제출과 일부 정점/프래그먼트 작업은 발생할 수 있다. 그래서 완전히 숨은 뒤 `forceRenderingOff`로 전환하는 방식이 유효하다.

### 17.2 Alpha Clip과 오버드로

Alpha Clip은 투명 블렌딩보다 정렬 문제가 적고 깊이 기록이 가능하지만, 복잡한 디더 패턴과 화면을 크게 덮는 메시에서는 프래그먼트 비용이 증가할 수 있다.

### 17.3 Shader Variant

디더를 Keyword로 켜고 끄면 Variant가 늘어난다. 모든 Material이 항상 디더 대응 셰이더를 사용한다면 런타임 bool 분기 또는 값 기반 제어가 더 단순할 수 있지만, 플랫폼과 컴파일 결과를 프로파일링해야 한다.

### 17.4 SRP Batcher와 프로퍼티 배치

URP에서는 SRP Batcher 호환성을 유지하기 위해 Material 프로퍼티를 적절한 `UnityPerMaterial` 상수 버퍼에 두어야 한다. Shader Graph는 일반적인 배치를 자동 생성하지만 수동 셰이더 복제본에서는 Frame Debugger와 Inspector의 호환성 표시를 확인해야 한다.

### 17.5 MaterialPropertyBlock

Renderer별 Fade 값을 가장 쉽게 전달할 수 있지만, URP의 SRP Batcher 최적화와 충돌할 가능성이 있다. 다음 대안을 비교할 수 있다.

- MPB: 구현 간단, Renderer별 값에 적합
- Material 인스턴스: 관리와 메모리 비용 증가
- 공유 Material의 전역 값: 모든 대상이 같은 Fade일 때만 적합
- GPU Instancing용 인스턴스 데이터: 구조 변경 비용이 큼

Medical Room 전체가 같은 중심/거리 기준으로 동시에 Fade한다면 공유 값 또는 전역 값을 활용할 가능성도 있다. 반대로 Renderer마다 다른 거리나 타이밍이 필요하면 MPB가 자연스럽다.

### 17.6 측정 도구

- Frame Debugger: 어떤 패스와 Draw Call로 그려졌는지 확인
- Rendering Debugger: URP 조명, 재질, 오버드로 관련 디버그 뷰
- Unity Profiler: CPU Render Thread, Batches, SetPass, GPU 시간 확인
- Render Graph Viewer: 패스와 리소스 의존성 확인
- RenderDoc: 플랫폼별 GPU 프레임 정밀 분석

---

## 18. GrayZone의 현재 URP 설정 스냅샷

현재 확인된 프로젝트 설정이다. 이후 설정 변경 시 이 장은 오래된 스냅샷이 될 수 있다.

| 항목 | 현재 값 |
|---|---|
| Unity | 6000.3.17f1 |
| URP package | 17.3.0 |
| Pipeline Asset | `GrayZone_New Universal Render Pipeline Asset` |
| Renderer Data | `GrayZone_Renderer` |
| Rendering Path | Forward |
| Depth Texture | On |
| Opaque Texture | Off |
| HDR | On |
| MSAA | Disabled |
| Main Light | Per Pixel |
| Additional Lights | Per Pixel, Per Object Limit 4 |
| Shadow Distance | 50 |
| SRP Batcher | On |
| Dynamic Batching | Off |
| Depth Priming | Disabled |
| Intermediate Texture | Always |
| Renderer Feature | Screen Space Ambient Occlusion 1개 |

설정 에셋:

- `Assets/3.Resources/HaYW/RenderSettings/GrayZone_New Universal Render Pipeline Asset.asset`
- `Assets/3.Resources/HaYW/RenderSettings/GrayZone_Renderer.asset`

프로젝트의 아트 렌더 설정 수치와 조명/Volume 실무 내용은 기존 문서를 함께 참고한다.

- `C:\Users\user\GrayZone_privateDoc-main\Docs\SHELTER_ART_RENDER_PIPELINE_HANDOFF_KR.md`

---

## 19. 현재 상태와 미구현 범위

### 검증 완료

- Medical Room 거리 표시/숨김의 주체가 `DistanceRendererCullingGroup`임
- `SetActive`가 아니라 `Renderer.forceRenderingOff`를 사용함
- Collider가 남는 이유
- 27개 Material과 5개 Shader 계열 구성
- `MI_*` Material의 원본 프로퍼티 계약과 `DF_Test03` 불일치
- 테스트용 Material 27개 복사본 생성, 원본과 Scene 할당 미변경
- `DF_Test03`이 URP/Lit 계열 테스트 Graph임

### 테스트 자산 상태

- `DF_Test03`은 Lit 계열 검증용이며 모든 Medical 재질을 대체하는 범용 셰이더가 아님
- 복사된 Medical Material은 테스트 폴더에 있으며 원본 자산과 독립적임
- `MI_*`를 `DF_Test03`으로 직접 변경하면 프로퍼티 불일치로 외형이 깨짐

### 아직 구현되지 않은 권장안

- 공용 `MedicalDither.hlsl`
- SurvivorBase 3계열의 디더 파생 셰이더
- RealBlendVariationURP의 디더 파생 Graph
- DistanceRendererCullingGroup의 Fade 상태 머신 통합
- Forward/Shadow/Depth 패스 전체 검증
- Play Mode 거리 왕복 및 성능 프로파일링

---

## 20. 권장 구현 순서

1. **값 규약 확정**  
   `_DitherVisibility`: `0=숨김, 1=보임`처럼 이름과 방향을 통일한다.

2. **URP/Lit 한 계열만 수직 검증**  
   `DF_Test03`으로 Forward, ShadowCaster, DepthOnly, DepthNormals와 MPB 제어를 검증한다.

3. **거리 전환 상태 머신 구현**  
   8~10m에서 Fade하고 완전히 숨긴 뒤 `forceRenderingOff=true`가 되는지 확인한다.

4. **공용 디더 HLSL 추출**  
   검증된 최소 계산만 `MedicalDither.hlsl`로 옮긴다.

5. **원본 셰이더 계열별 파생본 제작**  
   `M_Standart_Master`, `M_Standart_Cutout`, `M_Glass`, `RealBlendVariationURP` 순으로 원본 외형을 유지하며 적용한다.

6. **원본/파생본 비교**  
   동일 카메라, 동일 조명, 동일 Volume에서 텍스처, 광택, Normal, AO, 유리, Cutout, 그림자를 비교한다.

7. **성능 측정**  
   Fade 중과 완전 컬링 후의 Batches, SetPass, CPU/GPU 시간을 측정한다.

8. **전체 Medical Room 적용**  
   테스트 Material에서 검증한 뒤 Scene/Prefab의 실제 Material 교체 범위를 결정한다.

---

## 21. 문제 진단 체크리스트

### 텍스처가 사라졌을 때

- 기존/새 셰이더의 프로퍼티 이름이 같은가
- Texture 타입과 2D/Array/Cube 타입이 같은가
- `[Normal]` 처리와 샘플 타입이 맞는가
- ARM 채널 분리가 원본과 같은가
- Smoothness와 Roughness 방향이 같은가
- Material의 직렬화 데이터에는 기존 참조가 남아 있는가

### 디더가 반대로 동작할 때

- 값 규약이 Visibility인지 Occlusion인지 확인
- 0과 1 중 어느 쪽이 보임인지 확인
- `One Minus`가 중복 적용됐는지 확인
- 스크립트 목표값과 Graph 입력값을 동시에 로그/Inspector로 확인

### 본체와 그림자가 다르게 사라질 때

- ShadowCaster 패스에도 Clip이 적용되는가
- Shadow Map에서 사용하는 좌표 기준이 적절한가
- Renderer shadowCastingMode를 별도로 전환하는 코드가 있는가

### Collider는 남고 화면에서만 사라질 때

- `forceRenderingOff`
- `Renderer.enabled`
- Camera Layer Culling
- Occlusion Culling
- LOD 최종 단계
- Shader의 Alpha Clip 값

### 거리 경계에서 깜빡일 때

- Show/Hide 거리 히스테리시스 존재 여부
- 거리 기준점과 Bounding Sphere 반지름
- 카메라 흔들림과 Update 시점
- Fade 상태가 매 프레임 반전되는지
- CullingGroup 이벤트와 수동 거리 계산이 중복되는지

---

## 22. 용어 정리

- **렌더 파이프라인**: 장면에서 최종 화면까지 만드는 전체 절차
- **렌더링 경로**: Forward, Forward+, Deferred 같은 조명/표면 처리 전략
- **SRP**: C#으로 렌더 루프를 구성하는 Unity의 Scriptable Render Pipeline 체계
- **URP**: 다양한 플랫폼을 대상으로 하는 Unity의 범용 SRP
- **ShaderLab**: Unity 셰이더의 Properties, SubShader, Pass, 렌더 상태를 정의하는 구조
- **HLSL**: GPU 계산을 작성하는 셰이딩 언어
- **Shader Graph**: 노드 기반으로 ShaderLab/HLSL을 생성하는 제작 도구
- **Sub Graph**: 여러 Shader Graph에서 재사용하는 함수형 노드 묶음
- **Material**: Shader 참조와 그 프로퍼티 값을 저장한 에셋
- **MaterialPropertyBlock**: Material 복제 없이 Renderer별 프로퍼티를 덮어쓰는 데이터
- **Alpha Clip**: 기준보다 작은 Alpha의 프래그먼트를 완전히 폐기하는 처리
- **Dither Fade**: 공간 패턴과 Fade 값을 비교해 픽셀을 점진적으로 폐기하는 전환
- **Render Pass**: 그림자, 깊이, 불투명 등 특정 목적의 렌더 단계
- **Draw Call**: 메시를 특정 상태로 그리라는 CPU → GPU 명령
- **Shader Variant**: Keyword와 플랫폼 조건에 따라 컴파일된 셰이더 변형
- **SRP Batcher**: SRP 셰이더의 CPU 상태 설정 비용을 줄이는 Unity 최적화
- **CullingGroup**: Bounding Sphere 기반 가시성/거리 밴드를 효율적으로 추적하는 Unity API
- **Hysteresis**: 표시와 숨김 기준을 다르게 둬 경계의 반복 전환을 막는 방식

---

## 23. 프로젝트 관련 경로

```text
거리 컬링 스크립트
C:\Users\user\GrayZone\Assets\1.Scripts\Rendering\DistanceRendererCullingGroup.cs

기존 지붕 가림 페이드
C:\Users\user\GrayZone\Assets\1.Scripts\Shelter\TPS\CameraOcclusionFader.cs
C:\Users\user\GrayZone\Assets\1.Scripts\Shelter\TPS\RoofOcclusionTarget.cs

Lit 디더 테스트 Graph
C:\Users\user\GrayZone\Assets\3.Resources\TempImage\TestShader\DF_Test03.shadergraph

Medical Material 테스트 복사본
C:\Users\user\GrayZone\Assets\3.Resources\TempImage\TestShader\Medical

SurvivorBase 원본 셰이더
C:\Users\user\GrayZone\Assets\3.Resources\ThirdParty\SurvivorBase\Shaders\M_Standart_Master.shader
C:\Users\user\GrayZone\Assets\3.Resources\ThirdParty\SurvivorBase\Shaders\M_Standart_Masked.shader
C:\Users\user\GrayZone\Assets\3.Resources\ThirdParty\SurvivorBase\Shaders\M_Glass.shader

기존 아트 렌더 파이프라인 인계 문서
C:\Users\user\GrayZone_privateDoc-main\Docs\SHELTER_ART_RENDER_PIPELINE_HANDOFF_KR.md
```

---

## 24. 공식 참고 자료

- [Microsoft Direct3D 10 Graphics Pipeline Stages](https://learn.microsoft.com/en-us/windows/win32/direct3d10/d3d10-graphics-programming-guide-pipeline-stages)
- [Unity 6 렌더 파이프라인 소개](https://docs.unity3d.com/kr/6000.0/Manual/render-pipelines.html)
- [Unity 렌더 파이프라인 선택](https://docs.unity3d.com/kr/6000.0/Manual/choose-a-render-pipeline.html)
- [Unity 렌더 파이프라인 기능 비교](https://docs.unity3d.com/kr/current/Manual/render-pipelines-feature-comparison.html)
- [URP의 렌더링 동작](https://docs.unity3d.com/kr/6000.0/Manual/urp/rendering-in-universalrp.html)
- [URP Forward 및 Forward+ 렌더링 경로](https://docs.unity3d.com/kr/current/Manual/urp/rendering/forward-rendering-paths.html)
- [URP Deferred 렌더링 경로](https://docs.unity3d.com/kr/current/Manual/urp/rendering/deferred-rendering-path-landing.html)
- [URP Render Graph](https://docs.unity3d.com/kr/6000.0/Manual/urp/render-graph.html)
- [Shader Graph 17 Sub Graph](https://docs.unity3d.com/Packages/com.unity.shadergraph@17.0/manual/Sub-graph.html)
- [Shader Graph 17 Custom Function Node](https://docs.unity3d.com/Packages/com.unity.shadergraph@17.0/manual/Custom-Function-Node.html)
- [Unity Draw Call 최적화 방법 선택](https://docs.unity3d.com/kr/current/Manual/optimizing-draw-calls-choose-method.html)
- [URP GPU Resident Drawer](https://docs.unity3d.com/kr/current/Manual/urp/gpu-resident-drawer.html)

---

## 25. 최종 요약

이번 문제는 “Unity라서 디더 전용 셰이더를 렌더 파이프라인 중간에 못 넣는다”의 문제가 아니다. 핵심은 **최종 표면 셰이더가 다섯 가지 서로 다른 입력·렌더 상태 계약을 갖고 있다는 점**이다.

공용 디더 계산은 자체 엔진의 include 함수처럼 하나로 공유할 수 있다. Shader Graph끼리는 Sub Graph를 사용할 수 있고, Shader Graph와 수동 `.shader`를 함께 지원하려면 외부 HLSL + Custom Function/`#include`가 적합하다. 그러나 그 공용 함수가 각 셰이더의 Albedo, Normal, ARM, Cutout, Glass, Pass 구조를 자동으로 통합해 주지는 않는다.

따라서 GrayZone Medical Room의 현실적인 해법은 다음 한 줄로 요약된다.

> **기존 5개 셰이더 계열의 외형과 프로퍼티 계약을 유지한 디더 대응본을 만들고, 8~10m 구간에서는 셰이더로 보간하며 완전히 숨은 뒤에만 `forceRenderingOff`로 실제 컬링한다.**
