using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 변이체가 스쿼드 캐릭터에 대해 무엇을 알고 있는지를 소유하고, 그중 현재 대상을 선정하는 Module입니다.
/// </summary>
/// <remarks>
/// 이 컴포넌트는 "무엇을 아는가"만 다루고, 그 결과로 무엇을 할지는 HFSM 상태가 결정합니다.
/// 후보는 <see cref="SquadManager.SquadMembers"/>로 고정하며 갱신마다 전역 탐색을 하지 않습니다.
/// 정보 갱신은 두 경로로 들어옵니다.
/// Pull은 주기적인 시야 판정이고, Push는 직접 피격·소음·하울링처럼 사건이 생긴 순간의 통보입니다.
/// 슬라이스 1에서는 Pull의 시야 감지만 구현하고 Push 진입점은 자리만 만들어 둡니다.
/// 설계 근거: 공용 `적 시스템` v0.2 §5.4(감지), §5.6(스쿼드 AI 비전투 감지 보호), §5.7(대상 정보와 대상 선정).
/// </remarks>
public class EnemyTargetSensor : MonoBehaviour
{
    /// <summary>대상 정보를 얻게 된 경로입니다. 규칙이 출처마다 다르므로 구분해 둡니다.</summary>
    public enum InfoSource
    {
        /// <summary>아는 것이 없는 상태입니다.</summary>
        None,

        /// <summary>시야 또는 근접 감지로 직접 인식했습니다.</summary>
        DirectPerception,

        /// <summary>자신을 공격한 캐릭터로 확인했습니다.</summary>
        DirectAttacker,

        /// <summary>다른 변이체의 하울링으로 위치를 제공받았습니다.</summary>
        Howl,
    }

    /// <summary>
    /// 스쿼드 캐릭터 한 명에 대해 이 변이체가 기억하는 내용입니다.
    /// </summary>
    /// <remarks>
    /// 공용 문서 §5.7.1의 캐릭터별 대상 정보를 그대로 옮긴 구조입니다.
    /// 출전 스쿼드 인원수만큼만 만들어 재사용하므로 변이체가 늘어도 이 기록 자체는 부담이 되지 않습니다.
    /// </remarks>
    public class TargetInfo
    {
        /// <summary>이 기록이 가리키는 스쿼드 캐릭터입니다.</summary>
        public SquadMemberController Member;

        /// <summary>지금 이 캐릭터의 실시간 위치를 알고 있는지 여부입니다.</summary>
        public bool HasLivePosition;

        /// <summary>실시간 위치를 계속 아는 것이 끝나는 시각입니다.</summary>
        public float TrackingExpireTime;

        /// <summary>마지막으로 위치를 확인한 지점입니다. 대상이 움직여도 따라가지 않습니다.</summary>
        public Vector3 LastKnownPosition;

        /// <summary>마지막으로 위치를 확인한 시각입니다.</summary>
        public float LastKnownTime;

        /// <summary>이 정보를 얻게 된 경로입니다.</summary>
        public InfoSource Source;

        /// <summary>기록을 아무것도 모르는 상태로 되돌립니다.</summary>
        public void Clear()
        {
            HasLivePosition = false;
            TrackingExpireTime = 0f;
            LastKnownPosition = Vector3.zero;
            LastKnownTime = 0f;
            Source = InfoSource.None;
        }
    }

    [Header("Squad")]
    [Tooltip("후보 캐릭터를 제공할 스쿼드 매니저입니다. 비어 있으면 씬에서 한 번만 찾습니다.")]
    [SerializeField] private SquadManager m_squadManager;

    [Header("Sight")]
    [Tooltip("시야를 가로막는 고정 환경 장애물 레이어입니다. 다른 변이체는 여기 포함하지 않습니다.")]
    [SerializeField] private LayerMask m_obstacleLayer;

    [Tooltip("시야 판정 Raycast의 시작점입니다. 비어 있으면 머리 높이의 기본 위치를 사용합니다.")]
    [SerializeField] private Transform m_eyePoint;

    [Tooltip("시야로 대상을 감지할 수 있는 최대 거리입니다.")]
    [SerializeField] private float m_sightRange = 12f;

    [Tooltip("전방을 기준으로 하는 전체 시야각입니다.")]
    [SerializeField] private float m_sightAngle = 120f;

    [Tooltip("시야 판정을 다시 수행하는 주기입니다.")]
    [SerializeField] private float m_perceptionInterval = 0.25f;

    [Tooltip("시야에서 벗어난 뒤에도 실시간 위치를 계속 아는 시간입니다.")]
    [SerializeField] private float m_trackingHoldDuration = 2f;

    [Header("Noise")]
    [Tooltip("소음의 도달 거리에 곱하는 청각 배수입니다. 1이면 도달 거리 그대로, 1보다 크면 더 멀리 듣습니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_noiseHearingMultiplier = 1f;

    [Tooltip("소음 인지 게이지가 이 값에 닿으면 소음 위치를 추적하기 시작합니다. 낮으면 금방 알아챕니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_noiseAwarenessThreshold = 1f;

    [Tooltip("소음 인지 게이지가 초당 줄어드는 양입니다. 소음이 끊기면 이 속도로 빠져 결국 경계를 풉니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_noiseAwarenessDecayPerSecond = 0.35f;

    [Header("Target Selection")]
    [Tooltip("현재 대상을 다시 고를지 판단하는 주기입니다.")]
    [SerializeField] private float m_reevaluateInterval = 1f;

    [Tooltip("새 후보가 현재 대상보다 이만큼 더 가까워야 대상을 바꿉니다.")]
    [SerializeField] private float m_switchPathDistanceDelta = 2f;

    /// <summary>스쿼드 캐릭터별 기록입니다. 인원수만큼만 만들고 재사용합니다.</summary>
    private readonly List<TargetInfo> m_infos = new List<TargetInfo>();

    /// <summary>경로 거리 계산에 재사용하는 버퍼입니다. 매번 새로 만들지 않기 위한 것입니다.</summary>
    /// <remarks>
    /// 필드 초기화자에서 만들면 안 됩니다. Unity가 MonoBehaviour 생성자 시점의 NavMeshPath 생성을 막습니다.
    /// </remarks>
    private NavMeshPath m_pathBuffer;

    /// <summary>다음 시야 판정 시각입니다.</summary>
    private float m_nextPerceptionTime;

    /// <summary>다음 대상 재평가 시각입니다.</summary>
    private float m_nextReevaluateTime;

    /// <summary>현재 선택된 대상입니다.</summary>
    private SquadMemberController m_currentTarget;

    /// <summary>이 변이체가 교전 상태인지 여부입니다. 비전투 감지 보호 판단에 사용합니다.</summary>
    private bool m_isEngaged;

    // =========================
    // 받아들인 소음 (대상 정보와 분리)
    // =========================
    // 소음은 캐릭터를 유효 대상으로 만들지 않으므로 TargetInfo에 섞지 않습니다(§5.4.4).
    // 우선순위를 발생 시점에만 평가하므로 "지금 추적 중인 소음" 하나만 들고 있습니다.

    /// <summary>받아들인 소음이 있는지 여부입니다.</summary>
    private bool m_hasNoise;

    /// <summary>지금 추적 중인 소음의 소음원 식별값입니다.</summary>
    private int m_noiseSourceId;

    /// <summary>지금 추적 중인 소음의 위치입니다.</summary>
    private Vector3 m_noisePosition;

    /// <summary>지금 추적 중인 소음의 감지 강도입니다. 다른 소음원과의 비교에 씁니다.</summary>
    private float m_noiseIntensity;

    /// <summary>지금 추적 중인 소음의 발생 시점입니다. 강도가 같을 때의 비교에 씁니다.</summary>
    private float m_noiseTime;

    /// <summary>마지막 확인 이후 소음이 새로 갱신됐는지 여부입니다. 상태가 소비합니다.</summary>
    private bool m_noiseUpdated;

    /// <summary>
    /// 소음 인지 게이지입니다. 소음을 들을 때마다 그 강도만큼 쌓이고 시간이 지나면 줄어듭니다.
    /// </summary>
    /// <remarks>
    /// 들리는 즉시 추적하지 않고 이 값이 한계에 닿을 때까지 두리번거리게 만드는 장치입니다.
    /// 큰 소리는 한 번에 한계를 넘고 작은 소리는 여러 번 필요하므로, 예외 코드 없이 수치로만
    /// "총성은 즉시, 발소리는 반복해야 들킴"이 나옵니다.
    /// 설계 근거: 기획 확정(2026-08-03). 공용 문서 §5.3.2의 각성 준비 시간을 누적 방식으로 일반화한 것입니다.
    /// </remarks>
    private float m_noiseAwareness;

    /// <summary>
    /// 인지 게이지가 한계에 닿았는지 여부입니다.
    /// </summary>
    /// <remarks>
    /// 게이지 값을 매번 한계치와 비교하지 않고 별도 플래그로 잠급니다.
    /// 비교로 하면 도달 판정이 구조적으로 실패합니다. 게이지가 한계치에서 잘리는데 감소가 판정보다 먼저 돌아
    /// 값이 항상 한계치보다 조금 낮아지기 때문입니다. 실측에서 만충인데도 추적으로 넘어가지 않아 발견했습니다.
    ///
    /// 의미상으로도 맞습니다. "다 쌓이면 알아챈다"는 한 번 일어나면 되돌지 않는 사건이므로
    /// 매 프레임 다시 판정할 대상이 아닙니다.
    /// </remarks>
    private bool m_noiseAwarenessReached;

    /// <summary>실제로 사용할 시야 차단 레이어입니다. 지정이 없으면 기본값으로 채웁니다.</summary>
    private int m_resolvedObstacleMask;

    /// <summary>현재 대상입니다. 더 이상 유효하지 않으면 null입니다.</summary>
    public SquadMemberController CurrentTarget => IsValidTarget(FindInfo(m_currentTarget)) ? m_currentTarget : null;

    /// <summary>현재 대상의 Transform입니다.</summary>
    public Transform CurrentTargetTransform => CurrentTarget != null ? CurrentTarget.transform : null;

    /// <summary>유효 대상이 한 명이라도 있는지 여부입니다.</summary>
    public bool HasAnyValidTarget
    {
        get
        {
            for (int i = 0; i < m_infos.Count; i++)
            {
                if (IsValidTarget(m_infos[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>캐릭터별 대상 정보 전체입니다. 상태가 읽기 전용으로 참고합니다.</summary>
    public IReadOnlyList<TargetInfo> Infos => m_infos;

    /// <summary>
    /// 적 밸런스 데이터에서 감지와 대상 선정 수치를 적용합니다.
    /// </summary>
    /// <param name="balance">적용할 순수 수치 밸런스 데이터입니다.</param>
    public void ApplyBalance(EnemyBalanceSO balance)
    {
        if (balance == null)
        {
            return;
        }

        m_perceptionInterval = balance.PerceptionInterval;
        m_sightRange = balance.SightRange;
        m_sightAngle = balance.SightAngle;
        m_trackingHoldDuration = balance.LoseSightDelay;
        m_reevaluateInterval = balance.TargetReevaluateInterval;
        m_switchPathDistanceDelta = balance.TargetSwitchPathDistanceDelta;
        m_noiseHearingMultiplier = balance.NoiseHearingMultiplier;
        m_noiseAwarenessThreshold = balance.NoiseAwarenessThreshold;
        m_noiseAwarenessDecayPerSecond = balance.NoiseAwarenessDecayPerSecond;
    }

    private void Awake()
    {
        m_pathBuffer = new NavMeshPath();
        m_resolvedObstacleMask = EnemyLayers.ResolveObstacleMask(m_obstacleLayer, this, "시야 차단");

        // 스쿼드 매니저는 씬당 하나이므로 여기서 한 번만 찾습니다.
        // 없애려는 것은 갱신마다 도는 전역 탐색이지 최초 1회 캐싱이 아닙니다.
        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>();
        }
    }

    /// <summary>소음 수신 목록에 등록합니다.</summary>
    /// <remarks>
    /// 소음 발신마다 씬을 훑지 않으려면 듣는 쪽을 미리 등록해 두어야 합니다.
    /// 사망 시 이 컴포넌트를 끄면 자동으로 빠지므로 시체가 소음을 듣지 않습니다.
    /// </remarks>
    private void OnEnable()
    {
        NoiseSystem.Register(this);
    }

    /// <summary>소음 수신 목록에서 빠집니다.</summary>
    private void OnDisable()
    {
        NoiseSystem.Unregister(this);
    }

    /// <summary>
    /// 이 변이체의 교전 상태를 설정합니다.
    /// </summary>
    /// <param name="engaged">교전 중이면 true입니다.</param>
    /// <remarks>
    /// 비교전 상태에서는 AI가 조작하는 캐릭터를 시야로 먼저 감지하지 않습니다(§5.6).
    /// 플레이어의 의도와 무관한 동료의 움직임 때문에 새 변이체가 끌려오는 것을 막기 위한 규칙입니다.
    /// 교전이 끝나면 보호를 다시 적용해야 하므로 상태 종료 시 false로 되돌립니다.
    /// </remarks>
    public void SetEngaged(bool engaged)
    {
        m_isEngaged = engaged;
    }

    /// <summary>
    /// 주기가 되었으면 시야 판정을 수행해 캐릭터별 대상 정보를 갱신합니다.
    /// </summary>
    /// <param name="force">true이면 주기를 무시하고 즉시 갱신합니다.</param>
    public void UpdatePerception(bool force = false)
    {
        SyncSquadMembers();

        // 게이지 감소는 시야 판정 주기와 무관하게 매 프레임 진행해야 합니다.
        // 주기에 묶으면 감소가 뚝뚝 끊겨 "소리를 멈추면 서서히 잊는다"가 성립하지 않습니다.
        DecayNoiseAwareness();

        if (!force && Time.time < m_nextPerceptionTime)
        {
            ExpireTracking();
            return;
        }

        m_nextPerceptionTime = Time.time + Mathf.Max(0.01f, m_perceptionInterval);

        Vector3 origin = GetEyePosition();
        for (int i = 0; i < m_infos.Count; i++)
        {
            TargetInfo info = m_infos[i];
            if (!IsAliveMember(info.Member))
            {
                // 다운·사망 캐릭터의 정보는 즉시 제거합니다(§5.7.5).
                info.Clear();
                continue;
            }

            if (!m_isEngaged && info.Member.IsAiSquadMember)
            {
                // 비전투 감지 보호: 아직 교전에 들어가지 않았으면 AI 조작 캐릭터는 감지 대상이 아닙니다.
                continue;
            }

            if (CanSee(origin, info.Member))
            {
                MarkDirectlyPerceived(info);
            }
        }

        ExpireTracking();
    }

    /// <summary>
    /// 주기가 되었으면 현재 대상을 다시 고릅니다.
    /// </summary>
    /// <param name="force">true이면 주기를 무시하고 즉시 재평가합니다.</param>
    /// <remarks>
    /// 현재 대상이 유효하지 않게 되면 주기를 기다리지 않고 즉시 다시 고릅니다(§5.7.4).
    /// 경로 거리는 이 시점에만 계산합니다. 매 틱 계산하면 변이체 수에 비례해 비용이 커집니다.
    /// </remarks>
    public void ReevaluateTarget(bool force = false)
    {
        bool currentLost = !IsValidTarget(FindInfo(m_currentTarget));
        if (!force && !currentLost && Time.time < m_nextReevaluateTime)
        {
            return;
        }

        m_nextReevaluateTime = Time.time + Mathf.Max(0.01f, m_reevaluateInterval);

        SquadMemberController best = null;
        float bestDistance = float.PositiveInfinity;
        float currentDistance = float.PositiveInfinity;

        for (int i = 0; i < m_infos.Count; i++)
        {
            TargetInfo info = m_infos[i];
            if (!IsValidTarget(info))
            {
                continue;
            }

            float distance = GetPathDistance(info.Member.transform.position);
            if (float.IsPositiveInfinity(distance))
            {
                // 이동 가능한 경로가 없으면 대상으로 삼지 않습니다.
                continue;
            }

            if (info.Member == m_currentTarget)
            {
                currentDistance = distance;
            }

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = info.Member;
            }
        }

        if (best == null)
        {
            m_currentTarget = null;
            return;
        }

        if (currentLost || m_currentTarget == null)
        {
            m_currentTarget = best;
            return;
        }

        // 이미 대상이 있으면 충분히 더 가까울 때만 바꿉니다. 경계에서 대상이 떨리는 것을 막습니다.
        if (best != m_currentTarget && currentDistance - bestDistance >= m_switchPathDistanceDelta)
        {
            m_currentTarget = best;
        }
    }

    /// <summary>
    /// 현재 대상의 추적 목적지를 반환합니다.
    /// </summary>
    /// <param name="position">실시간 위치를 아는 동안에는 최신 위치, 아니면 마지막 확인 위치입니다.</param>
    /// <returns>사용할 위치가 있으면 true입니다.</returns>
    public bool TryGetCurrentTargetPosition(out Vector3 position)
    {
        position = Vector3.zero;

        TargetInfo info = FindInfo(m_currentTarget);
        if (info == null)
        {
            return false;
        }

        if (info.HasLivePosition && info.Member != null)
        {
            position = info.Member.transform.position;
            return true;
        }

        if (info.Source != InfoSource.None)
        {
            position = info.LastKnownPosition;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 몸이 맞닿은 캐릭터를 즉시 인식합니다.
    /// </summary>
    /// <param name="member">몸이 닿은 스쿼드 캐릭터입니다.</param>
    /// <remarks>
    /// 평소에는 시야로만 찾고, 실제로 부딪히면 그 순간 위치를 알게 하는 규칙입니다(기획 확정).
    /// 공용 문서 §5.4.2는 반경 기반 360도 근접 감지를 규정하지만, 이 프로젝트는 접촉 기준으로 좁혀 씁니다.
    /// 뒤로 몰래 지나가는 것을 허용해 잠입을 너그럽게 만들려는 선택입니다.
    /// 비전투 감지 보호(§5.6)는 접촉에도 그대로 적용합니다.
    /// AI 동료가 스쳤다고 주변 변이체가 깨어나면 플레이어 의도와 무관하게 교전이 시작되기 때문입니다.
    /// 교전에 들어간 뒤에는 보호가 풀리므로 누가 부딪히든 인식합니다.
    /// </remarks>
    public void NotifyContact(SquadMemberController member)
    {
        TargetInfo info = FindInfo(member);
        if (info == null || !IsAliveMember(member))
        {
            return;
        }

        if (!m_isEngaged && member.IsAiSquadMember)
        {
            return;
        }

        MarkDirectlyPerceived(info);
    }

    /// <summary>
    /// 직접 피격당했을 때 공격자를 인식 대상으로 기록합니다.
    /// </summary>
    /// <param name="attacker">이 변이체를 공격한 스쿼드 캐릭터입니다.</param>
    /// <remarks>
    /// 공격자를 즉시 현재 대상으로 선택합니다(§5.7.3 직접 공격자 우선).
    /// 시야·접촉과 달리 <b>비전투 감지 보호를 무시</b>합니다. AI 동료가 쐈더라도 실제로 맞았으면 알아채야 하기 때문입니다(§5.6).
    /// 조준이 빗나간 총알이 멀리 있던 변이체를 맞혔을 때 그 변이체가 쏜 쪽을 쫓아오는 규칙이 이 경로로 성립합니다.
    /// </remarks>
    public void NotifyDamagedBy(SquadMemberController attacker)
    {
        TargetInfo info = FindInfo(attacker);
        if (info == null || !IsAliveMember(attacker))
        {
            return;
        }

        info.HasLivePosition = true;
        info.TrackingExpireTime = Time.time + Mathf.Max(0f, m_trackingHoldDuration);
        info.LastKnownPosition = attacker.transform.position;
        info.LastKnownTime = Time.time;
        info.Source = InfoSource.DirectAttacker;

        m_currentTarget = attacker;
    }

    /// <summary>
    /// 소음을 감지했을 때 추적할 위치를 받습니다.
    /// </summary>
    /// <param name="noise">발생한 소음 이벤트입니다.</param>
    /// <remarks>
    /// 소음은 위치만 알려주며 캐릭터를 유효 대상으로 만들지 않습니다(§5.4.4).
    /// 그래서 <see cref="TargetInfo"/>가 아니라 별도의 소음 추적 목적지로 보관합니다.
    /// 대상 정보에 섞으면 소음만으로 공격 대상이 생겨 문서 규칙이 깨집니다.
    ///
    /// 우선순위는 이 순간에만 평가합니다. 매 프레임 모든 소음을 비교하지 않습니다(§5.4.4).
    /// 그래서 "지금 추적 중인 소음" 하나만 들고 있으면 충분하며 이벤트 목록을 쌓지 않습니다.
    ///
    /// 판정 순서에 이유가 있습니다. 가청 -> 비전투 감지 보호 -> 교전 중 무시 -> 우선순위입니다.
    /// 가청을 먼저 보는 것은 못 들은 소음에 다른 규칙을 적용할 필요가 없기 때문입니다.
    ///
    /// 강도는 가청 여부를 정하지 않고 우선순위 비교에만 씁니다. 도달 거리가 곧 들리는 거리입니다.
    /// </remarks>
    public void NotifyNoise(in NoiseEvent noise)
    {
        if (!noise.IsAudibleAt(transform.position, m_noiseHearingMultiplier))
        {
            return;
        }

        // 비전투 감지 보호: 교전에 들어가지 않았으면 AI 조작 캐릭터의 소음은 듣지 않습니다(§5.6).
        // 발신 쪽에서 거르지 않는 이유는 같은 소음을 교전 중 개체는 듣고 비교전 개체는 무시해야 하기 때문입니다.
        if (!m_isEngaged && noise.EmittedByAi)
        {
            return;
        }

        // 유효 대상을 쫓거나 공격 중이면 관련 없는 소음으로 교전을 중단하지 않습니다(§5.4.4).
        // 교전 수색(유효 대상이 없는 교전 상태)은 아직 없으므로 그 분기는 두지 않았습니다.
        if (m_isEngaged && HasAnyValidTarget)
        {
            return;
        }

        float intensity = noise.GetIntensityAt(transform.position);
        if (!ShouldReplaceTrackedNoise(noise, intensity))
        {
            return;
        }

        m_hasNoise = true;
        m_noiseSourceId = noise.SourceId;
        m_noisePosition = noise.Position;
        m_noiseIntensity = intensity;
        m_noiseTime = noise.Time;
        m_noiseUpdated = true;

        // 인지 게이지는 감지 강도만큼 쌓입니다. 가까운 총성은 한 번에 한계를 넘고 발소리는 여러 번 필요합니다.
        m_noiseAwareness = Mathf.Min(m_noiseAwareness + intensity, m_noiseAwarenessThreshold);

        // 도달을 값 비교가 아니라 플래그로 잠급니다. 감소가 판정보다 먼저 돌기 때문입니다.
        if (m_noiseAwareness >= m_noiseAwarenessThreshold)
        {
            m_noiseAwarenessReached = true;
        }
    }

    /// <summary>
    /// 소음 인지 게이지를 시간에 따라 줄입니다.
    /// </summary>
    /// <remarks>
    /// 소음이 끊기면 게이지가 빠져 결국 경계를 풉니다. 이것이 "멈춰서 숨는다"를 실제 대응 수단으로 만듭니다.
    /// 이미 한계에 닿아 추적으로 넘어간 뒤에는 <see cref="ClearNoiseAwareness"/>가 게이지를 비우므로
    /// 여기서 별도 분기를 두지 않습니다.
    /// </remarks>
    private void DecayNoiseAwareness()
    {
        if (m_noiseAwareness <= 0f)
        {
            return;
        }

        m_noiseAwareness = Mathf.Max(0f, m_noiseAwareness - m_noiseAwarenessDecayPerSecond * Time.deltaTime);
    }

    /// <summary>소음 인지 게이지의 현재 값입니다.</summary>
    public float NoiseAwareness => m_noiseAwareness;

    /// <summary>소음 인지 게이지의 진행도입니다. 0이면 평온, 1이면 한계 도달입니다.</summary>
    /// <remarks>두리번 강도를 애니메이터로 넘길 때 씁니다.</remarks>
    public float NoiseAwareness01 =>
        m_noiseAwarenessThreshold > 0f ? Mathf.Clamp01(m_noiseAwareness / m_noiseAwarenessThreshold) : 0f;

    /// <summary>소음을 알아채 추적을 시작할 만큼 게이지가 찼는지 여부입니다.</summary>
    public bool IsNoiseAwarenessFull => m_hasNoise && m_noiseAwarenessReached;

    /// <summary>소음이 들려 경계 중이지만 아직 알아채지는 못한 상태인지 여부입니다.</summary>
    /// <remarks>이 값이 true인 동안 두리번거립니다.</remarks>
    public bool IsNoiseAlert => m_hasNoise && m_noiseAwareness > 0f && !IsNoiseAwarenessFull;

    /// <summary>
    /// 소음 인지 게이지를 비웁니다.
    /// </summary>
    /// <remarks>
    /// 소음 추적에 들어갈 때 부릅니다. 비우지 않으면 수색에 실패해 배회로 돌아온 순간 게이지가 아직 만충이라
    /// 곧바로 다시 추적으로 튕겨 나가 무한히 반복합니다.
    /// </remarks>
    public void ClearNoiseAwareness()
    {
        m_noiseAwareness = 0f;
        m_noiseAwarenessReached = false;
    }

    /// <summary>
    /// 새 소음이 지금 추적 중인 소음을 대체해야 하는지 판단합니다.
    /// </summary>
    /// <remarks>
    /// 공용 문서 §5.4.4의 세 규칙을 그대로 옮겼습니다.
    /// 같은 소음원이면 최신 위치로 갱신하고, 다른 소음원은 더 강할 때만 교체하며, 같으면 더 최근 것을 택합니다.
    ///
    /// 같은 소음원을 강도와 무관하게 받아들이는 것이 중요합니다. 소리를 낸 캐릭터가 멀어지면 강도가 약해지는데,
    /// 강도로만 비교하면 그 순간 목적지 갱신이 멈춰 변이체가 낡은 위치로 계속 갑니다.
    /// </remarks>
    private bool ShouldReplaceTrackedNoise(in NoiseEvent noise, float intensity)
    {
        if (!m_hasNoise)
        {
            return true;
        }

        if (noise.SourceId == m_noiseSourceId)
        {
            return true;
        }

        if (intensity > m_noiseIntensity)
        {
            return true;
        }

        return Mathf.Approximately(intensity, m_noiseIntensity) && noise.Time > m_noiseTime;
    }

    /// <summary>
    /// 지금 추적할 소음 위치를 반환합니다.
    /// </summary>
    /// <param name="position">추적할 소음 위치입니다.</param>
    /// <returns>받아들인 소음이 있으면 true입니다.</returns>
    public bool TryGetNoisePosition(out Vector3 position)
    {
        position = m_noisePosition;
        return m_hasNoise;
    }

    /// <summary>
    /// 소음을 새로 받아들였는지 확인하고 그 표시를 내립니다.
    /// </summary>
    /// <returns>마지막 확인 이후 새 소음을 받아들였으면 true입니다.</returns>
    /// <remarks>
    /// 소음 추적 중 목적지를 다시 잡을지, 수색 중 다시 추적으로 돌아갈지 판단하는 데 씁니다(§5.4.5).
    /// 상태가 이 표시를 소비하므로 같은 소음으로 두 번 반응하지 않습니다.
    /// </remarks>
    public bool ConsumeNoiseUpdated()
    {
        if (!m_noiseUpdated)
        {
            return false;
        }

        m_noiseUpdated = false;
        return true;
    }

    /// <summary>
    /// 받아들인 소음 기록을 지웁니다.
    /// </summary>
    /// <remarks>
    /// 소음 수색이 끝나 배회로 돌아갈 때 부릅니다. 지우지 않으면 배회 상태가 같은 소음을 다시 집어
    /// 추적과 수색을 무한히 반복합니다.
    /// </remarks>
    public void ClearNoise()
    {
        m_hasNoise = false;
        m_noiseSourceId = 0;
        m_noisePosition = Vector3.zero;
        m_noiseIntensity = 0f;
        m_noiseTime = 0f;
        m_noiseUpdated = false;
        m_noiseAwareness = 0f;
        m_noiseAwarenessReached = false;
    }

    /// <summary>
    /// 하울링을 수신해 스쿼드 캐릭터들의 실시간 위치를 제공받습니다.
    /// </summary>
    /// <param name="members">위치를 제공받을 캐릭터 목록입니다.</param>
    /// <remarks>
    /// 슬라이스 3에서 구현합니다. 하울링 위치 정보는 시야와 장애물을 무시하며,
    /// 유지 시간이 직접 인식과 별도로 설정됩니다(§5.5.5).
    /// </remarks>
    public void NotifyHowl(IReadOnlyList<SquadMemberController> members)
    {
        // TODO(슬라이스 3): 하울링 전용 유지 시간으로 실시간 위치 정보 부여.
    }

    /// <summary>
    /// 교전이 끝날 때 이 변이체가 알고 있던 대상 정보를 모두 지웁니다.
    /// </summary>
    /// <remarks>
    /// 공용 문서 §5.8.4의 교전 상태 종료 시 초기화 목록에 해당합니다.
    ///
    /// <b>소음 기록은 지우지 않습니다.</b> §5.8.4의 초기화 목록에 소음이 없고, 교전이 끝난 직후에도
    /// 방금 들린 소리를 향해 갈 수 있어야 하기 때문입니다. 소음은 소음 수색이 끝날 때
    /// <see cref="ClearNoise"/>로 따로 지웁니다.
    /// </remarks>
    public void ClearAllInfo()
    {
        for (int i = 0; i < m_infos.Count; i++)
        {
            m_infos[i].Clear();
        }

        m_currentTarget = null;
        m_nextReevaluateTime = 0f;
    }

    /// <summary>
    /// 스쿼드 구성이 바뀌었으면 기록 슬롯을 맞춥니다.
    /// </summary>
    /// <remarks>슬롯은 인원수만큼만 유지하며 매 갱신마다 새로 만들지 않습니다.</remarks>
    private void SyncSquadMembers()
    {
        IReadOnlyList<SquadMemberController> members = m_squadManager != null ? m_squadManager.SquadMembers : null;
        if (members == null)
        {
            return;
        }

        // 목록이 그대로면 아무것도 하지 않습니다. 대부분의 프레임이 이 경로입니다.
        if (m_infos.Count == members.Count)
        {
            bool identical = true;
            for (int i = 0; i < members.Count; i++)
            {
                if (m_infos[i].Member != members[i])
                {
                    identical = false;
                    break;
                }
            }

            if (identical)
            {
                return;
            }
        }

        m_infos.Clear();
        for (int i = 0; i < members.Count; i++)
        {
            m_infos.Add(new TargetInfo { Member = members[i] });
        }

        m_currentTarget = null;
    }

    /// <summary>이 캐릭터를 지금 직접 보고 있다고 기록합니다.</summary>
    private void MarkDirectlyPerceived(TargetInfo info)
    {
        info.HasLivePosition = true;
        info.TrackingExpireTime = Time.time + Mathf.Max(0f, m_trackingHoldDuration);
        info.LastKnownPosition = info.Member.transform.position;
        info.LastKnownTime = Time.time;
        info.Source = InfoSource.DirectPerception;
    }

    /// <summary>
    /// 유지 시간이 끝난 실시간 위치 정보를 마지막 확인 위치만 남기고 내립니다.
    /// </summary>
    /// <remarks>
    /// 시야 판정 주기와 무관하게 매 프레임 확인합니다. 유지 시간이 판정 주기보다 짧을 수 있기 때문입니다.
    /// </remarks>
    private void ExpireTracking()
    {
        float now = Time.time;
        for (int i = 0; i < m_infos.Count; i++)
        {
            TargetInfo info = m_infos[i];
            if (!info.HasLivePosition)
            {
                continue;
            }

            if (!IsAliveMember(info.Member))
            {
                info.Clear();
                continue;
            }

            if (now >= info.TrackingExpireTime)
            {
                // 마지막으로 갱신된 위치를 마지막 확인 위치로 남깁니다(§5.4.3).
                info.HasLivePosition = false;
            }
        }
    }

    /// <summary>
    /// 거리, 시야각, 장애물 순으로 이 캐릭터가 보이는지 판정합니다.
    /// </summary>
    /// <remarks>
    /// 싼 검사부터 수행해 Raycast는 앞의 두 조건을 통과한 후보에만 사용합니다.
    /// 다른 살아 있는 변이체는 서로의 시야를 막지 않으므로 고정 장애물 레이어만 검사합니다(§5.4.1).
    /// </remarks>
    private bool CanSee(Vector3 origin, SquadMemberController member)
    {
        // 캐릭터의 가슴 높이를 겨눕니다. 발밑을 겨누면 턱이나 경사에 가려 안 보이는 판정이 잦습니다.
        // 공용 문서 §5.4.1은 몸통 전용 시각 감지 콜라이더를 규정하지만, 이 프로젝트는 쓰지 않기로 했습니다(기획 확정).
        Vector3 target = member.transform.position + Vector3.up;
        Vector3 delta = target - origin;

        if (delta.sqrMagnitude > m_sightRange * m_sightRange)
        {
            return false;
        }

        float distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            return true;
        }

        Vector3 direction = delta / distance;
        if (Vector3.Angle(transform.forward, direction) > m_sightAngle * 0.5f)
        {
            return false;
        }

        return !Physics.Raycast(origin, direction, distance, m_resolvedObstacleMask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// 이 변이체에서 지정 지점까지 실제로 이동 가능한 경로 길이를 구합니다.
    /// </summary>
    /// <returns>경로가 없으면 <see cref="float.PositiveInfinity"/>입니다.</returns>
    /// <remarks>직선거리가 아니라 경로 거리로 대상을 고르라는 §5.7.3 규칙 때문에 필요합니다.</remarks>
    private float GetPathDistance(Vector3 destination)
    {
        if (m_pathBuffer == null)
        {
            m_pathBuffer = new NavMeshPath();
        }

        if (!NavMesh.CalculatePath(transform.position, destination, NavMesh.AllAreas, m_pathBuffer)
            || m_pathBuffer.status != NavMeshPathStatus.PathComplete)
        {
            return float.PositiveInfinity;
        }

        Vector3[] corners = m_pathBuffer.corners;
        float total = 0f;
        for (int i = 1; i < corners.Length; i++)
        {
            total += Vector3.Distance(corners[i - 1], corners[i]);
        }

        return total;
    }

    /// <summary>지정 캐릭터의 기록을 찾습니다. 없으면 null입니다.</summary>
    private TargetInfo FindInfo(SquadMemberController member)
    {
        if (member == null)
        {
            return null;
        }

        for (int i = 0; i < m_infos.Count; i++)
        {
            if (m_infos[i].Member == member)
            {
                return m_infos[i];
            }
        }

        return null;
    }

    /// <summary>
    /// 이 기록이 지금 유효 대상인지 판단합니다.
    /// </summary>
    /// <remarks>
    /// 실시간 위치를 아는 캐릭터만 유효 대상이 됩니다.
    /// 마지막 확인 위치만 남은 캐릭터는 수색의 기준일 뿐 공격 대상이 아닙니다(§5.7.2).
    /// </remarks>
    private static bool IsValidTarget(TargetInfo info)
    {
        return info != null && info.HasLivePosition && IsAliveMember(info.Member);
    }

    /// <summary>대상으로 삼을 수 있는 살아 있는 스쿼드 캐릭터인지 확인합니다.</summary>
    private static bool IsAliveMember(SquadMemberController member)
    {
        return member != null && member.IsAlive && !member.IsDown;
    }

    /// <summary>시야 판정에 사용할 눈 위치를 반환합니다.</summary>
    private Vector3 GetEyePosition()
    {
        return m_eyePoint != null
            ? m_eyePoint.position
            : transform.position + Vector3.up * 1.5f;
    }

    /// <summary>선택된 변이체의 시야 범위를 Scene 뷰에서 확인하기 위한 Gizmo입니다.</summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, m_sightRange);

        // 시야각은 콜라이더가 아니라 계산으로 판정하므로, 경계 방향만 선으로 표시합니다.
        Vector3 eye = GetEyePosition();
        Quaternion left = Quaternion.AngleAxis(-m_sightAngle * 0.5f, Vector3.up);
        Quaternion right = Quaternion.AngleAxis(m_sightAngle * 0.5f, Vector3.up);
        Gizmos.DrawRay(eye, left * transform.forward * m_sightRange);
        Gizmos.DrawRay(eye, right * transform.forward * m_sightRange);
    }
}
