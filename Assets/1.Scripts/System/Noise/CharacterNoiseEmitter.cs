using UnityEngine;
using VInspector;

/// <summary>
/// 스쿼드 캐릭터 한 명이 내는 소음을 모아 발신하는 컴포넌트입니다.
/// </summary>
/// <remarks>
/// 소음을 내는 행동은 사격·이동·상호작용으로 서로 다른 시스템에 흩어져 있습니다.
/// 그것들이 각자 <see cref="NoiseSystem"/>을 직접 부르면 소음원 식별값과 소음 수치가 함께 흩어지므로
/// 캐릭터마다 이 컴포넌트 하나를 두고 그곳을 거치게 합니다.
///
/// <b>소음원 식별값은 이 컴포넌트의 인스턴스입니다.</b> 즉 같은 캐릭터의 사격과 발소리는 같은 소음원입니다.
/// 공용 `적 시스템` v0.2 §5.4.4가 "같은 소음원에서 반복되는 이벤트는 추적 목적지를 최신 위치로 갱신한다"고
/// 정하므로, 소음을 낸 캐릭터가 움직이면 변이체의 목적지가 그 캐릭터를 따라 갱신됩니다.
/// 행동별로 식별값을 나누면 발소리와 총성이 서로 다른 소음원으로 경쟁해 강도 비교를 거치게 되는데,
/// 같은 사람이 낸 소리를 두 곳으로 취급하는 것이라 규칙과 어긋납니다.
///
/// 이동 소음은 스스로 발신합니다. 위치 변화로 속력을 재므로 플레이어의 CharacterController와
/// AI 동료의 NavMeshAgent를 구분하지 않고 같은 경로로 다룹니다.
///
/// <b>도달 거리가 곧 들리는 거리입니다.</b> 감쇠는 우선순위 비교에만 쓰이며 가청 여부를 정하지 않습니다.
/// 그래서 거리 값을 정할 때 감쇠를 역산할 필요가 없고, 변이체 시야(12m)와 바로 비교하면 됩니다.
/// 걷기는 시야보다 작게(걸으면 소리로 안 들킴 = 잠입의 보상), 달리기는 시야보다 크게(안 보이는 곳에서도
/// 들킴 = 달리기의 대가) 두는 것이 기준입니다.
///
/// 감쇠 형태(<see cref="m_noiseFloorRatio"/>·<see cref="m_noiseCurveExponent"/>)를 개별 소음이 아니라
/// 캐릭터 단위로 둔 이유는, 그것이 "소리가 공간에서 어떻게 줄어드는가"라 행동별로 다를 이유가 약하기
/// 때문입니다. 발신원별로 달라야 할 값은 소음량과 도달 거리입니다.
///
/// 수치는 전부 기획 미확정입니다. 콘텐츠 문서 `변이체 잡몹 1 콘텐츠` §12에서 소음 감지 기준과 거리 감쇠가
/// "확정 필요"로 남아 있어, 변이체 시야(12m)를 기준으로 임시값을 넣었습니다.
/// </remarks>
public class CharacterNoiseEmitter : MonoBehaviour
{
    [Header("References")]
    [Tooltip("이 캐릭터의 스쿼드 멤버 컨트롤러입니다. 비어 있으면 자신과 부모에서 찾습니다.")]
    [SerializeField] private SquadMemberController m_member;

    [Header("Attenuation")]
    [Tooltip("도달 거리 끝에서 남는 강도의 비율입니다. 가청 여부가 아니라 어느 소음이 우선인지에만 영향을 줍니다. 0이면 끝에서 0까지 떨어져 먼 소음끼리 우선순위가 뒤섞입니다.")]
    [SerializeField] private float m_noiseFloorRatio = 0.25f;

    [Tooltip("감쇠 곡선의 지수입니다. 1이면 선형입니다. 1보다 크면 근거리에서 강도를 유지하다 도달 거리 근처에서 급히 떨어지고, 1보다 작으면 발신원 근처에서 먼저 급히 떨어집니다(실제 소리에 가까움).")]
    [SerializeField] private float m_noiseCurveExponent = 1f;

    [Header("Movement Noise")]
    [Tooltip("이 속력 미만으로 움직이면 이동 소음을 내지 않습니다. 단위는 m/s입니다.")]
    [SerializeField] private float m_walkSpeedThreshold = 0.6f;

    [Tooltip("이 속력 이상이면 달리기 소음으로 취급합니다. 단위는 m/s입니다.")]
    [SerializeField] private float m_runSpeedThreshold = 4.0f;

    [Tooltip("걷기 발소리의 기본 소음량입니다. 우선순위 비교에만 쓰입니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_walkNoiseLevel = 0.3f;

    [Tooltip("걷기 발소리가 들리는 거리(m)입니다. 변이체 시야(12m)보다 작게 두어 걸으면 소리로는 들키지 않게 합니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_walkNoiseRange = 7f;

    [Tooltip("걷기 발소리를 내는 간격(초)입니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_walkNoiseInterval = 0.6f;

    [Tooltip("달리기 발소리의 기본 소음량입니다. 우선순위 비교에만 쓰입니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_runNoiseLevel = 0.6f;

    [Tooltip("달리기 발소리가 들리는 거리(m)입니다. 변이체 시야(12m)보다 크게 두어야 보이지 않는 곳에서도 들켜 달리기에 대가가 생깁니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_runNoiseRange = 19f;

    [Tooltip("달리기 발소리를 내는 간격(초)입니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_runNoiseInterval = 0.4f;

    [Header("Interaction Noise")]
    [Tooltip("구조 등 상호작용의 기본 소음량입니다. 우선순위 비교에만 쓰입니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_interactionNoiseLevel = 0.4f;

    [Tooltip("상호작용 소음이 들리는 거리(m)입니다. 반복 발신이라 체감은 더 큽니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_interactionNoiseRange = 11f;

    [Tooltip("상호작용 소음을 반복해 낼 간격(초)입니다. 홀드가 유지되는 동안 이 간격으로 냅니다.")]
    [SerializeField] private float m_interactionNoiseInterval = 0.5f;

    [Foldout("Debug")]
    [Tooltip("이 캐릭터를 선택했을 때 걷기·뛰기·상호작용 소음의 도달 반경을 Scene 뷰에 원으로 표시합니다. 실제 발신 여부와 무관하게 설정값을 그립니다.")]
    [SerializeField] private bool m_debugDrawNoiseRanges = false;

    /// <summary>직전 프레임의 위치입니다. 속력을 재는 데 씁니다.</summary>
    private Vector3 m_lastPosition;

    /// <summary>다음 이동 소음을 낼 수 있는 시각입니다.</summary>
    private float m_nextMovementNoiseTime;

    /// <summary>다음 상호작용 소음을 낼 수 있는 시각입니다.</summary>
    private float m_nextInteractionNoiseTime;

    /// <summary>가장 최근에 측정한 속력입니다. 진단용입니다.</summary>
    public float MeasuredSpeed { get; private set; }

    private void Awake()
    {
        if (m_member == null)
        {
            m_member = GetComponentInParent<SquadMemberController>();
        }

        m_lastPosition = transform.position;
    }

    private void OnEnable()
    {
        // 비활성 동안 벌어진 위치 차이가 한 프레임의 속력으로 잡히지 않게 기준을 다시 잡습니다.
        m_lastPosition = transform.position;
    }

    private void Update()
    {
        UpdateMovementNoise();
    }

    /// <summary>
    /// 사격 소음을 발신합니다.
    /// </summary>
    /// <param name="baseLevel">이 사격의 기본 소음량입니다.</param>
    /// <param name="range">이 사격 소음이 닿는 거리입니다.</param>
    /// <remarks>
    /// 소음량과 거리는 총기가 정합니다. 소음기 같은 부착물이 값을 바꾸는 것도 총기 쪽 관심사이므로,
    /// 이 컴포넌트는 총기가 넘겨준 값을 그대로 씁니다. 사격 소음만 수치를 여기 두지 않는 이유입니다.
    /// </remarks>
    public void EmitGunshot(float baseLevel, float range)
    {
        Emit(baseLevel, range, NoiseBehaviorType.Gunshot);
    }

    /// <summary>
    /// 상호작용 소음을 발신합니다.
    /// </summary>
    /// <remarks>
    /// 홀드가 이어지는 동안 매 프레임 불러도 됩니다. 설정된 간격보다 자주는 나가지 않습니다.
    /// 구조처럼 시간이 걸리는 상호작용이 한 번만 소리를 내면 위치를 알려 주는 의미가 약해지므로
    /// 간격을 두고 반복해 냅니다.
    /// </remarks>
    public void EmitInteraction()
    {
        if (Time.time < m_nextInteractionNoiseTime)
        {
            return;
        }

        m_nextInteractionNoiseTime = Time.time + Mathf.Max(0.05f, m_interactionNoiseInterval);
        Emit(m_interactionNoiseLevel, m_interactionNoiseRange, NoiseBehaviorType.Interaction);
    }

    /// <summary>측정한 속력에 따라 걷기 또는 달리기 소음을 간격마다 발신합니다.</summary>
    private void UpdateMovementNoise()
    {
        Vector3 current = transform.position;

        // 수직 이동은 제외합니다. 낙하나 계단 오르내림이 발소리로 잡히면 실제 이동량과 어긋납니다.
        Vector3 delta = current - m_lastPosition;
        delta.y = 0f;
        m_lastPosition = current;

        float deltaTime = Time.deltaTime;
        MeasuredSpeed = deltaTime > 0f ? delta.magnitude / deltaTime : 0f;

        if (MeasuredSpeed < m_walkSpeedThreshold)
        {
            return;
        }

        bool running = MeasuredSpeed >= m_runSpeedThreshold;
        float interval = running ? m_runNoiseInterval : m_walkNoiseInterval;

        if (Time.time < m_nextMovementNoiseTime)
        {
            return;
        }

        m_nextMovementNoiseTime = Time.time + Mathf.Max(0.05f, interval);
        Emit(
            running ? m_runNoiseLevel : m_walkNoiseLevel,
            running ? m_runNoiseRange : m_walkNoiseRange,
            NoiseBehaviorType.Movement);
    }

    /// <summary>소음 이벤트를 만들어 방송합니다.</summary>
    /// <remarks>
    /// AI 조작 여부를 발신 시점에 담습니다. 비전투 감지 보호(§5.6)의 판단은 수신자별 교전 상태에 따라
    /// 달라지므로 여기서 거르지는 않습니다. 같은 소음을 교전 중 변이체는 듣고 비교전 변이체는 무시해야 하기
    /// 때문입니다. 조작권이 바뀌면 그 시점 이후의 소음부터 다르게 취급됩니다.
    /// </remarks>
    private void Emit(float baseLevel, float range, NoiseBehaviorType behaviorType)
    {
        if (baseLevel <= 0f || range <= 0f)
        {
            return;
        }

        bool emittedByAi = m_member != null && m_member.IsAiSquadMember;

        NoiseSystem.Emit(new NoiseEvent(
            BuildSourceId(behaviorType),
            transform.position,
            baseLevel,
            range,
            m_noiseFloorRatio,
            m_noiseCurveExponent,
            Time.time,
            behaviorType,
            emittedByAi));
    }

    /// <summary>
    /// 소음원 식별값을 만듭니다. 누가 냈는지와 무슨 행동이었는지를 함께 봅니다.
    /// </summary>
    /// <param name="behaviorType">소음을 낸 행동의 분류입니다.</param>
    /// <returns>발신자와 행동을 조합한 식별값입니다.</returns>
    /// <remarks>
    /// 이 값이 §5.4.4의 "같은 소음원에서 반복되는 이벤트는 최신 위치로 갱신한다"를 판정하는 기준입니다.
    /// 그래서 무엇을 같은 소음원으로 볼지가 곧 게임 동작을 정합니다.
    ///
    /// <b>발신자만으로 잡으면 안 됩니다.</b> 달리며 쏘면 총성과 발소리가 같은 소음원이 되어,
    /// "무조건 갱신" 규칙에 걸려 약한 발소리가 방금 난 총성을 덮어씁니다.
    ///
    /// <b>행동만으로 잡아도 안 됩니다.</b> 서로 다른 곳에 있는 두 캐릭터의 총성이 같은 소음원이 되어
    /// 위치가 서로를 계속 덮어쓰고, 어느 쪽이 더 가까운지가 무시됩니다.
    ///
    /// 둘을 조합하면 세 경우가 모두 맞습니다.
    /// 같은 사람의 같은 행동은 따라가고, 같은 사람의 다른 행동은 강도를 비교하며,
    /// 다른 사람의 같은 행동도 강도를 비교합니다.
    /// </remarks>
    private int BuildSourceId(NoiseBehaviorType behaviorType)
    {
        // 인스턴스 ID에 행동 종류를 섞습니다. 곱셈 상수는 서로 다른 조합이 같은 값으로 겹치지 않게 하기 위한 것입니다.
        return GetInstanceID() * 31 + (int)behaviorType;
    }

    /// <summary>
    /// 선택했을 때 행동별 소음 도달 반경을 그립니다.
    /// </summary>
    /// <remarks>
    /// 잠입은 "어디까지 들리는가"가 전부인데 그 거리가 숫자로만 있어 배치와 대조할 방법이 없었습니다.
    /// 변이체의 소음 인지 게이지(<see cref="EnemyTargetSensor"/>)는 이미 머리 위에 표시되므로,
    /// 이 원과 함께 보면 "왜 저 개체의 게이지가 차는지"를 한 화면에서 확인할 수 있습니다.
    ///
    /// 실제 발신 여부와 무관하게 설정값을 그립니다. 발신 순간에만 그리면 사람이 눈으로 잡기에는 너무 짧습니다.
    /// 도달 거리는 감쇠 이전의 최대 거리이며, 실제로 들리는지는 듣는 쪽의 청각 배수도 함께 봅니다.
    /// </remarks>
    private void OnDrawGizmosSelected()
    {
        if (!m_debugDrawNoiseRanges)
        {
            return;
        }

        Vector3 origin = transform.position;

        // 걷기(가장 조용함) -> 상호작용 -> 뛰기(가장 시끄러움) 순으로 색을 진하게 둡니다.
        Gizmos.color = new Color(0.4f, 1.0f, 0.5f, 0.6f);
        Gizmos.DrawWireSphere(origin, m_walkNoiseRange);

        Gizmos.color = new Color(1.0f, 0.9f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(origin, m_interactionNoiseRange);

        Gizmos.color = new Color(1.0f, 0.35f, 0.3f, 0.7f);
        Gizmos.DrawWireSphere(origin, m_runNoiseRange);
    }
}
