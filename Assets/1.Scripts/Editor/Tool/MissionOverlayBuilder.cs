using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 열린 전투 씬의 작전 실패(GameOver UI)와 임무 완료(Result UI) 오버레이를 Figma 배치대로 다시 만듭니다.
/// </summary>
/// <remarks>
/// 기준: Figma `Misson Fail Prototype 2_3`(904:981), `Misson Complete Prototype 2`(884:659), 1920x1080.
/// 좌표는 프레임 왼쪽 위 기준 값을 그대로 옮겼습니다.
///
/// 오버레이 오브젝트와 컨트롤러 컴포넌트는 남기고 자식만 다시 만듭니다. 그래야 씬 컨트롤러와 귀환 시스템이 들고 있는
/// 참조가 끊기지 않습니다. 이미지는 SVN 원본의 가져오기 설정을 바꾸지 않도록 모두 RawImage와 Texture2D로 씁니다.
/// 같은 씬에 다시 실행해도 결과가 같습니다.
/// </remarks>
public static class MissionOverlayBuilder
{
    private const string FontMedium = "Assets/3.Resources/Fonts/GmarketSansTTFMedium SDF.asset";
    private const string FontBold = "Assets/3.Resources/Fonts/GmarketSansTTFBold SDF.asset";
    private const string FailVideo = "Assets/3.Resources/UI/Misson/Mission_fail.mp4";
    private const string SelectLeft = "Assets/3.Resources/UI/Main_Start/Select_Point_L.png";
    private const string SelectRight = "Assets/3.Resources/UI/Main_Start/Select_Point_R.png";
    private const string CompleteBackground = "Assets/3.Resources/UI/Misson/Mission_complete.png";
    private const string CompleteTitle = "Assets/3.Resources/UI/Misson/Mission_complete_title.png";
    private const string Arrow = "Assets/3.Resources/UI/Misson/Arrow_mark.png";
    private const string PortraitRoot = "Assets/3.Resources/UI/Combat/260923/Character_Portrait/";

    /// <summary>오버레이 정렬 순서입니다. HUD와 조작법 안내(500)보다 위, ESC 메뉴(10000)보다 아래입니다.</summary>
    private const int OverlaySortingOrder = 1000;

    [MenuItem("GrayZone/UI/미션 결과 오버레이 다시 만들기")]
    public static void RebuildInActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        List<string> log = new List<string>();
        int built = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (GameOverUIController gameOver in root.GetComponentsInChildren<GameOverUIController>(true))
            {
                BuildGameOver(gameOver, log);
                built++;
            }

            foreach (ResultUIController result in root.GetComponentsInChildren<ResultUIController>(true))
            {
                BuildResult(result, log);
                built++;
            }
        }

        if (built > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }

        Debug.Log($"[MissionOverlayBuilder] {scene.name}: 오버레이 {built}개를 다시 만들었습니다.\n" + string.Join("\n", log));
    }

    // ─────────────────────────────────────────────────────────────
    // 작전 실패
    // ─────────────────────────────────────────────────────────────

    private static void BuildGameOver(GameOverUIController controller, List<string> log)
    {
        RectTransform root = PrepareOverlayRoot(controller.gameObject);
        ClearChildren(root);

        // 배경 영상. 제목과 "작전 중단. 퇴각합니다." 문구는 영상(배경)에 들어 있습니다.
        // 화면 비율이 16:9가 아니어도 빈틈이 없도록 화면 전체를 덮게 늘립니다.
        CreateBackdrop(root);
        RawImage background = CreateFullScreenBackground(root, "Background", null, 16f / 9f);
        VideoPlayer video = background.gameObject.AddComponent<VideoPlayer>();
        video.clip = AssetDatabase.LoadAssetAtPath<VideoClip>(FailVideo);
        video.playOnAwake = false;
        video.isLooping = false;
        video.audioOutputMode = VideoAudioOutputMode.None;
        video.renderMode = VideoRenderMode.RenderTexture;
        video.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;

        RectTransform frame = CreateFrame(root);
        RectTransform menu = CreateRect(frame, "Menu", 0, 0, 1920, 1080);
        Button retry = CreateTextButton(menu, "RetryFromSaveButton", "마지막 저장 지점 부터", FontMedium, 40, 775, 758, 380, 46);
        Button title = CreateTextButton(menu, "TitleButton", "타이틀로", FontMedium, 40, 888, 836, 154, 46);

        // 타이틀 화면과 같은 선택 표시. Figma의 Select_Menu(813,847,303x24)가 "타이틀로"(888~1042) 양옆에 오도록 간격을 맞춥니다.
        Texture2D left = AssetDatabase.LoadAssetAtPath<Texture2D>(SelectLeft);
        Texture2D right = AssetDatabase.LoadAssetAtPath<Texture2D>(SelectRight);
        float gap = left != null ? Mathf.Max(0f, 888f - 813f - left.width) : 12f;
        MenuSelectionIndicator indicator = Undo.AddComponent<MenuSelectionIndicator>(menu.gameObject);
        SerializedObject indicatorSo = new SerializedObject(indicator);
        indicatorSo.FindProperty("m_leftTexture").objectReferenceValue = left;
        indicatorSo.FindProperty("m_rightTexture").objectReferenceValue = right;
        SerializedProperty buttons = indicatorSo.FindProperty("m_buttons");
        buttons.arraySize = 2;
        buttons.GetArrayElementAtIndex(0).objectReferenceValue = retry;
        buttons.GetArrayElementAtIndex(1).objectReferenceValue = title;
        indicatorSo.FindProperty("m_textGap").floatValue = gap;
        indicatorSo.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject so = new SerializedObject(controller);
        so.FindProperty("m_backgroundVideo").objectReferenceValue = video;
        so.FindProperty("m_backgroundImage").objectReferenceValue = background;
        so.FindProperty("m_retryFromSaveButton").objectReferenceValue = retry;
        so.FindProperty("m_titleButton").objectReferenceValue = title;
        so.FindProperty("m_selectionIndicator").objectReferenceValue = indicator;
        so.FindProperty("m_messageText").objectReferenceValue = null;
        so.ApplyModifiedPropertiesWithoutUndo();

        controller.gameObject.SetActive(false);
        log.Add($"GameOver UI '{controller.name}': 영상={(video.clip != null)}, 선택 장식 간격={gap:0.#}");
    }

    // ─────────────────────────────────────────────────────────────
    // 임무 완료
    // ─────────────────────────────────────────────────────────────

    private static void BuildResult(ResultUIController controller, List<string> log)
    {
        RectTransform root = PrepareOverlayRoot(controller.gameObject);
        ClearChildren(root);

        // 이 배경에는 구획선·노란 막대·칸 테두리가 그려져 있어 글자와 위치가 맞아야 합니다.
        // 그래서 화면 전체로 늘리지 않고 1920x1080 틀 안에 고정합니다. 틀 밖은 검은 바탕입니다.
        CreateBackdrop(root);
        RectTransform frame = CreateFrame(root);
        CreateRawImage(frame, "Background", AssetDatabase.LoadAssetAtPath<Texture2D>(CompleteBackground), 0, 0, 1920, 1081);
        CreateRawImage(frame, "Title", AssetDatabase.LoadAssetAtPath<Texture2D>(CompleteTitle), 43, 26, 734, 291);
        CreateText(frame, "Subtitle", "쉘터로 복귀합니다", FontMedium, 45, 88, 277, 359, 52, TextAlignmentOptions.Left);

        CreateText(frame, "KillHeader", "처치한 적 수", FontBold, 33, 170, 409, 178, 38, TextAlignmentOptions.Left);
        TextMeshProUGUI killCount = CreateText(frame, "KillCount", "0", FontBold, 90, 170, 472, 400, 104, TextAlignmentOptions.Left);

        CreateText(frame, "ResourceHeader", "획득 자원", FontBold, 33, 170, 634, 137, 38, TextAlignmentOptions.Left);

        // 자원 칸은 배경 이미지의 네 칸(가로 165~835, 세로 713~828)에 맞춥니다.
        float[] slotX = { 165f, 343f, 515f, 679f };
        float[] slotW = { 178f, 172f, 164f, 156f };
        ResultUIController.ResourceSlot[] slots = new ResultUIController.ResourceSlot[slotX.Length];
        for (int i = 0; i < slotX.Length; i++)
        {
            RectTransform slot = CreateRect(frame, $"ResourceSlot{i + 1}", slotX[i], 713, slotW[i], 115);
            slots[i].Icon = CreateRawImage(slot, "Icon", null, 18, 25, 64, 64);
            slots[i].Count = CreateText(slot, "Count", string.Empty, FontBold, 35, slotW[i] - 90, 53, 70, 40, TextAlignmentOptions.Right);
        }

        CreateText(frame, "CharacterHeader", "캐릭터 상태", FontBold, 33, 1075, 409, 168, 38, TextAlignmentOptions.Left);

        // 칸 순서와 위치는 Figma(나린 1091, 청솔 1306, 서하 1524)를 따릅니다.
        (PlayerbleCharacterId id, string name, float x, string normal, string injured)[] columns =
        {
            (PlayerbleCharacterId.Narin, "나린", 1091f, "Basic/Narin_HUD_Portrait_v03_transparent.png", "Injured/Narin_Injury_Noise.png"),
            (PlayerbleCharacterId.Cheongsol, "청솔", 1306f, "Basic/Cheongsol_HUD_Portrait_v01_transparent.png", "Injured/Chungsol_Injury_Noise.png"),
            (PlayerbleCharacterId.Seoha, "서하", 1524f, "Basic/Seoha_HUD_Portrait_v01_transparent.png", "Injured/Seoha_Injury_Noise.png"),
        };

        ResultUIController.CharacterColumn[] characterColumns = new ResultUIController.CharacterColumn[columns.Length];
        for (int i = 0; i < columns.Length; i++)
        {
            var c = columns[i];
            RectTransform column = CreateRect(frame, $"Character_{c.id}", c.x, 499, 206, 330);
            characterColumns[i].CharacterId = c.id;
            characterColumns[i].Root = column.gameObject;
            characterColumns[i].NormalPortrait = AssetDatabase.LoadAssetAtPath<Texture2D>(PortraitRoot + c.normal);
            characterColumns[i].InjuredPortrait = AssetDatabase.LoadAssetAtPath<Texture2D>(PortraitRoot + c.injured);
            characterColumns[i].Portrait = CreateRawImage(column, "Portrait", characterColumns[i].NormalPortrait, 0, 0, 206, 206);
            characterColumns[i].NameText = CreateText(column, "Name", c.name, FontBold, 33, 0, 220, 206, 38, TextAlignmentOptions.Center);
            characterColumns[i].StateText = CreateText(column, "State", "정상", FontMedium, 30, 0, 280, 206, 35, TextAlignmentOptions.Center);
        }

        Button returnButton = CreateTextButton(frame, "ReturnButton", "쉘터로 복귀", FontBold, 45, 1456, 919, 274, 52);
        TextMeshProUGUI returnLabel = returnButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (returnLabel != null)
        {
            returnLabel.alignment = TextAlignmentOptions.Left;
        }
        CreateRawImage((RectTransform)returnButton.transform, "Arrow", AssetDatabase.LoadAssetAtPath<Texture2D>(Arrow), 230, 5, 44, 42);

        SerializedObject so = new SerializedObject(controller);
        so.FindProperty("m_killCountText").objectReferenceValue = killCount;
        so.FindProperty("m_returnButton").objectReferenceValue = returnButton;
        SetResourceSlots(so.FindProperty("m_resourceSlots"), slots);
        SetCharacterColumns(so.FindProperty("m_characterColumns"), characterColumns);
        so.ApplyModifiedPropertiesWithoutUndo();

        controller.gameObject.SetActive(false);
        log.Add($"Result UI '{controller.name}': 자원 칸 {slots.Length}, 캐릭터 칸 {characterColumns.Length}");
    }

    private static void SetResourceSlots(SerializedProperty property, ResultUIController.ResourceSlot[] slots)
    {
        property.arraySize = slots.Length;
        for (int i = 0; i < slots.Length; i++)
        {
            SerializedProperty element = property.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("Icon").objectReferenceValue = slots[i].Icon;
            element.FindPropertyRelative("Count").objectReferenceValue = slots[i].Count;
        }
    }

    private static void SetCharacterColumns(SerializedProperty property, ResultUIController.CharacterColumn[] columns)
    {
        property.arraySize = columns.Length;
        for (int i = 0; i < columns.Length; i++)
        {
            SerializedProperty element = property.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("CharacterId").enumValueIndex = IndexOfEnum(columns[i].CharacterId);
            element.FindPropertyRelative("Root").objectReferenceValue = columns[i].Root;
            element.FindPropertyRelative("Portrait").objectReferenceValue = columns[i].Portrait;
            element.FindPropertyRelative("NameText").objectReferenceValue = columns[i].NameText;
            element.FindPropertyRelative("StateText").objectReferenceValue = columns[i].StateText;
            element.FindPropertyRelative("NormalPortrait").objectReferenceValue = columns[i].NormalPortrait;
            element.FindPropertyRelative("InjuredPortrait").objectReferenceValue = columns[i].InjuredPortrait;
        }
    }

    private static int IndexOfEnum(PlayerbleCharacterId value)
    {
        return System.Array.IndexOf(System.Enum.GetValues(typeof(PlayerbleCharacterId)), value);
    }

    // ─────────────────────────────────────────────────────────────
    // 공통 생성 도우미
    // ─────────────────────────────────────────────────────────────

    /// <summary>오버레이 루트를 화면 전체를 덮는 독립 캔버스로 맞춥니다. HUD보다 위에 그립니다.</summary>
    private static RectTransform PrepareOverlayRoot(GameObject overlay)
    {
        RectTransform rect = overlay.GetComponent<RectTransform>();
        if (rect == null)
        {
            rect = overlay.AddComponent<RectTransform>();
        }

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;

        Canvas canvas = overlay.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = overlay.AddComponent<Canvas>();
        }

        // 비활성 오브젝트에서는 overrideSorting 프로퍼티 대입이 저장되지 않아 직렬화 값으로 직접 씁니다.
        SerializedObject canvasSo = new SerializedObject(canvas);
        canvasSo.FindProperty("m_OverrideSorting").boolValue = true;
        canvasSo.FindProperty("m_SortingOrder").intValue = OverlaySortingOrder;
        canvasSo.ApplyModifiedPropertiesWithoutUndo();

        if (overlay.GetComponent<GraphicRaycaster>() == null)
        {
            overlay.AddComponent<GraphicRaycaster>();
        }

        return rect;
    }

    /// <summary>1920x1080 기준 프레임을 화면 가운데에 두고, 화면 높이에 맞춰 크기를 조정합니다.</summary>
    /// <remarks>상위 캔버스가 1920x1080 기준으로 높이 맞춤이라, 프레임을 가운데 고정하면 Figma 좌표를 그대로 쓸 수 있습니다.</remarks>
    private static RectTransform CreateFrame(RectTransform parent)
    {
        RectTransform frame = new GameObject("Frame", typeof(RectTransform)).GetComponent<RectTransform>();
        Undo.RegisterCreatedObjectUndo(frame.gameObject, "Create Frame");
        frame.SetParent(parent, false);
        frame.anchorMin = new Vector2(0.5f, 0.5f);
        frame.anchorMax = new Vector2(0.5f, 0.5f);
        frame.pivot = new Vector2(0.5f, 0.5f);
        frame.sizeDelta = new Vector2(1920f, 1080f);
        frame.anchoredPosition = Vector2.zero;
        return frame;
    }

    /// <summary>화면 전체를 덮는 검은 바탕입니다. 배경이 늦게 뜨거나 비는 순간 뒤의 게임 화면이 비치지 않게 합니다.</summary>
    private static void CreateBackdrop(RectTransform parent)
    {
        RectTransform rect = new GameObject("Backdrop", typeof(RectTransform)).GetComponent<RectTransform>();
        Undo.RegisterCreatedObjectUndo(rect.gameObject, "Create Backdrop");
        rect.SetParent(parent, false);
        Stretch(rect);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = true;
    }

    /// <summary>비율을 지키면서 화면 전체를 덮도록 늘린 배경입니다. 남는 부분은 잘립니다.</summary>
    private static RawImage CreateFullScreenBackground(RectTransform parent, string name, Texture texture, float aspect)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        Undo.RegisterCreatedObjectUndo(rect.gameObject, "Create " + name);
        rect.SetParent(parent, false);
        Stretch(rect);

        RawImage image = rect.gameObject.AddComponent<RawImage>();
        image.texture = texture;
        image.color = Color.white;
        image.raycastTarget = false;

        AspectRatioFitter fitter = rect.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = aspect;
        return image;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static RectTransform CreateRect(RectTransform parent, string name, float x, float y, float width, float height)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        Undo.RegisterCreatedObjectUndo(rect.gameObject, "Create " + name);
        rect.SetParent(parent, false);
        PlaceTopLeft(rect, x, y, width, height);
        return rect;
    }

    private static void PlaceTopLeft(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, -y);
    }

    private static RawImage CreateRawImage(RectTransform parent, string name, Texture texture, float x, float y, float width, float height)
    {
        RectTransform rect = CreateRect(parent, name, x, y, width, height);
        RawImage image = rect.gameObject.AddComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI CreateText(
        RectTransform parent,
        string name,
        string text,
        string fontPath,
        float size,
        float x,
        float y,
        float width,
        float height,
        TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateRect(parent, name, x, y, width, height);
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
        label.text = text;
        label.fontSize = size;
        label.color = Color.white;
        label.alignment = alignment;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        return label;
    }

    /// <summary>글자만 보이는 버튼을 만듭니다. 누를 수 있는 영역은 글자 영역 전체입니다.</summary>
    private static Button CreateTextButton(
        RectTransform parent,
        string name,
        string text,
        string fontPath,
        float size,
        float x,
        float y,
        float width,
        float height)
    {
        RectTransform rect = CreateRect(parent, name, x, y, width, height);

        // 버튼이 클릭을 받으려면 레이캐스트 대상 그래픽이 필요합니다. 투명 이미지로 둡니다.
        Image hitArea = rect.gameObject.AddComponent<Image>();
        hitArea.color = new Color(1f, 1f, 1f, 0f);

        Button button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hitArea;

        TextMeshProUGUI label = CreateText(rect, "Label", text, fontPath, size, 0, 0, width, height, TextAlignmentOptions.Center);
        label.raycastTarget = false;
        return button;
    }

    private static void ClearChildren(RectTransform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);
        }
    }
}
