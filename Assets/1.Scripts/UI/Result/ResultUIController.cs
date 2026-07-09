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
/// 씬에 미리 배치된 자식(Title/Character/MissionHeader/ResourceHeader/KillCount/StatusRow1~3/
/// ResourcePanel/SlotContainer/Slot1~5/ReturnButton)을 이름으로 찾아 값만 채웁니다.
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
        SetResources(resources);
        gameObject.SetActive(true);
    }

    /// <summary>결과창을 숨깁니다.</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void HandleReturnClicked()
    {
        OnReturnToShelter?.Invoke();
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

    /// <summary>
    /// 씬에 미리 배치된 자식들을 이름으로 찾아 참조를 채웁니다. 이미 할당된 참조는 덮어쓰지 않습니다.
    /// </summary>
    private void AutoFindReferences()
    {
        if (m_killCountText == null)
        {
            Transform kill = transform.Find("KillCount");
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
                Transform row = transform.Find($"StatusRow{i}");
                if (row != null)
                {
                    rows.Add(row.GetComponent<RectTransform>());
                }
            }
            m_statusRows = rows.ToArray();
        }

        if (m_slots == null || m_slots.Length == 0)
        {
            Transform slotContainer = transform.Find("SlotContainer");
            if (slotContainer != null)
            {
                List<RectTransform> slots = new List<RectTransform>();
                for (int i = 1; i <= 5; i++)
                {
                    Transform slot = slotContainer.Find($"Slot{i}");
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
            Transform button = transform.Find("ReturnButton");
            if (button != null)
            {
                m_returnButton = button.GetComponent<Button>();
            }
        }
    }
}
