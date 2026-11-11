using UnityEngine;

/// <summary>
/// 설정 화면(Figma Gameplay Settings UI의 Setting Scene 프레임)을 그릴 이미지 묶음입니다.
/// </summary>
/// <remarks>
/// 설정 화면은 여러 씬에 놓인 <see cref="GameplayPauseMenuController"/>가 런타임에 만들기 때문에, 씬마다 이미지를 연결하지 않고
/// Resources 기본 에셋 하나를 같이 읽습니다. 컨트롤러에 따로 지정하면 그 에셋을 씁니다.
/// 비어 있는 칸은 각 컨트롤이 예전 단색 모양으로 그립니다.
/// </remarks>
[CreateAssetMenu(fileName = "GameplaySettingsTheme", menuName = "GrayZone/Settings/Gameplay Settings Theme")]
public sealed class GameplaySettingsTheme : ScriptableObject
{
    /// <summary>Resources 기본 에셋 경로입니다.</summary>
    public const string ResourcesPath = "Settings/GameplaySettingsTheme";

    [Header("Page")]
    [Tooltip("설정 화면 전체 배경입니다. 위·아래 구분선이 들어 있습니다. (Preferences_base_01)")]
    public Sprite pageBackground;

    [Tooltip("위쪽 탭과 전투 왼쪽 메뉴에서 고른 칸 배경입니다. (Select_mark_01)")]
    public Sprite tabSelected;

    [Tooltip("항목 제목 왼쪽의 세로 막대입니다. (Table_mark)")]
    public Sprite titleMark;

    [Tooltip("전투 탭 왼쪽 메뉴와 내용을 나누는 세로선입니다. (Side_menu_bar)")]
    public Sprite sideMenuBar;

    [Header("Buttons")]
    [Tooltip("되돌리기·변경사항 저장·뒤로가기와 조준선 기본값 버튼 배경입니다. (Preferences_button)")]
    public Sprite actionButton;

    [Tooltip("되돌리기 버튼 아이콘입니다. (Reset_icon)")]
    public Sprite resetIcon;

    [Tooltip("변경사항 저장 버튼 아이콘입니다. (Save_icon)")]
    public Sprite saveIcon;

    [Tooltip("뒤로가기 버튼 아이콘입니다. (Arrow_mark)")]
    public Sprite backIcon;

    [Tooltip("캐릭터·내부/외부·모양처럼 하나를 고르는 버튼에서 고른 칸 배경입니다. (Select_mark_02)")]
    public Sprite choiceSelected;

    [Tooltip("하나를 고르는 버튼에서 고르지 않은 칸 배경입니다. (Select_mark_03)")]
    public Sprite choiceIdle;

    [Header("Dropdown")]
    [Tooltip("드롭다운 닫힌 칸 배경입니다. (Drop_box_01)")]
    public Sprite dropdown;

    [Tooltip("드롭다운 목록에서 지금 고른 칸과 마우스를 올린 칸 배경입니다. 최대 프레임 드롭다운 칸 배경으로도 씁니다. (Drop_box_02)")]
    public Sprite dropdownSelectedItem;

    [Tooltip("드롭다운 목록 칸 배경입니다. (Drop_box_03)")]
    public Sprite dropdownItem;

    [Tooltip("드롭다운 오른쪽 화살표입니다. (Drop_box_arrow)")]
    public Sprite dropdownArrow;

    [Header("Toggle")]
    [Tooltip("체크박스 칸입니다. (Chack_box)")]
    public Sprite checkBox;

    [Tooltip("체크 표시입니다. (Chack)")]
    public Sprite checkMark;

    [Header("Slider")]
    [Tooltip("슬라이더 바탕 막대입니다. (Gauge_base)")]
    public Sprite sliderTrack;

    [Tooltip("슬라이더 채움 막대입니다. (Gauge)")]
    public Sprite sliderFill;

    [Tooltip("슬라이더 손잡이입니다. (Handle)")]
    public Sprite sliderHandle;

    [Header("Crosshair")]
    [Tooltip("조준선 편집 칸을 묶는 반투명 패널입니다. (Preferences_base_02)")]
    public Sprite crosshairPanel;

    [Tooltip("조준선 미리보기 틀입니다. 가운데 회색 칸에 미리보기가 그려집니다. (Preview)")]
    public Sprite crosshairPreview;

    /// <summary>Resources 기본 에셋을 읽습니다. 없으면 null입니다.</summary>
    public static GameplaySettingsTheme LoadFromResources()
    {
        return Resources.Load<GameplaySettingsTheme>(ResourcesPath);
    }
}
