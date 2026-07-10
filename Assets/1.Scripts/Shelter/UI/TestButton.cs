using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// NPC 후보 목록에서 초상화와 부상 아이콘을 표시하는 임시 행 버튼
/// </summary>
public class TestButton : MonoBehaviour, INPCListItemView
{
    [Header("Catalogs")]
    [SerializeField] private NpcPortraitCatalog npcCatalog;
    [SerializeField] private NpcInjuryIconCatalog injuryIconCatalog;

    [Header("Images")]
    [SerializeField] private Image portraitImage;
    [SerializeField] private Image injuryImage;
    [SerializeField] private Button button;

    private string definitionId;
    private NPCInjuryState injuryState;
    private Action<string> clicked;

    private void Awake()
    {
        CacheReferences();
    }

    private void Reset()
    {
        CacheReferences();
    }

    /// <summary>
    /// NPC 데이터를 버튼 행에 표시하고 클릭 콜백을 연결
    /// </summary>
    /// <param name="npcData">표시할 NPC 런타임 데이터</param>
    /// <param name="onClicked">클릭 시 NPC 정의 ID를 전달할 콜백</param>
    public void Bind(NPCRuntimeData npcData, Action<string> onClicked)
    {
        CacheReferences();

        if (npcData == null || string.IsNullOrWhiteSpace(npcData.DefinitionId))
        {
            Clear();
            return;
        }

        gameObject.SetActive(true);
        definitionId = npcData.DefinitionId.Trim();
        injuryState = npcData.GetCurrentInjuryState();
        clicked = onClicked;

        Sprite portrait = npcCatalog != null ? npcCatalog.GetPortrait(definitionId) : null;
        Sprite injuryIcon = injuryIconCatalog != null ? injuryIconCatalog.GetIcon(injuryState) : null;

        if (portraitImage != null)
        {
            portraitImage.sprite = portrait;
            portraitImage.enabled = portrait != null;
        }

        if (injuryImage != null)
        {
            injuryImage.sprite = injuryIcon;
            injuryImage.enabled = injuryIcon != null;
        }

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleClick);
            button.interactable = !string.IsNullOrWhiteSpace(definitionId);
        }
    }

    /// <summary>
    /// 표시 중인 NPC 데이터와 버튼 클릭 상태를 비우고 비활성화
    /// </summary>
    public void Clear()
    {
        CacheReferences();

        definitionId = null;
        injuryState = NPCInjuryState.Healthy;
        clicked = null;

        if (portraitImage != null)
        {
            portraitImage.sprite = null;
            portraitImage.enabled = false;
        }

        if (injuryImage != null)
        {
            injuryImage.sprite = null;
            injuryImage.enabled = false;
        }

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.interactable = false;
        }

        gameObject.SetActive(false);
    }

    private void HandleClick()
    {
        if (!string.IsNullOrWhiteSpace(definitionId))
            clicked?.Invoke(definitionId);
    }

    private void CacheReferences()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (portraitImage == null)
            portraitImage = GetComponent<Image>();
    }
}
