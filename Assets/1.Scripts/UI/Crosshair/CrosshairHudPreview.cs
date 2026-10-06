using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>
/// 설정 화면 미리보기입니다. 실제 조준선 HUD를 복제해 RenderTexture에 그리고, 그 텍스처를 <see cref="RawImage"/>에 실제 화면 픽셀 그대로 보여 줍니다.
/// </summary>
/// <remarks>
/// 실제 HUD와 같은 코드(<see cref="CrosshairController"/>)와 같은 UXML로 그리므로 모양·두께·간격이 게임 화면과 같습니다.
/// 텍스처 한 픽셀이 화면 한 픽셀이 되도록 크기를 맞추고, UI 배율도 실제 HUD 패널과 같게 둡니다.
/// 복제본은 다른 코드의 <c>FindFirstObjectByType&lt;CrosshairController&gt;</c>에 잡히지 않도록 설정 화면이 열려 있는 동안만 둡니다.
/// </remarks>
public sealed class CrosshairHudPreview : IDisposable
{
    private readonly CrosshairController m_source;
    private readonly RawImage m_image;
    private GameObject m_holder;
    private CrosshairController m_clone;
    private PanelSettings m_panelSettings;
    private RenderTexture m_texture;

    public CrosshairHudPreview(CrosshairController source, RawImage image)
    {
        m_source = source;
        m_image = image;
    }

    /// <summary>
    /// 복제한 HUD에 <paramref name="apply"/>로 값을 넣고 미리보기를 갱신합니다. 처음 부르거나 화면 크기가 바뀌면 복제본을 다시 맞춥니다.
    /// </summary>
    /// <returns>복제본을 만들 수 없으면 false입니다(실제 HUD가 없는 씬 등).</returns>
    public bool Render(Action<CrosshairController> apply)
    {
        if (m_source == null || m_image == null || !EnsureClone())
        {
            return false;
        }

        m_image.enabled = true;
        apply(m_clone);
        return true;
    }

    /// <summary>미리보기를 비웁니다. 복제본은 남겨 둡니다.</summary>
    public void Clear()
    {
        if (m_image != null)
        {
            m_image.enabled = false;
        }
    }

    public void Dispose()
    {
        if (m_holder != null)
        {
            Object.Destroy(m_holder);
            m_holder = null;
        }

        m_clone = null;
        if (m_panelSettings != null)
        {
            Object.Destroy(m_panelSettings);
            m_panelSettings = null;
        }

        if (m_texture != null)
        {
            m_texture.Release();
            Object.Destroy(m_texture);
            m_texture = null;
        }

        if (m_image != null)
        {
            m_image.texture = null;
        }
    }

    private bool EnsureClone()
    {
        Vector2Int size = ResolveScreenPixelSize();
        if (size.x <= 0 || size.y <= 0)
        {
            return false;
        }

        float scale = ResolveSourcePanelScale();
        if (m_clone != null && m_texture != null && m_texture.width == size.x && m_texture.height == size.y
            && Mathf.Approximately(m_panelSettings.scale, scale))
        {
            return true;
        }

        Dispose();

        m_texture = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32)
        {
            name = "CrosshairHudPreview",
            filterMode = FilterMode.Point,
        };
        m_texture.Create();

        UIDocument sourceDocument = m_source.GetComponent<UIDocument>();
        if (sourceDocument == null || sourceDocument.panelSettings == null)
        {
            return false;
        }

        // 원본 패널은 화면 DPI에 따라 배율이 정해집니다. 텍스처에 그릴 때도 같은 배율이 되도록 지금 원본의 배율을 고정값으로 씁니다.
        m_panelSettings = Object.Instantiate(sourceDocument.panelSettings);
        m_panelSettings.name = "CrosshairHudPreviewPanel";
        m_panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
        m_panelSettings.scale = scale;
        m_panelSettings.targetTexture = m_texture;
        m_panelSettings.clearColor = true;
        m_panelSettings.colorClearValue = Color.clear;

        // 비활성 부모 아래에 복제해 Awake가 돌기 전에 필요 없는 부품을 걷어냅니다.
        m_holder = new GameObject("CrosshairHudPreview");
        m_holder.SetActive(false);
        GameObject copy = Object.Instantiate(m_source.gameObject, m_holder.transform);
        copy.name = "CrosshairHUD (Preview)";

        foreach (MonoBehaviour behaviour in copy.GetComponents<MonoBehaviour>())
        {
            if (behaviour is not CrosshairController && behaviour is not UIDocument)
            {
                Object.DestroyImmediate(behaviour);
            }
        }

        for (int i = copy.transform.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(copy.transform.GetChild(i).gameObject);
        }

        copy.GetComponent<UIDocument>().panelSettings = m_panelSettings;
        m_clone = copy.GetComponent<CrosshairController>();
        m_holder.SetActive(true);

        // 조준선 설정만 보여 줍니다. 탄약 게이지·피격 표시는 설정 대상이 아닙니다.
        m_clone.ShowAmmoGauge = false;
        m_clone.SetVisible(true);

        m_image.texture = m_texture;
        m_image.uvRect = new Rect(0f, 0f, 1f, 1f);
        return true;
    }

    /// <summary>미리보기 이미지가 실제 화면에서 차지하는 픽셀 크기입니다.</summary>
    private Vector2Int ResolveScreenPixelSize()
    {
        RectTransform rect = m_image.rectTransform;
        Canvas canvas = m_image.canvas;
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        return new Vector2Int(Mathf.RoundToInt(Mathf.Abs(max.x - min.x)), Mathf.RoundToInt(Mathf.Abs(max.y - min.y)));
    }

    private float ResolveSourcePanelScale()
    {
        UIDocument document = m_source.GetComponent<UIDocument>();
        IPanel panel = document != null && document.rootVisualElement != null ? document.rootVisualElement.panel : null;
        return panel != null ? panel.scaledPixelsPerPoint : 1f;
    }
}
