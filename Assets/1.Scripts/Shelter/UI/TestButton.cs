using System;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// 캐릭터 후보 목록에서 초상화와 부상 아이콘을 표시하는 임시 행 버튼
/// </summary>
public class TestButton : MonoBehaviour, ICharacterListItemView
{
    [Header("Catalogs")]
    [FormerlySerializedAs("npcCatalog")]
    [SerializeField] private CharacterPortraitCatalog characterCatalog;
    [SerializeField] private CharacterInjuryIconCatalog injuryIconCatalog;

    [Header("Images")]
    [SerializeField] private Image portraitImage;
    [SerializeField] private Image injuryImage;
    [SerializeField] private Button button;

    private string runtimeId;
    private CharacterInjuryState injuryState;
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
    /// 캐릭터 데이터를 버튼 행에 표시하고 클릭 콜백을 연결
    /// </summary>
    /// <param name="character">표시할 셸터 캐릭터 런타임 데이터</param>
    /// <param name="onClicked">클릭 시 캐릭터 런타임 ID를 전달할 콜백</param>
    public void Bind(ShelterMemberRuntimeData character, Action<string> onClicked)
    {
        CacheReferences();

        if (character == null || string.IsNullOrWhiteSpace(character.RuntimeId))
        {
            Clear();
            return;
        }

        gameObject.SetActive(true);
        runtimeId = character.RuntimeId.Trim();
        injuryState = character.InjuryState;
        clicked = onClicked;

        Sprite portrait = characterCatalog != null ? characterCatalog.GetPortrait(character.DefinitionId) : null;
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
            button.interactable = !string.IsNullOrWhiteSpace(runtimeId);
        }
    }

    /// <summary>
    /// 표시 중인 캐릭터 데이터와 버튼 클릭 상태를 비우고 비활성화
    /// </summary>
    public void Clear()
    {
        CacheReferences();

        runtimeId = null;
        injuryState = CharacterInjuryState.Normal;
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
        if (!string.IsNullOrWhiteSpace(runtimeId))
            clicked?.Invoke(runtimeId);
    }

    private void CacheReferences()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (portraitImage == null)
            portraitImage = GetComponent<Image>();
    }
}
