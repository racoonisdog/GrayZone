using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 전투 종료 후 표시되는 결과창(Result UI)입니다.
/// </summary>
/// <remarks>
/// <see cref="ReviveHudController"/>와 동일한 방식입니다: 이 컴포넌트는 아무 UI도 만들지 않습니다.
/// 씬에 미리 배치된 자식(Title/CharacterImage/MissionHeader/ResourceHeader/KillCount/StatusRow1~3/
/// ResourcePanel/SlotContainer/Slot1~5/ReturnButton)을 이름으로 찾아 값만 채웁니다. 탐색은 계층 전체를
/// 재귀적으로 훑으므로(<see cref="FindDeep"/>), StatusRow1~3을 SquadProfile 같은 정리용 상위 그룹 밑에
/// 옮겨도 계속 정상 동작합니다.
/// </remarks>
[DisallowMultipleComponent]
public class ResultUIController : MonoBehaviour
{
    /// <summary>결과창에 표시할 파티원 한 명의 정보입니다.</summary>
    [Serializable]
    public struct CharacterResult
    {
        public string Name;
        public CharacterInjuryState InjuryState;
        public bool IsCombatOut;

        /// <summary>캐릭터 초상화 선택에 사용하는 고정 식별자입니다. 알 수 없으면 <see cref="PlayerbleCharacterId.Unknown"/>입니다.</summary>
        public PlayerbleCharacterId CharacterId;

        /// <summary>PlayerbleUnitData에서 필드 결과로 직접 전달한 초상화입니다.</summary>
        public Sprite Portrait;
    }

    /// <summary>결과창에 표시할 획득 자원 한 종류의 정보입니다.</summary>
    [Serializable]
    public struct ResourceResult
    {
        public Texture2D Icon;
        public int Count;
    }

    [Header("References (비워두면 자식 이름으로 자동 탐색)")]
    [SerializeField] private TextMeshProUGUI m_killCountText;
    [SerializeField] private RectTransform[] m_statusRows;
    [SerializeField] private RectTransform[] m_slots;
    [SerializeField] private Button m_returnButton;
    [SerializeField] private Image m_characterImage;

    [Header("Scene Transition")]
    [Tooltip("'셸터로 복귀' 버튼을 누르면 전환할 씬 이름입니다(Build Settings에 등록되어 있어야 합니다).")]
    [SerializeField] private string m_returnSceneName = "TEst";

    /// <summary>'셸터로 복귀' 버튼을 눌렀을 때 발생합니다.</summary>
    public event Action OnReturnToShelter;

    private static readonly Color s_normalColor = new Color(0.3f, 0.9f, 0.3f);
    private static readonly Color s_injuredColor = new Color(0.95f, 0.65f, 0.15f);
    private static readonly Color s_criticalColor = new Color(0.85f, 0.1f, 0.1f);
    private void Reset()
    {
        AutoFindReferences();
    }

    private void Awake()
    {
        AutoFindReferences();
    }

    /// <summary>
    /// 결과 데이터를 채우고 결과창을 표시합니다.
    /// </summary>
    /// <param name="kills">적 처치 수입니다.</param>
    /// <param name="characters">파티원 상태 목록(StatusRow1~3 순서로 채웁니다. 3명 미만이면 남는 행은 숨깁니다).</param>
    /// <param name="resources">획득 자원 목록(Slot1~5 순서로 채웁니다. 5개 미만이면 남는 슬롯은 비웁니다).</param>
    public void ShowResult(int kills, IList<CharacterResult> characters, IList<ResourceResult> resources)
    {
        SetKills(kills);
        SetCharacters(characters);
        SetCharacterPortrait(characters);
        SetResources(resources);
        gameObject.SetActive(true);
        TestSceneUiEventSystemBridge.EnableForResultUI();

        // TODO(Save integration): 결과 오버레이를 연 직후, 확정된 FieldResultData를
        // GameDataManager에 반영한 뒤 AutoSave()를 요청한다.
        // AutoSave 반환 규약: 1 = 정상 성공, 0 = 정상 실패, -1 = 비정상 실패.
        // 1이 오기 전에는 FieldPhase를 Completed로 바꾸거나 셸터 복귀를 허용하지 않는다.
        // 성공 후 셸터 전환 시 셸터 측은 현재 GameDataManager에서 초기화한다.
        // 이 경로에서 저장 파일을 다시 읽지는 않으며, 재시작/명시적 불러오기가 파일 복구를 담당한다.
        // TODO(FieldManager): AutoSave 성공을 받은 뒤에만 ReturnButton을 활성화하는 게이트를 연결한다.
        // 현재는 테스트 전환 확인 단계라 ReturnToShelter()가 즉시 씬 전환을 수행한다.
    }

    /// <summary>전투 매니저가 확정한 귀환 정산 스냅샷을 표시합니다.</summary>
    public void ShowResult(FieldSceneDataManager.ResultSnapshot result)
    {
        if (result == null)
        {
            return;
        }

        List<CharacterResult> characters = new();
        for (int i = 0; i < result.Characters.Count; i++)
        {
            FieldSceneDataManager.PlayerbleResult character = result.Characters[i];
            characters.Add(new CharacterResult
            {
                Name = character.DisplayName,
                InjuryState = character.InjuryState,
                IsCombatOut = character.IsCombatOut,
                CharacterId = character.CharacterId,
                Portrait = character.Portrait
            });
        }

        List<ResourceResult> resources = new();
        for (int i = 0; i < result.Resources.Count; i++)
        {
            FieldSceneDataManager.ResourceResult resource = result.Resources[i];
            resources.Add(new ResourceResult
            {
                Icon = resource.Icon,
                Count = resource.Count
            });
        }

        ShowResult(result.KillCount, characters, resources);
    }

    /// <summary>결과창을 숨깁니다.</summary>
    /// <remarks>
    /// 인게임 HUD는 여기서 끄지 않습니다. 조준선 패널을 이 캔버스보다 아래(sortingOrder −1)로
    /// 두어 UI 레이어가 가리는 쪽으로 처리합니다. 무엇이 무엇 위에 오는지를 UI 시스템이
    /// 담당하는 축에서 선언하는 편이 읽기 쉽기 때문입니다.
    /// 표시 자체를 끄는 경로가 필요해지면 <see cref="FieldHudVisibility"/>가 준비되어 있습니다.
    /// </remarks>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// ReturnButton의 Inspector OnClick에서 호출하는 셸터 복귀 진입점입니다.
    /// </summary>
    public void ReturnToShelter()
    {
        OnReturnToShelter?.Invoke();
        TestSceneUiEventSystemBridge.DisableBeforeShelterTransition();
        SceneTransitionController.LoadScene(m_returnSceneName);
    }

    private void SetKills(int kills)
    {
        if (m_killCountText != null)
        {
            m_killCountText.text = $"적 처치 수 : {Mathf.Max(0, kills)}";
        }
    }

    private void SetCharacters(IList<CharacterResult> characters)
    {
        if (m_statusRows == null)
        {
            return;
        }

        for (int i = 0; i < m_statusRows.Length; i++)
        {
            RectTransform row = m_statusRows[i];
            if (row == null)
            {
                continue;
            }

            bool hasData = characters != null && i < characters.Count;
            row.gameObject.SetActive(hasData);
            if (!hasData)
            {
                continue;
            }

            CharacterResult character = characters[i];
            TextMeshProUGUI nameText = row.Find("NameText")?.GetComponent<TextMeshProUGUI>();
            if (nameText != null)
            {
                nameText.text = character.Name;
            }

            TextMeshProUGUI stateText = row.Find("StateText")?.GetComponent<TextMeshProUGUI>();
            if (stateText != null)
            {
                stateText.text = ResolveStateLabel(character.InjuryState, character.IsCombatOut);
                stateText.color = ResolveStateColor(character.InjuryState, character.IsCombatOut);
            }

            Transform overlay = row.Find("CriticalOverlay");
            if (overlay != null)
            {
                overlay.gameObject.SetActive(character.IsCombatOut || character.InjuryState == CharacterInjuryState.Critical);
            }
        }
    }

    /// <summary>
    /// 이번 필드에 참여한 캐릭터가 직접 전달한 풀바디 초상화 중 첫 번째를 표시합니다.
    /// </summary>
    /// <remarks>
    /// 초상화는 PlayerbleUnitData에서 필드 결과 스냅샷으로 직접 전달됩니다.
    /// </remarks>
    private void SetCharacterPortrait(IList<CharacterResult> characters)
    {
        if (m_characterImage == null)
        {
            return;
        }

        Sprite chosen = null;

        if (characters != null)
        {
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].Portrait != null)
                {
                    chosen = characters[i].Portrait;
                    break;
                }
            }
        }

        m_characterImage.sprite = chosen;
        m_characterImage.enabled = chosen != null;
    }


    private void SetResources(IList<ResourceResult> resources)
    {
        if (m_slots == null)
        {
            return;
        }

        for (int i = 0; i < m_slots.Length; i++)
        {
            RectTransform slot = m_slots[i];
            if (slot == null)
            {
                continue;
            }

            bool hasData = resources != null && i < resources.Count;
            RawImage icon = slot.Find("Icon")?.GetComponent<RawImage>();
            TextMeshProUGUI count = slot.Find("Count")?.GetComponent<TextMeshProUGUI>();

            if (icon != null)
            {
                icon.texture = hasData ? resources[i].Icon : null;
                icon.enabled = hasData && resources[i].Icon != null;
            }

            if (count != null)
            {
                count.text = hasData ? resources[i].Count.ToString() : string.Empty;
            }
        }
    }

    private static Color ResolveStateColor(CharacterInjuryState injuryState, bool isCombatOut)
    {
        if (isCombatOut || injuryState == CharacterInjuryState.Critical)
        {
            return s_criticalColor;
        }

        return injuryState == CharacterInjuryState.Normal
            ? s_normalColor
            : s_injuredColor;
    }

    private static string ResolveStateLabel(CharacterInjuryState injuryState, bool isCombatOut)
    {
        if (isCombatOut || injuryState == CharacterInjuryState.Critical)
        {
            return "치명상";
        }

        return injuryState == CharacterInjuryState.Normal
            ? "정상"
            : "부상";
    }

    /// <summary>
    /// 씬에 미리 배치된 자식들을 이름으로 찾아 참조를 채웁니다. 이미 할당된 참조는 덮어쓰지 않습니다.
    /// </summary>
    /// <remarks>
    /// 이름으로 하위 계층 전체를 재귀 탐색합니다(<see cref="FindDeep"/>). StatusRow1~3처럼 정리용
    /// 상위 그룹(예: SquadProfile) 밑으로 옮겨도 계속 찾을 수 있도록, 직계 자식만 보는
    /// <see cref="Transform.Find"/> 대신 이 방식을 씁니다.
    /// </remarks>
    private void AutoFindReferences()
    {
        if (m_killCountText == null)
        {
            Transform kill = FindDeep(transform, "KillCount");
            if (kill != null)
            {
                m_killCountText = kill.GetComponent<TextMeshProUGUI>();
            }
        }

        if (m_statusRows == null || m_statusRows.Length == 0)
        {
            List<RectTransform> rows = new List<RectTransform>();
            for (int i = 1; i <= 3; i++)
            {
                Transform row = FindDeep(transform, $"StatusRow{i}");
                if (row != null)
                {
                    rows.Add(row.GetComponent<RectTransform>());
                }
            }
            m_statusRows = rows.ToArray();
        }

        if (m_slots == null || m_slots.Length == 0)
        {
            Transform slotContainer = FindDeep(transform, "SlotContainer");
            if (slotContainer != null)
            {
                List<RectTransform> slots = new List<RectTransform>();
                for (int i = 1; i <= 5; i++)
                {
                    Transform slot = FindDeep(slotContainer, $"Slot{i}");
                    if (slot != null)
                    {
                        slots.Add(slot.GetComponent<RectTransform>());
                    }
                }
                m_slots = slots.ToArray();
            }
        }

        if (m_returnButton == null)
        {
            Transform button = FindDeep(transform, "ReturnButton");
            if (button != null)
            {
                m_returnButton = button.GetComponent<Button>();
            }
        }

        if (m_characterImage == null)
        {
            Transform character = FindDeep(transform, "CharacterImage") ?? FindDeep(transform, "Character");
            if (character != null)
            {
                m_characterImage = character.GetComponent<Image>();
            }
        }
    }

    /// <summary>지정한 이름의 자손 Transform을 하위 계층 전체에서 재귀적으로 찾습니다(직계 자식 한정 아님).</summary>
    private static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform child in root)
        {
            if (child.name == name)
            {
                return child;
            }

            Transform found = FindDeep(child, name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
