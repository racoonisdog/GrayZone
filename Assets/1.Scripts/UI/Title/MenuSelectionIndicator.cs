using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 세로 메뉴에서 마우스를 올리거나 선택한 버튼의 글자 양옆에 좌우 장식을 띄우는 공용 선택 표시입니다.
/// </summary>
/// <remarks>
/// 타이틀 화면의 선택 방식을 미션 실패 같은 다른 메뉴에서도 똑같이 쓰려고 <see cref="TitleScreenController"/>에서 떼어 냈습니다.
/// 씬에 직접 붙여 인스펙터 값으로 쓰거나, 실행 중에 붙인 뒤 <see cref="Configure"/>로 값을 넘겨 씁니다.
///
/// 버튼들은 같은 부모 아래에 있고 좌상단 앵커(0,1)·좌상단 피벗을 쓴다고 가정합니다. 장식은 그 부모를 꽉 채우는
/// 루트 아래에 만들어지며, 버튼의 세로 중심과 글자 실제 폭을 기준으로 놓입니다.
/// 페이드는 <see cref="Time.unscaledDeltaTime"/>으로 진행하므로 시간이 멈춘 오버레이에서도 동작합니다.
/// </remarks>
public sealed class MenuSelectionIndicator : MonoBehaviour
{
    [Tooltip("메뉴 글자 왼쪽에 표시할 선택 장식 텍스처입니다.")]
    [SerializeField] private Texture2D m_leftTexture;

    [Tooltip("메뉴 글자 오른쪽에 표시할 선택 장식 텍스처입니다.")]
    [SerializeField] private Texture2D m_rightTexture;

    [Tooltip("선택 표시를 붙일 세로 메뉴 버튼들입니다. 같은 부모 아래에 있어야 합니다.")]
    [SerializeField] private Button[] m_buttons;

    [Tooltip("메뉴 글자와 좌우 선택 장식 사이에 유지할 동일한 간격입니다.")]
    [SerializeField, Min(0f)] private float m_textGap = 12f;

    [Tooltip("선택 장식이 나타나고 사라지는 시간입니다. 시간 정지와 무관하게 실제 시간으로 흐릅니다.")]
    [SerializeField, Min(0f)] private float m_fadeDuration = 0.15f;

    private RectTransform m_root;
    private RawImage m_leftImage;
    private RawImage m_rightImage;
    private CanvasGroup m_canvasGroup;
    private Coroutine m_fadeRoutine;
    private Button m_pointerButton;
    private Button m_selectedButton;

    private void Awake()
    {
        Build();
    }

    private void Update()
    {
        RefreshPointerButtonFromMouse();
    }

    /// <summary>
    /// 실행 중에 붙였을 때 장식 텍스처와 버튼을 넘겨 선택 표시를 만듭니다.
    /// </summary>
    public void Configure(Texture2D left, Texture2D right, Button[] buttons, float textGap, float fadeDuration)
    {
        m_leftTexture = left;
        m_rightTexture = right;
        m_buttons = buttons;
        m_textGap = Mathf.Max(0f, textGap);
        m_fadeDuration = Mathf.Max(0f, fadeDuration);
        Build();
    }

    /// <summary>장식을 즉시 숨기고 선택 상태를 비웁니다. 메뉴를 다시 열 때 이전 선택이 남지 않게 합니다.</summary>
    public void ResetSelection()
    {
        m_pointerButton = null;
        m_selectedButton = null;

        if (m_fadeRoutine != null)
        {
            StopCoroutine(m_fadeRoutine);
            m_fadeRoutine = null;
        }

        if (m_canvasGroup != null)
        {
            m_canvasGroup.alpha = 0f;
        }
    }

    private void Build()
    {
        if (m_root != null
            || m_leftTexture == null
            || m_rightTexture == null
            || m_buttons == null
            || m_buttons.Length == 0)
        {
            return;
        }

        Button firstButton = null;
        for (int i = 0; i < m_buttons.Length; i++)
        {
            if (m_buttons[i] != null)
            {
                firstButton = m_buttons[i];
                break;
            }
        }

        if (firstButton == null)
        {
            return;
        }

        GameObject indicatorObject = new("Menu Selection", typeof(RectTransform), typeof(CanvasGroup));
        indicatorObject.transform.SetParent(firstButton.transform.parent, false);
        indicatorObject.transform.SetAsLastSibling();

        RectTransform indicatorRect = (RectTransform)indicatorObject.transform;
        indicatorRect.anchorMin = Vector2.zero;
        indicatorRect.anchorMax = Vector2.one;
        indicatorRect.offsetMin = Vector2.zero;
        indicatorRect.offsetMax = Vector2.zero;
        m_root = indicatorRect;

        m_leftImage = CreateImage("Left Selection", m_leftTexture);
        m_rightImage = CreateImage("Right Selection", m_rightTexture);

        m_canvasGroup = indicatorObject.GetComponent<CanvasGroup>();
        m_canvasGroup.alpha = 0f;
        m_canvasGroup.interactable = false;
        m_canvasGroup.blocksRaycasts = false;

        for (int i = 0; i < m_buttons.Length; i++)
        {
            Button button = m_buttons[i];
            if (button == null)
            {
                continue;
            }

            AddSelectionEvent(button, EventTriggerType.PointerEnter, _ => SetPointerButton(button));
            AddSelectionEvent(button, EventTriggerType.PointerExit, _ => ClearPointerButton(button));
            AddSelectionEvent(button, EventTriggerType.Select, _ => SetSelectedButton(button));
            AddSelectionEvent(button, EventTriggerType.Deselect, _ => ClearSelectedButton(button));
        }
    }

    private void SetPointerButton(Button button)
    {
        if (m_pointerButton == button)
        {
            return;
        }

        m_pointerButton = button;
        Refresh();
    }

    private void ClearPointerButton(Button button)
    {
        if (m_pointerButton == button)
        {
            m_pointerButton = null;
        }

        Refresh();
    }

    private void RefreshPointerButtonFromMouse()
    {
        if (Mouse.current == null || m_buttons == null || m_root == null)
        {
            return;
        }

        Vector2 pointerPosition = Mouse.current.position.ReadValue();
        Button hoveredButton = null;
        for (int i = m_buttons.Length - 1; i >= 0; i--)
        {
            Button button = m_buttons[i];
            if (button == null
                || !button.gameObject.activeInHierarchy
                || !button.IsInteractable()
                || button.transform is not RectTransform buttonRect
                || !RectTransformUtility.RectangleContainsScreenPoint(buttonRect, pointerPosition))
            {
                continue;
            }

            hoveredButton = button;
            break;
        }

        if (m_pointerButton == hoveredButton)
        {
            return;
        }

        m_pointerButton = hoveredButton;
        Refresh();
    }

    private void SetSelectedButton(Button button)
    {
        m_selectedButton = button;
        Refresh();
    }

    private void ClearSelectedButton(Button button)
    {
        if (m_selectedButton == button)
        {
            m_selectedButton = null;
        }

        Refresh();
    }

    private void Refresh()
    {
        Button target = m_pointerButton != null ? m_pointerButton : m_selectedButton;
        if (target == null)
        {
            Fade(0f);
            return;
        }

        Show(target);
    }

    private void Show(Button button)
    {
        if (m_root == null || m_leftImage == null || m_rightImage == null || m_canvasGroup == null || button == null)
        {
            return;
        }

        RectTransform buttonRect = button.transform as RectTransform;
        if (buttonRect == null)
        {
            return;
        }

        GetLabelHorizontalBounds(button, out float textLeft, out float textRight);
        float leftCapWidth = m_leftImage.rectTransform.rect.width;
        float verticalCenter = buttonRect.anchoredPosition.y - (buttonRect.rect.height * 0.5f);

        m_leftImage.rectTransform.anchoredPosition = new Vector2(textLeft - m_textGap - leftCapWidth, verticalCenter);
        m_rightImage.rectTransform.anchoredPosition = new Vector2(textRight + m_textGap, verticalCenter);
        Fade(1f);
    }

    private void Fade(float targetAlpha)
    {
        if (m_canvasGroup == null)
        {
            return;
        }

        if (m_fadeRoutine != null)
        {
            StopCoroutine(m_fadeRoutine);
        }

        if (m_fadeDuration <= 0f || !isActiveAndEnabled)
        {
            m_canvasGroup.alpha = targetAlpha;
            m_fadeRoutine = null;
            return;
        }

        m_fadeRoutine = StartCoroutine(FadeRoutine(targetAlpha));
    }

    private IEnumerator FadeRoutine(float targetAlpha)
    {
        float startAlpha = m_canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < m_fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / m_fadeDuration);
            float eased = normalized * normalized * (3f - (2f * normalized));
            m_canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, eased);
            yield return null;
        }

        m_canvasGroup.alpha = targetAlpha;
        m_fadeRoutine = null;
    }

    private RawImage CreateImage(string name, Texture2D texture)
    {
        GameObject capObject = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        capObject.transform.SetParent(m_root, false);

        RectTransform capRect = (RectTransform)capObject.transform;
        capRect.anchorMin = new Vector2(0f, 1f);
        capRect.anchorMax = new Vector2(0f, 1f);
        capRect.pivot = new Vector2(0f, 0.5f);
        capRect.sizeDelta = new Vector2(texture.width, texture.height);

        RawImage capImage = capObject.GetComponent<RawImage>();
        capImage.texture = texture;
        capImage.color = Color.white;
        capImage.raycastTarget = false;
        return capImage;
    }

    private void GetLabelHorizontalBounds(Button button, out float left, out float right)
    {
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label == null)
        {
            left = button.transform is RectTransform buttonRect ? buttonRect.anchoredPosition.x : 0f;
            right = left;
            return;
        }

        label.ForceMeshUpdate();
        RectTransform labelRect = label.rectTransform;
        float logicalLeft = labelRect.rect.xMin + label.margin.x;
        float logicalRight = logicalLeft + label.preferredWidth;
        Vector3 start = m_root.InverseTransformPoint(labelRect.TransformPoint(new Vector3(logicalLeft, 0f, 0f)));
        Vector3 end = m_root.InverseTransformPoint(labelRect.TransformPoint(new Vector3(logicalRight, 0f, 0f)));

        // 선택 파츠는 루트의 좌상단 앵커를 사용하므로, 중심 피벗 기준 로컬 좌표를
        // 루트 Rect의 왼쪽 끝에서 시작하는 anchoredPosition 좌표로 변환합니다.
        float leftOrigin = m_root.rect.xMin;
        left = Mathf.Min(start.x, end.x) - leftOrigin;
        right = Mathf.Max(start.x, end.x) - leftOrigin;
    }

    private static void AddSelectionEvent(
        Button button,
        EventTriggerType eventType,
        UnityEngine.Events.UnityAction<BaseEventData> callback)
    {
        EventTrigger trigger = button.GetComponent<EventTrigger>();
        if (trigger == null)
        {
            trigger = button.gameObject.AddComponent<EventTrigger>();
        }

        trigger.triggers ??= new System.Collections.Generic.List<EventTrigger.Entry>();
        EventTrigger.Entry entry = new() { eventID = eventType, callback = new EventTrigger.TriggerEvent() };
        entry.callback.AddListener(callback);
        trigger.triggers.Add(entry);
    }
}
