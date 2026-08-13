using UnityEngine;

/// <summary>
/// 필드 씬에서 소음이 구조물에 얼마나 막히는지를 판정하는 매니저입니다.
/// </summary>
/// <remarks>
/// <para>
/// 재질 판별(<see cref="SurfaceMaterialTag"/> -> <see cref="NoiseOcclusionSO"/>), 구조물 예외
/// (<see cref="NoiseOccluder"/>), 무엇이 소리를 막는지(레이어), 경로상 차폐 누적을 소유합니다.
/// <see cref="EffectManager"/>가 표면 피드백에 대해 같은 자리를 맡는 것과 같은 구조이며, 재질 축도
/// 그쪽과 공유합니다.
/// </para>
/// <para>
/// 차폐 판정을 듣는 쪽(<see cref="EnemyTargetSensor"/>)이 아니라 여기에 둔 이유는 "어떤 재질이 소리를
/// 얼마나 막는가"가 개체가 아니라 필드의 성질이기 때문입니다. 개체마다 들면 같은 벽의 차폐율이
/// 프리팹 수만큼 생깁니다. 무엇을 차폐물로 볼지도 같은 이유로 여기 있습니다. 예민한 개체와 둔한 개체가
/// 서로 다른 벽에 막힌다는 것은 말이 되지 않습니다.
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
    /// <summary>차폐 레이어를 지정하지 않았을 때 사용할 구조물 레이어 이름입니다.</summary>
    /// <remarks>
    /// <c>Default</c>를 일부러 뺐습니다. 시야 판정은 <c>Default</c>를 포함하지만, 그 레이어에는 난간·소품처럼
    /// 시야는 가려도 소리는 거의 막지 않는 것이 섞여 있습니다. 소리와 시야가 같은 집합을 쓰면 화분 뒤에
    /// 섰다고 총성이 절반으로 줄어듭니다. 구조물 레이어가 정리되기 전까지는 이 기본값에서 차폐가 거의
    /// 걸리지 않는 것이 정상이며, 그것이 "레이어가 아직 정리되지 않았다"는 신호입니다.
    /// </remarks>
    private static readonly string[] s_defaultOccluderLayerNames =
    {
        "Ground", "Environment", "Prop", "ShelterRoof", "ShelterFacility",
    };

    [Tooltip("재질별 소음 차폐율 표입니다. 비어 있으면 차폐를 적용하지 않습니다.")]
    [SerializeField] private NoiseOcclusionSO m_occlusionTable;

    [Tooltip("소음을 막는 것으로 취급할 레이어입니다. 시야 차단 레이어와 따로 둡니다. 비어 있으면 구조물 레이어 기본값을 쓰고 경고를 남깁니다.")]
    [SerializeField] private LayerMask m_occluderLayer;

    [Tooltip("소음 하나가 통과하는 경로에서 셀 수 있는 구조물의 최대 개수입니다. 이보다 많이 겹치면 초과분은 세지 않아 차폐가 실제보다 약해집니다.")]
    [Min(1)]
    [SerializeField] private int m_maxOccluderCount = 8;

    [Header("Debug")]
    [Tooltip("차폐 판정마다 통과 비율과 맞은 구조물 수를 콘솔에 남깁니다. 발소리처럼 잦은 소음에서는 로그가 크게 늘어납니다.")]
    [SerializeField] private bool m_debugLogTransmission;

    /// <summary>차폐 판정에 재사용하는 Raycast 결과 버퍼입니다.</summary>
    private RaycastHit[] m_hitBuffer;

    /// <summary>실제로 사용할 차폐 레이어입니다. 지정이 없으면 기본값으로 채웁니다.</summary>
    private int m_resolvedOccluderMask;

    /// <summary>버퍼가 꽉 차 차폐를 과소평가했다는 경고를 이미 남겼는지 여부입니다.</summary>
    private bool m_warnedBufferFull;

    /// <summary>차폐율 표가 없다는 경고를 이미 남겼는지 여부입니다.</summary>
    private bool m_warnedMissingTable;

    /// <summary>재질별 소음 차폐율 표입니다. 없으면 <c>null</c>입니다.</summary>
    public NoiseOcclusionSO OcclusionTable => m_occlusionTable;

    /// <summary>실제로 사용 중인 차폐 레이어 마스크입니다. 진단용입니다.</summary>
    public int OccluderMask => m_resolvedOccluderMask;

    private void Awake()
    {
        m_hitBuffer = new RaycastHit[Mathf.Max(1, m_maxOccluderCount)];
        m_resolvedOccluderMask = ResolveOccluderMask();
    }

    /// <summary>
    /// 소음이 듣는 쪽까지 도달하며 남는 비율을 구합니다.
    /// </summary>
    /// <param name="listenerPosition">소음을 듣는 위치입니다. 귀 높이를 넘기는 편이 정확합니다.</param>
    /// <param name="noisePosition">소음이 발생한 위치입니다.</param>
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
    public float GetTransmission(Vector3 listenerPosition, Vector3 noisePosition)
    {
        if (m_occlusionTable == null)
        {
            WarnMissingTable();
            return 1.0f;
        }

        if (m_resolvedOccluderMask == 0)
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
            listenerPosition, direction, m_hitBuffer, distance, m_resolvedOccluderMask, QueryTriggerInteraction.Ignore);

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
    /// 구조물 지정(<see cref="NoiseOccluder"/>)이 재질보다 앞섭니다. 두께처럼 재질로 설명되지 않는 것을
    /// 위한 자리이며, 재질 이름으로 말할 수 있는 예외는 <see cref="SurfaceMaterialTag"/>의 소음 재질
    /// 지정을 쓰는 편이 낫습니다. 그쪽은 차폐율 표 안에 남지만 이쪽은 표 밖의 날숫자가 됩니다.
    /// </remarks>
    private float ResolveColliderOcclusion(Collider collider)
    {
        if (NoiseOccluder.TryResolve(collider, out float overridden))
        {
            return overridden;
        }

        return m_occlusionTable.Resolve(SurfaceMaterialTag.ResolveNoise(collider));
    }

    /// <summary>지정된 차폐 레이어를 그대로 쓰되, 비어 있으면 구조물 기본값으로 대체합니다.</summary>
    /// <remarks>
    /// 비워 두면 <c>Nothing</c>이라 아무것도 맞히지 못해 차폐가 통째로 죽습니다. 에러 없이 조용히 지나가고
    /// "왜 벽 뒤 총성이 그대로 들리지?"로만 드러나므로, <see cref="EnemyLayers"/>와 같은 방식으로 기본값을
    /// 채우고 경고를 남깁니다.
    /// </remarks>
    private int ResolveOccluderMask()
    {
        if (m_occluderLayer.value != 0)
        {
            return m_occluderLayer.value;
        }

        int fallback = 0;
        for (int i = 0; i < s_defaultOccluderLayerNames.Length; i++)
        {
            int layer = LayerMask.NameToLayer(s_defaultOccluderLayerNames[i]);
            if (layer >= 0)
            {
                fallback |= 1 << layer;
            }
        }

        Debug.LogWarning(
            $"[NoiseManager] 소음 차폐 레이어가 비어 있어 기본값({string.Join("/", s_defaultOccluderLayerNames)})을 사용합니다. " +
            "시야 차단과 달리 Default는 일부러 제외했습니다. 구조물이 Default에 있으면 차폐가 걸리지 않으니 " +
            "레이어를 정리하거나 인스펙터에서 명시하십시오.",
            this);

        return fallback;
    }

    private void WarnMissingTable()
    {
        if (m_warnedMissingTable)
        {
            return;
        }

        m_warnedMissingTable = true;
        Debug.LogWarning(
            "[NoiseManager] 재질별 소음 차폐율 표(NoiseOcclusionSO)가 비어 있어 차폐를 적용하지 않습니다. " +
            "기본 차폐율까지 표가 소유하므로, 코드에 같은 값을 두 벌 두지 않기 위해 대체값을 쓰지 않습니다.",
            this);
    }
}
