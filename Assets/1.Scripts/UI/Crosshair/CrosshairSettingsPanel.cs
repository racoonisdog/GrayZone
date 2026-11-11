using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ESC 설정 화면의 조준선 페이지입니다. 캐릭터별 조준선 또는 공용 투척물 조준선의 내부(Main)/외부(Sub) 값을 편집하고 미리 보여 줍니다.
/// </summary>
/// <remarks>
/// 설정 화면 원칙(바꾸면 바로 적용, 파일 저장은 '변경사항 저장'을 눌러야 함)을 따릅니다. 편집할 때마다
/// <see cref="ApplyPendingValues"/>로 <see cref="CrosshairSettingsService"/>에 넘겨 HUD에 바로 적용하고, 파일 저장은 설정 화면의
/// 저장 버튼이 맡습니다. 되돌리기는 설정 화면이 조준선 값을 연 때의 값으로 바꾼 뒤 <see cref="RevertToCommittedValues"/>로
/// 서비스 값을 다시 읽습니다. 기본값 버튼은 씬 값으로 바꾸고 바로 적용합니다.
///
/// 미리보기는 <see cref="CrosshairController"/>의 배치 계산(링 기준 지름, 바깥쪽 두께 확장, 팔 위치)을 따라 텍스처에 그립니다.
/// 탄퍼짐에 따른 간격 변화는 반영하지 않고 기본 간격으로 그립니다.
///
/// 배치는 Figma 'Setting Scene Battle Character'·'Setting Scene Battle Throwable Items' 프레임(1920x1080) 좌표를 그대로 씁니다.
/// 부모는 설정 화면 전체 크기여야 합니다.
/// </remarks>
public sealed class CrosshairSettingsPanel : MonoBehaviour
{
    // 편집 패널(Preferences_base_02)입니다. 캐릭터 조준선은 y 450, 투척물 조준선은 y 223에서 시작합니다.
    private const float PanelLeft = 371f;
    private const float PanelWidth = 775f;
    private const float PanelMinHeight = 489f;
    private const float CharacterPanelTop = 450f;
    private const float ThrowablePanelTop = 223f;

    /// <summary>편집 패널이 내려올 수 있는 가장 아래 y입니다. 아래 버튼 줄 구분선(y 977) 위에서 멈춥니다.</summary>
    private const float PanelBottomLimit = 965f;

    // 패널 안 줄 배치입니다. 이름은 x 48, 버튼·슬라이더·색 견본은 x 240, 값 글자는 x 654에서 시작합니다.
    private const float RowLabelLeft = 48f;
    private const float RowControlLeft = 240f;
    private const float RowValueLeft = 654f;
    private const float RowHeight = 44f;

    /// <summary>첫 줄(모양)과 둘째 줄의 세로 가운데입니다. 그 아래 줄은 <see cref="RowPitch"/> 간격으로 이어집니다.</summary>
    private const float FirstRowCenter = 123.5f;
    private const float SecondRowCenter = 181f;
    private const float RowPitch = 48f;

    /// <summary>줄이 많아 패널이 아래 한계를 넘을 때 줄여 쓸 수 있는 가장 좁은 간격입니다. 색 견본(36px)이 겹치지 않는 값입니다.</summary>
    private const float MinRowPitch = 36f;

    private static readonly Vector2 SliderSize = new(408f, 26.65f);
    private const float SwatchSize = 36f;
    private const float SwatchPitch = 60f;

    private enum Layer { Main, Sub }

    private sealed class Row
    {
        public GameObject Root;
        public Func<CrosshairStyle, bool> IsVisible;
        public Action<CrosshairStyle> Refresh;
    }

    private static readonly Color[] FillSwatches =
    {
        Color.white,
        new Color(0.95f, 0.18f, 0.18f, 1f),
        new Color(0.15f, 0.95f, 0.8f, 1f),
        new Color(1f, 0.84f, 0.18f, 1f),
        new Color(0.3f, 0.95f, 0.3f, 1f),
        new Color(0.35f, 0.65f, 1f, 1f),
    };

    private static readonly Color[] StrokeSwatches = { Color.black, new Color(0.25f, 0.25f, 0.25f, 1f), Color.white };
    // 테마 이미지가 없을 때 고르는 버튼을 칠할 색입니다. Select_mark_02(올리브)·Select_mark_03(검정)과 같은 색입니다.
    private static readonly Color SelectedColor = new(0.69f, 0.65f, 0.33f, 1f);
    private static readonly Color IdleColor = Color.black;

    /// <summary>고른 색 견본 테두리 색입니다. Figma 선택 칸과 같은 노란색입니다.</summary>
    private static readonly Color AccentColor = new(0.97f, 0.78f, 0.18f, 1f);

    private readonly List<KeyValuePair<PlayerbleCharacterId, string>> m_characters = new();
    private readonly Dictionary<PlayerbleCharacterId, CrosshairStyle> m_workingStyles = new();
    private readonly List<Image> m_characterButtonImages = new();
    private readonly Image[] m_layerButtonImages = new Image[2];
    private readonly Image[] m_sideButtonImages = new Image[2];
    private readonly List<Row> m_mainRows = new();
    private readonly List<Row> m_subRows = new();
    private readonly List<Image> m_mainShapeButtons = new();
    private readonly List<Image> m_subShapeButtons = new();

    private TMP_FontAsset m_regularFont;
    private TMP_FontAsset m_boldFont;
    private bool m_isThrowableMode;
    private bool m_isBuilt;
    private bool m_isRefreshing;
    private int m_selectedCharacterIndex;
    private Layer m_selectedLayer;

    /// <summary>조준(ADS) 쪽을 편집하고 있는지 여부입니다. 거짓이면 비조준 쪽입니다. 동적 크로스헤어를 끈 경우에만 씁니다.</summary>
    private bool m_adsTab;
    private float m_panelTop;
    private Image m_dynamicCheck;
    private RectTransform m_sideRow;
    private RectTransform m_layerRow;
    private RectTransform m_controlsPanel;
    private CrosshairStyle m_workingThrowable;
    private RectTransform m_characterRow;
    private RectTransform m_mainRowsRoot;
    private RectTransform m_subRowsRoot;
    private TextMeshProUGUI m_unavailableText;
    private TextMeshProUGUI m_previewProfileLabel;
    private TextMeshProUGUI m_previewZoomLabel;
    private RawImage m_previewImage;
    private CrosshairHudPreview m_hudPreview;

    /// <summary>
    /// 설정 화면의 다른 탭과 같은 슬라이더를 만드는 함수입니다. (이름, 부모, 왼쪽 위 위치, 크기, 최소, 최대, 정수만) 순서로 받습니다.
    /// Build 전에 넣어야 하고, 비어 있으면 이 패널의 기본 슬라이더를 씁니다.
    /// </summary>
    public Func<string, Transform, Vector2, Vector2, float, float, bool, Slider> SliderFactory { get; set; }

    /// <summary>설정 화면 이미지 묶음입니다. Build 전에 넣어야 하고, 비어 있으면 단색으로 그립니다.</summary>
    public GameplaySettingsTheme Theme { get; set; }

    /// <summary>캐릭터별 조준선 편집 화면을 만듭니다.</summary>
    public void Build(RectTransform parent, TMP_FontAsset regularFont, TMP_FontAsset boldFont)
    {
        BuildInternal(parent, regularFont, boldFont, false);
    }

    /// <summary>모든 캐릭터가 같이 쓰는 투척물 조준선 편집 화면을 만듭니다.</summary>
    public void BuildSharedThrowable(RectTransform parent, TMP_FontAsset regularFont, TMP_FontAsset boldFont)
    {
        BuildInternal(parent, regularFont, boldFont, true);
    }

    /// <summary>편집 중인 값을 서비스에 넘겨 HUD에 적용합니다. 설정 파일 저장 전에 호출해야 파일에 들어갑니다.</summary>
    public void ApplyPendingValues()
    {
        CrosshairSettingsService service = CrosshairSettingsService.Instance;
        if (service == null || !m_isBuilt)
        {
            return;
        }

        if (m_isThrowableMode)
        {
            if (m_workingThrowable != null)
            {
                service.SetThrowableStyle(m_workingThrowable);
            }
        }
        else
        {
            service.SetCharacterStyles(m_workingStyles);
        }
    }

    /// <summary>저장이 끝난 뒤 호출합니다. 서비스의 확정 값으로 작업 사본을 맞춥니다.</summary>
    public void CommitCurrentValues()
    {
        RevertToCommittedValues();
    }

    /// <summary>저장하지 않은 편집을 버리고 서비스의 확정 값을 다시 읽습니다.</summary>
    public void RevertToCommittedValues()
    {
        if (!m_isBuilt)
        {
            return;
        }

        CrosshairSettingsService service = CrosshairSettingsService.Instance;
        m_workingStyles.Clear();
        m_workingThrowable = null;
        if (service != null)
        {
            if (m_isThrowableMode)
            {
                m_workingThrowable = service.GetThrowableStyle();
            }
            else
            {
                RebuildCharacterButtons(service);
                foreach (KeyValuePair<PlayerbleCharacterId, string> pair in m_characters)
                {
                    m_workingStyles[pair.Key] = service.GetCharacterStyle(pair.Key);
                }
            }
        }

        RefreshAll();
    }

    // 미리보기용 HUD 복제본은 이 화면이 보이는 동안만 둡니다. 다시 열리면 DrawPreview가 새로 만듭니다.
    private void OnDisable()
    {
        m_hudPreview?.Dispose();
        m_hudPreview = null;
    }

    private void OnEnable()
    {
        if (m_isBuilt)
        {
            RefreshAll();
        }
    }

    private void BuildInternal(RectTransform parent, TMP_FontAsset regularFont, TMP_FontAsset boldFont, bool throwableMode)
    {
        if (m_isBuilt || parent == null) return;

        m_isBuilt = true;
        m_isThrowableMode = throwableMode;
        m_regularFont = regularFont;
        m_boldFont = boldFont != null ? boldFont : regularFont;

        if (!throwableMode)
        {
            CreateSectionTitle(parent, 371f, 160f, "캐릭터 조준선");
            CreateText("Notice", parent, new Vector2(627f, -184f), new Vector2(600f, 15f),
                "캐릭터마다 따로 저장되며, 조작 캐릭터를 바꾸면 그 캐릭터의 조준선으로 바뀝니다.", 15f,
                TextAlignmentOptions.Left, m_regularFont);
            CreateText("Character Label", parent, new Vector2(393f, -216f), new Vector2(200f, 30f),
                "캐릭터", 30f, TextAlignmentOptions.Left, m_regularFont);
            m_characterRow = CreateTopLeftObject("Character Buttons", parent, new Vector2(415f, -265f),
                new Vector2(PanelWidth, 39f)).GetComponent<RectTransform>();

            CreateSectionTitle(parent, 371f, 343f, "조준선 커스텀");

            // 동적 크로스헤어: 켜면 탄퍼짐·반동·조준에 따라 벌어지고 줄어들며(양은 고정값) 비조준·조준을 나누지 않습니다.
            // 끄면 미리보기 크기 그대로 고정되고, 패널 오른쪽 위 비조준/조준 버튼으로 두 쪽을 따로 정합니다.
            // Figma Chack Box: 체크 칸(31x31)보다 큰 체크 표시(51x42)가 칸 왼쪽 위로 삐져나오게 겹칩니다.
            Button box = CreateButton("Dynamic Checkbox", parent, new Vector2(394f, -390f), new Vector2(330f, 48f),
                string.Empty, 15f, Color.clear);
            box.onClick.AddListener(ToggleDynamic);
            Image boxImage = CreateImage("Box", box.transform, new Vector2(4f, -16f), new Vector2(31f, 31f), new Color(1f, 1f, 1f, 0.16f));
            boxImage.raycastTarget = false;
            ApplySprite(boxImage, Theme != null ? Theme.checkBox : null);
            m_dynamicCheck = CreateImage("Check", box.transform, Vector2.zero, new Vector2(51f, 42f), new Color(0f, 1f, 0.08f, 1f));
            m_dynamicCheck.raycastTarget = false;
            if (Theme != null && Theme.checkMark != null)
            {
                ApplySprite(m_dynamicCheck, Theme.checkMark);
            }
            else
            {
                SetTopLeftRect(m_dynamicCheck.rectTransform, new Vector2(10f, -22f), new Vector2(19f, 19f));
            }

            CreateText("Dynamic Label", box.transform, new Vector2(51f, -18f), new Vector2(280f, 30f),
                "동적 크로스헤어 사용", 30f, TextAlignmentOptions.Left, m_regularFont);
            CreateText("Dynamic Description", parent, new Vector2(738f, -422f), new Vector2(400f, 15f),
                "반동·조준에 따라 벌어지고 줄어듭니다.", 15f, TextAlignmentOptions.Left, m_regularFont);
        }
        else
        {
            CreateSectionTitle(parent, 371f, 160f, "투척물 커스텀");
        }

        m_panelTop = throwableMode ? ThrowablePanelTop : CharacterPanelTop;
        BuildControls(parent);
        BuildPreview(parent);

        m_selectedLayer = Layer.Main;
        m_selectedCharacterIndex = 0;
        RevertToCommittedValues();
    }

    private void BuildControls(Transform parent)
    {
        Image panel = CreateImage("Controls Panel", parent, new Vector2(PanelLeft, -m_panelTop), new Vector2(PanelWidth, PanelMinHeight),
            new Color(0.13f, 0.16f, 0.2f, 0.75f));
        ApplySprite(panel, Theme != null ? Theme.crosshairPanel : null);
        m_controlsPanel = panel.rectTransform;
        RectTransform panelRect = panel.rectTransform;

        // Figma Select: 내부/외부 버튼은 패널 왼쪽 위(x 48, y 24)에 163x47로 놓입니다.
        m_layerRow = CreateTopLeftObject("Layer Buttons", panelRect, new Vector2(RowLabelLeft, -24f),
            new Vector2(360f, 47f)).GetComponent<RectTransform>();
        string[] layerLabels = { "내부 (중앙)", "외부 (주변)" };
        for (int i = 0; i < layerLabels.Length; i++)
        {
            int index = i;
            Button button = CreateButton($"Layer {i}", m_layerRow, new Vector2(187f * i, 0f),
                new Vector2(163f, 47f), layerLabels[i], 25f, IdleColor);
            button.onClick.AddListener(() => SelectLayer((Layer)index));
            m_layerButtonImages[i] = button.GetComponent<Image>();
        }

        if (!m_isThrowableMode)
        {
            // 비조준/조준 버튼은 Figma에 없어서, 동적 크로스헤어를 껐을 때 같은 줄 오른쪽에 같은 모양 버튼으로 둡니다.
            m_sideRow = CreateTopLeftObject("Side Buttons", panelRect, new Vector2(PanelWidth - 24f - 312f, -24f),
                new Vector2(312f, 47f)).GetComponent<RectTransform>();
            string[] sideLabels = { "비조준", "조준 (ADS)" };
            for (int i = 0; i < sideLabels.Length; i++)
            {
                int index = i;
                Button button = CreateButton($"Side {i}", m_sideRow, new Vector2(162f * i, 0f),
                    new Vector2(150f, 47f), sideLabels[i], 25f, IdleColor);
                button.onClick.AddListener(() =>
                {
                    m_adsTab = index == 1;
                    RefreshAll();
                });
                m_sideButtonImages[i] = button.GetComponent<Image>();
            }
        }

        m_unavailableText = CreateText("Unavailable", panelRect, new Vector2(RowLabelLeft, -102f), new Vector2(680f, 30f),
            "전투 씬에서만 조준선을 설정할 수 있습니다.", 22f, TextAlignmentOptions.Left, m_regularFont,
            new Color(1f, 1f, 1f, 0.6f));

        m_mainRowsRoot = CreateStretchObject("Main Rows", panelRect).GetComponent<RectTransform>();
        m_subRowsRoot = CreateStretchObject("Sub Rows", panelRect).GetComponent<RectTransform>();
        BuildMainRows(m_mainRowsRoot);
        BuildSubRows(m_subRowsRoot);

        // Figma Set Character Button: 미리보기 아래 오른쪽, Preferences_button 바탕입니다.
        Button resetButton = CreateButton("Reset To Default", parent,
            new Vector2(1455.76f, m_isThrowableMode ? -842f : -835f), new Vector2(369.24f, 58f),
            m_isThrowableMode ? "기본값으로 설정" : "이 캐릭터 기본값으로 설정", 25f, new Color(0.02f, 0.03f, 0.04f, 1f));
        ApplySprite(resetButton.GetComponent<Image>(), Theme != null ? Theme.actionButton : null);
        resetButton.onClick.AddListener(ResetSelectedToDefault);
    }

    /// <summary>Figma 항목 제목(Table)입니다. 노란 세로 막대(Table_mark 8x38) 오른쪽에 36px 굵은 제목을 둡니다.</summary>
    private void CreateSectionTitle(Transform parent, float left, float top, string title)
    {
        Image mark = CreateImage($"{title} Mark", parent, new Vector2(left, -top), new Vector2(8f, 38f), AccentColor);
        mark.raycastTarget = false;
        ApplySprite(mark, Theme != null ? Theme.titleMark : null);
        CreateText($"{title} Title", parent, new Vector2(left + 22f, -(top + 2f)), new Vector2(400f, 36f),
            title, 36f, TextAlignmentOptions.Left, m_boldFont);
    }

    /// <summary>하나를 고르는 버튼의 바탕을 고른 칸(Select_mark_02)·고르지 않은 칸(Select_mark_03)으로 바꿉니다.</summary>
    private void SetChoiceSelected(Image image, bool selected)
    {
        Sprite sprite = Theme == null ? null : selected ? Theme.choiceSelected : Theme.choiceIdle;
        if (sprite != null)
        {
            image.sprite = sprite;
            image.color = Color.white;
            return;
        }

        image.sprite = null;
        image.color = selected ? SelectedColor : IdleColor;
    }

    private void BuildMainRows(RectTransform root)
    {
        AddShapeRow(root, m_mainRows, m_mainShapeButtons, new[] { "없음", "점", "원형" },
            index => Edit(s => s.mainShape = (CrosshairController.MainShape)index),
            s => (int)s.mainShape);
        AddSliderRow(root, m_mainRows, "점 크기", 0f, 12f, 0.5f, "0.0",
            s => s.mainSizePixels, (s, v) => s.mainSizePixels = v, s => s.mainShape == CrosshairController.MainShape.Dot);
        AddSliderRow(root, m_mainRows, "원 크기", 2f, 40f, 1f, "0",
            s => s.mainRingSizePixels, (s, v) => s.mainRingSizePixels = v, s => s.mainShape == CrosshairController.MainShape.Ring);
        AddSliderRow(root, m_mainRows, "원 두께", 1f, 6f, 0.5f, "0.0",
            s => s.mainRingThicknessPixels, (s, v) => s.mainRingThicknessPixels = v, s => s.mainShape == CrosshairController.MainShape.Ring);
        AddColorRows(root, m_mainRows, "색상", FillSwatches,
            s => s.mainColor, (s, c) => s.mainColor = c, HasMain);
        AddSliderRow(root, m_mainRows, "테두리 두께", 0f, 4f, 0.5f, "0.0",
            s => s.mainStrokeThicknessPixels, (s, v) => s.mainStrokeThicknessPixels = v, HasMain);
        AddColorRows(root, m_mainRows, "테두리 색", StrokeSwatches,
            s => s.mainStrokeColor, (s, c) => s.mainStrokeColor = c, s => HasMain(s) && s.mainStrokeThicknessPixels > 0f);
    }

    private void BuildSubRows(RectTransform root)
    {
        AddShapeRow(root, m_subRows, m_subShapeButtons, new[] { "없음", "둥근 십자", "사각 십자", "원형", "점" },
            index => Edit(s => s.subShape = (CrosshairController.SubShape)index),
            s => (int)s.subShape);
        AddSliderRow(root, m_subRows, "중앙 간격", 0f, 40f, 1f, "0",
            s => s.centerSpacePixels, (s, v) => s.centerSpacePixels = v, HasSub);
        AddSliderRow(root, m_subRows, "선 길이", 1f, 40f, 1f, "0",
            s => s.subWidthPixels, (s, v) => s.subWidthPixels = v, IsSubCross);
        AddSliderRow(root, m_subRows, "선 두께", 0.5f, 8f, 0.5f, "0.0",
            s => s.subThicknessPixels, (s, v) => s.subThicknessPixels = v, IsSubCross);
        AddSliderRow(root, m_subRows, "모서리 둥글기", 0f, 6f, 0.5f, "0.0",
            s => s.cornerRadiusPixels, (s, v) => s.cornerRadiusPixels = v, s => s.subShape == CrosshairController.SubShape.RoundedCross);
        AddSliderRow(root, m_subRows, "점 크기", 0.5f, 12f, 0.5f, "0.0",
            s => s.subSizePixels, (s, v) => s.subSizePixels = v, s => s.subShape == CrosshairController.SubShape.Dot);
        AddSliderRow(root, m_subRows, "원 크기", 2f, 60f, 1f, "0",
            s => s.subRingSizePixels, (s, v) => s.subRingSizePixels = v, s => s.subShape == CrosshairController.SubShape.Ring);
        AddSliderRow(root, m_subRows, "원 두께", 1f, 6f, 0.5f, "0.0",
            s => s.subRingThicknessPixels, (s, v) => s.subRingThicknessPixels = v, s => s.subShape == CrosshairController.SubShape.Ring);
        AddColorRows(root, m_subRows, "색상", FillSwatches,
            s => s.subColor, (s, c) => s.subColor = c, HasSub);
        AddSliderRow(root, m_subRows, "테두리 두께", 0f, 4f, 0.5f, "0.0",
            s => s.subStrokeThicknessPixels, (s, v) => s.subStrokeThicknessPixels = v, HasSub);
        AddColorRows(root, m_subRows, "테두리 색", StrokeSwatches,
            s => s.subStrokeColor, (s, c) => s.subStrokeColor = c, s => HasSub(s) && s.subStrokeThicknessPixels > 0f);
    }

    /// <summary>라벨과 버튼 여러 개로 하나를 고르는 줄을 추가합니다.</summary>
    private void AddChoiceRow(RectTransform root, List<Row> rows, string label, string[] labels,
        Action<int> onSelect, Func<CrosshairStyle, int> getIndex, Func<CrosshairStyle, bool> isVisible = null)
    {
        GameObject rowObject = CreateRowObject($"{label} Row", root, label);
        var buttonImages = new List<Image>();
        float buttonWidth = labels.Length > 2 ? 85f : 163f;
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            Button button = CreateButton($"Choice {i}", rowObject.transform, new Vector2(RowControlLeft + (buttonWidth + 28f) * i, -0.5f),
                new Vector2(buttonWidth, 43f), labels[i], 22f, IdleColor);
            button.onClick.AddListener(() => onSelect(index));
            buttonImages.Add(button.GetComponent<Image>());
        }

        rows.Add(new Row
        {
            Root = rowObject,
            IsVisible = isVisible ?? (_ => true),
            Refresh = style =>
            {
                int selected = getIndex(style);
                for (int i = 0; i < buttonImages.Count; i++)
                {
                    SetChoiceSelected(buttonImages[i], i == selected);
                }
            },
        });
    }

    /// <summary>편집 패널 한 줄을 만들고 왼쪽에 22px 이름을 둡니다. 줄 높이는 44px이고 내용은 세로 가운데에 맞춥니다.</summary>
    private GameObject CreateRowObject(string name, RectTransform root, string label)
    {
        GameObject rowObject = CreateTopLeftObject(name, root, Vector2.zero, new Vector2(PanelWidth, RowHeight));
        CreateText("Label", rowObject.transform, new Vector2(RowLabelLeft, -11f), new Vector2(190f, 22f),
            label, 22f, TextAlignmentOptions.Left, m_regularFont);
        return rowObject;
    }

    private static bool HasMain(CrosshairStyle s) => s.mainShape != CrosshairController.MainShape.None;
    private static bool HasSub(CrosshairStyle s) => s.subShape != CrosshairController.SubShape.None;

    private static bool IsSubCross(CrosshairStyle s) =>
        s.subShape == CrosshairController.SubShape.RoundedCross || s.subShape == CrosshairController.SubShape.SquareCross;

    private void AddShapeRow(RectTransform root, List<Row> rows, List<Image> buttonImages, string[] labels,
        Action<int> onSelect, Func<CrosshairStyle, int> getIndex)
    {
        GameObject rowObject = CreateRowObject("Shape Row", root, "모양");

        // Figma Shape: 85x43 버튼을 113px 간격으로 놓습니다. 외부 모양은 이름이 길고 5개라 버튼을 넓히고 간격을 좁혀 패널 안에 맞춥니다.
        bool wide = labels.Length > 3;
        float buttonWidth = wide ? 96f : 85f;
        float buttonPitch = wide ? 104f : 113f;
        float fontSize = wide ? 20f : 22f;
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            Button button = CreateButton($"Shape {i}", rowObject.transform, new Vector2(RowControlLeft + buttonPitch * i, -0.5f),
                new Vector2(buttonWidth, 43f), labels[i], fontSize, IdleColor);
            button.onClick.AddListener(() => onSelect(index));
            buttonImages.Add(button.GetComponent<Image>());
        }

        rows.Add(new Row
        {
            Root = rowObject,
            IsVisible = _ => true,
            Refresh = style =>
            {
                int selected = getIndex(style);
                for (int i = 0; i < buttonImages.Count; i++)
                {
                    SetChoiceSelected(buttonImages[i], i == selected);
                }
            },
        });
    }

    private void AddSliderRow(RectTransform root, List<Row> rows, string label, float min, float max, float step, string format,
        Func<CrosshairStyle, float> getter, Action<CrosshairStyle, float> setter, Func<CrosshairStyle, bool> isVisible,
        Func<float, string> valueText = null)
    {
        GameObject rowObject = CreateRowObject($"{label} Row", root, label);
        TextMeshProUGUI value = CreateValueText(rowObject.transform);
        bool wholeNumbers = Mathf.Approximately(step, 1f) && Mathf.Approximately(min, Mathf.Round(min));
        Slider slider = CreateRowSlider(rowObject.transform, min, max, wholeNumbers);
        slider.onValueChanged.AddListener(raw =>
        {
            if (m_isRefreshing) return;
            // 다른 탭 슬라이더처럼 값을 단위에 맞춰 자른 뒤 슬라이더와 숫자 표시를 바로 그 값으로 맞춥니다.
            float snapped = step > 0f ? Mathf.Clamp(Mathf.Round(raw / step) * step, min, max) : raw;
            slider.SetValueWithoutNotify(snapped);
            value.text = valueText != null ? valueText(snapped) : $"{snapped.ToString(format)} px";
            Edit(s => setter(s, snapped));
        });

        rows.Add(new Row
        {
            Root = rowObject,
            IsVisible = isVisible,
            Refresh = style =>
            {
                float v = getter(style);
                slider.SetValueWithoutNotify(v);
                value.text = valueText != null ? valueText(v) : $"{v.ToString(format)} px";
            },
        });
    }

    /// <summary>색 견본 줄과 불투명도 줄을 함께 추가합니다. 불투명도는 색의 알파 값입니다.</summary>
    private void AddColorRows(RectTransform root, List<Row> rows, string label, Color[] swatches,
        Func<CrosshairStyle, Color> getter, Action<CrosshairStyle, Color> setter, Func<CrosshairStyle, bool> isVisible)
    {
        GameObject rowObject = CreateRowObject($"{label} Row", root, label);
        var outlines = new List<Outline>();
        for (int i = 0; i < swatches.Length; i++)
        {
            Color swatch = swatches[i];
            Button button = CreateButton($"Swatch {i}", rowObject.transform,
                new Vector2(RowControlLeft + 1f + SwatchPitch * i, -((RowHeight - SwatchSize) * 0.5f)),
                new Vector2(SwatchSize, SwatchSize), string.Empty, 1f, swatch);

            // Figma Color_.palette: 고른 색은 노란 테두리로 둘러쌉니다.
            Outline swatchOutline = button.gameObject.AddComponent<Outline>();
            swatchOutline.effectColor = AccentColor;
            swatchOutline.effectDistance = new Vector2(3f, -3f);
            swatchOutline.useGraphicAlpha = false;
            outlines.Add(swatchOutline);
            button.onClick.AddListener(() => Edit(s =>
            {
                Color current = getter(s);
                setter(s, new Color(swatch.r, swatch.g, swatch.b, current.a));
            }));
        }

        rows.Add(new Row
        {
            Root = rowObject,
            IsVisible = isVisible,
            Refresh = style =>
            {
                Color current = getter(style);
                for (int i = 0; i < swatches.Length; i++)
                {
                    outlines[i].enabled = Mathf.Abs(swatches[i].r - current.r) < 0.01f
                        && Mathf.Abs(swatches[i].g - current.g) < 0.01f
                        && Mathf.Abs(swatches[i].b - current.b) < 0.01f;
                }
            },
        });

        GameObject alphaRow = CreateRowObject($"{label} Alpha Row", root, $"{label.Replace(" 색", string.Empty)} 불투명도");
        TextMeshProUGUI value = CreateValueText(alphaRow.transform);
        Slider slider = CreateRowSlider(alphaRow.transform, 0f, 1f, false);
        slider.onValueChanged.AddListener(raw =>
        {
            if (m_isRefreshing) return;
            float alpha = Mathf.Round(raw * 100f) / 100f;
            slider.SetValueWithoutNotify(alpha);
            value.text = $"{alpha * 100f:0}%";
            Edit(s =>
            {
                Color current = getter(s);
                current.a = alpha;
                setter(s, current);
            });
        });

        rows.Add(new Row
        {
            Root = alphaRow,
            IsVisible = isVisible,
            Refresh = style =>
            {
                float alpha = getter(style).a;
                slider.SetValueWithoutNotify(alpha);
                value.text = $"{alpha * 100f:0}%";
            },
        });
    }

    /// <summary>
    /// 편집 줄의 변경을 적용합니다. 외곽의 조준 탭이면 조준 외곽을 담은 보기용 사본을 고친 뒤 조준 외곽에 되돌려 씁니다.
    /// </summary>
    /// <remarks>편집 줄은 모두 비조준 외곽 필드를 읽고 쓰므로, 같은 줄로 조준 외곽을 고치려면 이 우회가 필요합니다.</remarks>
    private void Edit(Action<CrosshairStyle> change)
    {
        CrosshairStyle style = CurrentStyle;
        if (style == null)
        {
            return;
        }

        if (IsEditingAds)
        {
            CrosshairStyle view = style.CreateAdsView();
            change(view);
            style.StoreAdsView(view);
        }
        else
        {
            change(style);
        }

        ApplyPendingValues();
        RefreshAll();
    }

    /// <summary>지금 조준(ADS) 쪽 조준선(내부·외부)을 편집하고 있는지 여부입니다. 동적 크로스헤어를 켜면 항상 거짓입니다.</summary>
    private bool IsEditingAds => !m_isThrowableMode && m_adsTab
        && CurrentStyle != null && !CurrentStyle.subDynamic;

    /// <summary>편집 줄과 미리보기에 보여 줄 스타일입니다. 조준 쪽 편집 중이면 조준 내부·외부를 담은 보기용 사본입니다.</summary>
    private CrosshairStyle DisplayStyle
    {
        get
        {
            CrosshairStyle style = CurrentStyle;
            return style != null && IsEditingAds ? style.CreateAdsView() : style;
        }
    }

    private void SelectLayer(Layer layer)
    {
        m_selectedLayer = layer;
        RefreshAll();
    }

    private void SelectCharacter(int index)
    {
        m_selectedCharacterIndex = index;
        RefreshAll();
    }

    private void ResetSelectedToDefault()
    {
        CrosshairSettingsService service = CrosshairSettingsService.Instance;
        if (service == null)
        {
            return;
        }

        if (m_isThrowableMode)
        {
            m_workingThrowable = service.GetDefaultThrowableStyle();
        }
        else if (m_selectedCharacterIndex < m_characters.Count)
        {
            PlayerbleCharacterId id = m_characters[m_selectedCharacterIndex].Key;
            m_workingStyles[id] = service.GetDefaultCharacterStyle(id);
        }

        ApplyPendingValues();
        RefreshAll();
    }

    private CrosshairStyle CurrentStyle
    {
        get
        {
            if (m_isThrowableMode)
            {
                return m_workingThrowable;
            }

            if (m_selectedCharacterIndex < 0 || m_selectedCharacterIndex >= m_characters.Count)
            {
                return null;
            }

            return m_workingStyles.TryGetValue(m_characters[m_selectedCharacterIndex].Key, out CrosshairStyle style) ? style : null;
        }
    }

    private void RebuildCharacterButtons(CrosshairSettingsService service)
    {
        List<KeyValuePair<PlayerbleCharacterId, string>> characters = service.GetCharacters();
        bool same = characters.Count == m_characters.Count;
        for (int i = 0; same && i < characters.Count; i++)
        {
            same = characters[i].Key == m_characters[i].Key;
        }

        if (same)
        {
            return;
        }

        m_characters.Clear();
        m_characters.AddRange(characters);
        m_characterButtonImages.Clear();
        for (int i = m_characterRow.childCount - 1; i >= 0; i--)
        {
            Destroy(m_characterRow.GetChild(i).gameObject);
        }

        for (int i = 0; i < m_characters.Count; i++)
        {
            int index = i;
            // Figma Select Charcter: 115x39 버튼을 139px 간격으로 놓습니다.
            Button button = CreateButton($"Character {m_characters[i].Key}", m_characterRow, new Vector2(139f * i, 0f),
                new Vector2(115f, 39f), m_characters[i].Value, 25f, IdleColor);
            button.onClick.AddListener(() => SelectCharacter(index));
            m_characterButtonImages.Add(button.GetComponent<Image>());
        }

        m_selectedCharacterIndex = Mathf.Clamp(m_selectedCharacterIndex, 0, Mathf.Max(0, m_characters.Count - 1));
    }

    /// <summary>동적 크로스헤어를 켜고 끕니다. 켜면 비조준/조준 구분이 없어지므로 비조준 쪽으로 돌아갑니다.</summary>
    private void ToggleDynamic()
    {
        CrosshairStyle style = CurrentStyle;
        if (style == null)
        {
            return;
        }

        style.subDynamic = !style.subDynamic;
        m_adsTab = false;
        ApplyPendingValues();
        RefreshAll();
    }

    /// <summary>
    /// 동적 크로스헤어 체크와 비조준/조준 버튼을 맞춥니다. 동적을 켜면 비조준/조준 버튼이 사라집니다.
    /// </summary>
    private void LayoutHeader(CrosshairStyle style)
    {
        if (m_isThrowableMode)
        {
            return;
        }

        bool dynamic = style == null || style.subDynamic;
        m_dynamicCheck.enabled = dynamic;
        m_sideRow.gameObject.SetActive(!dynamic);
        for (int i = 0; i < m_sideButtonImages.Length; i++)
        {
            SetChoiceSelected(m_sideButtonImages[i], i == (m_adsTab ? 1 : 0));
        }
    }

    private void RefreshAll()
    {
        CrosshairStyle style = CurrentStyle;
        bool available = style != null;
        m_unavailableText.gameObject.SetActive(!available);

        for (int i = 0; i < m_characterButtonImages.Count; i++)
        {
            SetChoiceSelected(m_characterButtonImages[i], i == m_selectedCharacterIndex);
        }

        for (int i = 0; i < m_layerButtonImages.Length; i++)
        {
            SetChoiceSelected(m_layerButtonImages[i], i == (int)m_selectedLayer);
        }

        LayoutHeader(style);

        m_mainRowsRoot.gameObject.SetActive(available && m_selectedLayer == Layer.Main);
        m_subRowsRoot.gameObject.SetActive(available && m_selectedLayer == Layer.Sub);
        if (!available)
        {
            DrawPreview(null);
            return;
        }

        CrosshairStyle display = DisplayStyle;
        m_isRefreshing = true;
        LayoutRows(m_selectedLayer == Layer.Main ? m_mainRows : m_subRows, display);
        m_isRefreshing = false;
        DrawPreview(display);
    }

    /// <summary>
    /// 현재 모양에 쓰이는 줄만 위에서부터 차례로 보이게 배치하고, 줄 수에 맞춰 편집 패널 높이를 정합니다.
    /// </summary>
    /// <remarks>
    /// Figma 간격(첫 줄 가운데 123.5, 둘째 줄 181, 그 뒤 48)을 쓰다가, 줄이 많아 패널이 아래 버튼 줄 구분선을 넘게 되면
    /// 셋째 줄부터의 간격을 <see cref="MinRowPitch"/>까지 줄입니다.
    /// </remarks>
    private void LayoutRows(List<Row> rows, CrosshairStyle style)
    {
        int visibleCount = 0;
        foreach (Row row in rows)
        {
            if (row.IsVisible(style)) visibleCount++;
        }

        float maxHeight = PanelBottomLimit - m_panelTop;
        float pitch = visibleCount > 2
            ? Mathf.Clamp((maxHeight - 40f - SecondRowCenter) / (visibleCount - 2), MinRowPitch, RowPitch)
            : RowPitch;

        int index = 0;
        float lastCenter = FirstRowCenter;
        foreach (Row row in rows)
        {
            bool visible = row.IsVisible(style);
            row.Root.SetActive(visible);
            if (!visible) continue;

            float center = index == 0 ? FirstRowCenter : SecondRowCenter + pitch * (index - 1);
            ((RectTransform)row.Root.transform).anchoredPosition = new Vector2(0f, -(center - RowHeight * 0.5f));
            row.Refresh(style);
            lastCenter = center;
            index++;
        }

        // Figma 패널은 마지막 줄 아래로 여유가 68px 있습니다. 줄이 많으면 여유를 줄여 아래 한계 안에 둡니다.
        float height = Mathf.Max(PanelMinHeight, Mathf.Min(lastCenter + 68f, Mathf.Max(maxHeight, lastCenter + 30f)));
        m_controlsPanel.sizeDelta = new Vector2(PanelWidth, height);
    }

    /// <summary>Figma Preview 묶음입니다. 틀(Preview 542x559) 안 회색 칸(x 50, y 70, 442x446)에 미리보기를 그립니다.</summary>
    private void BuildPreview(Transform parent)
    {
        Image preview = CreateImage("Crosshair Preview", parent, new Vector2(1259f, -225f), new Vector2(542f, 559f),
            new Color(0.2f, 0.2f, 0.2f, 1f));
        ApplySprite(preview, Theme != null ? Theme.crosshairPreview : null);
        RectTransform previewRect = preview.rectTransform;
        CreateText("Preview Title", previewRect, new Vector2(47f, -23f), new Vector2(300f, 19f),
            "조준선 미리보기", 19f, TextAlignmentOptions.Left, m_boldFont);
        m_previewProfileLabel = CreateText("Preview Profile", previewRect, new Vector2(291f, -23f), new Vector2(200f, 19f),
            string.Empty, 19f, TextAlignmentOptions.Right, m_regularFont);

        // 틀 이미지에 회색 칸이 그려져 있으면 칸은 비워 두고, 틀이 없을 때만 회색으로 칠합니다.
        Image field = CreateImage("Preview Field", previewRect, new Vector2(50f, -70f), new Vector2(442f, 446f),
            Theme != null && Theme.crosshairPreview != null ? Color.clear : new Color(0.56f, 0.56f, 0.56f, 1f));
        field.raycastTarget = false;
        GameObject imageObject = CreateStretchObject("Preview Image", field.transform);
        m_previewImage = imageObject.AddComponent<RawImage>();
        m_previewImage.raycastTarget = false;
        m_previewImage.enabled = false;

        m_previewZoomLabel = CreateText("Preview Zoom", previewRect, new Vector2(0f, -534f), new Vector2(542f, 14f),
            string.Empty, 14f, TextAlignmentOptions.Center, m_regularFont);
    }

    /// <summary>
    /// 미리보기를 그립니다. 실제 조준선 HUD를 복제해 게임 화면과 같은 크기·간격으로 그립니다.
    /// 캐릭터 조준선은 그 캐릭터가 쏘지 않고 있을 때의 간격이고, 조준(ADS) 외곽을 고치는 중이면 조준을 마친 상태입니다.
    /// </summary>
    private void DrawPreview(CrosshairStyle style)
    {
        if (m_previewImage == null)
        {
            return;
        }

        m_previewProfileLabel.text = m_isThrowableMode
            ? "모든 캐릭터 공용"
            : m_selectedCharacterIndex < m_characters.Count ? m_characters[m_selectedCharacterIndex].Value : string.Empty;

        // 저장·되돌리기는 화면이 꺼져 있을 때도 값을 새로 그립니다. 그때 복제본을 만들면 OnDisable이 다시 오지 않아 남으므로 만들지 않습니다.
        CrosshairSettingsService service = CrosshairSettingsService.Instance;
        if (!isActiveAndEnabled || style == null || service == null || service.Crosshair == null)
        {
            m_hudPreview?.Clear();
            m_previewZoomLabel.text = string.Empty;
            return;
        }

        m_hudPreview ??= new CrosshairHudPreview(service.Crosshair, m_previewImage);
        CrosshairStyle stored = CurrentStyle;
        bool ads = IsEditingAds;
        AimController aim = !m_isThrowableMode && m_selectedCharacterIndex < m_characters.Count
            ? service.FindAimController(m_characters[m_selectedCharacterIndex].Key)
            : null;

        bool drawn = m_hudPreview.Render(clone =>
        {
            if (aim != null)
            {
                aim.ApplyCrosshairPreview(clone, stored, ads);
                return;
            }

            stored.ApplyTo(clone, true);
            clone.SetShotRecoilPulseEnabled(false);
            clone.ClearShotRecoilPulse();
            clone.SetSpread(0f, SpreadDistribution.Gaussian, 3f, 60f, true);
        });

        m_previewZoomLabel.text = !drawn
            ? string.Empty
            : m_isThrowableMode
                ? "게임 화면과 같은 크기입니다."
                : stored.subDynamic
                    ? "게임 화면과 같은 크기입니다. 쏘지 않을 때 모습이며 반동·조준에 따라 움직입니다."
                    : ads
                        ? "게임 화면과 같은 크기입니다. 조준 중에는 항상 이 모습입니다."
                        : "게임 화면과 같은 크기입니다. 비조준 중에는 항상 이 모습입니다.";
    }

    /// <summary>편집 줄의 슬라이더입니다. Figma Setting Gauge(408x26.65)를 이름 오른쪽(x 240)에 세로 가운데로 둡니다.</summary>
    private Slider CreateRowSlider(Transform parent, float min, float max, bool wholeNumbers)
    {
        return CreateSlider(parent, new Vector2(RowControlLeft, -((RowHeight - SliderSize.y) * 0.5f)), SliderSize, min, max, wholeNumbers);
    }

    /// <summary>편집 줄 슬라이더 오른쪽의 20px 값 글자입니다.</summary>
    private TextMeshProUGUI CreateValueText(Transform parent)
    {
        return CreateText("Value", parent, new Vector2(RowValueLeft, -12f), new Vector2(110f, 20f),
            string.Empty, 20f, TextAlignmentOptions.Left, m_regularFont);
    }

    private Slider CreateSlider(Transform parent, Vector2 position, Vector2 size, float min, float max, bool wholeNumbers)
    {
        if (SliderFactory != null)
        {
            return SliderFactory("Slider", parent, position, size, min, max, wholeNumbers);
        }

        GameObject sliderObject = CreateTopLeftObject("Slider", parent, position, size);
        Slider slider = sliderObject.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = wholeNumbers;

        // 보이는 막대는 위아래를 깎아 얇게 그립니다. 슬라이더 높이 전체에서 클릭과 드래그를 받도록 투명한 영역을 깝니다.
        Image hitArea = sliderObject.AddComponent<Image>();
        hitArea.color = Color.clear;

        Image background =CreateStretchImage("Background", sliderObject.transform, new Color(1f, 1f, 1f, 0.13f));
        background.rectTransform.offsetMin = new Vector2(0f, 8f);
        background.rectTransform.offsetMax = new Vector2(0f, -8f);

        GameObject fillArea = CreateStretchObject("Fill Area", sliderObject.transform);
        RectTransform fillAreaRect = (RectTransform)fillArea.transform;
        fillAreaRect.offsetMin = new Vector2(0f, 8f);
        fillAreaRect.offsetMax = new Vector2(-12f, -8f);
        Image fill = CreateStretchImage("Fill", fillArea.transform, new Color(0.95f, 0.08f, 0.08f, 1f));

        GameObject handleArea = CreateStretchObject("Handle Slide Area", sliderObject.transform);
        RectTransform handleAreaRect = (RectTransform)handleArea.transform;
        handleAreaRect.offsetMin = new Vector2(8f, 0f);
        handleAreaRect.offsetMax = new Vector2(-8f, 0f);
        Image handle = CreateCenteredImage("Handle", handleArea.transform);
        handle.rectTransform.sizeDelta = new Vector2(18f, 18f);
        handle.color = Color.white;

        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    /// <summary>아트 스프라이트를 원본 색 그대로 보이게 적용합니다. 스프라이트가 없으면 지금 색을 그대로 둡니다.</summary>
    private static void ApplySprite(Image image, Sprite sprite)
    {
        if (image == null || sprite == null)
        {
            return;
        }

        image.sprite = sprite;
        image.color = Color.white;
        image.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
    }

    private static void SetTopLeftRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private Button CreateButton(string name, Transform parent, Vector2 position, Vector2 size, string label, float fontSize,
        Color backgroundColor)
    {
        GameObject buttonObject = CreateTopLeftObject(name, parent, position, size);
        Image image = buttonObject.AddComponent<Image>();
        image.color = backgroundColor;
        Button button = buttonObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.pressedColor = new Color(0.65f, 0.65f, 0.65f, 1f);
        colors.selectedColor = Color.white;
        button.colors = colors;
        if (!string.IsNullOrEmpty(label))
        {
            CreateStretchText("Label", buttonObject.transform, label, fontSize, TextAlignmentOptions.Center, m_regularFont);
        }

        return button;
    }

    private TextMeshProUGUI CreateText(string name, Transform parent, Vector2 position, Vector2 size, string value,
        float fontSize, TextAlignmentOptions alignment, TMP_FontAsset font, Color? color = null)
    {
        GameObject textObject = CreateTopLeftObject(name, parent, position, size);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        ConfigureText(text, value, fontSize, alignment, font, color ?? Color.white);
        return text;
    }

    private static TextMeshProUGUI CreateStretchText(string name, Transform parent, string value, float fontSize,
        TextAlignmentOptions alignment, TMP_FontAsset font)
    {
        GameObject textObject = CreateStretchObject(name, parent);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        ConfigureText(text, value, fontSize, alignment, font, Color.white);
        return text;
    }

    private static void ConfigureText(TextMeshProUGUI text, string value, float fontSize, TextAlignmentOptions alignment,
        TMP_FontAsset font, Color color)
    {
        text.text = value;
        text.font = font;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
    }

    private static Image CreateImage(string name, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        GameObject imageObject = CreateTopLeftObject(name, parent, position, size);
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static Image CreateCenteredImage(string name, Transform parent)
    {
        GameObject imageObject = new(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)imageObject.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(4f, 4f);
        Image image = imageObject.AddComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private static Image CreateStretchImage(string name, Transform parent, Color color)
    {
        GameObject imageObject = CreateStretchObject(name, parent);
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static GameObject CreateStretchObject(string name, Transform parent)
    {
        GameObject child = new(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)child.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        return child;
    }

    private static GameObject CreateTopLeftObject(string name, Transform parent, Vector2 position, Vector2 size)
    {
        GameObject child = new(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)child.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        return child;
    }
}
