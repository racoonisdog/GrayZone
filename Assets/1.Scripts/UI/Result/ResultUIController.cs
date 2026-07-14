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
    /// <summary>파티원 생존 상태입니다. 상태 텍스트 색·치명상 오버레이 표시에 사용합니다.</summary>
    public enum CharacterState
    {
        /// <summary>정상 생존입니다.</summary>
        Normal,

        /// <summary>부상(경상)입니다.</summary>
        Injured,

        /// <summary>치명상입니다.</summary>
        Critical,
    }

    /// <summary>결과창에 표시할 파티원 한 명의 정보입니다.</summary>
    [Serializable]
    public struct CharacterResult
    {
        public string Name;
        public CharacterState State;

        /// <summary>캐릭터 초상화 선택에 사용하는 고정 식별자입니다. 알 수 없으면 <see cref="PlayableCharacterId.Unknown"/>입니다.</summary>
        public PlayableCharacterId CharacterId;
    }

    /// <summary>캐릭터 식별자 하나에 매칭되는 풀바디 초상화입니다. 아트가 아직 없으면 <see cref="Portrait"/>를 비워 둡니다.</summary>
    [Serializable]
    public struct CharacterPortraitEntry
    {
        public PlayableCharacterId CharacterId;
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

    [Header("Character Portraits")]
    [Tooltip("캐릭터 식별자별 풀바디 초상화입니다. 아트가 아직 없는 캐릭터는 Portrait를 비워 두면 선택 대상에서 제외됩니다.")]
    [SerializeField] private CharacterPortraitEntry[] m_characterPortraits;

    [Header("Scene Transition")]
    [Tooltip("'셸터로 복귀' 버튼을 누르면 전환할 씬 이름입니다(Build Settings에 등록되어 있어야 합니다).")]
    [SerializeField] private string m_returnSceneName = "TEst";

    /// <summary>'셸터로 복귀' 버튼을 눌렀을 때 발생합니다.</summary>
    public event Action OnReturnToShelter;

    private static readonly Color s_normalColor = new Color(0.3f, 0.9f, 0.3f);
    private static readonly Color s_injuredColor = new Color(0.95f, 0.65f, 0.15f);
    private static readonly Color s_criticalColor = new Color(0.85f, 0.1f, 0.1f);
    private static readonly string[] s_stateLabels = { "정상", "부상", "치명상" };

    private void Reset()
    {
        AutoFindReferences();
    }

    private void Awake()
    {
        AutoFindReferences();
        if (m_returnButton != null)
        {
            m_returnButton.onClick.AddListener(HandleReturnClicked);
        }
    }

    private void OnDestroy()
    {
        if (m_returnButton != null)
        {
            m_returnButton.onClick.RemoveListener(HandleReturnClicked);
        }
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
    }

    /// <summary>전투 매니저가 확정한 귀환 정산 스냅샷을 표시합니다.</summary>
    public void ShowResult(BattleSceneDataManager.ResultSnapshot result)
    {
        if (result == null)
        {
            return;
        }

        List<CharacterResult> characters = new();
        for (int i = 0; i < result.Characters.Count; i++)
        {
            BattleSceneDataManager.PlayerbleResult character = result.Characters[i];
            characters.Add(new CharacterResult
            {
                Name = character.DisplayName,
                State = ToUiCharacterState(character.InjuryState, character.IsCombatOut),
                CharacterId = character.CharacterId
            });
        }

        List<ResourceResult> resources = new();
        for (int i = 0; i < result.Resources.Count; i++)
        {
            BattleSceneDataManager.ResourceResult resource = result.Resources[i];
            resources.Add(new ResourceResult
            {
                Icon = resource.Icon,
                Count = resource.Count
            });
        }

        ShowResult(result.KillCount, characters, resources);
    }

    /// <summary>결과창을 숨깁니다.</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void HandleReturnClicked()
    {
        OnReturnToShelter?.Invoke();
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
                int stateIndex = (int)character.State;
                stateText.text = s_stateLabels[stateIndex];
                stateText.color = ResolveStateColor(character.State);
            }

            Transform overlay = row.Find("CriticalOverlay");
            if (overlay != null)
            {
                overlay.gameObject.SetActive(character.State == CharacterState.Critical);
            }
        }
    }

    /// <summary>
    /// 이번 전투에 참여한 캐릭터 중 풀바디 초상화(<see cref="m_characterPortraits"/>)가 준비된 캐릭터를
    /// 무작위로 하나 골라 표시합니다. 아트가 준비된 캐릭터가 하나도 없으면 초상화를 숨깁니다.
    /// </summary>
    /// <remarks>
    /// 다수 캐릭터가 아직 풀바디 아트 없이 프로토타입 단계인 상황을 고려해, 매칭 실패는 예외가 아니라
    /// "이번엔 표시하지 않음"으로 처리합니다. 아트가 추가되면 <see cref="m_characterPortraits"/>에
    /// 캐릭터 식별자-스프라이트 항목만 추가하면 되고, 이 로직은 변경할 필요가 없습니다.
    /// </remarks>
    private void SetCharacterPortrait(IList<CharacterResult> characters)
    {
        if (m_characterImage == null)
        {
            return;
        }

        Sprite chosen = null;

        if (characters != null && m_characterPortraits != null && m_characterPortraits.Length > 0)
        {
            List<Sprite> candidates = new List<Sprite>();
            for (int i = 0; i < characters.Count; i++)
            {
                Sprite portrait = ResolvePortrait(characters[i].CharacterId);
                if (portrait != null)
                {
                    candidates.Add(portrait);
                }
            }

            if (candidates.Count > 0)
            {
                chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            }
        }

        m_characterImage.sprite = chosen;
        m_characterImage.enabled = chosen != null;
    }

    private Sprite ResolvePortrait(PlayableCharacterId characterId)
    {
        if (characterId == PlayableCharacterId.Unknown || m_characterPortraits == null)
        {
            return null;
        }

        for (int i = 0; i < m_characterPortraits.Length; i++)
        {
            if (m_characterPortraits[i].CharacterId == characterId)
            {
                return m_characterPortraits[i].Portrait;
            }
        }

        return null;
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

    private static Color ResolveStateColor(CharacterState state)
    {
        switch (state)
        {
            case CharacterState.Critical:
                return s_criticalColor;
            case CharacterState.Injured:
                return s_injuredColor;
            default:
                return s_normalColor;
        }
    }

    private static CharacterState ToUiCharacterState(PlayerInjuryState injuryState, bool isCombatOut)
    {
        if (isCombatOut || injuryState == PlayerInjuryState.Critical)
        {
            return CharacterState.Critical;
        }

        return injuryState == PlayerInjuryState.Normal
            ? CharacterState.Normal
            : CharacterState.Injured;
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
