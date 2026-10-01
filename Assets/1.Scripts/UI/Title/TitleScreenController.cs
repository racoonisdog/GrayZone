using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 타이틀 화면의 반복 배경 영상을 준비하고 재생합니다.
/// </summary>
/// <remarks>
/// 타이틀 로고와 메뉴 버튼의 표시 애니메이션은 각 오브젝트에 부착된
/// <see cref="CanvasGroupFader"/>가 독립적으로 담당합니다.
/// </remarks>
public sealed class TitleScreenController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("검은 배경 대신 반복 재생할 타이틀 배경 VideoPlayer입니다.")]
    [SerializeField] private VideoPlayer m_backgroundVideo;

    [Tooltip("VideoPlayer의 RenderTexture를 전체 화면에 표시할 RawImage입니다.")]
    [SerializeField] private RawImage m_backgroundImage;

    [Header("Title Prototype 1")]
    [Tooltip("메뉴 글자 왼쪽에 표시할 선택 장식 텍스처입니다.")]
    [SerializeField] private Texture2D m_menuSelectionLeftTexture;

    [Tooltip("메뉴 글자 오른쪽에 표시할 선택 장식 텍스처입니다.")]
    [SerializeField] private Texture2D m_menuSelectionRightTexture;

    [Tooltip("Title Prototype 1의 세로 메뉴 버튼들입니다.")]
    [SerializeField] private Button[] m_menuButtons;

    [Tooltip("메뉴 글자와 좌우 선택 장식 사이에 유지할 동일한 간격입니다.")]
    [SerializeField, Min(0f)] private float m_menuSelectionTextGap = 12f;

    [Tooltip("선택 장식이 나타나고 사라지는 시간입니다.")]
    [SerializeField, Min(0f)] private float m_menuSelectionFadeDuration = 0.15f;

    private RenderTexture m_backgroundRenderTexture;
    private RectTransform m_menuSelectionRoot;
    private RawImage m_menuSelectionLeftImage;
    private RawImage m_menuSelectionRightImage;
    private CanvasGroup m_menuSelectionCanvasGroup;
    private Coroutine m_menuSelectionFadeRoutine;
    private Button m_pointerMenuButton;
    private Button m_selectedMenuButton;

    private void Awake()
    {
        ConfigureBackgroundVideo();
        ConfigureMenuSelection();
    }

    private void OnEnable()
    {
        PlayBackgroundVideo();
    }

    private void Update()
    {
        RefreshPointerMenuButtonFromMouse();
    }

    private void OnDestroy()
    {
        ReleaseBackgroundTexture();
    }

    /// <summary>배경 영상을 처음부터 반복 재생합니다.</summary>
    public void PlayBackgroundVideo()
    {
        if (m_backgroundVideo == null || m_backgroundVideo.clip == null)
        {
            Debug.LogWarning("[TitleScreen] 배경 VideoPlayer 또는 VideoClip이 연결되지 않았습니다.", this);
            return;
        }

        if (!m_backgroundVideo.isPlaying)
        {
            m_backgroundVideo.Play();
        }
    }

    private void ConfigureBackgroundVideo()
    {
        if (m_backgroundVideo == null || m_backgroundImage == null)
        {
            Debug.LogWarning("[TitleScreen] 배경 VideoPlayer 또는 RawImage가 연결되지 않았습니다.", this);
            return;
        }

        int width = m_backgroundVideo.clip != null ? Mathf.Max(16, (int)m_backgroundVideo.clip.width) : 1920;
        int height = m_backgroundVideo.clip != null ? Mathf.Max(16, (int)m_backgroundVideo.clip.height) : 1080;

        m_backgroundRenderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
        {
            name = "Title Background Video",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false
        };
        m_backgroundRenderTexture.Create();

        m_backgroundVideo.playOnAwake = true;
        m_backgroundVideo.isLooping = true;
        m_backgroundVideo.waitForFirstFrame = true;
        m_backgroundVideo.skipOnDrop = true;
        m_backgroundVideo.audioOutputMode = VideoAudioOutputMode.None;
        m_backgroundVideo.renderMode = VideoRenderMode.RenderTexture;
        m_backgroundVideo.targetTexture = m_backgroundRenderTexture;
        m_backgroundImage.texture = m_backgroundRenderTexture;
    }

    private void ReleaseBackgroundTexture()
    {
        if (m_backgroundRenderTexture == null)
        {
            return;
        }

        if (m_backgroundVideo != null && m_backgroundVideo.targetTexture == m_backgroundRenderTexture)
        {
            m_backgroundVideo.targetTexture = null;
        }

        if (m_backgroundImage != null && m_backgroundImage.texture == m_backgroundRenderTexture)
        {
            m_backgroundImage.texture = null;
        }

        m_backgroundRenderTexture.Release();
        Destroy(m_backgroundRenderTexture);
        m_backgroundRenderTexture = null;
    }

    private void ConfigureMenuSelection()
    {
        if (m_menuSelectionLeftTexture == null
            || m_menuSelectionRightTexture == null
            || m_menuButtons == null
            || m_menuButtons.Length == 0)
        {
            return;
        }

        Button firstButton = null;
        for (int i = 0; i < m_menuButtons.Length; i++)
        {
            if (m_menuButtons[i] != null)
            {
                firstButton = m_menuButtons[i];
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
        m_menuSelectionRoot = indicatorRect;

        m_menuSelectionLeftImage = CreateMenuSelectionImage("Left Selection", m_menuSelectionLeftTexture);
        m_menuSelectionRightImage = CreateMenuSelectionImage("Right Selection", m_menuSelectionRightTexture);

        m_menuSelectionCanvasGroup = indicatorObject.GetComponent<CanvasGroup>();
        m_menuSelectionCanvasGroup.alpha = 0f;
        m_menuSelectionCanvasGroup.interactable = false;
        m_menuSelectionCanvasGroup.blocksRaycasts = false;

        for (int i = 0; i < m_menuButtons.Length; i++)
        {
            Button button = m_menuButtons[i];
            if (button == null)
            {
                continue;
            }

            AddSelectionEvent(button, EventTriggerType.PointerEnter, _ => SetPointerMenuButton(button));
            AddSelectionEvent(button, EventTriggerType.PointerExit, _ => ClearPointerMenuButton(button));
            AddSelectionEvent(button, EventTriggerType.Select, _ => SetSelectedMenuButton(button));
            AddSelectionEvent(button, EventTriggerType.Deselect, _ => ClearSelectedMenuButton(button));
        }
    }

    private void SetPointerMenuButton(Button button)
    {
        if (m_pointerMenuButton == button)
        {
            return;
        }

        m_pointerMenuButton = button;
        RefreshMenuSelection();
    }

    private void ClearPointerMenuButton(Button button)
    {
        if (m_pointerMenuButton == button)
        {
            m_pointerMenuButton = null;
        }

        RefreshMenuSelection();
    }

    private void RefreshPointerMenuButtonFromMouse()
    {
        if (Mouse.current == null || m_menuButtons == null)
        {
            return;
        }

        Vector2 pointerPosition = Mouse.current.position.ReadValue();
        Button hoveredButton = null;
        for (int i = m_menuButtons.Length - 1; i >= 0; i--)
        {
            Button button = m_menuButtons[i];
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

        if (m_pointerMenuButton == hoveredButton)
        {
            return;
        }

        m_pointerMenuButton = hoveredButton;
        RefreshMenuSelection();
    }

    private void SetSelectedMenuButton(Button button)
    {
        m_selectedMenuButton = button;
        RefreshMenuSelection();
    }

    private void ClearSelectedMenuButton(Button button)
    {
        if (m_selectedMenuButton == button)
        {
            m_selectedMenuButton = null;
        }

        RefreshMenuSelection();
    }

    private void RefreshMenuSelection()
    {
        Button target = m_pointerMenuButton != null ? m_pointerMenuButton : m_selectedMenuButton;
        if (target == null)
        {
            FadeMenuSelection(0f);
            return;
        }

        ShowMenuSelection(target);
    }

    private void ShowMenuSelection(Button button)
    {
        if (m_menuSelectionRoot == null
            || m_menuSelectionLeftImage == null
            || m_menuSelectionRightImage == null
            || m_menuSelectionCanvasGroup == null
            || button == null)
        {
            return;
        }

        RectTransform buttonRect = button.transform as RectTransform;
        if (buttonRect == null)
        {
            return;
        }

        GetMenuLabelHorizontalBounds(button, out float textLeft, out float textRight);
        float leftCapWidth = m_menuSelectionLeftImage.rectTransform.rect.width;
        float verticalCenter = buttonRect.anchoredPosition.y - (buttonRect.rect.height * 0.5f);

        m_menuSelectionLeftImage.rectTransform.anchoredPosition = new Vector2(
            textLeft - m_menuSelectionTextGap - leftCapWidth,
            verticalCenter);
        m_menuSelectionRightImage.rectTransform.anchoredPosition = new Vector2(
            textRight + m_menuSelectionTextGap,
            verticalCenter);
        FadeMenuSelection(1f);
    }

    private void FadeMenuSelection(float targetAlpha)
    {
        if (m_menuSelectionCanvasGroup == null)
        {
            return;
        }

        if (m_menuSelectionFadeRoutine != null)
        {
            StopCoroutine(m_menuSelectionFadeRoutine);
        }

        if (m_menuSelectionFadeDuration <= 0f)
        {
            m_menuSelectionCanvasGroup.alpha = targetAlpha;
            m_menuSelectionFadeRoutine = null;
            return;
        }

        m_menuSelectionFadeRoutine = StartCoroutine(FadeMenuSelectionRoutine(targetAlpha));
    }

    private IEnumerator FadeMenuSelectionRoutine(float targetAlpha)
    {
        float startAlpha = m_menuSelectionCanvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < m_menuSelectionFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / m_menuSelectionFadeDuration);
            float eased = normalized * normalized * (3f - (2f * normalized));
            m_menuSelectionCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, eased);
            yield return null;
        }

        m_menuSelectionCanvasGroup.alpha = targetAlpha;
        m_menuSelectionFadeRoutine = null;
    }

    private RawImage CreateMenuSelectionImage(string name, Texture2D texture)
    {
        GameObject capObject = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        capObject.transform.SetParent(m_menuSelectionRoot, false);

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

    private void GetMenuLabelHorizontalBounds(Button button, out float left, out float right)
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
        Vector3 start = m_menuSelectionRoot.InverseTransformPoint(
            labelRect.TransformPoint(new Vector3(logicalLeft, 0f, 0f)));
        Vector3 end = m_menuSelectionRoot.InverseTransformPoint(
            labelRect.TransformPoint(new Vector3(logicalRight, 0f, 0f)));

        // 선택 파츠는 루트의 좌상단 앵커를 사용하므로, 중심 피벗 기준 로컬 좌표를
        // 루트 Rect의 왼쪽 끝에서 시작하는 anchoredPosition 좌표로 변환합니다.
        float leftOrigin = m_menuSelectionRoot.rect.xMin;
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
