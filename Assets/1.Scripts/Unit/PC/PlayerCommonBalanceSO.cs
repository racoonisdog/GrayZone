using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// ThirdPersonController, AimController, PlayerHealth에 주입할 순수 수치 밸런스 데이터를 보관합니다.
/// </summary>
/// <remarks>
/// 이 파일은 SO CSV 도구가 위 스크립트들의 [BalanceField] 필드에서 생성했습니다.
/// 필드 이름은 "{스크립트 이름}_{필드 이름}" 규칙을 따릅니다. 통합 SO 하나가 여러 컴포넌트 값을 함께 담아도
/// 이름이 겹치지 않게 하기 위해서이며, BindManager가 같은 규칙으로 짝을 찾습니다.
/// [BalanceField(Shared = true)]로 선언한 필드만 접두사 없이 생성되어 여러 컴포넌트가 같은 값을 받습니다.
/// 값의 정본은 SO이며 CSV는 내보낸 스냅샷입니다. 허용 범위는 스크립트 쪽 [Clamp]에 선언됩니다.
/// 미디어·런타임 참조는 담지 않습니다. 표현 리소스는 Feedback SO가 소유합니다.
/// </remarks>
[CreateAssetMenu(fileName = "PlayerCommonBalanceSO", menuName = "GrayZone/Balance/PlayerCommonBalanceSO")]
public sealed class PlayerCommonBalanceSO : ScriptableObject, IBalanceTableData
{
    // ───────────── ThirdPersonController ─────────────

    [Header("ThirdPersonController")]
    [Tooltip("캐릭터의 기본 이동 속도입니다. 단위는 m/s입니다. (0 이상)")]
    [FormerlySerializedAs("m_moveSpeed")]
    [SerializeField] private float ThirdPersonController_m_moveSpeed = 2f;

    [Tooltip("캐릭터의 전력질주 속도입니다. 단위는 m/s입니다. (0 이상)")]
    [FormerlySerializedAs("m_sprintSpeed")]
    [SerializeField] private float ThirdPersonController_m_sprintSpeed = 6f;

    [Tooltip("캐릭터가 이동 방향을 바라보도록 회전하는 데 걸리는 보간 시간입니다. (범위 0~0.3)")]
    [FormerlySerializedAs("m_rotationSmoothTime")]
    [SerializeField] private float ThirdPersonController_m_rotationSmoothTime = 0.12f;

    [Tooltip("가속과 감속 반응 속도입니다. (0 이상)")]
    [FormerlySerializedAs("m_speedChangeRate")]
    [SerializeField] private float ThirdPersonController_m_speedChangeRate = 10f;

    [Tooltip("캐릭터가 점프할 수 있는 높이입니다. (0 이상)")]
    [FormerlySerializedAs("m_jumpHeight")]
    [SerializeField] private float ThirdPersonController_m_jumpHeight = 1.2f;

    [Tooltip("캐릭터에 적용할 중력 값입니다. Unity 기본 중력은 -9.81입니다.")]
    [FormerlySerializedAs("m_gravity")]
    [SerializeField] private float ThirdPersonController_m_gravity = -15f;

    [Tooltip("다음 점프가 가능해지기까지 필요한 대기 시간입니다. 0이면 즉시 다시 점프할 수 있습니다. (0 이상)")]
    [FormerlySerializedAs("m_jumpTimeout")]
    [SerializeField] private float ThirdPersonController_m_jumpTimeout = 0.5f;

    [Tooltip("낙하 상태로 전환되기 전까지의 대기 시간입니다. 계단 이동 같은 작은 단차 처리에 유용합니다. (0 이상)")]
    [FormerlySerializedAs("m_fallTimeout")]
    [SerializeField] private float ThirdPersonController_m_fallTimeout = 0.15f;

    [Tooltip("지면 감지 위치의 Y축 오프셋입니다. 울퉁불퉁한 지형에서 보정용으로 사용합니다.")]
    [FormerlySerializedAs("m_groundedOffset")]
    [SerializeField] private float ThirdPersonController_m_groundedOffset = -0.14f;

    [Tooltip("지면 감지 구체의 반지름입니다. CharacterController 반지름과 맞추는 것이 좋습니다. (0 이상)")]
    [FormerlySerializedAs("m_groundedRadius")]
    [SerializeField] private float ThirdPersonController_m_groundedRadius = 0.28f;

    [Tooltip("카메라를 위로 회전할 수 있는 최대 각도입니다.")]
    [FormerlySerializedAs("m_topClamp")]
    [SerializeField] private float ThirdPersonController_m_topClamp = 70f;

    [Tooltip("카메라를 아래로 회전할 수 있는 최대 각도입니다.")]
    [FormerlySerializedAs("m_bottomClamp")]
    [SerializeField] private float ThirdPersonController_m_bottomClamp = -30f;

    [Tooltip("카메라 각도에 추가로 적용할 보정 각도입니다. 고정 카메라 튜닝에 사용할 수 있습니다.")]
    [FormerlySerializedAs("m_cameraAngleOverride")]
    [SerializeField] private float ThirdPersonController_m_cameraAngleOverride = 0f;

    [Tooltip("반동 오프셋이 0(원래 조준)으로 복귀하는 속도입니다. 클수록 빠르게 제자리로 돌아옵니다. (0 이상)")]
    [FormerlySerializedAs("m_recoilRecoverySpeed")]
    [SerializeField] private float ThirdPersonController_m_recoilRecoverySpeed = 8f;

    [Tooltip("발사 입력을 놓은 뒤 반동 회복을 시작하기까지의 유예 시간입니다. 입력을 유지하는 동안에는 발수·누적량과 무관하게 회복하지 않습니다. 0이면 버튼을 놓은 직후부터 복귀합니다. (0 이상)")]
    [FormerlySerializedAs("m_recoilRecoveryDelay")]
    [SerializeField] private float ThirdPersonController_m_recoilRecoveryDelay = 0.15f;

    [Tooltip("반동 온셋 보간 속도입니다. 클수록 더 빠르게(앞쪽으로 더 쏠려) 목표에 도달합니다. Recoil Onset Interp가 켜져 있을 때만 적용됩니다. (0 이상)")]
    [FormerlySerializedAs("m_recoilOnsetSpeed")]
    [SerializeField] private float ThirdPersonController_m_recoilOnsetSpeed = 35f;

    [Tooltip("세로(피치) 반동 회복분 오프셋의 고정 상한 각도(도)입니다. Use Pitch Offset Cap이 켜져 있을 때만 적용됩니다. (0 이상)")]
    [FormerlySerializedAs("m_recoilMaxPitch")]
    [SerializeField] private float ThirdPersonController_m_recoilMaxPitch = 4f;

    [Tooltip("좌우(요) 반동 회복분 오프셋의 고정 상한 각도(도)입니다. Use Yaw Offset Cap이 켜져 있을 때만 적용됩니다(회복분에만). (0 이상)")]
    [FormerlySerializedAs("m_recoilMaxYaw")]
    [SerializeField] private float ThirdPersonController_m_recoilMaxYaw = 3f;

    [Tooltip("세로(pitch) 반동 회복 비율입니다. 1=자동(멈추면 완전 회복), 0=하드(조준에 영구 반영·안 돌아옴 → 상하 조준 한계까지 상승), 중간=부분(일부만 회복). 영구분은 상하 조준 한계로 제한됩니다. (범위 0~1)")]
    [FormerlySerializedAs("m_pitchRecoveryRatio")]
    [SerializeField] private float ThirdPersonController_m_pitchRecoveryRatio = 1f;

    [Tooltip("좌우(yaw) 반동 회복 비율입니다. 1=자동(완전 회복), 0=하드(영구 반영·안 돌아옴 → 플레이어가 되잡음, Strinova식), 중간=부분. 하드는 Alternate 패턴과 궁합이 좋습니다. (범위 0~1)")]
    [FormerlySerializedAs("m_yawRecoveryRatio")]
    [SerializeField] private float ThirdPersonController_m_yawRecoveryRatio = 1f;

    [Tooltip("세로 반동 곡선 하나의 길이(초)입니다. 정규화 시간 0~1을 재는 기준이 됩니다. 연사 간격보다 길면 발끼리 겹쳐 누적됩니다. (0 이상)")]
    [FormerlySerializedAs("m_pitchRecoilEnvelopeDuration")]
    [SerializeField] private float ThirdPersonController_m_pitchRecoilEnvelopeDuration = 0.5f;

    [Tooltip("좌우 반동 곡선 하나의 길이(초)입니다. (0 이상)")]
    [FormerlySerializedAs("m_yawRecoilEnvelopeDuration")]
    [SerializeField] private float ThirdPersonController_m_yawRecoilEnvelopeDuration = 0.45f;

    [Tooltip("발소리와 착지음의 재생 음량입니다. (범위 0~1)")]
    [FormerlySerializedAs("m_footstepAudioVolume")]
    [SerializeField] private float ThirdPersonController_m_footstepAudioVolume = 0.5f;

    // ───────────── AimController ─────────────

    [Header("AimController")]
    [Tooltip("지향점(LookPoint)을 카메라 전방 이 거리에 항상 둡니다. 레이캐스트와 무관하게 늘 먼 지점을 바라보며, 무기 히트스캔 사거리보다 작으면 사거리만큼으로 보정됩니다. (0 이상)")]
    [FormerlySerializedAs("m_lookDistance")]
    [SerializeField] private float AimController_m_lookDistance = 20f;

    [Tooltip("힙파이어 사격 후 전투 자세를 유지하는 시간(초)입니다. 0이면 사격을 멈추는 즉시 해제합니다. (0 이상)")]
    [FormerlySerializedAs("m_hipfireHoldDuration")]
    [SerializeField] private float AimController_m_hipfireHoldDuration = 2f;

    [Tooltip("ADS(조준) 시 백뷰 카메라 FOV입니다. 값이 작을수록 더 확대됩니다. (1 이상)")]
    [FormerlySerializedAs("m_adsFov")]
    [SerializeField] private float AimController_m_adsFov = 20f;

    [Tooltip("힙파이어(비조준) 시 백뷰 카메라 FOV입니다. 줌 없는 기본 시야 값(기본 30)입니다. (1 이상)")]
    [FormerlySerializedAs("m_hipfireFov")]
    [SerializeField] private float AimController_m_hipfireFov = 30f;

    [Tooltip("ADS↔힙파이어 전환 시 FOV 보간 속도입니다. 매우 크게 두면 즉시 전환에 가까워집니다. (0 이상)")]
    [FormerlySerializedAs("m_zoomLerpSpeed")]
    [SerializeField] private float AimController_m_zoomLerpSpeed = 10f;

    [Tooltip("PerShotReset일 때, 시각 킥이 거의(~95%) 회복되는 데 걸리는 발수(무기 ShootDelay 기준)입니다. 1이면 다음 발 전에 거의 리셋됩니다. (0.01 이상)")]
    [FormerlySerializedAs("m_visualKickRecoverShots")]
    [SerializeField] private float AimController_m_visualKickRecoverShots = 1f;

    [Tooltip("누적될 수 있는 카메라 롤(Dutch) 상한(도)입니다. 유지 없이 발당 순간 펀치 후 회복합니다. (0 이상)")]
    [FormerlySerializedAs("m_visualKickMaxRoll")]
    [SerializeField] private float AimController_m_visualKickMaxRoll = 3f;

    [Tooltip("누적될 수 있는 FOV 펀치 상한(도)입니다. (0 이상)")]
    [FormerlySerializedAs("m_visualKickMaxFovPunch")]
    [SerializeField] private float AimController_m_visualKickMaxFovPunch = 1.5f;

    [Tooltip("카메라 롤 킥 곡선 하나의 길이(초)입니다. 정규화 시간 0~1을 재는 기준입니다. (0 이상)")]
    [FormerlySerializedAs("m_rollKickEnvelopeDuration")]
    [SerializeField] private float AimController_m_rollKickEnvelopeDuration = 0.35f;

    [Tooltip("힙파이어 FOV 펀치 곡선 하나의 길이(초)입니다. (0 이상)")]
    [FormerlySerializedAs("m_hipfireFovPunchEnvelopeDuration")]
    [SerializeField] private float AimController_m_hipfireFovPunchEnvelopeDuration = 0.25f;

    [Tooltip("ADS FOV 펀치 곡선 하나의 길이(초)입니다. 조준 중에는 화면이 확대돼 같은 펀치도 더 크게 보이므로 따로 둡니다. (0 이상)")]
    [FormerlySerializedAs("m_adsFovPunchEnvelopeDuration")]
    [SerializeField] private float AimController_m_adsFovPunchEnvelopeDuration = 0.22f;

    [Tooltip("ADS 진입(확대)에 걸리는 시간(초)입니다. (0 이상)")]
    [FormerlySerializedAs("m_zoomInDuration")]
    [SerializeField] private float AimController_m_zoomInDuration = 0.18f;

    [Tooltip("ADS 해제(축소)에 걸리는 시간(초)입니다. 진입과 따로 둘 수 있어 빠르게 들어가고 느리게 나오는 식이 가능합니다. (0 이상)")]
    [FormerlySerializedAs("m_zoomOutDuration")]
    [SerializeField] private float AimController_m_zoomOutDuration = 0.24f;

    // ───────────── PlayerHealth ─────────────

    [Header("PlayerHealth")]
    [Tooltip("실제 HP 피해량을 부상 게이지로 변환할 때 곱하는 비율입니다. 기획 공식의 r 값입니다. (0 이상)")]
    [FormerlySerializedAs("m_injuryConversionRatio")]
    [SerializeField] private float PlayerHealth_m_injuryConversionRatio = 0.35f;

    [Tooltip("정상 상태로 판정되는 반올림 부상 게이지 최대값입니다. 1차 프로토타입 기준 0~10입니다. (0 이상)")]
    [FormerlySerializedAs("m_normalInjuryMaxGauge")]
    [SerializeField] private int PlayerHealth_m_normalInjuryMaxGauge = 10;

    [Tooltip("경상 상태로 판정되는 반올림 부상 게이지 최대값입니다. 1차 프로토타입 기준 11~40입니다. (0 이상)")]
    [FormerlySerializedAs("m_minorInjuryMaxGauge")]
    [SerializeField] private int PlayerHealth_m_minorInjuryMaxGauge = 40;

    [Tooltip("치명상 상태로 판정되기 시작하는 반올림 부상 게이지 최소값입니다. 1차 프로토타입 기준 71입니다. (0 이상)")]
    [FormerlySerializedAs("m_criticalInjuryMinGauge")]
    [SerializeField] private int PlayerHealth_m_criticalInjuryMinGauge = 71;

    [Tooltip("아직 구조된 적이 없을 때 적용되는 부상 게이지 배율입니다. (0 이상)")]
    [FormerlySerializedAs("m_baseInjuryMultiplier")]
    [SerializeField] private float PlayerHealth_m_baseInjuryMultiplier = 1f;

    [Tooltip("첫 번째 구조 이후 적용되는 부상 게이지 배율입니다. (0 이상)")]
    [FormerlySerializedAs("m_revivedOnceInjuryMultiplier")]
    [SerializeField] private float PlayerHealth_m_revivedOnceInjuryMultiplier = 1.2f;

    [Tooltip("두 번째 구조 이후 적용되는 부상 게이지 배율입니다. (0 이상)")]
    [FormerlySerializedAs("m_revivedTwiceInjuryMultiplier")]
    [SerializeField] private float PlayerHealth_m_revivedTwiceInjuryMultiplier = 1.5f;

    [Tooltip("세 번째 구조 이후 적용되는 부상 게이지 배율입니다. (0 이상)")]
    [FormerlySerializedAs("m_revivedThreeTimesInjuryMultiplier")]
    [SerializeField] private float PlayerHealth_m_revivedThreeTimesInjuryMultiplier = 1.8f;

    [Tooltip("다운된 플레이어가 구조되지 않았을 때 전투 이탈 처리되기까지 걸리는 시간입니다. (0 이상)")]
    [FormerlySerializedAs("m_downDuration")]
    [SerializeField] private float PlayerHealth_m_downDuration = 30f;

    [Tooltip("이 횟수만큼 구조된 뒤 다시 HP가 0이 되면 즉시 전투 이탈 처리됩니다. (0 이상)")]
    [FormerlySerializedAs("m_maxReviveCount")]
    [SerializeField] private int PlayerHealth_m_maxReviveCount = 3;

    [Tooltip("현재 출격에서 첫 번째 구조 시 최대 HP 기준으로 회복되는 비율입니다. (범위 0~100)")]
    [FormerlySerializedAs("m_firstReviveHpPercent")]
    [SerializeField] private int PlayerHealth_m_firstReviveHpPercent = 50;

    [Tooltip("현재 출격에서 두 번째 구조 시 최대 HP 기준으로 회복되는 비율입니다. (범위 0~100)")]
    [FormerlySerializedAs("m_secondReviveHpPercent")]
    [SerializeField] private int PlayerHealth_m_secondReviveHpPercent = 25;

    [Tooltip("현재 출격에서 세 번째 구조 시 최대 HP 기준으로 회복되는 비율입니다. (범위 0~100)")]
    [FormerlySerializedAs("m_thirdReviveHpPercent")]
    [SerializeField] private int PlayerHealth_m_thirdReviveHpPercent = 10;
}
