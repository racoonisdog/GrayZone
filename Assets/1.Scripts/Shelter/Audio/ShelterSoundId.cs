/// <summary>
/// 셸터에서 소리가 날 수 있는 "상황" 목록입니다.
/// </summary>
/// <remarks>
/// <para>
/// 게임 코드는 FMOD 이벤트 경로를 직접 알지 않고 이 ID로만 재생을 요청합니다.
/// 어떤 FMOD 이벤트를 틀지는 <see cref="ShelterSoundCatalog"/> 에셋에서 연결합니다.
/// 그래서 기획에서 사운드를 받기 전에도 코드에 재생 지점을 먼저 심어 둘 수 있고,
/// 카탈로그 칸이 비어 있으면 아무 소리도 나지 않을 뿐 오류는 나지 않습니다.
/// </para>
/// <para>
/// 값은 카탈로그 에셋에 숫자로 저장되므로 <b>이미 쓰는 값은 바꾸거나 재사용하지 마십시오.</b>
/// 분류별로 100 단위 구간을 두었으니 새 상황은 해당 구간의 빈 번호로 추가합니다.
/// </para>
/// </remarks>
public enum ShelterSoundId
{
    /// <summary>지정 안 함. 재생 요청 시 무시됩니다.</summary>
    None = 0,

    // ── UI (100~) ─────────────────────────────
    /// <summary>일반 버튼 클릭입니다.</summary>
    UiButtonClick = 100,
    /// <summary>버튼에 마우스를 올렸을 때입니다.</summary>
    UiButtonHover = 101,
    /// <summary>시설 UI 등 패널이 열릴 때입니다.</summary>
    UiPanelOpen = 102,
    /// <summary>패널이 닫힐 때입니다.</summary>
    UiPanelClose = 103,
    /// <summary>재화 부족 등으로 요청이 거부됐을 때입니다.</summary>
    UiDenied = 104,
    /// <summary>업그레이드·출격 등 최종 확정 버튼이 처리됐을 때입니다.</summary>
    UiConfirm = 105,
    /// <summary>+/- 입력으로 수량이 실제로 바뀌었을 때입니다.</summary>
    UiQuantityChange = 106,

    // ── 시설 (200~) ───────────────────────────
    /// <summary>시설과 상호작용을 시작할 때입니다.</summary>
    FacilityInteract = 200,
    /// <summary>시설 업그레이드를 시작할 때입니다.</summary>
    FacilityUpgradeStart = 201,
    /// <summary>시설 업그레이드가 완료됐을 때입니다.</summary>
    FacilityUpgradeComplete = 202,
    /// <summary>업그레이드 연출에서 화면이 가려지고 건물이 교체될 때 재생하는 건축 진행음입니다.</summary>
    FacilityUpgradeBuild = 203,

    // ── 제작 (300~) ───────────────────────────
    /// <summary>제작을 시작할 때입니다.</summary>
    CraftStart = 300,
    /// <summary>제작이 완료됐을 때입니다.</summary>
    CraftComplete = 301,

    // ── 의료 (400~) ───────────────────────────
    /// <summary>치료를 시작할 때입니다.</summary>
    TreatmentStart = 400,
    /// <summary>치료가 완료됐을 때입니다.</summary>
    TreatmentComplete = 401,
    /// <summary>치료 대상을 빈 환자 슬롯에 배정했을 때입니다.</summary>
    MedicalPatientAssign = 402,
    /// <summary>환자 슬롯의 치료 대상을 해제했을 때입니다.</summary>
    MedicalPatientRelease = 403,

    // ── 진행 (500~) ───────────────────────────
    /// <summary>안내 목표가 갱신됐을 때입니다.</summary>
    ObjectiveUpdated = 500,

    // ── 재화 (600~) ───────────────────────────
    /// <summary>재화를 획득했을 때입니다.</summary>
    CurrencyGained = 600,
    /// <summary>재화를 소비했을 때입니다.</summary>
    CurrencySpent = 601,

    // ── NPC (700~) ────────────────────────────
    /// <summary>NPC 말풍선이 나타날 때입니다.</summary>
    NpcSpeech = 700,
}
