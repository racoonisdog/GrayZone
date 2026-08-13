using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 필드 씬에서 소음이 구조물에 얼마나 막히는지를 판정하는 매니저입니다.
/// </summary>
/// <remarks>
/// <para>
/// 재질 판별(<see cref="SurfaceMaterialTag"/> -> <see cref="NoiseOcclusionSO"/>), 구조물 예외
/// (<see cref="NoiseOccluder"/>), 경로상 차폐 누적을 소유합니다.
/// <see cref="EffectManager"/>가 표면 피드백에 대해 같은 자리를 맡는 것과 같은 구조이며, 재질 축도
/// 그쪽과 공유합니다.
/// </para>
/// <para>
/// 차폐 판정을 듣는 쪽(<see cref="EnemyTargetSensor"/>)이 아니라 여기에 둔 이유는 "어떤 재질이 소리를
/// 얼마나 막는가"가 개체가 아니라 필드의 성질이기 때문입니다. 개체마다 들면 같은 벽의 차폐율이
/// 프리팹 수만큼 생깁니다. 반대로 무엇을 장애물로 볼지(레이어)는 듣는 쪽이 넘겨 줍니다.
/// </para>
/// <para>
/// <b>기획 편차</b>: 공용 `적 시스템` v0.2 §5.4.4는 "거리 감쇠 외에 벽이나 엄폐물에 의한 장애물 감쇠는
/// 적용하지 않는다", §6은 "소음 감지는 거리 감쇠만 적용하며 장애물을 판정하지 않는다"고 정합니다.
/// 이 매니저는 그 조항과 어긋나며, 사용자 지시로 코드를 먼저 바꾼 상태입니다. 문서 개정 전까지
/// 이 편차를 문서 쪽 실수로 오해해 되돌리지 말 것.
/// </para>
/// </remarks>
[DisallowMultipleComponent]
public sealed class NoiseManager : MonoBehaviour
{
    [Tooltip("등록된 재질과 일치하지 않는 구조물에 적용할 기본 차폐율입니다. 0이면 그대로 통과하고 1이면 완전히 막습니다. 물리 머티리얼이 배치되기 전에는 사실상 모든 벽이 이 값을 씁니다. 기획 미확정 - 임시값입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_defaultOcclusion = 0.5f;

    [Tooltip("필드에서 판별할 재질별 소음 차폐율 목록입니다.")]
    [SerializeField] private NoiseOcclusionSO[] m_occlusions = Array.Empty<NoiseOcclusionSO>();

    [Tooltip("소음 하나가 통과하는 경로에서 셀 수 있는 구조물의 최대 개수입니다. 이보다 많이 겹치면 초과분은 세지 않아 차폐가 실제보다 약해집니다.")]
    [Min(1)]
    [SerializeField] private int m_maxOccluderCount = 8;

    [Header("Debug")]
    [Tooltip("차폐 판정마다 통과 비율과 맞은 구조물을 콘솔에 남깁니다. 발소리처럼 잦은 소음에서는 로그가 크게 늘어납니다.")]
    [SerializeField] private bool m_debugLogTransmission;

    /// <summary>차폐 판정에 재사용하는 Raycast 결과 버퍼입니다.</summary>
    private RaycastHit[] m_hitBuffer;

    /// <summary>버퍼가 꽉 차 차폐를 과소평가했다는 경고를 이미 남겼는지 여부입니다.</summary>
    private bool m_warnedBufferFull;

    /// <summary>일치하는 재질이 없을 때 적용할 기본 차폐율입니다.</summary>
    public float DefaultOcclusion => Mathf.Clamp01(m_defaultOcclusion);

    /// <summary>필드에서 사용할 재질별 소음 차폐율 목록입니다.</summary>
    public IReadOnlyList<NoiseOcclusionSO> Occlusions => m_occlusions;

    private void Awake()
    {
        m_hitBuffer = new RaycastHit[Mathf.Max(1, m_maxOccluderCount)];
    }

    /// <summary>
    /// 소음이 듣는 쪽까지 도달하며 남는 비율을 구합니다.
    /// </summary>
    /// <param name="listenerPosition">소음을 듣는 위치입니다. 귀 높이를 넘기는 편이 정확합니다.</param>
    /// <param name="noisePosition">소음이 발생한 위치입니다.</param>
    /// <param name="occluderMask">소음을 막는 것으로 취급할 레이어 마스크입니다.</param>
    /// <returns>1이면 아무것도 막지 않았고, 0이면 완전히 막혔습니다.</returns>
    /// <remarks>
    /// 겹친 구조물은 비율을 <b>곱합니다</b>. 차폐율 0.5인 벽 두 겹이면 0.25가 남습니다. 더하면 벽 두 겹에서
    /// 이미 1을 넘어 세 번째 벽부터 의미가 없어지고, 최대값만 쓰면 벽을 여러 겹 두는 선택이 무의미해집니다.
    ///
    /// 볼록 콜라이더 하나는 들어가는 면에서 한 번만 맞으므로 벽 하나가 한 겹으로 셉니다. 다만 같은 벽을
    /// 콜라이더 여러 개로 만들어 두면 그만큼 겹으로 세어집니다. 레벨 구성에 따라 달라지는 부분입니다.
    ///
    /// 버퍼가 꽉 차면 초과분은 세지 않습니다. 즉 오차는 항상 "덜 막힌다" 쪽이며, 소리가 통과하는 것이
    /// 막히는 것보다 눈에 덜 띄어 조용히 지나가므로 한 번은 경고를 남깁니다.
    /// </remarks>
    public float GetTransmission(Vector3 listenerPosition, Vector3 noisePosition, int occluderMask)
    {
        if (occluderMask == 0)
        {
            return 1.0f;
        }

        if (m_hitBuffer == null || m_hitBuffer.Length != Mathf.Max(1, m_maxOccluderCount))
        {
            m_hitBuffer = new RaycastHit[Mathf.Max(1, m_maxOccluderCount)];
        }

        Vector3 delta = noisePosition - listenerPosition;
        float distance = delta.magnitude;

        if (distance <= 0.0001f)
        {
            return 1.0f;
        }

        Vector3 direction = delta / distance;

        int count = Physics.RaycastNonAlloc(
            listenerPosition, direction, m_hitBuffer, distance, occluderMask, QueryTriggerInteraction.Ignore);

        if (count >= m_hitBuffer.Length && !m_warnedBufferFull)
        {
            m_warnedBufferFull = true;
            Debug.LogWarning(
                $"[NoiseManager] 소음 경로에서 구조물이 최대 개수({m_hitBuffer.Length})를 채웠습니다. " +
                "초과분은 세지 않으므로 차폐가 실제보다 약해집니다. Max Occluder Count를 올리는 편이 안전합니다.",
                this);
        }

        float transmission = 1.0f;

        for (int i = 0; i < count; i++)
        {
            Collider hitCollider = m_hitBuffer[i].collider;
            if (hitCollider == null)
            {
                continue;
            }

            transmission *= 1.0f - ResolveColliderOcclusion(hitCollider);

            if (transmission <= 0.0f)
            {
                transmission = 0.0f;
                break;
            }
        }

        if (m_debugLogTransmission)
        {
            Debug.Log($"[NoiseManager] 차폐 {count}겹 -> 통과 비율 {transmission:F3}", this);
        }

        return transmission;
    }

    /// <summary>
    /// 콜라이더 하나가 소음을 얼마나 막는지 구합니다.
    /// </summary>
    /// <param name="collider">경로에서 맞은 콜라이더입니다.</param>
    /// <returns>이 구조물을 한 겹 통과할 때 깎이는 비율입니다.</returns>
    /// <remarks>
    /// 구조물 지정(<see cref="NoiseOccluder"/>)이 재질 기본값을 이깁니다. 소리를 막는 것은 표면 재질이
    /// 아니라 구조라서, 같은 콘크리트라도 칸막이와 방호벽이 달라야 하기 때문입니다. 다만 전부 손으로
    /// 지정하면 값이 필드에 흩어지므로 재질로 설명되지 않는 것만 덮습니다.
    /// </remarks>
    private float ResolveColliderOcclusion(Collider collider)
    {
        if (NoiseOccluder.TryResolve(collider, out float overridden))
        {
            return overridden;
        }

        return ResolveOcclusion(SurfaceMaterialTag.Resolve(collider));
    }

    /// <summary>
    /// 재질에 대응하는 차폐율을 찾습니다.
    /// </summary>
    /// <param name="materialType">구조물의 재질입니다. <c>Unknown</c>이면 기본값을 씁니다.</param>
    /// <returns>이 재질을 한 겹 통과할 때 깎이는 비율입니다.</returns>
    /// <remarks>
    /// 목록을 앞에서부터 훑어 처음 일치하는 항목을 씁니다. 같은 재질을 두 에셋이 가리키면 앞선 것이
    /// 이깁니다. 항목 수가 재질 수만큼이라 사전을 만들 이득이 없어 선형 탐색으로 둡니다.
    /// <see cref="EffectManager.ResolveSurfaceFeedback"/>과 같은 방식입니다.
    /// </remarks>
    public float ResolveOcclusion(SurfaceMaterialType materialType)
    {
        if (materialType == SurfaceMaterialType.Unknown || m_occlusions == null)
        {
            return DefaultOcclusion;
        }

        for (int occlusionIndex = 0; occlusionIndex < m_occlusions.Length; occlusionIndex++)
        {
            NoiseOcclusionSO occlusion = m_occlusions[occlusionIndex];
            if (occlusion == null || occlusion.MaterialTypes == null)
            {
                continue;
            }

            for (int materialIndex = 0; materialIndex < occlusion.MaterialTypes.Count; materialIndex++)
            {
                if (occlusion.MaterialTypes[materialIndex] == materialType)
                {
                    return occlusion.Occlusion;
                }
            }
        }

        return DefaultOcclusion;
    }
}
