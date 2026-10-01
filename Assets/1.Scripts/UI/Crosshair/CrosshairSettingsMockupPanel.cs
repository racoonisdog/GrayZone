using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ESC 설정 화면에서 캐릭터별 조준선 값을 조절하고 결과를 즉시 미리 보여 주는 목업 패널입니다.
/// </summary>
/// <remarks>
/// 현재 값은 실행 중인 세션 안에서만 유지되며 실제 전투 HUD나 영구 설정 파일에는 적용하지 않습니다.
/// </remarks>
public sealed class CrosshairSettingsMockupPanel : MonoBehaviour
{
    private const float PreviewCenterX = 300f;
    private const float PreviewCenterY = 280f;

    private enum CharacterSlot { Narin, ChungSol, SeoHa }
    private enum AimMode { HipFire, Ads }
    private enum CrosshairShape { Cross, Dot, Ring }

    [Serializable]
    private sealed class CrosshairPreset
    {
        public CrosshairShape shape = CrosshairShape.Cross;
        public bool showCenterDot = true;
        public float lineLength = 18f;
        public float lineThickness = 3f;
        public float centerGap = 8f;
        public float opacity = 1f;
        public Color color = Color.white;

        public CrosshairPreset Clone()
        {
            return new CrosshairPreset
            {
                shape = shape,
                showCenterDot = showCenterDot,
                lineLength = lineLength,
                lineThickness = lineThickness,
                centerGap = centerGap,
                opacity = opacity,
                color = color
            };
        }
    }

    private readonly Dictionary<string, CrosshairPreset> m_presets = new();
    private readonly Dictionary<string, CrosshairPreset> m_savedPresets = new();
    private readonly Image[] m_characterButtonImages = new Image[3];
    private readonly Image[] m_aimModeButtonImages = new Image[2];
    private readonly Image[] m_shapeButtonImages = new Image[3];
    private readonly Image[] m_crosshairLines = new Image[4];

    private TMP_FontAsset m_regularFont;
    private TMP_FontAsset m_boldFont;
    private CharacterSlot m_selectedCharacter;
    private AimMode m_selectedAimMode;
    private Toggle m_centerDotToggle;
    private Slider m_lengthSlider;
    private Slider m_thicknessSlider;
    private Slider m_gapSlider;
    private Slider m_opacitySlider;
    private TextMeshProUGUI m_lengthValue;
    private TextMeshProUGUI m_thicknessValue;
    private TextMeshProUGUI m_gapValue;
    private TextMeshProUGUI m_opacityValue;
    private TextMeshProUGUI m_previewCharacterLabel;
    private TextMeshProUGUI m_ringPreview;
    private TextMeshProUGUI m_centerDot;
    private bool m_isThrowableMode;
    private bool m_isRefreshing;
    private bool m_isBuilt;

    /// <summary>캐릭터별 비조준/조준 상태용 조준선 목업 UI를 생성합니다.</summary>
    public void Build(RectTransform parent, TMP_FontAsset regularFont, TMP_FontAsset boldFont)
    {
        BuildInternal(parent, regularFont, boldFont, false);
    }

    /// <summary>모든 캐릭터가 공유하는 투척물 조준선 목업 UI를 생성합니다.</summary>
    public void BuildSharedThrowable(RectTransform parent, TMP_FontAsset regularFont, TMP_FontAsset boldFont)
    {
        BuildInternal(parent, regularFont, boldFont, true);
    }

    private void BuildInternal(RectTransform parent, TMP_FontAsset regularFont, TMP_FontAsset boldFont, bool throwableMode)
    {
        if (m_isBuilt || parent == null) return;

        m_isBuilt = true;
        m_isThrowableMode = throwableMode;
        m_regularFont = regularFont;
        m_boldFont = boldFont != null ? boldFont : regularFont;
        CreateDefaultPresets();

        string title = throwableMode ? "투척물 조준선" : "캐릭터 조준선";
        string notice = throwableMode
            ? "모든 캐릭터가 공유하는 투척물 조준선 초안 · 실제 전투 HUD에는 아직 적용되지 않습니다."
            : "캐릭터별 비조준/조준 프리셋 초안 · 실제 전투 HUD에는 아직 적용되지 않습니다.";
        CreateText("Page Title", parent, new Vector2(430f, -34f), new Vector2(500f, 50f),
            title, 32f, TextAlignmentOptions.Left, m_boldFont);
        CreateText("Draft Notice", parent, new Vector2(430f, -82f), new Vector2(700f, 32f),
            notice, 17f, TextAlignmentOptions.Left, m_regularFont, new Color(1f, 1f, 1f, 0.6f));

        if (!throwableMode)
        {
            BuildCharacterSelector(parent);
            BuildAimModeSelector(parent);
        }

        BuildControls(parent, throwableMode ? -156f : -326f);
        BuildPreview(parent);
        m_selectedCharacter = CharacterSlot.Narin;
        m_selectedAimMode = AimMode.HipFire;
        RefreshSelectors();
        RefreshControlsAndPreview();
        CommitCurrentValues();
    }

    /// <summary>현재 목업 값을 되돌리기 기준으로 저장합니다. 영구 저장은 수행하지 않습니다.</summary>
    public void CommitCurrentValues()
    {
        m_savedPresets.Clear();
        foreach (KeyValuePair<string, CrosshairPreset> pair in m_presets)
            m_savedPresets.Add(pair.Key, pair.Value.Clone());
    }

    /// <summary>모든 목업 값을 마지막 저장 기준으로 되돌립니다.</summary>
    public void RevertToCommittedValues()
    {
        if (m_savedPresets.Count == 0) return;
        m_presets.Clear();
        foreach (KeyValuePair<string, CrosshairPreset> pair in m_savedPresets)
            m_presets.Add(pair.Key, pair.Value.Clone());
        RefreshControlsAndPreview();
    }

    private void CreateDefaultPresets()
    {
        if (m_isThrowableMode)
        {
            m_presets["Throwable"] = CreatePreset(CrosshairShape.Ring, true, 20f, 4f, 10f,
                new Color(0.95f, 0.18f, 0.18f, 1f));
            return;
        }

        m_presets[GetPresetKey(CharacterSlot.Narin, AimMode.HipFire)] = CreatePreset(CrosshairShape.Cross, true, 16f, 3f, 7f, Color.white);
        m_presets[GetPresetKey(CharacterSlot.Narin, AimMode.Ads)] = CreatePreset(CrosshairShape.Dot, true, 10f, 3f, 2f, Color.white);
        m_presets[GetPresetKey(CharacterSlot.ChungSol, AimMode.HipFire)] = CreatePreset(CrosshairShape.Cross, false, 22f, 4f, 12f, new Color(0.15f, 0.95f, 0.8f, 1f));
        m_presets[GetPresetKey(CharacterSlot.ChungSol, AimMode.Ads)] = CreatePreset(CrosshairShape.Ring, true, 15f, 3f, 5f, new Color(0.15f, 0.95f, 0.8f, 1f));
        m_presets[GetPresetKey(CharacterSlot.SeoHa, AimMode.HipFire)] = CreatePreset(CrosshairShape.Dot, true, 13f, 2f, 5f, new Color(1f, 0.84f, 0.18f, 1f));
        m_presets[GetPresetKey(CharacterSlot.SeoHa, AimMode.Ads)] = CreatePreset(CrosshairShape.Cross, true, 12f, 2f, 4f, new Color(1f, 0.84f, 0.18f, 1f));
    }

    private static CrosshairPreset CreatePreset(CrosshairShape shape, bool dot, float length, float thickness, float gap, Color color)
    {
        return new CrosshairPreset { shape = shape, showCenterDot = dot, lineLength = length, lineThickness = thickness, centerGap = gap, color = color };
    }

    private void BuildCharacterSelector(Transform parent)
    {
        CreateText("Character Label", parent, new Vector2(430f, -126f), new Vector2(160f, 32f),
            "캐릭터", 20f, TextAlignmentOptions.Left, m_boldFont);
        string[] labels = { "나린", "청솔", "서하" };
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            Button button = CreateButton($"Character {labels[i]}", parent,
                new Vector2(430f + (180f * i), -164f), new Vector2(164f, 42f), labels[i], 19f,
                new Color(1f, 1f, 1f, 0.08f));
            button.onClick.AddListener(() => SelectCharacter((CharacterSlot)index));
            m_characterButtonImages[i] = button.GetComponent<Image>();
        }
    }

    private void BuildAimModeSelector(Transform parent)
    {
        CreateText("Aim Mode Label", parent, new Vector2(430f, -220f), new Vector2(160f, 32f),
            "조준 상태", 20f, TextAlignmentOptions.Left, m_boldFont);
        string[] labels = { "비조준", "조준" };
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            Button button = CreateButton($"Aim Mode {labels[i]}", parent,
                new Vector2(430f + (180f * i), -258f), new Vector2(164f, 42f), labels[i], 18f,
                new Color(1f, 1f, 1f, 0.08f));
            button.onClick.AddListener(() => SelectAimMode((AimMode)index));
            m_aimModeButtonImages[i] = button.GetComponent<Image>();
        }
    }

    private void BuildControls(Transform parent, float top)
    {
        Image panel = CreateImage("Controls Panel", parent, new Vector2(430f, top), new Vector2(650f, 610f),
            new Color(0.035f, 0.035f, 0.035f, 0.92f));
        Outline outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.16f);
        outline.effectDistance = new Vector2(1f, -1f);
        RectTransform panelRect = panel.rectTransform;

        CreateText("Shape Label", panelRect, new Vector2(32f, -24f), new Vector2(120f, 32f),
            "모양", 18f, TextAlignmentOptions.Left, m_boldFont);
        string[] shapeLabels = { "십자", "점", "원형" };
        for (int i = 0; i < shapeLabels.Length; i++)
        {
            int shapeIndex = i;
            Button shapeButton = CreateButton($"Shape {shapeLabels[i]}", panelRect,
                new Vector2(132f + (120f * i), -18f), new Vector2(108f, 38f), shapeLabels[i], 16f,
                new Color(1f, 1f, 1f, 0.08f));
            shapeButton.onClick.AddListener(() => SelectShape((CrosshairShape)shapeIndex));
            m_shapeButtonImages[i] = shapeButton.GetComponent<Image>();
        }

        m_centerDotToggle = CreateToggle("Center Dot", panelRect, new Vector2(32f, -78f), "중앙점 표시");
        m_centerDotToggle.onValueChanged.AddListener(OnCenterDotChanged);
        m_lengthSlider = CreateLabeledSlider(panelRect, "Length", "선 길이 / 원 크기", new Vector2(32f, -140f), 4f, 32f, out m_lengthValue);
        m_thicknessSlider = CreateLabeledSlider(panelRect, "Thickness", "선 두께", new Vector2(32f, -220f), 1f, 8f, out m_thicknessValue);
        m_gapSlider = CreateLabeledSlider(panelRect, "Gap", "중앙 간격", new Vector2(32f, -300f), 0f, 30f, out m_gapValue);
        m_opacitySlider = CreateLabeledSlider(panelRect, "Opacity", "불투명도", new Vector2(32f, -380f), 0.2f, 1f, out m_opacityValue);
        m_lengthSlider.onValueChanged.AddListener(_ => OnSliderChanged());
        m_thicknessSlider.onValueChanged.AddListener(_ => OnSliderChanged());
        m_gapSlider.onValueChanged.AddListener(_ => OnSliderChanged());
        m_opacitySlider.onValueChanged.AddListener(_ => OnSliderChanged());

        CreateText("Color Label", panelRect, new Vector2(32f, -466f), new Vector2(100f, 28f),
            "색상", 18f, TextAlignmentOptions.Left, m_boldFont);
        Color[] colors = { Color.white, new Color(0.95f, 0.18f, 0.18f, 1f), new Color(0.15f, 0.95f, 0.8f, 1f), new Color(1f, 0.84f, 0.18f, 1f) };
        for (int i = 0; i < colors.Length; i++)
        {
            Color color = colors[i];
            Button swatch = CreateButton($"Color Swatch {i}", panelRect,
                new Vector2(130f + (58f * i), -460f), new Vector2(42f, 42f), string.Empty, 1f, color);
            swatch.onClick.AddListener(() => SetCrosshairColor(color));
        }

        string resetLabel = m_isThrowableMode ? "공용 기본값" : "현재 프리셋 기본값";
        Button resetButton = CreateButton("Reset Profile", panelRect, new Vector2(430f, -538f),
            new Vector2(184f, 42f), resetLabel, 15f, new Color(1f, 1f, 1f, 0.12f));
        resetButton.onClick.AddListener(ResetSelectedPreset);
    }

    private void BuildPreview(Transform parent)
    {
        Image preview = CreateImage("Crosshair Simulation", parent, new Vector2(1130f, -132f), new Vector2(650f, 684f),
            new Color(0.015f, 0.018f, 0.02f, 0.96f));
        Outline outline = preview.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.2f);
        outline.effectDistance = new Vector2(1f, -1f);
        RectTransform previewRect = preview.rectTransform;
        CreateText("Preview Title", previewRect, new Vector2(28f, -24f), new Vector2(320f, 34f),
            "실시간 크로스헤어 시뮬레이션", 20f, TextAlignmentOptions.Left, m_boldFont);
        m_previewCharacterLabel = CreateText("Preview Profile", previewRect, new Vector2(350f, -24f), new Vector2(270f, 34f),
            string.Empty, 18f, TextAlignmentOptions.Right, m_regularFont, new Color(1f, 1f, 1f, 0.65f));

        Image targetArea = CreateImage("Preview Field", previewRect, new Vector2(25f, -78f), new Vector2(600f, 560f),
            new Color(0.08f, 0.09f, 0.1f, 1f));
        RectTransform targetRect = targetArea.rectTransform;
        CreateTargetGuide(targetRect, 360f, 0.10f);
        CreateTargetGuide(targetRect, 220f, 0.14f);
        CreateTargetGuide(targetRect, 100f, 0.18f);
        m_crosshairLines[0] = CreateCenteredImage("Crosshair Left", targetRect);
        m_crosshairLines[1] = CreateCenteredImage("Crosshair Right", targetRect);
        m_crosshairLines[2] = CreateCenteredImage("Crosshair Top", targetRect);
        m_crosshairLines[3] = CreateCenteredImage("Crosshair Bottom", targetRect);
        m_centerDot = CreateCenteredText("Crosshair Center Dot", targetRect, "●", 24f, m_boldFont);
        m_ringPreview = CreateCenteredText("Crosshair Ring", targetRect, "○", 76f, m_boldFont);

        CreateText("Preview Hint", previewRect, new Vector2(25f, -646f), new Vector2(600f, 24f),
            "모양과 수치를 바꾸면 이 영역에 즉시 반영됩니다.", 16f, TextAlignmentOptions.Center,
            m_regularFont, new Color(1f, 1f, 1f, 0.5f));
    }

    private void CreateTargetGuide(Transform parent, float size, float alpha)
    {
        Image guide = CreateImage($"Target Guide {size:0}", parent,
            new Vector2(PreviewCenterX - size * 0.5f, -(PreviewCenterY - size * 0.5f)),
            new Vector2(size, size), Color.clear);
        Outline outline = guide.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, alpha);
        outline.effectDistance = new Vector2(1f, -1f);
        guide.raycastTarget = false;
    }

    private Slider CreateLabeledSlider(
        Transform parent,
        string name,
        string label,
        Vector2 position,
        float min,
        float max,
        out TextMeshProUGUI valueText)
    {
        CreateText($"{name} Label", parent, position, new Vector2(180f, 30f), label, 18f,
            TextAlignmentOptions.Left, m_regularFont);
        valueText = CreateText($"{name} Value", parent, position + new Vector2(485f, 0f), new Vector2(100f, 30f),
            string.Empty, 18f, TextAlignmentOptions.Right, m_boldFont);

        GameObject sliderObject = CreateTopLeftObject($"{name} Slider", parent, position + new Vector2(0f, -38f), new Vector2(585f, 24f));
        Slider slider = sliderObject.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;

        Image background = CreateStretchImage("Background", sliderObject.transform, new Color(1f, 1f, 1f, 0.13f));
        RectTransform backgroundRect = background.rectTransform;
        backgroundRect.offsetMin = new Vector2(0f, 8f);
        backgroundRect.offsetMax = new Vector2(0f, -8f);

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

    private Toggle CreateToggle(string name, Transform parent, Vector2 position, string label)
    {
        GameObject toggleObject = CreateTopLeftObject(name, parent, position, new Vector2(585f, 38f));
        Toggle toggle = toggleObject.AddComponent<Toggle>();
        Image background = CreateImage("Background", toggleObject.transform, Vector2.zero, new Vector2(30f, 30f),
            new Color(1f, 1f, 1f, 0.16f));
        Image checkmark = CreateImage("Checkmark", background.transform, new Vector2(6f, -6f), new Vector2(18f, 18f),
            new Color(0.95f, 0.08f, 0.08f, 1f));
        CreateText("Label", toggleObject.transform, new Vector2(48f, -1f), new Vector2(300f, 32f), label, 18f,
            TextAlignmentOptions.Left, m_regularFont);
        toggle.targetGraphic = background;
        toggle.graphic = checkmark;
        return toggle;
    }

    private void SelectCharacter(CharacterSlot slot) { m_selectedCharacter = slot; RefreshSelectors(); RefreshControlsAndPreview(); }
    private void SelectAimMode(AimMode mode) { m_selectedAimMode = mode; RefreshSelectors(); RefreshControlsAndPreview(); }
    private void SelectShape(CrosshairShape shape) { CurrentPreset.shape = shape; RefreshSelectors(); RefreshPreview(CurrentPreset); }

    private void RefreshSelectors()
    {
        for (int i = 0; i < m_characterButtonImages.Length; i++)
            if (m_characterButtonImages[i] != null) m_characterButtonImages[i].color = i == (int)m_selectedCharacter ? SelectedColor : IdleColor;
        for (int i = 0; i < m_aimModeButtonImages.Length; i++)
            if (m_aimModeButtonImages[i] != null) m_aimModeButtonImages[i].color = i == (int)m_selectedAimMode ? SelectedColor : IdleColor;
        CrosshairPreset preset = CurrentPreset;
        for (int i = 0; i < m_shapeButtonImages.Length; i++)
            if (m_shapeButtonImages[i] != null) m_shapeButtonImages[i].color = i == (int)preset.shape ? SelectedColor : IdleColor;
    }

    private void RefreshControlsAndPreview()
    {
        CrosshairPreset preset = CurrentPreset;
        m_isRefreshing = true;
        m_centerDotToggle.isOn = preset.showCenterDot;
        m_lengthSlider.value = preset.lineLength;
        m_thicknessSlider.value = preset.lineThickness;
        m_gapSlider.value = preset.centerGap;
        m_opacitySlider.value = preset.opacity;
        m_isRefreshing = false;
        RefreshSelectors();
        RefreshValueLabels(preset);
        RefreshPreview(preset);
    }

    private void OnCenterDotChanged(bool visible)
    {
        if (m_isRefreshing)
        {
            return;
        }

        CurrentPreset.showCenterDot = visible;
        RefreshPreview(CurrentPreset);
    }

    private void OnSliderChanged()
    {
        if (m_isRefreshing)
        {
            return;
        }

        CrosshairPreset preset = CurrentPreset;
        preset.lineLength = m_lengthSlider.value;
        preset.lineThickness = m_thicknessSlider.value;
        preset.centerGap = m_gapSlider.value;
        preset.opacity = m_opacitySlider.value;
        RefreshValueLabels(preset);
        RefreshPreview(preset);
    }

    private void SetCrosshairColor(Color color)
    {
        CrosshairPreset preset = CurrentPreset;
        preset.color = color;
        RefreshPreview(preset);
    }

    private void ResetSelectedPreset()
    {
        string key = CurrentPresetKey;
        if (m_isThrowableMode)
            m_presets[key] = CreatePreset(CrosshairShape.Ring, true, 20f, 4f, 10f, new Color(0.95f, 0.18f, 0.18f, 1f));
        else
        {
            Color color = m_selectedCharacter == CharacterSlot.Narin ? Color.white
                : m_selectedCharacter == CharacterSlot.ChungSol ? new Color(0.15f, 0.95f, 0.8f, 1f)
                : new Color(1f, 0.84f, 0.18f, 1f);
            CrosshairShape shape = m_selectedAimMode == AimMode.Ads ? CrosshairShape.Dot : CrosshairShape.Cross;
            m_presets[key] = CreatePreset(shape, true, m_selectedAimMode == AimMode.Ads ? 10f : 16f, 3f, 6f, color);
        }
        RefreshControlsAndPreview();
    }

    private void RefreshValueLabels(CrosshairPreset preset)
    {
        m_lengthValue.text = $"{preset.lineLength:0} px";
        m_thicknessValue.text = $"{preset.lineThickness:0.0} px";
        m_gapValue.text = $"{preset.centerGap:0} px";
        m_opacityValue.text = $"{preset.opacity * 100f:0}%";
    }

    private void RefreshPreview(CrosshairPreset preset)
    {
        Color color = preset.color;
        color.a = preset.opacity;
        float length = preset.lineLength * 2f;
        float thickness = preset.lineThickness * 2f;
        float gap = preset.centerGap * 2f;
        bool showCross = preset.shape == CrosshairShape.Cross;
        for (int i = 0; i < m_crosshairLines.Length; i++) m_crosshairLines[i].gameObject.SetActive(showCross);
        if (showCross)
        {
            SetPreviewPart(m_crosshairLines[0], new Vector2(-(gap + length * 0.5f), 0f), new Vector2(length, thickness), color);
            SetPreviewPart(m_crosshairLines[1], new Vector2(gap + length * 0.5f, 0f), new Vector2(length, thickness), color);
            SetPreviewPart(m_crosshairLines[2], new Vector2(0f, gap + length * 0.5f), new Vector2(thickness, length), color);
            SetPreviewPart(m_crosshairLines[3], new Vector2(0f, -(gap + length * 0.5f)), new Vector2(thickness, length), color);
        }

        m_ringPreview.gameObject.SetActive(preset.shape == CrosshairShape.Ring);
        m_ringPreview.color = color;
        m_ringPreview.fontSize = Mathf.Clamp((preset.lineLength + preset.centerGap) * 3f, 36f, 132f);
        m_centerDot.gameObject.SetActive(preset.shape == CrosshairShape.Dot || preset.showCenterDot);
        m_centerDot.color = color;
        float dotSize = preset.shape == CrosshairShape.Dot ? Mathf.Max(8f, thickness * 2f) : Mathf.Max(3f, thickness);
        m_centerDot.fontSize = Mathf.Clamp(dotSize * 2.5f, 10f, 48f);
        m_previewCharacterLabel.text = m_isThrowableMode
            ? "모든 캐릭터 공용"
            : $"{GetCharacterName(m_selectedCharacter)} · {GetAimModeName(m_selectedAimMode)}";
    }

    private CrosshairPreset CurrentPreset => m_presets[CurrentPresetKey];
    private string CurrentPresetKey => m_isThrowableMode ? "Throwable" : GetPresetKey(m_selectedCharacter, m_selectedAimMode);
    private static string GetPresetKey(CharacterSlot slot, AimMode mode) => $"{slot}:{mode}";
    private static readonly Color SelectedColor = new(0.95f, 0.08f, 0.08f, 0.88f);
    private static readonly Color IdleColor = new(1f, 1f, 1f, 0.08f);
    private static string GetAimModeName(AimMode mode) => mode == AimMode.Ads ? "조준" : "비조준";

    private static void SetPreviewPart(Image image, Vector2 position, Vector2 size, Color color)
    {
        image.rectTransform.anchoredPosition = position;
        image.rectTransform.sizeDelta = size;
        image.color = color;
    }

    private static string GetCharacterName(CharacterSlot slot)
    {
        return slot switch
        {
            CharacterSlot.Narin => "나린 전용",
            CharacterSlot.ChungSol => "청솔 전용",
            CharacterSlot.SeoHa => "서하 전용",
            _ => string.Empty
        };
    }

    private Button CreateButton(
        string name,
        Transform parent,
        Vector2 position,
        Vector2 size,
        string label,
        float fontSize,
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

    private TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        Vector2 position,
        Vector2 size,
        string value,
        float fontSize,
        TextAlignmentOptions alignment,
        TMP_FontAsset font,
        Color? color = null)
    {
        GameObject textObject = CreateTopLeftObject(name, parent, position, size);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        ConfigureText(text, value, fontSize, alignment, font, color ?? Color.white);
        return text;
    }

    private static TextMeshProUGUI CreateStretchText(
        string name,
        Transform parent,
        string value,
        float fontSize,
        TextAlignmentOptions alignment,
        TMP_FontAsset font)
    {
        GameObject textObject = CreateStretchObject(name, parent);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        ConfigureText(text, value, fontSize, alignment, font, Color.white);
        return text;
    }

    private static void ConfigureText(
        TextMeshProUGUI text,
        string value,
        float fontSize,
        TextAlignmentOptions alignment,
        TMP_FontAsset font,
        Color color)
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

    private static TextMeshProUGUI CreateCenteredText(
        string name,
        Transform parent,
        string value,
        float fontSize,
        TMP_FontAsset font)
    {
        GameObject textObject = new(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)textObject.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(180f, 180f);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        ConfigureText(text, value, fontSize, TextAlignmentOptions.Center, font, Color.white);
        return text;
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
